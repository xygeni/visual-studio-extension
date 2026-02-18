using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;
using vs2026_plugin.Models;
using vs2026_plugin.Services;

namespace vs2026_plugin.Editor
{
    [Export(typeof(AdornmentLayerDefinition))]
    [Name(XygeniIssueLineDecorator.LayerName)]
    [Order(After = PredefinedAdornmentLayers.TextMarker, Before = PredefinedAdornmentLayers.Caret)]
    internal AdornmentLayerDefinition xygeniIssueLineDecoratorLayer;

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class XygeniIssueLineDecoratorFactory : IWpfTextViewCreationListener
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService = null;

        public void TextViewCreated(IWpfTextView textView)
        {
            new XygeniIssueLineDecorator(textView, TextDocumentFactoryService);
        }
    }

    internal sealed class XygeniIssueLineDecorator
    {
        public const string LayerName = "XygeniIssueLineDecoratorLayer";

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _adornmentLayer;
        private readonly ITextDocumentFactoryService _textDocumentFactoryService;
        private readonly Dictionary<int, List<IXygeniIssue>> _issuesByLine = new Dictionary<int, List<IXygeniIssue>>();

        private XygeniIssueService _issueService;
        private string _currentFilePath;
        private bool _isClosed;

        public XygeniIssueLineDecorator(IWpfTextView view, ITextDocumentFactoryService textDocumentFactoryService)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _textDocumentFactoryService = textDocumentFactoryService;
            _adornmentLayer = _view.GetAdornmentLayer(LayerName);

            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnViewClosed;

            if (TryAttachIssueService())
            {
                RefreshIssueMap();
            }

            RedrawAdornments();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_isClosed)
            {
                return;
            }

            if (_issueService == null)
            {
                TryAttachIssueService();
            }

            string latestFilePath = GetCurrentFilePath();
            if (!string.Equals(_currentFilePath, latestFilePath, StringComparison.OrdinalIgnoreCase))
            {
                RefreshIssueMap();
            }
            else if (_issuesByLine.Count == 0 && _issueService != null)
            {
                RefreshIssueMap();
            }

            RedrawAdornments();
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            _isClosed = true;

            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnViewClosed;

            if (_issueService != null)
            {
                _issueService.IssuesChanged -= OnIssuesChanged;
            }
        }

        private void OnIssuesChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (_isClosed)
                {
                    return;
                }

                RefreshIssueMap();
                RedrawAdornments();
            });
        }

        private bool TryAttachIssueService()
        {
            if (_issueService != null)
            {
                return true;
            }

            try
            {
                _issueService = XygeniIssueService.GetInstance();
                _issueService.IssuesChanged += OnIssuesChanged;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RefreshIssueMap()
        {
            _issuesByLine.Clear();
            _currentFilePath = GetCurrentFilePath();

            if (string.IsNullOrEmpty(_currentFilePath) || _issueService == null)
            {
                return;
            }

            var allIssues = _issueService.GetIssues();
            if (allIssues == null || allIssues.Count == 0)
            {
                return;
            }

            string rootDirectory = GetRootDirectorySafe();

            foreach (var issue in allIssues)
            {
                if (issue == null || string.IsNullOrEmpty(issue.File) || issue.BeginLine <= 0)
                {
                    continue;
                }

                if (!IsIssueForCurrentFile(issue.File, _currentFilePath, rootDirectory))
                {
                    continue;
                }

                if (!_issuesByLine.TryGetValue(issue.BeginLine, out var lineIssues))
                {
                    lineIssues = new List<IXygeniIssue>();
                    _issuesByLine[issue.BeginLine] = lineIssues;
                }

                lineIssues.Add(issue);
            }

            foreach (var lineIssues in _issuesByLine.Values)
            {
                lineIssues.Sort((left, right) => left.GetSeverityLevel().CompareTo(right.GetSeverityLevel()));
            }
        }

        private void RedrawAdornments()
        {
            _adornmentLayer.RemoveAllAdornments();

            if (_issuesByLine.Count == 0 || _view.TextViewLines == null)
            {
                return;
            }

            foreach (ITextViewLine line in _view.TextViewLines)
            {
                var snapshotLine = line.Start.GetContainingLine();
                int lineNumber = snapshotLine.LineNumber + 1;

                if (!_issuesByLine.TryGetValue(lineNumber, out var issuesForLine))
                {
                    continue;
                }

                var span = new SnapshotSpan(snapshotLine.End, snapshotLine.End);
                var adornment = CreateLineAdornment(issuesForLine);
                _adornmentLayer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, snapshotLine, adornment, null);
            }
        }

        private UIElement CreateLineAdornment(IEnumerable<IXygeniIssue> issuesForLine)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(12, 0, 0, 0)
            };

            var borderColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBorderColorKey);
            var foregroundColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
            var backgroundColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);

            foreach (var issue in issuesForLine)
            {
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(borderColor),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromArgb(220, backgroundColor.R, backgroundColor.G, backgroundColor.B)),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 1, 0, 1)
                };

                var text = new TextBlock
                {
                    Foreground = new SolidColorBrush(foregroundColor),
                    FontSize = 11
                };

                string issueType = string.IsNullOrWhiteSpace(issue.Type) ? "Issue" : issue.Type;
                text.Inlines.Add(new Run("Xygeni: " + issueType + " "));

                var link = new Hyperlink(new Run("details"))
                {
                    Cursor = Cursors.Hand
                };

                link.Click += (sender, e) => OpenIssueDetails(issue);
                text.Inlines.Add(link);

                border.Child = text;
                panel.Children.Add(border);
            }

            return panel;
        }

        private void OpenIssueDetails(IXygeniIssue issue)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    await IssueDetailsService.GetInstance().ShowIssueDetailsAsync(issue);
                }
                catch (Exception ex)
                {
                    vs2026_pluginPackage.Instance?.Logger?.Error(ex, "Error opening issue details from editor decorator");
                }
            });
        }

        private string GetCurrentFilePath()
        {
            if (_textDocumentFactoryService == null)
            {
                return null;
            }

            ITextBuffer documentBuffer = _view.TextDataModel?.DocumentBuffer ?? _view.TextBuffer;

            if (_textDocumentFactoryService.TryGetTextDocument(documentBuffer, out ITextDocument textDocument))
            {
                return NormalizePath(textDocument.FilePath);
            }

            return null;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string normalizedPath = path.Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(normalizedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.Replace('\\', '/').TrimStart('.', '/');
        }

        private static bool IsIssueForCurrentFile(string issueFilePath, string currentFilePath, string rootDirectory)
        {
            string normalizedCurrent = NormalizePath(currentFilePath);
            if (string.IsNullOrEmpty(normalizedCurrent))
            {
                return false;
            }

            string normalizedIssue = NormalizePath(issueFilePath);
            if (!string.IsNullOrEmpty(normalizedIssue) && Path.IsPathRooted(issueFilePath))
            {
                return string.Equals(normalizedCurrent, normalizedIssue, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(rootDirectory))
            {
                string rootedIssuePath = NormalizePath(Path.Combine(rootDirectory, issueFilePath));
                if (!string.IsNullOrEmpty(rootedIssuePath) &&
                    string.Equals(normalizedCurrent, rootedIssuePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string relativeIssuePath = NormalizeRelativePath(issueFilePath);
            if (string.IsNullOrEmpty(relativeIssuePath))
            {
                return false;
            }

            string normalizedCurrentUnix = normalizedCurrent.Replace('\\', '/');
            return normalizedCurrentUnix.EndsWith("/" + relativeIssuePath, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCurrentUnix.Equals(relativeIssuePath, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRootDirectorySafe()
        {
            try
            {
                string root = ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    return await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                });

                return NormalizePath(root);
            }
            catch
            {
                return null;
            }
        }
    }
}
