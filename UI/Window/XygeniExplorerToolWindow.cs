using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace vs2026_plugin.UI.Window
{
    [Guid("1d63630f-4f51-4e4b-9e4a-38d5c487b404")]
    public class XygeniExplorerToolWindow : ToolWindowPane
    {
        public XygeniExplorerToolWindow() : base(null)
        {
            this.Caption = "Xygeni Explorer";
            this.Content = new UI.Control.XygeniExplorerControl();
        }
    }
}
