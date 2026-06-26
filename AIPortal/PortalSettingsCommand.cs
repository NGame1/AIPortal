using AIPortal.Forms;
using Microsoft;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using Microsoft.VisualStudio.Shell;
using System.Diagnostics;
using System.Windows.Interop;

namespace AIPortal
{
    /// <summary>
    /// Command1 handler.
    /// </summary>
    [VisualStudioContribution]
    internal class PortalSettingsCommand : Command
    {
        private readonly TraceSource logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PortalSettingsCommand"/> class.
        /// </summary>
        /// <param name="traceSource">Trace source instance to utilize.</param>
        public PortalSettingsCommand(TraceSource traceSource)
        {
            // This optional TraceSource can be used for logging in the command. You can use dependency injection to access
            // other services here as well.
            this.logger = Requires.NotNull(traceSource, nameof(traceSource));
        }

        /// <inheritdoc />
        public override CommandConfiguration CommandConfiguration => new("%AIPortal.Settings.DisplayName%")
        {
            // Use this object initializer to set optional parameters for the command. The required parameter,
            // displayName, is set above. DisplayName is localized and references an entry in .vsextension\string-resources.json.
            Icon = new(ImageMoniker.KnownValues.Settings, IconSettings.IconAndText),
            Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
        };

        /// <inheritdoc />
        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            // Use InitializeAsync for any one-time setup or initialization.
            return base.InitializeAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var settings = new AISettings();
                var owner = WindowHelper.GetDialogOwnerHandle();
                new WindowInteropHelper(settings).Owner = owner;
                settings.ShowInTaskbar = false;
                var s = settings.ShowDialog();
                //await this.Extensibility.Shell().ShowDialogAsync(settings, DialogOption.OKCancel, cancellationToken);
            }
            catch (Exception ex)
            {
                await this.Extensibility.Shell().ShowPromptAsync(ex.Message, PromptOptions.OK, cancellationToken);
            }
        }
    }
}
