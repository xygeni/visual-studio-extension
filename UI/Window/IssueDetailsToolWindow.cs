using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using vs2026_plugin.UI.Control;

namespace vs2026_plugin.UI.Window
{
    [Guid("e359a341-9c60-49fa-9b43-261559436423")]
    public class IssueDetailsToolWindow : ToolWindowPane
    {
        public IssueDetailsToolWindow() : base(null)
        {
            this.Caption = "Xygeni Issue Details";
            this.Content = new IssueDetailsControl();
        }
    }
}
