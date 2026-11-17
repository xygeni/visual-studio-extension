using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// A non-modal info bar in the main window with action links ("Update" / "Ignore this version"), used for the
    /// MCP server update notifications (xygeni/xygeni-product-backlog#2382). Falls back to a message box when the
    /// info bar host is not available.
    /// </summary>
    internal sealed class McpUpdateInfoBar : IVsInfoBarUIEvents
    {
        private readonly Action<string> _onAction;
        private IVsInfoBarUIElement _element;
        private uint _cookie;

        private McpUpdateInfoBar(Action<string> onAction)
        {
            _onAction = onAction;
        }

        /// <summary>Shows <paramref name="message"/> with one hyperlink per (text, action id); <paramref name="onAction"/> gets the id of the clicked one.</summary>
        public static void Show(string message, (string text, string id)[] actions, Action<string> onAction, bool warning = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
                var factory = ServiceProvider.GlobalProvider.GetService(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;
                IVsInfoBarHost host = null;
                if (shell != null && factory != null
                    && ErrorHandler.Succeeded(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject)))
                {
                    host = hostObject as IVsInfoBarHost;
                }
                if (host == null)
                {
                    ShowMessageBox(message, actions, onAction, warning);
                    return;
                }

                var actionItems = new IVsInfoBarActionItem[actions.Length];
                for (int i = 0; i < actions.Length; i++)
                {
                    actionItems[i] = new InfoBarHyperlink(actions[i].text, actions[i].id);
                }
                var model = new InfoBarModel(
                    new[] { new InfoBarTextSpan(message) },
                    actionItems,
                    warning ? KnownMonikers.StatusWarning : KnownMonikers.StatusInformation,
                    isCloseButtonVisible: true);
                var infoBar = new McpUpdateInfoBar(onAction);
                infoBar._element = factory.CreateInfoBar(model);
                infoBar._element.Advise(infoBar, out infoBar._cookie);
                host.AddInfoBar(infoBar._element);
            }
            catch (Exception)
            {
                ShowMessageBox(message, actions, onAction, warning);
            }
        }

        // Yes = first action, No = second action (when any), Cancel = dismiss.
        private static void ShowMessageBox(string message, (string text, string id)[] actions, Action<string> onAction, bool warning)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string hint = actions.Length > 1
                ? $"\n\nYes: {actions[0].text}. No: {actions[1].text}. Cancel: decide later."
                : $"\n\nYes: {actions[0].text}.";
            int result = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message + hint, "Xygeni MCP server",
                warning ? OLEMSGICON.OLEMSGICON_WARNING : OLEMSGICON.OLEMSGICON_INFO,
                actions.Length > 1 ? OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL : OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            const int IDYES = 6, IDNO = 7;
            if (result == IDYES) onAction(actions[0].id);
            else if (result == IDNO && actions.Length > 1) onAction(actions[1].id);
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_element != null && _cookie != 0)
            {
                _element.Unadvise(_cookie);
                _cookie = 0;
            }
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string id = actionItem.ActionContext as string;
            infoBarUIElement.Close();
            if (id != null) _onAction(id);
        }
    }
}
