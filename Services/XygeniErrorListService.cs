using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
    public class XygeniErrorListService
    {
        private static XygeniErrorListService _instance;

        private readonly AsyncPackage _package;
        private readonly ILogger _logger;
        private readonly ErrorListProvider _errorListProvider;
        private readonly XygeniIssueService _issueService;

        private XygeniErrorListService(AsyncPackage package, ILogger logger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _package = package;
            _logger = logger;

            _errorListProvider = new ErrorListProvider(_package)
            {
                ProviderName = "Xygeni Issues"
            };

            _issueService = XygeniIssueService.GetInstance();
            _issueService.IssuesChanged += OnIssuesChanged;
        }

        public static XygeniErrorListService GetInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("XygeniErrorListService has not been initialized");
            }

            return _instance;
        }

        public static XygeniErrorListService GetInstance(AsyncPackage package = null, ILogger logger = null)
        {
            if (_instance == null && package != null)
            {
                _instance = new XygeniErrorListService(package, logger);
            }

            return _instance;
        }

        private void OnIssuesChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Refresh();
            });
        }

        public void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var issues = _issueService.GetIssues() ?? new List<IXygeniIssue>();
            string rootDirectory = GetRootDirectorySafe();

            _errorListProvider.SuspendRefresh();

            try
            {
                _errorListProvider.Tasks.Clear();

                foreach (var issue in issues)
                {
                    if (issue == null)
                    {
                        continue;
                    }

                    var task = new ErrorTask
                    {
                        Category = TaskCategory.BuildCompile,
                        ErrorCategory = GetTaskErrorCategory(issue.Severity),
                        Text = BuildTaskText(issue),
                        Document = ResolveIssuePath(issue.File, rootDirectory),
                        Line = Math.Max(0, issue.BeginLine - 1),
                        Column = Math.Max(0, issue.BeginColumn - 1)
                    };

                    task.Navigate += OnNavigate;
                    _errorListProvider.Tasks.Add(task);
                }
            }
            finally
            {
                _errorListProvider.ResumeRefresh();
            }
        }

        private void OnNavigate(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var errorTask = sender as ErrorTask;
                if (errorTask == null || string.IsNullOrEmpty(errorTask.Document))
                {
                    return;
                }

                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
                if (dte == null)
                {
                    return;
                }

                if (!File.Exists(errorTask.Document))
                {
                    return;
                }

                var window = dte.ItemOperations.OpenFile(errorTask.Document);
                if (window != null)
                {
                    var selection = dte.ActiveDocument?.Selection as TextSelection;
                    if (selection != null)
                    {
                        selection.GotoLine(errorTask.Line + 1, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error navigating from Xygeni Error List");
            }
        }

        private static TaskErrorCategory GetTaskErrorCategory(string severity)
        {
            switch ((severity ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "critical":
                case "high":
                    return TaskErrorCategory.Error;
                case "medium":
                case "low":
                    return TaskErrorCategory.Warning;
                default:
                    return TaskErrorCategory.Message;
            }
        }

        private static string BuildTaskText(IXygeniIssue issue)
        {
            string severity = string.IsNullOrWhiteSpace(issue.Severity) ? "info" : issue.Severity;
            string type = string.IsNullOrWhiteSpace(issue.Type) ? "Issue" : issue.Type;
            string category = string.IsNullOrWhiteSpace(issue.CategoryName) ? "Security" : issue.CategoryName;
            return $"[{severity}] {category}: {type}";
        }

        private static string ResolveIssuePath(string issueFilePath, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(issueFilePath))
            {
                return string.Empty;
            }

            try
            {
                if (Path.IsPathRooted(issueFilePath))
                {
                    return Path.GetFullPath(issueFilePath);
                }

                if (!string.IsNullOrWhiteSpace(rootDirectory))
                {
                    return Path.GetFullPath(Path.Combine(rootDirectory, issueFilePath));
                }
            }
            catch
            {
                // Keep the original issue path if resolution fails.
            }

            return issueFilePath;
        }

        private static string GetRootDirectorySafe()
        {
            try
            {
                return ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    return await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                });
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
