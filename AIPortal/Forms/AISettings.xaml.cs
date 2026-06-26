using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using System.Windows;

namespace AIPortal.Forms
{
    /// <summary>
    /// Interaction logic for AISettings.xaml
    /// </summary>
    public partial class AISettings : DialogWindow
    {
        private string CollectionPath { get; } = Constants.SETTINGS_COLLECTION_PATH;
        private string BaseUrlKey { get; } = Constants.SETTINGS_BASE_URL;
        private string ApiKey { get; } = Constants.SETTINGS_API_KEY;
        private string HttpPortKey { get; } = Constants.SETTINGS_HTTP_PORT_KEY;
        private string TimeoutSecondsKey { get; } = Constants.SETTINGS_TIMEOUT_SECONDS_KEY;

        private WritableSettingsStore WritableSettingsStore { get; }
        private ShellSettingsManager ShellSettingsManager { get; }

        public AISettings()
        {
            InitializeComponent();

            base.OverridesDefaultStyle = true;
            ShellSettingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            WritableSettingsStore = ShellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (!WritableSettingsStore.CollectionExists(CollectionPath))
            {
                WritableSettingsStore.CreateCollection(CollectionPath);
            }

            if (WritableSettingsStore.TryGetString(CollectionPath, BaseUrlKey, out string baseUrl))
                BaseUrlBox.Text = baseUrl;
            else
                BaseUrlBox.Text = Constants.DEFAULT_OPEN_ROUTER_BASE_URL;

            if (WritableSettingsStore.TryGetString(CollectionPath, ApiKey, out string apiKey))
                ApiKeyBox.Password = apiKey;

            if (WritableSettingsStore.TryGetInt32(CollectionPath, HttpPortKey, out int httpPort))
                HttpPortBox.Text = httpPort.ToString();
            else
                HttpPortBox.Text = Constants.DEFAULT_HTTP_PORT.ToString();

            if (WritableSettingsStore.TryGetInt32(CollectionPath, TimeoutSecondsKey, out int timeoutSeconds))
                TimeoutBox.Text = timeoutSeconds.ToString();
            else
                TimeoutBox.Text = Constants.DEFAULT_TIMEOUT_SECONDS.ToString();
        }

        private void RaiseError(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(BaseUrlBox.Text))
            {
                RaiseError("Base Url can not be empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            {
                RaiseError("API Key can not be empty.");
                return false;
            }

            if (!Uri.TryCreate(BaseUrlBox.Text, UriKind.Absolute, out Uri uri))
            {
                RaiseError("Invalid base Url, Please check your Url and try again.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(HttpPortBox.Text))
            {
                if (!int.TryParse(HttpPortBox.Text, out int httpPort))
                {
                    RaiseError($"Invalid HTTP Port, Please check your port and try again.");
                    return false;
                }
                else if (httpPort < 0 || httpPort > 65535)
                {
                    RaiseError($"Http port must be in a range of 1 to 65535, Please check your port and try again.");
                    return false;
                }
            }
            else
            {
                RaiseError("HTTP Port can not be empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(TimeoutBox.Text))
            {
                RaiseError("Timeout can not be empty.");
                return false;
            }

            if(!int.TryParse(TimeoutBox.Text, out int timeoutSeconds))
            {
                RaiseError($"Invalid Timeout, Please check your timeout and try again.");
                return false;
            }
            else if (timeoutSeconds < 1)
            {
                RaiseError($"Timeout must be greater than 0, Please check your timeout and try again.");
                return false;
            }

            return true;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveBtn.IsEnabled = false;

            if (!ValidateForm())
                return;

            try
            {
                WritableSettingsStore.SetString(CollectionPath, BaseUrlKey, BaseUrlBox.Text);
                WritableSettingsStore.SetString(CollectionPath, ApiKey, ApiKeyBox.Password);
                WritableSettingsStore.SetInt32(CollectionPath, HttpPortKey, int.Parse(HttpPortBox.Text));
                WritableSettingsStore.SetInt32(CollectionPath, TimeoutSecondsKey, int.Parse(TimeoutBox.Text));

                ExtensionEntrypoint.SetBaseUrl(BaseUrlBox.Text);
                ExtensionEntrypoint.SetAPIKey(ApiKeyBox.Password);
                ExtensionEntrypoint.SetTimeout(int.Parse(TimeoutBox.Text));
                ExtensionEntrypoint.SetHttpPort(int.Parse(HttpPortBox.Text));

                await ExtensionEntrypoint.RestartProxyProcessAsync();

                Close();
            }
            catch
            {
                SaveBtn.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

    }
}
