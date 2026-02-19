using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
    public class XygeniErrorListService
    {
        private static XygeniErrorListService _instance;

        private readonly ILogger _logger;
        private readonly XygeniIssueService _issueService;
        private readonly object _diagnosticsGate = new object();
        private Dictionary<string, ImmutableArray<Diagnostic>> _diagnosticsByFile =
            new Dictionary<string, ImmutableArray<Diagnostic>>(StringComparer.OrdinalIgnoreCase);

        private static readonly DiagnosticDescriptor ErrorDescriptor = new DiagnosticDescriptor(
            id: "XYGENI0001",
            title: "Xygeni security issue",
            messageFormat: "{0}",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WarningDescriptor = new DiagnosticDescriptor(
            id: "XYGENI0002",
            title: "Xygeni security issue",
            messageFormat: "{0}",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InfoDescriptor = new DiagnosticDescriptor(
            id: "XYGENI0003",
            title: "Xygeni security issue",
            messageFormat: "{0}",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        private XygeniErrorListService(AsyncPackage package, ILogger logger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _logger = logger;
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
            var diagnosticsByFile = new Dictionary<string, List<Diagnostic>>(StringComparer.OrdinalIgnoreCase);

            foreach (var issue in issues)
            {
                if (issue == null)
                {
                    continue;
                }

                try
                {
                    string resolvedPath = ResolveIssuePath(issue.File, rootDirectory);
                    string normalizedPath = NormalizePath(resolvedPath);
                    if (string.IsNullOrEmpty(normalizedPath))
                    {
                        continue;
                    }

                    Diagnostic diagnostic = CreateDiagnostic(issue, normalizedPath);
                    if (diagnostic == null)
                    {
                        continue;
                    }

                    if (!diagnosticsByFile.TryGetValue(normalizedPath, out List<Diagnostic> fileDiagnostics))
                    {
                        fileDiagnostics = new List<Diagnostic>();
                        diagnosticsByFile[normalizedPath] = fileDiagnostics;
                    }

                    fileDiagnostics.Add(diagnostic);
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error building Xygeni diagnostic entry");
                }
            }

            var immutableDiagnostics = new Dictionary<string, ImmutableArray<Diagnostic>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<Diagnostic>> pair in diagnosticsByFile)
            {
                immutableDiagnostics[pair.Key] = pair.Value.ToImmutableArray();
            }

            lock (_diagnosticsGate)
            {
                _diagnosticsByFile = immutableDiagnostics;
            }
        }

        internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnosticDescriptors
        {
            get
            {
                return ImmutableArray.Create(ErrorDescriptor, WarningDescriptor, InfoDescriptor);
            }
        }

        internal static ImmutableArray<Diagnostic> GetDiagnosticsForFile(string filePath)
        {
            var instance = _instance;
            if (instance == null)
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            return instance.GetDiagnosticsForFileInternal(filePath);
        }

        private ImmutableArray<Diagnostic> GetDiagnosticsForFileInternal(string filePath)
        {
            string normalizedPath = NormalizePath(filePath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            lock (_diagnosticsGate)
            {
                if (_diagnosticsByFile.TryGetValue(normalizedPath, out ImmutableArray<Diagnostic> diagnostics))
                {
                    return diagnostics;
                }
            }

            return ImmutableArray<Diagnostic>.Empty;
        }

        private static Diagnostic CreateDiagnostic(IXygeniIssue issue, string filePath)
        {
            DiagnosticSeverity severity = GetDiagnosticSeverity(issue.Severity);
            DiagnosticDescriptor descriptor = GetDescriptor(severity);
            Location location = CreateLocation(issue, filePath);
            string message = BuildDiagnosticMessage(issue);

            var properties = ImmutableDictionary<string, string>.Empty
                .Add("xygeniIssueId", issue.Id ?? string.Empty)
                .Add("xygeniCategory", issue.Category ?? string.Empty);

            return Diagnostic.Create(descriptor, location, properties, message);
        }

        private static Location CreateLocation(IXygeniIssue issue, string filePath)
        {
            int startLine = Math.Max(0, issue.BeginLine - 1);
            int startColumn = Math.Max(0, issue.BeginColumn - 1);

            int endLine = issue.EndLine > 0 ? issue.EndLine - 1 : startLine;
            int endColumn = issue.EndColumn > 0 ? issue.EndColumn - 1 : startColumn + 1;

            if (endLine < startLine || (endLine == startLine && endColumn <= startColumn))
            {
                endLine = startLine;
                endColumn = startColumn + 1;
            }

            var lineSpan = new LinePositionSpan(
                new LinePosition(startLine, startColumn),
                new LinePosition(endLine, endColumn));

            return Location.Create(filePath, new TextSpan(0, 1), lineSpan);
        }

        private static DiagnosticDescriptor GetDescriptor(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return ErrorDescriptor;
                case DiagnosticSeverity.Warning:
                    return WarningDescriptor;
                default:
                    return InfoDescriptor;
            }
        }

        private static DiagnosticSeverity GetDiagnosticSeverity(string severity)
        {
            switch ((severity ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "critical":
                case "high":
                    return DiagnosticSeverity.Error;
                case "medium":
                case "low":
                    return DiagnosticSeverity.Warning;
                default:
                    return DiagnosticSeverity.Info;
            }
        }

        private static string BuildDiagnosticMessage(IXygeniIssue issue)
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
                return null;
            }

            try
            {
                if (Path.IsPathRooted(issueFilePath))
                {
                    return NormalizePath(Path.GetFullPath(issueFilePath));
                }

                if (!string.IsNullOrWhiteSpace(rootDirectory))
                {
                    return NormalizePath(Path.GetFullPath(Path.Combine(rootDirectory, issueFilePath)));
                }
            }
            catch
            {
                // Keep the original issue path if resolution fails.
            }

            return NormalizePath(issueFilePath);
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
    }
}
