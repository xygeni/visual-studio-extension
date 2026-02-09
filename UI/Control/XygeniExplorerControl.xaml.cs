using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using vs2026_plugin.Services;
using vs2026_plugin.Models;
using vs2026_plugin.Commands;
using System.Reflection;


namespace vs2026_plugin.UI.Control
{
    // Helper class to hold tree node data with icon
    public class TreeNodeData
    {   
        public string IconPath { get; set; }
        public string DisplayText { get; set; }
        public object Tag { get; set; }

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
        private readonly XygeniInstallerService _installerService;

        private readonly XygeniExplorerViewModel _vm;

        public XygeniExplorerControl()
        {
            InitializeComponent();

            var issueService = XygeniIssueService.GetInstance();
            var scannerService = XygeniScannerService.GetInstance();

            _vm = new XygeniExplorerViewModel(issueService, scannerService);

            DataContext = _vm;

            _vm.IssueSelected += OnIssueSelected;
            issueService.IssuesChanged += OnIssuesChanged;
            
        }

        private async void OnIssuesChanged(object sender, EventArgs e)
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
                if (e.NewValue is TreeViewItem item && item.Header is TreeNodeData nodeData)
                {
                    _vm.SelectedItem = nodeData;
                }
                else
                {
                    _vm.SelectedItem = null;
                }
            }
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
