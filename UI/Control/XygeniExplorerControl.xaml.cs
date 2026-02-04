using System;
using System.Collections.Generic;
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
        public System.Collections.ObjectModel.ObservableCollection<TreeNodeData> Items { get; set; }

        public TreeNodeData(string displayText, string iconPath = null, object tag = null)
        {
            DisplayText = displayText;
            IconPath = iconPath;
            Tag = tag;
            Items = new System.Collections.ObjectModel.ObservableCollection<TreeNodeData>();
        }
    }

    public partial class XygeniExplorerControl : UserControl
    {
        private readonly XygeniIssueService _issueService;
        private readonly XygeniScannerService _scannerService;
        private readonly XygeniInstallerService _installerService;

        public XygeniExplorerControl()
        {
            InitializeComponent();
            _issueService = XygeniIssueService.GetInstance(null);
            _scannerService = XygeniScannerService.GetInstance(null);
            _installerService = XygeniInstallerService.GetInstance(null, null);

            _issueService.IssuesChanged += (s, e) => RefreshTree();
            _scannerService.Changed += (s, e) => RefreshTree();
            
            RefreshTree();
        }

        private string GetCategoryIcon(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;

            string iconFileName = null;
            string lowerCategory = categoryName.ToLower();

            if (lowerCategory.Contains("code") || lowerCategory.Contains("sast") || lowerCategory.Contains("security"))
            {
                iconFileName = "code-sec-new.svg";
            }
            else if (lowerCategory.Contains("secret"))
            {
                iconFileName = "secrets.svg";
            }
            else if (lowerCategory.Contains("misconf"))
            {
                iconFileName = "misconf.svg";
            }
            else if (lowerCategory.Contains("iac") || lowerCategory.Contains("infrastructure"))
            {
                iconFileName = "iacNew.svg";
            }
            else if (lowerCategory.Contains("ci") || lowerCategory.Contains("cd") || lowerCategory.Contains("pipeline"))
            {
                iconFileName = "ci-cd.svg";
            }
            else if (lowerCategory.Contains("open") || lowerCategory.Contains("source"))
            {
                iconFileName = "open-source.svg";
            }
            else if (lowerCategory.Contains("depend") || lowerCategory.Contains("vuln"))
            {
                iconFileName = "dependencies.svg";
            }

            if (iconFileName != null)
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(baseDir, "media", "icons", iconFileName);
                return iconPath;
            }
            return null;
        }

        private string GetSeverityIcon(string severity)
        {
            if (string.IsNullOrEmpty(severity)) return null;

            string iconFileName = null;
            string lowerSeverity = severity.ToLower();

            if (lowerSeverity == "critical")
            {
                iconFileName = "critical.svg";
            }
            else if (lowerSeverity == "high")
            {
                iconFileName = "high.svg";
            }
            else if (lowerSeverity == "medium")
            {
                iconFileName = "warning.svg";
            }
            else if (lowerSeverity == "low")
            {
                iconFileName = "low.svg";
            }
            else if (lowerSeverity == "info")
            {
                iconFileName = "info.svg";
            }

            if (iconFileName != null)
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(baseDir, "media", "icons", iconFileName);
                return iconPath;
            }
            return null;
        }

        private void RefreshTree()
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                ExplorerTree.Items.Clear();

                // 1. Scan Executions
                var scansRoot = new TreeViewItem { Header = "Scan Executions", IsExpanded = true };
                var scans = _scannerService.GetScans();
                foreach (var scan in scans.OrderByDescending(s => s.Timestamp))
                {
                    scansRoot.Items.Add(new TreeViewItem 
                    { 
                        Header = $"[{scan.Timestamp:HH:mm:ss}] {scan.Status} {scan.Summary}",
                        Tag = scan
                    });
                }
                ExplorerTree.Items.Add(scansRoot);

                // 2. Issues by Category
                var issuesRoot = new TreeViewItem { Header = "Issues by Category", IsExpanded = true };
                var issues = _issueService.GetIssues();
                var categories = issues.GroupBy(i => i.CategoryName).OrderBy(g => g.Key);

                foreach (var group in categories)
                {
                    // Create category node with icon
                    string categoryIcon = GetCategoryIcon(group.Key);
                    var catNodeData = new TreeNodeData(
                        $"{group.Key} ({group.Count()})", 
                        categoryIcon
                    );
                    
                    var catItem = new TreeViewItem 
                    { 
                        Header = catNodeData,
                        IsExpanded = false 
                    };
                    
                    foreach (var issue in group.OrderBy(i => i.GetSeverityLevel()))
                    {
                        // Create issue node with severity icon
                        string severityIcon = GetSeverityIcon(issue.Severity);
                        var issueNodeData = new TreeNodeData(
                            $"[{issue.Severity}] {issue.Type} - {Path.GetFileName(issue.File)}:{issue.BeginLine}",
                            severityIcon,
                            issue
                        );
                        
                        var issueItem = new TreeViewItem 
                        { 
                            Header = issueNodeData,
                            Tag = issue
                        };
                        catItem.Items.Add(issueItem);
                    }
                    issuesRoot.Items.Add(catItem);
                }
                ExplorerTree.Items.Add(issuesRoot);
            });
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshTree();
        }

        private void RunScanBtn_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            XygeniCommands.RunScan();
        }
        
        private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ExplorerTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is IXygeniIssue issue)
            {
                // Navigate to issue
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                         // Show Details View
                         await IssueDetailsService.GetInstance(vs2026_pluginPackage.Instance, vs2026_pluginPackage.Instance?.Logger)
                            .ShowIssueDetailsAsync(issue);

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
                            else
                            {
                                vs2026_pluginPackage.Instance?.Logger?.Log($"File not found: {filePath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        vs2026_pluginPackage.Instance?.Logger?.Error(ex,"Error opening file: " + ex.Message);
                        MessageBox.Show($"Error opening file: {ex.Message}", "Xygeni Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
        }
    }
}
