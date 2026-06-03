using System.Windows.Controls;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Shell;
using System.Windows;
using Microsoft.VisualStudio.Shell.Interop;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Windows.Media;

using vs2026_plugin.Services;
using vs2026_plugin.Commands;
using vs2026_plugin.Models;

namespace vs2026_plugin.UI.Control
{
    /// <summary>
    /// Interaction logic for XygeniConfigurationControl.xaml
    /// </summary>
    public partial class XygeniConfigurationControl : UserControl
    {
        private readonly XygeniConfigurationService _configurationService;
        private readonly XygeniInstallerService _installerService;
        private readonly XygeniScannerService _scannerService;
        private readonly LicenseService _licenseService;

        public XygeniConfigurationControl()
        {
            InitializeComponent();
            _configurationService = XygeniConfigurationService.GetInstance();

            _installerService = XygeniInstallerService.GetInstance();
            _installerService.Changed += OnInstallerServiceChanged;

            _scannerService = XygeniScannerService.GetInstance();
            _scannerService.Changed += OnScannerServiceChanged;

            _licenseService = LicenseService.GetInstance();
            _licenseService.Changed += OnLicenseServiceChanged;

            LoadSettings();
        }

        private void OnLicenseServiceChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateStatusText();
            });
        }

        private void OnInstallerServiceChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateStatusText();
            });
        }

        private void OnScannerServiceChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateStatusText();
            });
        }

        private void LoadSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ApiUrlTxt.Text = _configurationService.GetUrl();
            ApiTokenTxt.Password = _configurationService.GetToken();
            var proxySettings = _configurationService.GetProxySettings();

            ProxyProtocolTxt.Text = proxySettings.Protocol;
            ProxyHostTxt.Text = proxySettings.Host;
            ProxyPortTxt.Text = proxySettings.Port.HasValue ? proxySettings.Port.Value.ToString() : string.Empty;
            ProxyAuthenticationTxt.Text = proxySettings.Authentication;
            ProxyUsernameTxt.Text = proxySettings.Username;
            ProxyPasswordTxt.Password = proxySettings.Password;
            ProxyNonProxyHostsTxt.Text = proxySettings.NonProxyHosts;

            AutoScanChk.IsChecked = _configurationService.GetAutoScan();

            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            bool licensed = _licenseService.IsLicenseAvailable;
            bool isFree = _licenseService.IsFreeLicense();

            // Free plan: Auto Scan on Save relies on --incremental, which the CLI Free edition
            // rejects. Disable the toggle and offer an upgrade link, but keep manual scans usable.
            UpgradeMsg.Visibility = (licensed && isFree) ? Visibility.Visible : Visibility.Collapsed;

            if (!licensed && _licenseService.LicenseChecked)
            {
                StatusTxt.Text = "IDE License not available";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Red);
                RunScanBtn.IsEnabled = false;
                AutoScanChk.IsEnabled = false;
                return;
            }

            if (_installerService.IsInstalled)
            {
                StatusTxt.Text = "installed";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Green);
                RunScanBtn.IsEnabled = true;
                AutoScanChk.IsEnabled = !isFree;
            }
            else if (_installerService.InstallationRunning)
            {
                StatusTxt.Text = "installing...";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Orange);
                RunScanBtn.IsEnabled = false;
                AutoScanChk.IsEnabled = false;
            }
            else
            {
                StatusTxt.Text = "not installed";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Red);
                RunScanBtn.IsEnabled = false;
                AutoScanChk.IsEnabled = false;
            }
        }

        private void OpenUpgradeLink_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                System.Diagnostics.Process.Start("https://xygeni.io/pricing/");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open Xygeni pricing page: {ex.Message}");
            }
        }

        private void AutoScanChk_Changed(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsLoaded) return;
            _configurationService.SaveAutoScan(AutoScanChk.IsChecked ?? false);
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int? proxyPort = null;
            if (!string.IsNullOrWhiteSpace(ProxyPortTxt.Text))
            {
                if (!int.TryParse(ProxyPortTxt.Text.Trim(), out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
                {
                    MessageBox.Show("Proxy port must be a number between 1 and 65535.", "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                proxyPort = parsedPort;
            }

            var proxySettings = new ProxySettings
            {
                Protocol = NormalizeInput(ProxyProtocolTxt.Text),
                Host = NormalizeInput(ProxyHostTxt.Text),
                Port = proxyPort,
                Authentication = NormalizeInput(ProxyAuthenticationTxt.Text),
                Username = NormalizeInput(ProxyUsernameTxt.Text),
                Password = ProxyPasswordTxt.Password ?? string.Empty,
                NonProxyHosts = NormalizeInput(ProxyNonProxyHostsTxt.Text)
            };

            _configurationService.SaveProxySettings(proxySettings);
            _configurationService.SaveUrl(ApiUrlTxt.Text);
            _configurationService.SaveToken(ApiTokenTxt.Password);

            // Show Log Output
            if (vs2026_pluginPackage.Instance != null)
            {
                _ = vs2026_pluginPackage.Instance.ShowOutputPaneAsync();
            }
            
            // Run Installation
            string apiUrl = ApiUrlTxt.Text;
            string token = ApiTokenTxt.Password;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await _installerService.IsValidApiUrlAsync(apiUrl))
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        StatusTxt.Text = "API URL not valid";
                        RunScanBtn.IsEnabled = false;
                        return;
                    }

                    if (!await _installerService.IsValidTokenAsync(apiUrl, token))
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        StatusTxt.Text = "API Token not valid";
                        RunScanBtn.IsEnabled = false;
                        return;
                    }

                    if (!await LicenseService.GetInstance().IsValidLicenseAsync(apiUrl, token))
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        StatusTxt.Text = "IDE License not available";
                        RunScanBtn.IsEnabled = false;
                        AutoScanChk.IsEnabled = false;
                        MessageBox.Show("No Xygeni IDE License available. Please contact your administrator for more details.",
                            "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    await _installerService.InstallAsync(apiUrl, token);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                }
                catch (Exception ex)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    MessageBox.Show($"Settings saved, but installation failed: {ex.Message}", "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void RunScanBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = XygeniCommands.RunScan();
        }

        private void OpenOutputBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (vs2026_pluginPackage.Instance != null)
            {
                _ = vs2026_pluginPackage.Instance.ShowOutputPaneAsync();
            }
        }

        private void OpenXygeniExplorerBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (vs2026_pluginPackage.Instance != null)
            {
                _ = vs2026_pluginPackage.Instance.ShowToolWindowAsync(typeof(vs2026_plugin.UI.Window.XygeniExplorerToolWindow), 0, true, vs2026_pluginPackage.Instance.DisposalToken);
            }
        }

        private string NormalizeInput(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        
    }

    
}
