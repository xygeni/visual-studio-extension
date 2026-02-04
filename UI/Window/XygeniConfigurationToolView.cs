using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace vs2026_plugin.UI.Window
{
    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    /// <remarks>
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    /// <para>
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </para>
    /// </remarks>
    [Guid("574b6f75-2be9-4e5c-9c9e-5e04351a4e51")]
    public class XygeniConfigurationToolView : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="XygeniConfigurationToolView"/> class.
        /// </summary>
        public XygeniConfigurationToolView() : base(null)
        {
            this.Caption = "xygeniConfiguration";

            // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
            // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
            // the object returned by the Content property.
            this.Content = new UI.Control.XygeniConfigurationControl();
        }
    }
}
