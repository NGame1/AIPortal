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

        }

        private void RaiseError(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private bool ValidateForm()
        {
            if(string.IsNullOrWhiteSpace(BaseUrlBox.Text))
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

            return true;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm())
                return;

            WritableSettingsStore.SetString(CollectionPath, BaseUrlKey, BaseUrlBox.Text);
            WritableSettingsStore.SetString(CollectionPath, ApiKey, ApiKeyBox.Password);

            ExtensionEntrypoint.SetBaseUrl(BaseUrlBox.Text);
            ExtensionEntrypoint.SetAPIKey(ApiKeyBox.Password);

            await ExtensionEntrypoint.RestartProxyProcessAsync();

            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

    }
}
