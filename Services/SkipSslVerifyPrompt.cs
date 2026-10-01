using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// --skip-ssl-verify for corporate proxies that inspect TLS traffic (xygeni/tech-support#378). The user cannot
    /// be expected to know the CLI option, so a scanner call failing on the certificate offers to turn it on;
    /// it weakens transport security, so it is always confirmed first.
    /// </summary>
    public static class SkipSslVerifyPrompt
    {
        private const int IdYes = 6;

        private const string Risk = "Use it only when a corporate proxy inspects TLS traffic and re-signs it with an "
            + "internal CA, so the scan fails with certificate errors. This reduces transport security: use it only in "
            + "trusted networks.";

        private static volatile bool _suggested;

        /// <summary>Raised when the setting changes here, so the configuration window can refresh its checkbox.</summary>
        public static event EventHandler Changed;

        /// <summary>Once per session (concurrent scanner calls would repeat it); safe to call from any thread.</summary>
        public static void Suggest(ILogger logger)
        {
            if (_suggested) return;
            _suggested = true;
            string message = "The Xygeni scanner could not validate the server SSL certificate. If you are behind a corporate "
                + "proxy that inspects TLS traffic, enable \"Skip SSL verification\" in the Xygeni configuration.";
            logger.Log(message);
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (Ask(message + "\n\nEnable it now?\n\n" + Risk, "Xygeni: SSL Certificate Error"))
                {
                    SetEnabled(true);
                }
            });
        }

        /// <summary>Asks for confirmation and turns on --skip-ssl-verify; false when the user cancels. UI thread.</summary>
        public static bool ConfirmAndEnable()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!Ask(Risk + "\n\nEnable it?", "Skip SSL Certificate Validation?"))
            {
                return false;
            }
            SetEnabled(true);
            return true;
        }

        public static void SetEnabled(bool enabled)
        {
            XygeniConfigurationService.GetInstance().SaveSkipSslVerify(enabled);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static bool Ask(string message, string title)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, title,
                OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == IdYes;
        }
    }
}
