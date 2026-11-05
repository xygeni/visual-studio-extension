using System.Collections.Generic;
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
                // The scan types of .xygeni\ide.json, read off the UI thread; null when there is no solution root.
                List<string> configuredScans = null;
                try
                {
                    string root = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                    if (!string.IsNullOrEmpty(root)) configuredScans = ScanConfig.Load(root).Scans.ToList();
                }
                catch (Exception) { }
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RootItems.Clear();

                // 1. Scan Executions
                var scansRoot = new TreeNodeData("Scan Executions")
                {
                    IsExpanded = true
                };

                foreach (var scan in _scannerService.GetScans()
                            .OrderByDescending(scanResult => scanResult.Timestamp))
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

                var issuesByCategory = _issueService.GetIssues()
                    .GroupBy(categoryIssue => categoryIssue.CategoryName)
                    .ToDictionary(categoryGroup => categoryGroup.Key, categoryGroup => categoryGroup.ToList());
                // Every known category is listed, with findings or not, so each one can be run alone (#415), like
                // the per-view play of the other plugins; a category switched off in Scan configuration says so.
                var categoryNames = KnownCategories.Concat(issuesByCategory.Keys).Distinct().OrderBy(name => name);
                foreach (string categoryName in categoryNames)
                {
                    var categoryIssues = issuesByCategory.TryGetValue(categoryName, out var found) ? found : new List<IXygeniIssue>();
                    string scanType = GetCategoryScanType(categoryName);
                    bool inScanConfig = scanType == null || configuredScans == null || configuredScans.Contains(scanType);
                    string count = categoryIssues.Count == 0 && !inScanConfig ? "Not in the scan configuration" : categoryIssues.Count.ToString();
                    var catNode = new TreeNodeData(
                        $"{categoryName} ({count})",
                        GetCategoryIcon(categoryName),
                        categoryIssues
                    );
                    // First child: runs this scan type alone, keeping the findings of the other categories (#415).
                    if (scanType != null)
                    {
                        catNode.Items.Add(new TreeNodeData($"Run {categoryName} scan only", GetIconPath("play.png"), new RunCategoryScan(scanType)));
                    }

                    foreach (var issue in categoryIssues.OrderBy(categoryIssue => categoryIssue.GetSeverityLevel()))
                    {
                        // Service/module-scoped API flaws resolve to a file without a line, or to no file at all.
                        string locationSuffix = string.IsNullOrEmpty(issue.File) ? "" : $" - {Path.GetFileName(issue.File)}";
                        if (issue.HasLocation)
                        {
                            locationSuffix += $":{issue.BeginLine}";
                        }
                        var issueNodeData = new TreeNodeData(
                            $"[{issue.Severity}] {issue.Type}{locationSuffix}",
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

        

        /// <summary>The categories of the explorer, in the order of the scanners; the ones GetCategoryScanType knows.</summary>
        private static readonly string[] KnownCategories = { "SCA", "SAST", "Code Quality", "Misconfigurations", "Secrets", "IaC", "API Security", "AI Security", "Malware" };

        /// <summary>Category name of the explorer → the <c>--run</c> scan type that feeds it.</summary>
        private static string GetCategoryScanType(string categoryName)
        {
            switch (categoryName)
            {
                case "SCA": return "deps";
                case "SAST": return "sast";
                case "Code Quality": return "quality";
                case "Misconfigurations": return "misconf";
                case "Secrets": return "secrets";
                case "IaC": return "iac";
                case "API Security": return "apisec";
                case "AI Security": return "ai";
                case "Malware": return "malware";
                default: return null;
            }
        }

        private string GetCategoryIcon(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;

            string iconFileName = null;
            string lowerCategory = categoryName.ToLower();

            if (lowerCategory.Contains("api security"))
            {
                iconFileName = "api-security.png";
            }
            else if (lowerCategory.Contains("ai security"))
            {
                iconFileName = "ai-security.png";
            }
            else if (lowerCategory.Contains("malware"))
            {
                iconFileName = "malware.png";
            }
            else if ( lowerCategory.Contains("sast"))
            {
                iconFileName = "code-sec.png";
            }
            else if (lowerCategory.Contains("quality"))
            {
                iconFileName = "code-quality.png";
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

            string baseDir = Path.GetDirectoryName(this.GetType().Assembly.Location);
            string iconPath = Path.Combine(baseDir, "media", "icons", iconFileName);
            return iconPath;
        }    
    }
}
