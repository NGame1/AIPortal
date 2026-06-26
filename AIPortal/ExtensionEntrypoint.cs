using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.ProgressReporting;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace AIPortal;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    internal static string? APIKey { get; private set; }
    internal static string? BaseUrl { get; private set; }
    internal static int HttpPort { get; private set; } = Constants.DEFAULT_HTTP_PORT;
    internal static int Timeout { get; private set; }

    /// <summary>
    /// Automatically loads the extension on VS Shell Init
    /// </summary>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        RequiresInProcessHosting = true,
        LoadedWhen = ActivationConstraint.UIContext(Guid.Parse(VSConstants.UICONTEXT.ShellInitialized_string))
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        
        // You can configure dependency injection here by adding services to the serviceCollection.
    }

    /// <summary>
    /// Checks the Extension Settings and run Proxy, If it is not running.
    /// </summary>
    /// <param name="extensibility"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override async Task OnInitializedAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        await base.OnInitializedAsync(extensibility, cancellationToken);
        if (!await GetExtensionSettingsAsync(extensibility, cancellationToken))
            return;

        using var progress = await extensibility.Shell().StartProgressReportingAsync("SystemGroup AI Portal started loading components...", cancellationToken);
        ReportProgress(progress, 10, "Checking OpenRouterProxy Process...");

        try
        {
            await StartProxyProcessAsync(progress);
        }
        catch (Exception ex)
        {
            await extensibility.Shell().ShowPromptAsync(ex.Message, PromptOptions.ErrorConfirm, cancellationToken);
        }
        
        ReportProgress(progress, 100, "AI Portal is ready.");
    }

    async Task<bool> GetExtensionSettingsAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        var ShellSettingsManager = new ShellSettingsManager(Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider);
        var WritableSettingsStore = ShellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

        if (!WritableSettingsStore.CollectionExists(Constants.SETTINGS_COLLECTION_PATH))
        {
            WritableSettingsStore.CreateCollection(Constants.SETTINGS_COLLECTION_PATH);
        }

        if (WritableSettingsStore.TryGetString(Constants.SETTINGS_COLLECTION_PATH, Constants.SETTINGS_BASE_URL, out string baseUrl))
            SetBaseUrl(baseUrl);
        else
            SetBaseUrl(Constants.DEFAULT_OPEN_ROUTER_BASE_URL);

        if (WritableSettingsStore.TryGetString(Constants.SETTINGS_COLLECTION_PATH, Constants.SETTINGS_API_KEY, out string apiKey))
        {
            SetAPIKey(apiKey);
        }
        else
        {
            await extensibility.Shell().ShowPromptAsync("Please set your API Key from Extensions → AI Portal Settings.", PromptOptions.OK, cancellationToken);
            return false;
        }

        if (WritableSettingsStore.TryGetInt32(Constants.SETTINGS_COLLECTION_PATH, Constants.SETTINGS_HTTP_PORT_KEY, out int httpPort))
            SetHttpPort(httpPort);
        else
            SetHttpPort(Constants.DEFAULT_HTTP_PORT);

        if (WritableSettingsStore.TryGetInt32(Constants.SETTINGS_COLLECTION_PATH, Constants.SETTINGS_TIMEOUT_SECONDS_KEY, out int timeoutSeconds))
            SetTimeout(timeoutSeconds);
        else
            SetTimeout(Constants.DEFAULT_TIMEOUT_SECONDS);

        return true;
    }

    public static async Task RestartProxyProcessAsync(ProgressReporter? progress = null)
    {
        KillProxyProcess();
        await Task.Delay(2000);
        await StartProxyProcessAsync(progress);
    }

    public static void KillProxyProcess()
    {
        var proc = Process.GetProcessesByName(Constants.OPEN_ROUTER_PROXY_PROCESS_NAME);
        if (proc.Length == 0)
            return;

        foreach (var p in proc)
        {
            p.Kill();
        }
    }

    public static async Task StartProxyProcessAsync(ProgressReporter? progress)
    {
        var proc = Process.GetProcessesByName(Constants.OPEN_ROUTER_PROXY_PROCESS_NAME);
        Process openRouterProxyProc;

        if (proc.Length == 0)
        {
            ReportProgress(progress, 20, "Starting OpenRouterProxy Process...");
            try
            {
                var startInfo = new ProcessStartInfo()
                {
                    Arguments = $"--apikey {APIKey} --url {BaseUrl} --timeout {Timeout} --port {HttpPort}",
                    FileName = "OpenRouterProxy.exe",
                    //WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                };
                openRouterProxyProc = new Process() { StartInfo = startInfo };
                openRouterProxyProc.Start();
                ReportProgress(progress, 50, "OpenRouterProxy Process Started.");
            }
            catch (Exception)
            {
                ReportProgress(progress, 100, "Failed to start OpenRouterProxy Process.");
                throw;
            }
        }
        else
        {
            openRouterProxyProc = proc.First();
        }
    }

    public static void SetBaseUrl(string baseUrl)
    {
        BaseUrl = baseUrl;
    }

    public static void SetAPIKey(string apiKey)
    {
        APIKey = apiKey;
    }

    public static void SetHttpPort(int httpPort)
    {
        HttpPort = httpPort;
    }

    public static void SetTimeout(int timeout)
    {
        Timeout = timeout;
    }

    static void ReportProgress(ProgressReporter? progress, int? percentage = null, string desction = "")
    {
        progress?.Report(new ProgressStatus(percentage, desction));
    }
}
