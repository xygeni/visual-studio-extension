using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using vs2026_plugin.Services;

namespace vs2026_plugin.Editor
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TagType(typeof(IErrorTag))]
    internal sealed class XygeniIssueErrorTaggerProvider : IViewTaggerProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService = null;

        public ITagger<T> CreateTagger<T>(IWpfTextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null || textView.TextBuffer != buffer)
            {
                return null;
            }

            return new XygeniIssueErrorTagger(textView, buffer, TextDocumentFactoryService) as ITagger<T>;
        }
    }

    internal sealed class XygeniIssueErrorTagger : ITagger<IErrorTag>, IDisposable
    {
        private readonly IWpfTextView _view;
        private readonly ITextBuffer _buffer;
        private readonly ITextDocumentFactoryService _textDocumentFactoryService;

        private XygeniErrorListService _errorListService;
        private bool _isClosed;
        private string _currentFilePath;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public XygeniIssueErrorTagger(IWpfTextView view, ITextBuffer buffer, ITextDocumentFactoryService textDocumentFactoryService)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _textDocumentFactoryService = textDocumentFactoryService;
            _currentFilePath = GetCurrentFilePath();

            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnViewClosed;

            EnsureErrorListService();
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (_isClosed || spans == null || spans.Count == 0)
            {
                yield break;
            }

            EnsureErrorListService();
            if (_errorListService == null)
            {
                yield break;
            }

            string currentFilePath = GetCurrentFilePath();
            if (string.IsNullOrEmpty(currentFilePath))
            {
                yield break;
            }

            IReadOnlyList<XygeniIssueLocation> issueLocations = _errorListService.GetIssueLocationsForDocument(currentFilePath);
            if (issueLocations == null || issueLocations.Count == 0)
            {
                yield break;
            }

            ITextSnapshot snapshot = spans[0].Snapshot;
            foreach (var issueLocation in issueLocations)
            {
                if (issueLocation == null)
                {
                    continue;
                }

                if (!TryCreateIssueSpan(snapshot, issueLocation, out SnapshotSpan issueSpan))
                {
                    continue;
                }

                if (!spans.IntersectsWith(issueSpan))
                {
                    continue;
                }

                string errorType = GetErrorType(issueLocation.Severity);
                var errorTag = new ErrorTag(errorType, issueLocation.Message);
                yield return new TagSpan<IErrorTag>(issueSpan, errorTag);
            }
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_isClosed)
            {
                return;
            }

            EnsureErrorListService();

            string latestFilePath = GetCurrentFilePath();
            if (!string.Equals(_currentFilePath, latestFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilePath = latestFilePath;
                RaiseTagsChanged();
            }
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_isClosed)
            {
                return;
            }

            _isClosed = true;

            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnViewClosed;

            if (_errorListService != null)
            {
                _errorListService.IssueLocationsChanged -= OnIssueLocationsChanged;
            }
        }

        private void EnsureErrorListService()
        {
            if (_errorListService != null)
            {
                return;
            }

            if (!XygeniErrorListService.TryGetInstance(out XygeniErrorListService errorListService) || errorListService == null)
            {
                return;
            }

            _errorListService = errorListService;
            _errorListService.IssueLocationsChanged += OnIssueLocationsChanged;
            RaiseTagsChanged();
        }

        private void OnIssueLocationsChanged(object sender, EventArgs e)
        {
            if (_isClosed)
            {
                return;
            }

            RaiseTagsChanged();
        }

        private void RaiseTagsChanged()
        {
            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            var span = new SnapshotSpan(snapshot, 0, snapshot.Length);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(span));
        }

        private string GetCurrentFilePath()
        {
            if (_textDocumentFactoryService == null)
            {
                return null;
            }

            ITextBuffer documentBuffer = _view.TextDataModel?.DocumentBuffer ?? _buffer;
            if (_textDocumentFactoryService.TryGetTextDocument(documentBuffer, out ITextDocument textDocument))
            {
                return textDocument.FilePath;
            }

            return null;
        }

        private static bool TryCreateIssueSpan(ITextSnapshot snapshot, XygeniIssueLocation issueLocation, out SnapshotSpan issueSpan)
        {
            issueSpan = default(SnapshotSpan);

            if (snapshot == null || issueLocation == null || snapshot.LineCount == 0)
            {
                return false;
            }

            int startLineNumber = Math.Max(0, issueLocation.BeginLine - 1);
            if (startLineNumber >= snapshot.LineCount)
            {
                return false;
            }

            ITextSnapshotLine startLine = snapshot.GetLineFromLineNumber(startLineNumber);
            int startColumn = Math.Max(0, issueLocation.BeginColumn - 1);
            int startPosition = Math.Min(startLine.End.Position, startLine.Start.Position + startColumn);

            int endLineNumber = issueLocation.EndLine > 0 ? issueLocation.EndLine - 1 : startLineNumber;
            endLineNumber = Math.Max(startLineNumber, Math.Min(snapshot.LineCount - 1, endLineNumber));

            ITextSnapshotLine endLine = snapshot.GetLineFromLineNumber(endLineNumber);
            int endPosition;

            if (endLineNumber == startLineNumber)
            {
                int endColumn = issueLocation.EndColumn > 0 ? issueLocation.EndColumn - 1 : startColumn + 1;
                int safeEndColumn = Math.Max(startColumn + 1, endColumn);
                endPosition = Math.Min(endLine.End.Position, endLine.Start.Position + safeEndColumn);
            }
            else
            {
                if (issueLocation.EndColumn > 0)
                {
                    int endColumn = Math.Max(0, issueLocation.EndColumn - 1);
                    endPosition = Math.Min(endLine.End.Position, endLine.Start.Position + endColumn);
                }
                else
                {
                    endPosition = endLine.End.Position;
                }
            }

            if (endPosition <= startPosition)
            {
                endPosition = Math.Min(snapshot.Length, startPosition + 1);
            }

            if (endPosition <= startPosition)
            {
                return false;
            }

            issueSpan = new SnapshotSpan(snapshot, Span.FromBounds(startPosition, endPosition));
            return issueSpan.Length > 0;
        }

        private static string GetErrorType(string severity)
        {
            switch ((severity ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "critical":
                case "high":
                    return PredefinedErrorTypeNames.SyntaxError;
                case "medium":
                case "low":
                    return PredefinedErrorTypeNames.Warning;
                default:
                    return PredefinedErrorTypeNames.OtherError;
            }
        }
    }
}
