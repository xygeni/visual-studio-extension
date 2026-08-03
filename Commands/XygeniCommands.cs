using System;
using System.Windows;
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
                _installerService.InstallAsync(apiUrl, token);
            }
            else {
                vs2026_pluginPackage.Instance?.Logger?.Log("Xygeni Scanner is already installed.");
            }
        }

        public static async Task RunScan()
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

            XygeniScannerService.GetInstance().RunAnalysisAsync(rootDir, scannerPath);
        }

        /// <summary>
        /// Gates an interactive scan/feature on the IDE license. Forces an on-demand
        /// validation if the seat has never been checked, so users cannot exploit the
        /// startup race condition where _isLicenseAvailable is still in its default state.
        /// </summary>
        /// <param name="silent">When true, suppress the MessageBox (use for auto-scan).</param>
        public static async Task<bool> EnsureLicenseAsync(bool silent = false)
        {
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