using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using vs2026_plugin.Services;
using vs2026_plugin.Models;
using vs2026_plugin.Commands;
using System.Reflection;
using System.Windows.Media.Imaging;


namespace vs2026_plugin.UI.Control
{
    // Helper class to hold tree node data with icon
    public class TreeNodeData
    {   
        public string IconPath { get; set; }
        public string DisplayText { get; set; }
        public object Tag { get; set; }
        public bool IsExpanded { get; set; }

        public ObservableCollection<TreeNodeData> Items { get; } 
            = new ObservableCollection<TreeNodeData>();

        public TreeNodeData(string displayText, string iconPath = null, object tag = null)
        {
            DisplayText = displayText;
            IconPath = iconPath;
            Tag = tag;
        }
    }


    public partial class XygeniExplorerControl : UserControl
    {
        private readonly XygeniIssueService _issueService;
        private readonly XygeniScannerService _scannerService;

        private readonly XygeniExplorerViewModel _vm;

        public XygeniExplorerControl()
        {
            InitializeComponent();
            SetRunButtonIcon();

            _issueService = XygeniIssueService.GetInstance();
            _scannerService = XygeniScannerService.GetInstance();

            _vm = new XygeniExplorerViewModel(_issueService, _scannerService);

            DataContext = _vm;

            _vm.IssueSelected += OnIssueSelected;
            _issueService.IssuesChanged += OnIssuesChanged;
            _scannerService.Changed += OnScannerChanged;

            _vm.Refresh();
        }

        private void SetRunButtonIcon()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string iconPath = Path.Combine(baseDir, "media", "icons", "play.png");

            if (File.Exists(iconPath))
            {
                RunScanIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }

        private async void OnIssuesChanged(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _vm.Refresh();
        }

        private async void OnScannerChanged(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _vm.Refresh();
        }

              

        private void RunScanBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            XygeniCommands.RunScan();
        }

        private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_vm != null)
            {
                if (e.NewValue is TreeNodeData nodeData)
                {
                    _vm.SelectedItem = nodeData;
                }
                else
                {
                    _vm.SelectedItem = null;
                }
            }
        }

        private void ExplorerTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_vm == null) return;

            var clickedContainer = FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (clickedContainer?.DataContext is TreeNodeData nodeData &&
                nodeData.Tag is IXygeniIssue)
            {
                // Force issue-row selection to avoid the category container keeping selection.
                clickedContainer.IsSelected = true;
                clickedContainer.Focus();
                e.Handled = true;
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T typedParent)
                {
                    return typedParent;
                }

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        
        private void OnIssueSelected(IXygeniIssue issue)
        {
           
            // Navigate to issue
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    // Show Details View
                    await IssueDetailsService.GetInstance().ShowIssueDetailsAsync(issue);

                    // Open file
                    var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                    if (dte != null && !string.IsNullOrEmpty(issue.File))
                    {
                        string filePath = issue.File;
                        if (!Path.IsPathRooted(filePath))
                        {
                            string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                            if (!string.IsNullOrEmpty(rootDir))
                            {
                                filePath = Path.Combine(rootDir, filePath);
                            }
                        }

                        if (File.Exists(filePath))
                        {
                            var window = dte.ItemOperations.OpenFile(filePath);
                            if (window != null && issue.BeginLine > 0)
                            {
                                var selection = (EnvDTE.TextSelection)dte.ActiveDocument.Selection;
                                selection.GotoLine(issue.BeginLine, true);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                     vs2026_pluginPackage.Instance?.Logger?.Error(ex,"Error opening file: " + ex.Message);
                }
            });
                
            
        }
    }
}
