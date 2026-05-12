using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Drives the `xygeni util ai-explain` CLI invocation on demand and caches the result
    /// per issue id, so reopening the AI EXPLANATION tab for the same issue does not
    /// re-spend tokens. The cache is in-memory only (process lifetime).
    /// </summary>
    public class AIExplainService
    {
        private static AIExplainService _instance;
        private readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>();
        private readonly XygeniScannerService _scanner;

        private AIExplainService()
        {
            _scanner = XygeniScannerService.GetInstance();
        }

        public static AIExplainService GetInstance()
        {
            if (_instance == null)
            {
                _instance = new AIExplainService();
            }
            return _instance;
        }

        public bool TryGetCached(string issueId, out string markdown)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                markdown = null;
                return false;
            }
            return _cache.TryGetValue(issueId, out markdown);
        }

        public void Invalidate(string issueId)
        {
            if (!string.IsNullOrEmpty(issueId))
            {
                _cache.TryRemove(issueId, out _);
            }
        }

        public async Task<string> ExplainAsync(IXygeniIssue issue, string xygeniInstallPath, ILogger logger)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue));

            if (_cache.TryGetValue(issue.Id ?? string.Empty, out string cached))
            {
                return cached;
            }

            string rawJson = (issue as SastXygeniIssue)?.RawJson;
            if (string.IsNullOrEmpty(rawJson))
            {
                throw new InvalidOperationException("AI Explain is only available for SAST issues with raw report data.");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "xygeni-explain");
            Directory.CreateDirectory(tempDir);

            string kindPart = SanitizeForFileName(issue.Kind ?? "issue");
            string filePart = SanitizeForFileName(Path.GetFileName(issue.File ?? "unknown"));
            string idPart = SanitizeForFileName(issue.Id ?? Guid.NewGuid().ToString("N"));
            string outputFile = Path.Combine(tempDir, $"AIExplain-{kindPart}-At-{filePart}-{idPart}.md");

            try
            {
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Could not remove stale AI Explain output file");
            }

            await _scanner.RunAiExplainCommandAsync(rawJson, outputFile, xygeniInstallPath, logger);

            if (!File.Exists(outputFile))
            {
                throw new Exception("AI Explain finished but produced no output file.");
            }

            string markdown = File.ReadAllText(outputFile);
            if (!string.IsNullOrEmpty(issue.Id))
            {
                _cache[issue.Id] = markdown;
            }
            return markdown;
        }

        private static string SanitizeForFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s;
        }
    }
}
