using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using vs2026_plugin.Services;
using System.Threading.Tasks;

namespace vs2026_plugin.Commands
{
    public static class XygeniCommands
    {
        public static string ReportSuffix = "xygeni_report.json";

        public static async Task InstallScanner()
        {
            var _installerService = XygeniInstallerService.GetInstance();
            var _configurationService = XygeniConfigurationService.GetInstance();
            string apiUrl = _configurationService.GetUrl();
            string token = _configurationService.GetToken();

            if (!await _installerService.IsValidApiUrlAsync(apiUrl))
            {
                return;
            }

            if (!await _installerService.IsValidTokenAsync(apiUrl, token))
            {
                return;
            }

            vs2026_pluginPackage.Instance?.Logger?.Show();

            if(_installerService.InstallationRunning) {
                return;
            }

            if(!_installerService.CheckScannerInstallation()) {
                await _installerService.InstallAsync(apiUrl, token);
            }
            else {
                vs2026_pluginPackage.Instance?.Logger?.Log("Xygeni Scanner is already installed.");
                // The MCP server is installed on its own: it may be missing although the scanner is there.
                await _installerService.InstallMcpLibraryAsync();
            }
            // The connection and license were validated before: time for the daily MCP server version check.
            await CheckMcpUpdatesAsync(force: false);
        }

        /// <summary>
        /// Checks whether a newer MCP server is published and acts on the "MCP server updates" setting: notify
        /// (Update / Ignore this version), update in the background, or nothing. The installer checks at most once
        /// every 24 h across IDEs unless <paramref name="force"/> (the button, which also reports "up to date" and
        /// failures). A failed check is only logged (xygeni/xygeni-product-backlog#2382).
        /// </summary>
        public static async Task CheckMcpUpdatesAsync(bool force)
        {
            var logger = vs2026_pluginPackage.Instance?.Logger;
            var config = XygeniConfigurationService.GetInstance();
            string mode = config.GetMcpUpdateMode();
            if (!force && mode == XygeniConfigurationService.McpUpdateOff)
            {
                // No check, but a download kept as .pending (JAR locked at the time) is still applied on this start (MCP.md).
                try { await XygeniInstallerService.GetInstance().ApplyPendingMcpUpdateAsync().ConfigureAwait(false); }
                catch (Exception ex) { logger?.Error(ex, "Pending MCP server update"); }
                return;
            }

            XygeniInstallerService.McpUpdateCheck result;
            try
            {
                result = await XygeniInstallerService.GetInstance().CheckMcpUpdateAsync(force).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "MCP server update check failed");
                if (force) await ShowMcpMessageAsync($"Could not check for Xygeni MCP server updates: {ex.Message}", MessageBoxImage.Warning);
                return;
            }

            switch (result.Status)
            {
                case XygeniInstallerService.McpUpdateStatus.NotInstalled:
                    if (force) await ShowMcpMessageAsync("The Xygeni MCP server is not installed yet. Save & Install first.", MessageBoxImage.Warning);
                    return;
                case XygeniInstallerService.McpUpdateStatus.Skipped:
                    return;
                case XygeniInstallerService.McpUpdateStatus.Unavailable:
                    if (force) await ShowMcpMessageAsync("Could not check for Xygeni MCP server updates: the published version is not available.", MessageBoxImage.Warning);
                    return;
                case XygeniInstallerService.McpUpdateStatus.UpToDate:
                    if (force) await ShowMcpMessageAsync($"The Xygeni MCP server is up to date ({result.Available}).", MessageBoxImage.Information);
                    return;
            }

            if (!force && config.GetMcpIgnoredVersion() == result.Available)
            {
                logger?.Log($"MCP server {result.Available} is ignored by the user");
                return;
            }
            if (!force && mode == XygeniConfigurationService.McpUpdateAuto)
            {
                await UpdateMcpLibraryAsync();
                return;
            }

            string message = $"A new version of the Xygeni MCP server is available ({result.Installed ?? "unknown"} \u2192 {result.Available}).";
            string available = result.Available;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            McpUpdateInfoBar.Show(message, new[] { ("Update", "update"), ("Ignore this version", "ignore") }, action =>
            {
                if (action == "update")
                {
                    _ = UpdateMcpLibraryAsync();
                }
                else if (action == "ignore")
                {
                    config.SaveMcpIgnoredVersion(available);
                    logger?.Log($"MCP server {available} will not be offered again");
                }
            });
        }

        /// <summary>What the user must do after an MCP update, in their words: the extension only replaces the JAR, the assistant owns the process.</summary>
        public const string McpRestartNote = "Your AI assistant (Copilot, Claude Code, Cursor…) will use it the next time it starts: if it is open now, restart it.";

        /// <summary>The user restarted (or never had running) the AI assistant: the "restart pending" note goes away.</summary>
        public static void DismissMcpRestart()
        {
            XygeniConfigurationService.GetInstance().SaveMcpRestartPending(null);
            XygeniInstallerService.GetInstance().NotifyMcpStateChanged();
        }

        /// <summary>Downloads the published MCP server over the installed one and says when it is loaded.</summary>
        public static async Task UpdateMcpLibraryAsync()
        {
            var logger = vs2026_pluginPackage.Instance?.Logger;
            var installer = XygeniInstallerService.GetInstance();
            try
            {
                logger?.Log("Updating Xygeni MCP server...");
                var outcome = await installer.UpdateMcpLibraryAsync().ConfigureAwait(false);
                string version = installer.GetMcpInstalledVersion() ?? "the published version";
                if (outcome == XygeniInstallerService.McpDownloadOutcome.Installed)
                {
                    // The note stays in Xygeni Settings until the user says OK: the extension only replaced the JAR,
                    // the AI assistant that runs it keeps the old one until restarted.
                    XygeniConfigurationService.GetInstance().SaveMcpRestartPending(version);
                    installer.NotifyMcpStateChanged();
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    McpUpdateInfoBar.Show($"Xygeni MCP server updated to {version}. {McpRestartNote}",
                        new[] { ("Show me how", "how"), ("OK", "ok") }, action =>
                        {
                            if (action == "ok") DismissMcpRestart();
                            else if (action == "how") _ = XygeniSettingsCommand.ShowAsync();
                        });
                }
                else
                {
                    await ShowMcpMessageAsync("The Xygeni MCP server update was downloaded, but your AI assistant (Copilot, Claude Code, Cursor…) "
                        + "is still using the installed version: close it. The update is applied the next time Visual Studio starts.", MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Error updating the MCP server");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                McpUpdateInfoBar.Show($"Failed to update the Xygeni MCP server: {ex.Message}", new[] { ("Retry", "retry") }, action =>
                {
                    if (action == "retry") _ = UpdateMcpLibraryAsync();
                }, warning: true);
            }
        }

        private static async Task ShowMcpMessageAsync(string message, MessageBoxImage image)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            vs2026_pluginPackage.Instance?.Logger?.Log(message);
            MessageBox.Show(message, "Xygeni MCP server", MessageBoxButton.OK, image);
        }

        public static Task RunScan() => RunScan(null);

        /// <summary>Runs one scan type alone (the "Run ... scan only" node of a category in the explorer, #415).</summary>
        public static Task RunScanOnly(string scanType) => RunScan(new[] { scanType });

        private static async Task RunScan(IReadOnlyList<string> only)
        {
            if (!await EnsureLicenseAsync()) return;

            string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();

            if (string.IsNullOrEmpty(rootDir))
            {
                MessageBox.Show("Please open a solution or project first.", "Xygeni Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string scannerPath = XygeniInstallerService.GetInstance().GetScannerInstallationDir();
            if (string.IsNullOrEmpty(scannerPath) || !XygeniInstallerService.GetInstance().IsInstalled)
            {
                MessageBox.Show("Xygeni Scanner is not installed. Please configure it in Xygeni Settings.", "Xygeni Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            vs2026_pluginPackage.Instance?.Logger?.Show();

            XygeniScannerService.GetInstance().RunAnalysisAsync(rootDir, scannerPath, only);
        }

        /// <summary>
        /// Gates an interactive scan/feature on the IDE license. Forces an on-demand
        /// validation if the seat has never been checked, so users cannot exploit the
        /// startup race condition where _isLicenseAvailable is still in its default state.
        /// </summary>
        /// <param name="silent">When true, suppress the MessageBox (use for auto-scan).</param>
        public static async Task<bool> EnsureLicenseAsync(bool silent = false)
        {
            // A refused http URL is not a licence problem: say why, instead of sending the user to their administrator (#1438).
            var settings = XygeniConfigurationService.GetInstance();
            string insecureProblem = InsecureUrl.Problem(settings.GetUrl(), settings.GetAllowInsecureHttp());
            if (insecureProblem != null)
            {
                if (!silent)
                {
                    MessageBox.Show(insecureProblem, "Xygeni", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            LicenseService license;
            try
            {
                license = LicenseService.GetInstance();
            }
            catch
            {
                if (!silent)
                {
                    MessageBox.Show("Xygeni IDE License is not available. Please contact your administrator.",
                        "Xygeni", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            if (!license.LicenseChecked)
            {
                try
                {
                    var config = XygeniConfigurationService.GetInstance();
                    await license.IsValidLicenseAsync(config.GetUrl(), config.GetToken());
                }
                catch
                {
                    // SetLicenseAvailable(false) was already called inside IsValidLicenseAsync on failure.
                }
            }

            if (!license.IsLicenseAvailable)
            {
                if (!silent)
                {
                    MessageBox.Show("Xygeni IDE License is not available. Please contact your administrator.",
                        "Xygeni", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }
            return true;
        }

    }
}