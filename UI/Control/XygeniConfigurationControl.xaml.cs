using System.Windows.Controls;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Shell;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell.Interop;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using Newtonsoft.Json.Linq;

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

            SkipSslVerifyPrompt.Changed += OnSkipSslVerifyChanged;

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

            SkipSslVerifyChk.IsChecked = _configurationService.GetSkipSslVerify();
            SkipUpdateChk.IsChecked = _configurationService.GetSkipUpdate();
            VerboseChk.IsChecked = _configurationService.GetVerbose();
            AdditionalGlobalOptionsTxt.Text = _configurationService.GetAdditionalGlobalOptions();
            // Open the advanced options while any of them is on, so what changes the scanner is visible.
            ScannerOptionsExpander.IsExpanded = (SkipUpdateChk.IsChecked ?? false) || (VerboseChk.IsChecked ?? false)
                || !string.IsNullOrEmpty(AdditionalGlobalOptionsTxt.Text);

            _ = RefreshScanConfigAsync();

            string mcpUpdateMode = _configurationService.GetMcpUpdateMode();
            foreach (ComboBoxItem item in McpUpdateModeCmb.Items)
            {
                if ((item.Tag as string) == mcpUpdateMode) McpUpdateModeCmb.SelectedItem = item;
            }

            UpdateStatusText();
        }

        // Path, installed version and what the last update check found (xygeni/xygeni-product-backlog#2382).
        private void UpdateMcpStatus()
        {
            McpPathTxt.Text = _installerService.GetMcpLibraryPath();
            bool installed = _installerService.IsMcpLibraryInstalled();
            CheckMcpUpdatesBtn.IsEnabled = installed;
            CopyMcpConfigBtn.IsEnabled = installed && _installerService.IsInstalled;
            if (!installed)
            {
                McpStatusTxt.Text = "not installed (installed with the scanner by Save & Install)";
                return;
            }
            string version = _installerService.GetMcpInstalledVersion() ?? "unknown (installed before version tracking; it is updated on the next check)";
            var check = _installerService.LastMcpUpdateCheck;
            string updates = check == null ? "not checked yet in this session"
                : check.UpdateAvailable ? $"version {check.Available} is available" : "up to date";
            McpStatusTxt.Text = $"Installed version: {version}. Updates: {updates}.";
        }

        private void McpUpdateModeCmb_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsLoaded) return;
            string mode = (McpUpdateModeCmb.SelectedItem as ComboBoxItem)?.Tag as string;
            if (mode != null) _configurationService.SaveMcpUpdateMode(mode);
        }

        private void CheckMcpUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (vs2026_pluginPackage.Instance != null)
            {
                _ = vs2026_pluginPackage.Instance.ShowOutputPaneAsync();
            }
            _ = XygeniCommands.CheckMcpUpdatesAsync(force: true);
        }

        // The same configuration the IntelliJ and VS Code plugins generate in their MCP Setup view.
        private void CopyMcpConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string token = _configurationService.GetToken();
            var config = new JObject
            {
                ["servers"] = new JObject
                {
                    ["xygeni-mcp-server"] = new JObject
                    {
                        ["timeout"] = 60,
                        ["type"] = "stdio",
                        ["command"] = "java",
                        ["args"] = new JArray("-jar", _installerService.GetMcpLibraryPath(), "--scannerPath=" + _installerService.GetScannerInstallationDir()),
                        ["env"] = new JObject
                        {
                            ["JAVA_HOME"] = Environment.GetEnvironmentVariable("JAVA_HOME") ?? "$JAVA_HOME",
                            ["XYGENI_URL"] = string.IsNullOrEmpty(_configurationService.GetUrl()) ? "$XYGENI_URL" : _configurationService.GetUrl(),
                            ["XYGENI_TOKEN"] = string.IsNullOrEmpty(token) ? "$XYGENI_TOKEN" : token
                        }
                    }
                }
            };
            try
            {
                Clipboard.SetText(config.ToString(Newtonsoft.Json.Formatting.Indented));
                MessageBox.Show("MCP server configuration copied to the clipboard. Paste it into your MCP client configuration (mcp.json)."
                    + (string.IsNullOrEmpty(token) ? " Replace $XYGENI_TOKEN with your token." : " It contains your Xygeni token: keep that file private."),
                    "Xygeni MCP server", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy the configuration: {ex.Message}", "Xygeni MCP server", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnSkipSslVerifyChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SkipSslVerifyChk.IsChecked = _configurationService.GetSkipSslVerify();
            });
        }

        // Click (not Checked/Unchecked): only the user's own clicks, not the programmatic refreshes above.
        private void SkipSslVerifyChk_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (SkipSslVerifyChk.IsChecked ?? false)
            {
                SkipSslVerifyChk.IsChecked = SkipSslVerifyPrompt.ConfirmAndEnable();
            }
            else
            {
                SkipSslVerifyPrompt.SetEnabled(false);
            }
        }

        private void ScannerOptionChk_Changed(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsLoaded) return;
            _configurationService.SaveSkipUpdate(SkipUpdateChk.IsChecked ?? false);
            _configurationService.SaveVerbose(VerboseChk.IsChecked ?? false);
        }

        private void AdditionalGlobalOptionsTxt_LostFocus(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _configurationService.SaveAdditionalGlobalOptions(AdditionalGlobalOptionsTxt.Text);
        }

        private void UpdateStatusText()
        {
            UpdateMcpStatus();
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

        // ---- Per-project scan configuration (xygeni/xygeni-product-backlog#415) ----

        private string _scanConfigRoot;

        private void ScanConfigExpander_Expanded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = RefreshScanConfigAsync();
        }

        /// <summary>Reads `.xygeni/ide.json` of the open solution or folder and paints the rows; the checkboxes never hold state of their own.</summary>
        private async Task RefreshScanConfigAsync()
        {
            string root = null;
            try { root = await _configurationService.GetRootDirectoryAsync(); } catch (Exception) { }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _scanConfigRoot = string.IsNullOrEmpty(root) ? null : root;
            var config = ScanConfig.Load(_scanConfigRoot);
            bool hasProject = _scanConfigRoot != null;
            foreach (var button in new[] { SelectAllScanTypesBtn, ClearScanTypesBtn, RestoreScanDefaultsBtn, OpenScanConfigBtn })
            {
                button.IsEnabled = hasProject;
            }
            int switched = config.Scans.Count(type => type != "suspectdeps");
            string optionsNote = config.Options.Count == 0 ? "" : $", {config.Options.Count} option{(config.Options.Count > 1 ? "s" : "")}";
            ScanConfigExpander.Header = $"Scan configuration ({switched}/{ScanConfig.SwitchableScanners.Count()} scanners, {config.OnSave.Count} on save{optionsNote})";
            ScanConfigProjectTxt.Text = !hasProject ? "Open a solution or folder to configure its scan."
                : config.IsDefault ? "Defaults (nothing written yet) for " + _scanConfigRoot
                : "Saved in " + ScanConfig.PathFor(_scanConfigRoot);

            ScanConfigRows.Children.Clear();
            ScanConfigRows.RowDefinitions.Clear();
            AddScanConfigHeader();
            int row = 1;
            foreach (var scanner in ScanConfig.SwitchableScanners)
            {
                AddScanConfigRow(scanner, config, hasProject, row++);
            }

            ExcludesList.Items.Clear();
            foreach (string pattern in config.Excludes) ExcludesList.Items.Add(pattern);
            OptionsList.Items.Clear();
            foreach (string option in config.Options) OptionsList.Items.Add(option);
            OptionTxt.IsEnabled = hasProject;
            RemoveOptionBtn.IsEnabled = false;
            ValidateOption();
            foreach (var control in new UIElement[] { ExcludePatternTxt, AddExcludePatternBtn, AddExcludeFolderBtn })
            {
                control.IsEnabled = hasProject;
            }
            RemoveExcludeBtn.IsEnabled = false;
        }

        // ── Scanner options (#415): a command-line-like field with autocompletion and validation ───────────────────

        private static readonly Brush OptionErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));

        private void OptionTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ValidateOption();
            ShowOptionSuggestions();
        }

        /// <summary>The hint shows the description of a valid option, or the reason it is refused, in red; Add follows.</summary>
        private void ValidateOption()
        {
            string text = OptionTxt.Text ?? string.Empty;
            if (text.Trim().Length == 0)
            {
                OptionHintTxt.Text = " ";
                OptionHintTxt.ClearValue(TextBlock.ForegroundProperty);
                AddOptionBtn.IsEnabled = false;
                return;
            }
            var result = ScanOptions.Validate(text);
            if (result.Ok)
            {
                OptionHintTxt.Text = result.Spec.Description;
                OptionHintTxt.ClearValue(TextBlock.ForegroundProperty);
            }
            else
            {
                OptionHintTxt.Text = result.Error;
                if (result.Pending) OptionHintTxt.ClearValue(TextBlock.ForegroundProperty); else OptionHintTxt.Foreground = OptionErrorBrush;
            }
            AddOptionBtn.IsEnabled = result.Ok && _scanConfigRoot != null;
        }

        private void ShowOptionSuggestions()
        {
            var suggestions = ScanOptions.Suggestions(OptionTxt.Text);
            OptionSuggestions.Items.Clear();
            foreach (string suggestion in suggestions) OptionSuggestions.Items.Add(suggestion);
            if (suggestions.Count > 0) OptionSuggestions.SelectedIndex = 0;
            OptionPopup.IsOpen = suggestions.Count > 0 && OptionTxt.IsKeyboardFocusWithin;
        }

        private void AcceptOptionSuggestion()
        {
            if (!(OptionSuggestions.SelectedItem is string suggestion)) return;
            OptionTxt.Text = suggestion;
            OptionTxt.CaretIndex = suggestion.Length;
            OptionTxt.Focus();
        }

        private void OptionTxt_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool popupOpen = OptionPopup.IsOpen && OptionSuggestions.Items.Count > 0;
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                if (!popupOpen) { ShowOptionSuggestions(); popupOpen = OptionPopup.IsOpen; }
                if (popupOpen)
                {
                    int count = OptionSuggestions.Items.Count;
                    int next = (OptionSuggestions.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
                    OptionSuggestions.SelectedIndex = next;
                    OptionSuggestions.ScrollIntoView(OptionSuggestions.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && popupOpen)
            {
                AcceptOptionSuggestion();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // Enter picks the suggestion while the typed option is not complete; a complete one is added.
                if (popupOpen && !ScanOptions.Validate(OptionTxt.Text).Ok) AcceptOptionSuggestion(); else AddOption();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                OptionPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void OptionTxt_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // A click on a suggestion moves the focus into the popup: keep it open until that click is handled.
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!OptionSuggestions.IsKeyboardFocusWithin && !OptionTxt.IsKeyboardFocusWithin) OptionPopup.IsOpen = false;
            }));
        }

        private void OptionSuggestions_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var item = ItemsControl.ContainerFromElement(OptionSuggestions, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item == null) return;
            OptionSuggestions.SelectedItem = item.Content;
            AcceptOptionSuggestion();
            e.Handled = true;
        }

        private void AddOptionBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            AddOption();
        }

        private void AddOption()
        {
            if (_scanConfigRoot == null) return;
            var result = ScanOptions.Validate(OptionTxt.Text);
            if (!result.Ok)
            {
                OptionHintTxt.Text = result.Error;
                OptionHintTxt.Foreground = OptionErrorBrush;
                return;
            }
            OptionPopup.IsOpen = false;
            OptionTxt.Text = string.Empty;
            SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithScanOption(result.Option));
        }

        private void OptionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RemoveOptionBtn.IsEnabled = OptionsList.SelectedItem != null;
        }

        private void RemoveOptionBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(OptionsList.SelectedItem is string option) || _scanConfigRoot == null) return;
            SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithoutScanOption(ScanOptions.NameOf(option)));
        }

        private void ExcludesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RemoveExcludeBtn.IsEnabled = ExcludesList.SelectedItem != null;
        }

        // Types a glob pattern (scanner syntax: * file wildcard, ** any depth, "path:" prefix for a sub-project).
        private void AddExcludePatternBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string pattern = (ExcludePatternTxt.Text ?? string.Empty).Trim();
            if (pattern.Length == 0) return;
            if (pattern.Contains(","))
            {
                MessageBox.Show("One pattern at a time (no commas).", "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ExcludePatternTxt.Text = string.Empty;
            SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithExclude(pattern));
        }

        // Picks a folder of the project and excludes everything under it (<folder>/**).
        private void AddExcludeFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_scanConfigRoot == null) return;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Folder to exclude from the Xygeni scan (everything under it is left out of every scan)",
                SelectedPath = _scanConfigRoot,
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string pattern = ScanConfig.ExcludePatternForFolder(_scanConfigRoot, dialog.SelectedPath);
                if (pattern == null)
                {
                    MessageBox.Show("Only folders inside the project can be excluded.", "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithExclude(pattern));
            }
        }

        private void RemoveExcludeBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string pattern = ExcludesList.SelectedItem as string;
            if (pattern == null) return;
            SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithoutExclude(pattern));
        }

        private void AddScanConfigHeader()
        {
            ScanConfigRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var full = new TextBlock { Text = "Full scan", Opacity = 0.8, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
            var onSave = new TextBlock { Text = "On save", Opacity = 0.8, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var header in new[] { full, onSave })
            {
                header.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey);
            }
            Grid.SetRow(full, 0); Grid.SetColumn(full, 2);
            Grid.SetRow(onSave, 0); Grid.SetColumn(onSave, 3);
            ScanConfigRows.Children.Add(full);
            ScanConfigRows.Children.Add(onSave);
        }

        private void AddScanConfigRow(ScanConfig.Scanner scanner, ScanConfig config, bool hasProject, int row)
        {
            ScanConfigRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool inFullScan = config.Scans.Contains(scanner.Key);

            var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center, ToolTip = scanner.Description };
            try
            {
                string extensionDir = Path.GetDirectoryName(typeof(vs2026_pluginPackage).Assembly.Location);
                icon.Source = new BitmapImage(new Uri(Path.Combine(extensionDir, "media", "icons", scanner.Icon)));
            }
            catch (Exception) { }
            var label = new TextBlock { Text = scanner.Label, ToolTip = scanner.Description, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            label.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey);

            var full = new CheckBox { IsChecked = inFullScan, IsEnabled = hasProject, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = scanner.Description, Style = (Style)FindResource("SwitchCheckBox") };
            full.Click += (s, e) => SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithScanType(scanner.Key, full.IsChecked ?? false));

            UIElement onSaveControl;
            if (scanner.OnSave)
            {
                var onSave = new CheckBox
                {
                    Style = (Style)FindResource("SwitchCheckBox"),
                    IsChecked = config.OnSave.Contains(scanner.Key),
                    IsEnabled = hasProject && inFullScan,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = inFullScan ? scanner.Description : "Runs on save only while it is in the full scan."
                };
                // Dimmed, not disabled, while Auto Scan on Save is off: a click switches it on.
                bool autoScanOn = _configurationService.GetAutoScan();
                if (!autoScanOn && inFullScan) { onSave.Opacity = 0.5; onSave.ToolTip = "Auto Scan on Save is off: switching this on turns it on."; }
                onSave.Click += (s, e) =>
                {
                    var next = ScanConfig.Load(_scanConfigRoot).WithOnSaveType(scanner.Key, onSave.IsChecked ?? false);
                    SaveScanConfig(next);
                    // The on-save column and Auto Scan on Save help each other (#415): switching a type on turns the
                    // check on; switching the last one off turns it off, as nothing would run on save.
                    if (next.OnSave.Contains(scanner.Key) && !_configurationService.GetAutoScan()) { _configurationService.SaveAutoScan(true); AutoScanChk.IsChecked = true; }
                    else if (next.OnSave.Count == 0 && _configurationService.GetAutoScan()) { _configurationService.SaveAutoScan(false); AutoScanChk.IsChecked = false; }
                    _ = RefreshScanConfigAsync();
                };
                onSaveControl = onSave;
            }
            else
            {
                var dash = new TextBlock { Text = "\u2014", Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = scanner.Label + " scans the whole project, not just the changed files: not available on save." };
                dash.SetResourceReference(TextBlock.ForegroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey);
                onSaveControl = dash;
            }

            Grid.SetRow(icon, row); Grid.SetColumn(icon, 0);
            Grid.SetRow(label, row); Grid.SetColumn(label, 1);
            Grid.SetRow(full, row); Grid.SetColumn(full, 2);
            Grid.SetRow(onSaveControl, row); Grid.SetColumn(onSaveControl, 3);
            ScanConfigRows.Children.Add(icon);
            ScanConfigRows.Children.Add(label);
            ScanConfigRows.Children.Add(full);
            ScanConfigRows.Children.Add(onSaveControl);
        }

        private void SaveScanConfig(ScanConfig config)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_scanConfigRoot == null) return;
            try
            {
                ScanConfig.Save(_scanConfigRoot, config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the scan configuration: {ex.Message}", "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _ = RefreshScanConfigAsync();
        }

        private void SelectAllScanTypesBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithAllScanTypes(true));
        }

        private void ClearScanTypesBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SaveScanConfig(ScanConfig.Load(_scanConfigRoot).WithAllScanTypes(false));
        }

        private void RestoreScanDefaultsBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SaveScanConfig(ScanConfig.Defaults());
        }

        // Opens `.xygeni/ide.json`, writing the current (maybe default) configuration first when it does not exist.
        private void OpenScanConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_scanConfigRoot == null) return;
            try
            {
                string path = ScanConfig.PathFor(_scanConfigRoot);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, ScanConfig.Load(_scanConfigRoot).ToJson());
                }
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the scan configuration: {ex.Message}", "Xygeni Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
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
            bool autoScan = AutoScanChk.IsChecked ?? false;
            _configurationService.SaveAutoScan(autoScan);
            // Switched on with nothing chosen for on save: start from the default on-save types (#415).
            if (autoScan && _scanConfigRoot != null && ScanConfig.Load(_scanConfigRoot).OnSave.Count == 0)
            {
                try { ScanConfig.Save(_scanConfigRoot, ScanConfig.Load(_scanConfigRoot).WithDefaultOnSave()); } catch (Exception) { }
            }
            _ = RefreshScanConfigAsync();
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

            StatusTxt.Text = "Connecting…";
            StatusTxt.Foreground = new SolidColorBrush(Colors.Orange);
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
                        StatusTxt.Text = _installerService.LastConnectionProblem ?? "API Token not valid";
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
                    // The connection and license were just validated: time for the daily MCP server version check.
                    await XygeniCommands.CheckMcpUpdatesAsync(force: false);
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
