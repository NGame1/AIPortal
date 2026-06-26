using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
int listenPort = 11434;
string openRouterApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? "YOUR_API_KEY_HERE";
string openRouterUrl = "https://openrouter.ai/api/v1";
string modelFilter = "";
bool debugOutput = false;
int? timeoutInSeconds = null;

// Parse command-line args
for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLower())
    {
        case "--port": listenPort = int.Parse(args[++i]); break;
        case "--apikey": openRouterApiKey = args[++i]; break;
        case "--url": openRouterUrl = args[++i]; break;
        case "--filter": modelFilter = args[++i]; break;
        case "--debug": debugOutput = true; break;
        case "--timeout": timeoutInSeconds = int.Parse(args[++i]); break;
    }
}

if (string.IsNullOrWhiteSpace(openRouterApiKey))
{
    Log("ERROR", "No OpenRouter API key. Use --apikey or OPENROUTER_API_KEY env var.");
    return 1;
}

// ---------------------------------------------------------------------------
// Global Shared Resources
// ---------------------------------------------------------------------------
var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(timeoutInSeconds ?? 30) };

// Thread-safe model cache
var modelCacheLock = new object();
var modelCacheExpiry = DateTime.MinValue;
string ollamaTags = """{"models":[]}""";
string openAiModels = """{"object":"list","data":[]}""";

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------
void Log(string level, string msg)
{
    var color = level switch
    {
        "INFO" => ConsoleColor.Cyan,
        "WARN" => ConsoleColor.Yellow,
        "ERROR" => ConsoleColor.Red,
        "DEBUG" => ConsoleColor.DarkGray,
        _ => ConsoleColor.White
    };
    string ts = DateTime.Now.ToString("HH:mm:ss.fff");
    lock (Console.Out)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[{ts}][{level}] {msg}");
        Console.ResetColor();
    }
}

// ---------------------------------------------------------------------------
// HTTP response helpers
// ---------------------------------------------------------------------------
async Task SendJson(HttpListenerResponse resp, int code, string body)
{
    try
    {
        resp.StatusCode = code;
        resp.ContentType = "application/json; charset=utf-8";
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes,0, bytes.Length);
    }
    finally
    {
        try { resp.OutputStream.Close(); } catch { }
    }
}

async Task SendErr(HttpListenerResponse resp, int code, string msg)
{
    string escaped = msg
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "");
    await SendJson(resp, code, @$"""{{ ""error"": {{ ""message"": ""{escaped}"", ""code"": {code} }} }}""");
}

async Task<string> ReadBody(HttpListenerRequest req)
{
    if (req.ContentLength64 == 0) return "";
    using var ms = new MemoryStream();
    await req.InputStream.CopyToAsync(ms);
    return Encoding.UTF8.GetString(ms.ToArray());
}

// ---------------------------------------------------------------------------
// Model cache
// ---------------------------------------------------------------------------
bool IsOpenRouterBaseURL()
{
    return openRouterUrl.Contains("openrouter") || openRouterUrl.Contains("open-router");
}

async Task RefreshModels()
{
    if (DateTime.Now < modelCacheExpiry) return;

    bool lockTaken = false;
    Monitor.Enter(modelCacheLock, ref lockTaken);
    try
    {
        if (DateTime.Now < modelCacheExpiry) return; // double-check

        Log("INFO", "Fetching tool-calling models from OpenRouter...");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{openRouterUrl}/models");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {openRouterApiKey}");

        using var resp = await httpClient.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();

        // Parse model IDs and supported_parameters from raw JSON
        var ollamaList = new List<string>();
        var openAiList = new List<string>();
        string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Extract each model block (simple approach: match id + supported_parameters)
        var modelBlocks = Regex.Matches(json,
            @"\{[^{}]*""id""\s*:\s*""([^""]+)""[^{}]*\}",
            RegexOptions.Singleline);

        // More robust: split on top-level array items
        // Use a proper minimal extraction:
        var idMatches = Regex.Matches(json, @"""id""\s*:\s*""([^""]+)""");
        var paramMatches = Regex.Matches(json, @"""supported_parameters""\s*:\s*\[([^\]]*)\]");
        int count = 0;
        if (IsOpenRouterBaseURL())
        {
            count = Math.Min(idMatches.Count, paramMatches.Count);
        }
        else
        {
            count = idMatches.Count;
        }
        for (int i = 0; i < count; i++)
        {
            string id = idMatches[i].Groups[1].Value;
            if(IsOpenRouterBaseURL())
            {
                string pBlock = paramMatches[i].Groups[1].Value;

                bool hasTools = pBlock.Contains("\"tools\"");
                if (!hasTools) continue;
            }

            if (!string.IsNullOrEmpty(modelFilter) &&
                !id.ToLower().Contains(modelFilter.ToLower())) continue;

            string family = id.Split('/')[0];
            string jId = EscapeJson(id);
            string jFamily = EscapeJson(family);

            ollamaList.Add(
                $$$"""{"name":"{{{jId}}}","model":"{{{jId}}}","modified_at":"{{{nowIso}}}","size":0,"size_vram":0,"digest":"openrouter","details":{"parent_model":"","format":"gguf","family":"{{{jFamily}}}","families":["{{{jFamily}}}"],"parameter_size":"unknown","quantization_level":"Q4_0"}}"""
            );
            openAiList.Add(
                $$$"""{"id":"{{{jId}}}","object":"model","created":{{{nowUnix}}},"owned_by":"{{{jFamily}}}"}"""
            );
        }

        ollamaTags = $$"""{"models":[{{string.Join(",", ollamaList)}}]}""";
        openAiModels = $$"""{"object":"list","data":[{{string.Join(",", openAiList)}}]}""";
        modelCacheExpiry = DateTime.Now.AddMinutes(5);

        Log("INFO", $"Cached {ollamaList.Count} tool-calling models" +
            (string.IsNullOrEmpty(modelFilter) ? "" : $" (filter: *{modelFilter}*)"));
    }
    catch (Exception ex)
    {
        Log("WARN", $"Failed to fetch models: {ex.Message}");
    }
    finally
    {
        if (lockTaken) Monitor.Exit(modelCacheLock);
    }
}

string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

// ---------------------------------------------------------------------------
// Chat forwarder
// ---------------------------------------------------------------------------
async Task HandleChat(HttpListenerRequest req, HttpListenerResponse resp)
{
    string body = await ReadBody(req);
    if (string.IsNullOrEmpty(body)) { await SendErr(resp, 400, "Empty body"); return; }
    if (debugOutput) Log("DEBUG", $"REQ >> {body}");

    var modelMatch = Regex.Match(body, @"""model""\s*:\s*""([^""]+)""");
    var streamMatch = Regex.Match(body, @"""stream""\s*:\s*(true|false)");

    string model = modelMatch.Success ? modelMatch.Groups[1].Value : "openai/gpt-4o";
    bool isStream = streamMatch.Success && streamMatch.Groups[1].Value == "true";

    Log("INFO", $"--> OpenRouter  model={model}  stream={isStream}");

    string uri = $"{openRouterUrl}/chat/completions";

    if (isStream)
    {
        string outBody = body;
        if (!body.Contains("\"stream_options\""))
            outBody = Regex.Replace(body, @"\}\s*$", ",\"stream_options\":{\"include_usage\":true}}");

        using var cts = new CancellationTokenSource();
        try
        {
            resp.StatusCode = 200;
            resp.ContentType = "text/event-stream; charset=utf-8";
            resp.Headers.Add("Cache-Control", "no-cache");
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.Headers.Add("Connection", "keep-alive");
            resp.SendChunked = true;

            using var reqMsg = BuildRequest(HttpMethod.Post, uri, outBody);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutInSeconds ?? 30));

            HttpResponseMessage upResp;
            try
            {
                upResp = await httpClient.SendAsync(reqMsg,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log("ERROR", $"Upstream timeout after {timeoutInSeconds ?? 30} seconds (stream)");
                await SendErr(resp, 504, "Upstream did not respond within 30 seconds");
                return;
            }

            using (upResp)
            {
                if (!upResp.IsSuccessStatusCode)
                {
                    string errBody = await upResp.Content.ReadAsStringAsync();
                    Log("ERROR", $"Upstream error: {upResp.StatusCode} - {errBody}");
                    await SendErr(resp, (int)upResp.StatusCode, errBody);
                    return;
                }

                using var stream = await upResp.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(resp.OutputStream, Encoding.UTF8) { AutoFlush = true };

                string? promptTok = null, completionTok = null;

                while (!reader.EndOfStream)
                {
                    if (!resp.OutputStream.CanWrite)
                    {
                        Log("WARN", "Client disconnected. Aborting OpenRouter stream.");
                        cts.Cancel();
                        break;
                    }

                    string? line = await reader.ReadLineAsync();
                    if (line is null) break;

                    await writer.WriteLineAsync(line);
                    if (debugOutput) Log("DEBUG", $"<< {line}");

                    var m1 = Regex.Match(line, @"""prompt_tokens""\s*:\s*(\d+)");
                    var m2 = Regex.Match(line, @"""completion_tokens""\s*:\s*(\d+)");
                    if (m1.Success) promptTok = m1.Groups[1].Value;
                    if (m2.Success) completionTok = m2.Groups[1].Value;
                }

                if (promptTok is not null)
                    Log("INFO", $"<-- tokens  in={promptTok}  out={completionTok}");
            }
        }
        catch (IOException)
        {
            Log("WARN", "Client closed connection mid-stream.");
            cts.Cancel();
        }
        catch (OperationCanceledException)
        {
            Log("ERROR", $"Upstream timeout or cancelled after {timeoutInSeconds ?? 30} seconds");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Stream error: {ex.Message}");
        }
        finally
        {
            try { resp.OutputStream.Close(); } catch { }
        }
    }
    else
    {
        string? promptTok = null, completionTok = null;
        try
        {
            using var reqMsg = BuildRequest(HttpMethod.Post, uri, body);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutInSeconds ?? 30));

            HttpResponseMessage upResp;
            try
            {
                upResp = await httpClient.SendAsync(reqMsg, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log("ERROR", $"Upstream timeout after {timeoutInSeconds ?? 30} seconds");
                await SendErr(resp, 504, $"Upstream did not respond within {timeoutInSeconds ?? 30} seconds");
                return;
            }

            using (upResp)
            {
                if (!upResp.IsSuccessStatusCode)
                {
                    string errBody = await upResp.Content.ReadAsStringAsync();
                    Log("ERROR", $"Upstream error: {upResp.StatusCode} - {errBody}");
                    await SendErr(resp, (int)upResp.StatusCode, errBody);
                    return;
                }

                using var stream = await upResp.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(resp.OutputStream, Encoding.UTF8) { AutoFlush = true };

                while (!reader.EndOfStream)
                {
                    if (!resp.OutputStream.CanWrite)
                    {
                        Log("WARN", "Client disconnected.");
                        break;
                    }

                    string? line = await reader.ReadLineAsync();
                    if (line is null) break;

                    await writer.WriteLineAsync(line);
                    if (debugOutput) Log("DEBUG", $"<< {line}");

                    var m1 = Regex.Match(line, @"""prompt_tokens""\s*:\s*(\d+)");
                    var m2 = Regex.Match(line, @"""completion_tokens""\s*:\s*(\d+)");
                    if (m1.Success) promptTok = m1.Groups[1].Value;
                    if (m2.Success) completionTok = m2.Groups[1].Value;
                }

                if (promptTok is not null)
                    Log("INFO", $"<-- tokens  in={promptTok}  out={completionTok}");
            }
        }
        catch (OperationCanceledException)
        {
            Log("ERROR", $"Upstream timeout after {timeoutInSeconds ?? 30} seconds");
            await SendErr(resp, 504, $"Upstream did not respond within {timeoutInSeconds ?? 30} seconds");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"OpenRouter error: {ex.Message}");
            await SendErr(resp, 502, ex.Message);
        }
    }
}

HttpRequestMessage BuildRequest(HttpMethod method, string uri, string jsonBody)
{
    var msg = new HttpRequestMessage(method, uri)
    {
        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
    };
    msg.Headers.TryAddWithoutValidation("Authorization", $"Bearer {openRouterApiKey}");
    msg.Headers.TryAddWithoutValidation("HTTP-Referer", $"http://localhost:{listenPort}");
    msg.Headers.TryAddWithoutValidation("X-Title", "VS-Copilot-OpenRouter-Proxy");
    return msg;
}

// ---------------------------------------------------------------------------
// Main request dispatcher
// ---------------------------------------------------------------------------
async Task HandleRequest(HttpListenerContext ctx)
{
    var req = ctx.Request;
    var resp = ctx.Response;
    string method = req.HttpMethod;
    string path = req.Url!.AbsolutePath.TrimEnd('/');

    if (path != "/api/show") Log("INFO", $"{method} {path}");
    if (debugOutput)
    {
        var hdrs = string.Join(" | ", req.Headers.AllKeys!
            .Select(k => $"{k}={req.Headers[k]}"));
        Log("DEBUG", $"Headers: {hdrs}");
    }

    if (method == "OPTIONS") { await SendJson(resp, 204, ""); return; }
    if (method == "HEAD")
    {
        resp.StatusCode = 200;
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        try { resp.OutputStream.Close(); } catch { }
        return;
    }

    switch (path)
    {
        case "/api/version":
            await SendJson(resp, 200, """{"version":"0.3.0"}""");
            break;

        case "/api/tags":
            await RefreshModels();
            await SendJson(resp, 200, ollamaTags);
            break;

        case "/api/ps":
            await SendJson(resp, 200, """{"models":[]}""");
            break;

        case "/api/show":
            {
                string showBody = await ReadBody(req);
                var m = Regex.Match(showBody, @"""model""\s*:\s*""([^""]+)""");
                string modelId = m.Success ? m.Groups[1].Value : "";
                string family = modelId.Split('/')[0];
                string jId = EscapeJson(modelId);
                string jFamily = EscapeJson(family);
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

                string showResp = $$$$"""
{
  "model": "{{{{jId}}}}",
  "modified_at": "{{{{now}}}}",
  "modelfile": "# Modelfile for {{{{jId}}}}",
  "parameters": "",
  "template": "{{ .Prompt }}",
  "details": {
    "parent_model": "",
    "format": "gguf",
    "family": "{{{{jFamily}}}}",
    "families": ["{{{{jFamily}}}}"],
    "parameter_size": "unknown",
    "quantization_level": "Q4_0"
  },
  "model_info": {
    "general.architecture": "{{{{jFamily}}}}",
    "general.basename": "{{{{jId}}}}",
    "general.finetune": "",
    "general.quantization_version": 2,
    "general.size_label": "unknown"
  },
  "capabilities": ["completion", "tools"]
}
""";


                Log("INFO", $"POST /api/show  {modelId}");
                await SendJson(resp, 200, showResp);
                break;
            }

        case "/v1/models":
            await RefreshModels();
            await SendJson(resp, 200, openAiModels);
            break;

        case "/api/chat":
        case "/v1/chat/completions":
            await HandleChat(req, resp);
            break;

        default:
            Log("WARN", $"UNHANDLED {method} {path}");
            await SendErr(resp, 404, $"Unknown: {path}");
            break;
    }
}

// ---------------------------------------------------------------------------
// Startup
// ---------------------------------------------------------------------------
string prefix = $"http://localhost:{listenPort}/";
var listener = new HttpListener();
listener.Prefixes.Add(prefix);

try { listener.Start(); }
catch (Exception ex)
{
    Log("ERROR", $"Cannot bind to {prefix} — {ex.Message}");
    Log("WARN", "Try running as Administrator or use a different --port");
    return 1;
}

// Ctrl+C handler
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    try { listener.Stop(); } catch { }
};

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║   GitHub Copilot  →  OpenRouter  (Ollama-shape proxy)     ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"  Listening  : {prefix}");
Console.WriteLine($"  OpenRouter : {openRouterUrl}");
if (!string.IsNullOrEmpty(modelFilter))
    Console.WriteLine($"  Filter     : *{modelFilter}*");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine();
Console.WriteLine("  VS setup: Tools → Options → GitHub Copilot → Ollama endpoint");
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine($"            http://localhost:{listenPort}");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine();
Console.WriteLine("  Press Ctrl+C to stop.");
Console.WriteLine();
Console.ResetColor();

// ---------------------------------------------------------------------------
// Main request loop (fully async — no Runspace Pool needed)
// ---------------------------------------------------------------------------
var tasks = new List<Task>();

while (listener.IsListening)
{
    // Clean up completed tasks
    tasks.RemoveAll(t => t.IsCompleted);

    HttpListenerContext ctx;
    try
    {
        ctx = await listener.GetContextAsync();
    }
    catch (HttpListenerException) when (!listener.IsListening) { break; }
    catch (ObjectDisposedException) { break; }

    // Fire-and-forget per request (async concurrency replaces RunspacePool)
    tasks.Add(Task.Run(async () =>
    {
        try { await HandleRequest(ctx); }
        catch (Exception ex) { Log("ERROR", $"Unhandled: {ex.Message}"); }
    }));
}

// Cleanup
await Task.WhenAll(tasks.Where(t => !t.IsCompleted));
httpClient.Dispose();
listener.Close();
Log("INFO", "Proxy stopped.");
return 0;
