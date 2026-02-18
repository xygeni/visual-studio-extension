using System.Collections.ObjectModel;
using System.ComponentModel;
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
        public ObservableCollection<TreeNodeData> RootItems { get; } 
            = new ObservableCollection<TreeNodeData>();

        private TreeNodeData _selectedItem;

        // capture selected node changes
        public TreeNodeData SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged(nameof(SelectedItem));
                OnSelectedItemChanged(value);
            }
        }

        public event Action<IXygeniIssue> IssueSelected;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly XygeniIssueService _issueService;
        private readonly XygeniScannerService _scannerService;

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

                // 1. Scan Executions
                var scansRoot = new TreeNodeData("Scan Executions")
                {
                    IsExpanded = true
                };

                foreach (var scan in _scannerService.GetScans()
                            .OrderByDescending(s => s.Timestamp))
                {
                    scansRoot.Items.Add(new TreeNodeData(
                        $"[{scan.Timestamp:HH:mm:ss}] {scan.Status} {scan.Summary}",
                        tag: scan
                    ));
                }

                RootItems.Add(scansRoot);

                // 2. Issues by Category
                var issuesRoot = new TreeNodeData("Issues by Category")
                {
                    IsExpanded = true
                };

                var categories = _issueService.GetIssues()
                    .GroupBy(i => i.CategoryName)
                    .OrderBy(g => g.Key);

                foreach (var group in categories)
                {
                    var catNode = new TreeNodeData(
                        $"{group.Key} ({group.Count()})",
                        GetCategoryIcon(group.Key),
                        group
                    );

                    foreach (var issue in group.OrderBy(i => i.GetSeverityLevel()))
                    {
                        var issueNodeData = new TreeNodeData(
                            $"[{issue.Severity}] {issue.Type} - {Path.GetFileName(issue.File)}:{issue.BeginLine}",
                            GetSeverityIcon(issue.Severity),
                            issue
                        );
                        catNode.Items.Add(issueNodeData);
                    }

                    issuesRoot.Items.Add(catNode);
                }

                RootItems.Add(issuesRoot);
            });
        }

        private void OnSelectedItemChanged(TreeNodeData node)
        {
            if (node == null) return;

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
