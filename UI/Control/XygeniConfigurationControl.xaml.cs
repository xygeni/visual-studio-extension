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

namespace vs2026_plugin.UI.Control
{
    /// <summary>
    /// Interaction logic for XygeniConfigurationControl.xaml
    /// </summary>
    public partial class XygeniConfigurationControl : UserControl
    {
        private const string CollectionPath = "XygeniConfiguration";
        
        private readonly XygeniConfigurationService _configurationService;
        private readonly XygeniInstallerService _installerService;
        private readonly XygeniScannerService _scannerService;

        public XygeniConfigurationControl()
        {
            InitializeComponent();
            _configurationService = XygeniConfigurationService.GetInstance();
            
            _installerService = XygeniInstallerService.GetInstance();
            _installerService.Changed += OnInstallerServiceChanged;

            _scannerService = XygeniScannerService.GetInstance();
            _scannerService.Changed += OnScannerServiceChanged;

            LoadSettings();
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

            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            
            if (_installerService.IsInstalled)
            {
                StatusTxt.Text = "installed";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Green);
                RunScanBtn.IsEnabled = true;
            }
            else if (_installerService.InstallationRunning)
            {
                StatusTxt.Text = "installing...";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Orange);
                RunScanBtn.IsEnabled = false;
            }
            else
            {
                StatusTxt.Text = "not installed";
                StatusTxt.Foreground = new SolidColorBrush(Colors.Red);
                RunScanBtn.IsEnabled = false;
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();            

            _configurationService.SaveUrl(ApiUrlTxt.Text);
            _configurationService.SaveToken(ApiTokenTxt.Password);

            // Show Log Output
            vs2026_pluginPackage.Instance?.Logger?.Show();
            
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
            XygeniCommands.RunScan();
        }

        private void OpenOutputBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            vs2026_pluginPackage.Instance?.Logger?.Show();
        }

        private void OpenXygeniExplorerBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (vs2026_pluginPackage.Instance != null)
            {
                _ = vs2026_pluginPackage.Instance.ShowToolWindowAsync(typeof(vs2026_plugin.UI.Window.XygeniExplorerToolWindow), 0, true, vs2026_pluginPackage.Instance.DisposalToken);
            }
        }

        
    }

    
}
