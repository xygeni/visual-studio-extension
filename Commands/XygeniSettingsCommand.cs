using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Design;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using vs2026_plugin.UI.Window;
using Task = System.Threading.Tasks.Task;

namespace vs2026_plugin.Commands
{
    internal sealed class XygeniSettingsCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("c7b39864-46c5-43a9-9892-e31d4e0e5621");

        private readonly AsyncPackage package;

        private XygeniSettingsCommand(AsyncPackage package, IMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static XygeniSettingsCommand Instance { get; private set; }

        private IAsyncServiceProvider ServiceProvider => this.package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            IMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;
            Instance = new XygeniSettingsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            this.package.JoinableTaskFactory.RunAsync(async delegate
            {
                ToolWindowPane window = await this.package.ShowToolWindowAsync(typeof(XygeniConfigurationToolView), 0, true, this.package.DisposalToken);
                if ((null == window) || (null == window.Frame))
                {
                    throw new NotSupportedException("Cannot create tool window");
                }
            });
        }
    }
}
