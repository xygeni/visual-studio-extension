using System;
using System.Windows;
using vs2026_plugin.Services;

namespace vs2026_plugin.Commands
{
    public static class XygeniCommands
    {
        public static string ReportSuffix = "scanner.report.json";

        public static async void InstallScanner()
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

        public static void RunScan()    {
            string rootDir = XygeniConfigurationService.GetInstance().GetRootDirectoryAsync().Result;

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

    }
}