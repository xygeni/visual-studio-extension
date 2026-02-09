using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using System.IO;
using System.Linq;
using System;
using System.Reflection;
using vs2026_plugin.Services;
using vs2026_plugin.Models;
using vs2026_plugin.UI.Control;
using Microsoft.VisualStudio.Shell;

namespace vs2026_plugin.UI.Control
{
    public class XygeniExplorerViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TreeViewItem> RootItems { get; } 
            = new ObservableCollection<TreeViewItem>();

        private TreeViewItem _selectedItem;
        public TreeViewItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged(nameof(SelectedItem));
                OnSelectedItemChanged(value);
            }
        }

        private readonly XygeniIssueService _issueService;
        private readonly XygeniScannerService _scannerService;

        public event Action<IXygeniIssue> IssueSelected;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public XygeniExplorerViewModel(
            XygeniIssueService issueService,
            XygeniScannerService scannerService)
        {
            _issueService = issueService;
            _scannerService = scannerService;
        }

        public void Refresh()
        {
             ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RootItems.Clear();
                vs2026_pluginPackage.Instance?.Logger?.Log("Refreshing Xygeni Explorer");

                // 1. Scan Executions
                var scansRoot = new TreeViewItem { Header = "Scan Executions", IsExpanded = true };

                foreach (var scan in _scannerService.GetScans()
                            .OrderByDescending(s => s.Timestamp))
                {
                    scansRoot.Items.Add(new TreeViewItem 
                        { 
                            Header = $"[{scan.Timestamp:HH:mm:ss}] {scan.Status} {scan.Summary}",
                            Tag = scan
                        });
                }

                RootItems.Add(scansRoot);

                // 2. Issues by Category
                var issuesRoot = new TreeViewItem { Header = "Issues by Category", IsExpanded = true };

                var categories = _issueService.GetIssues()
                    .GroupBy(i => i.CategoryName)
                    .OrderBy(g => g.Key);

                foreach (var group in categories)
                {
                    vs2026_pluginPackage.Instance?.Logger?.Log("Refreshing Xygeni category: " + group.Key);
                    var catNode = new TreeViewItem 
                    {
                        Header = new TreeNodeData(
                            $"{group.Key} ({group.Count()})",
                            GetCategoryIcon(group.Key)
                        ),
                        Tag = group
                    };

                    foreach (var issue in group.OrderBy(i => i.GetSeverityLevel()))
                    {
                        vs2026_pluginPackage.Instance?.Logger?.Log("Refreshing Xygeni item: " + issue.Type);
                    
                        var issueNodeData = new TreeNodeData(
                            $"[{issue.Severity}] {issue.Type} - {Path.GetFileName(issue.File)}:{issue.BeginLine}",
                            GetSeverityIcon(issue.Severity),
                            issue
                        );
                        var issueItem = new TreeViewItem 
                        { 
                            Header = issueNodeData,
                            Tag = issue
                        };
                        catNode.Items.Add(issueItem);
                    }

                    issuesRoot.Items.Add(catNode);
                }

                 vs2026_pluginPackage.Instance?.Logger?.Log("issuesRoot: " + issuesRoot.Items.Count);

                RootItems.Add(issuesRoot);
            });
        }

        private void OnSelectedItemChanged(TreeViewItem node)
        {
            if (node?.Tag is IXygeniIssue issue)
            {
                IssueSelected?.Invoke(issue);
            }
        }

        

        private string GetCategoryIcon(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;

            string iconFileName = null;
            string lowerCategory = categoryName.ToLower();

            if ( lowerCategory.Contains("sast"))
            {
                iconFileName = "code-sec.png";
            }
            else if (lowerCategory.Contains("secret"))
            {
                iconFileName = "secrets.png";
            }
            else if (lowerCategory.Contains("misconf"))
            {
                iconFileName = "misconf.png";
            }
            else if (lowerCategory.Contains("iac") || lowerCategory.Contains("infrastructure"))
            {
                iconFileName = "iac.png";
            }
            else if (lowerCategory.Contains("deps") || lowerCategory.Contains("sca"))
            {
                iconFileName = "open-source.png";
            }

            if (iconFileName != null)
            {
                return GetIconPath(iconFileName);
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
                iconFileName = "critical16.png";
            }
            else if (lowerSeverity == "high")
            {
                iconFileName = "high16.png";
            }
            else if (lowerSeverity == "low")
            {
                iconFileName = "low16.png";
            }
            else if (lowerSeverity == "info")
            {
                iconFileName = "info16.png";
            }

            if (iconFileName != null)
            {
                return GetIconPath(iconFileName);
            }
            return null;
        }

        private string GetIconPath(string iconFileName)
        {
            if (string.IsNullOrEmpty(iconFileName)) return null;

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string iconPath = Path.Combine(baseDir, "media", "icons", iconFileName);
            return iconPath;
        }    
    }
}
