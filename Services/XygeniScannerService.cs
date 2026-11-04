using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using vs2026_plugin.Services;
using vs2026_plugin.Commands;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
   
    public class ScanResult
    {
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } // "running", "completed", "failed"
        public object IssuesFound { get; set; }
        public string Summary { get; set; }
        // Scan types from the `--run=` argument: the reports this scan rewrote. Null = unknown, re-read all.
        public IReadOnlyList<string> ScanTypes { get; set; }
        // A full scan limited to the configured scan types (#415): the categories it did not run are dropped, not kept.
        public bool FullScan { get; set; }
    }

    public class XygeniScannerService
    {
        private static XygeniScannerService _instance;

        // Configuration Keys
        private const string CollectionPath = "XygeniConfiguration";
        private const string ApiUrlKey = "ApiUrl";

        // Constants
        private const int TimeoutMs = 1800000; // 30 minutes

        // CLI ErrorCodes: 127 = some scan type not licensed; >= 128 = issues found (a successful scan).
        private const int LicenseErrorExitCode = 127;
        private const int AnyIssueFoundExitCode = 128;

        // `scan --run=<types> [--incremental] -f json -o <suffix> --no-upload --include-vulnerabilities`; the scan
        // types come from the project's `.xygeni/ide.json` (xygeni/xygeni-product-backlog#415).
        // With `only`, a single scan type instead of the configured full scan (the "Run ... scan only" node of a category,
        // #415). The scanner options of the file go after the excludes, each canonical entry (`--name=value`) as one argument.
        private static string[] BuildScanArgs(ScanConfig config, bool incremental, IReadOnlyList<string> only = null)
        {
            var run = only != null && only.Count > 0 ? only : incremental ? config.OnSave : config.Scans;
            var args = new List<string> { "scan", "--run=" + string.Join(",", run) };
            if (incremental) args.Add("--incremental");
            string excludeAdd = config.ExcludeAddArgument();
            if (excludeAdd != null) args.Add(excludeAdd);
            args.AddRange(ScanOptions.Normalize(config.Options));
            args.AddRange(new[] { "-f", "json", "-o", XygeniCommands.ReportSuffix, "--no-upload", "--include-vulnerabilities" });
            return args.ToArray();
        }

        /// <summary>The scan types plus the ones that run with them (SCA brings suspect dependencies), in CLI order.</summary>
        public static IReadOnlyList<string> WithCompanions(IEnumerable<string> types)
        {
            var all = new HashSet<string>(types);
            foreach (var scanner in ScanConfig.Scanners)
            {
                if (scanner.RunsWith != null && all.Contains(scanner.RunsWith)) all.Add(scanner.Key);
            }
            return ScanConfig.AllScanTypes.Where(all.Contains).ToList();
        }

        /// <summary>Whether the on-save scan has anything to run for the project (#415).</summary>
        public static bool HasOnSaveScanTypes(string sourceFolder) => ScanConfig.Load(sourceFolder).OnSave.Count > 0;

        private readonly string[] _runRectifyScaArgs = { "util", "rectify", "--sca" };
        private readonly string[] _runRectifySastArgs = { "util", "rectify", "--sast" };
        private readonly string[] _runRectifyQualityArgs = { "util", "rectify", "--quality" };
        // RectifyCommand.java `--ai` (runAiRectify) takes the same --file-path/--detector/--line as SAST/quality.
        private readonly string[] _runRectifyAiArgs = { "util", "rectify", "--ai" };

        // State
        private bool _scannerRunning = false;
        private int _activeScannerCount = 0;
        private readonly int _maxConcurrentScanners = 3;
        private int? _exitCode;
        private readonly List<ScanResult> _scans = new List<ScanResult>();
        private readonly List<Func<Task>> _scannerQueue = new List<Func<Task>>();

        // Dependencies
        private readonly XygeniConfigurationService _configurationService;
        private readonly ILogger _logger;

        public event EventHandler Changed;

        private XygeniScannerService(ILogger logger)
        {            
            _configurationService = XygeniConfigurationService.GetInstance();
            _logger = logger;
        }

        public static XygeniScannerService GetInstance() => GetInstance(null);        

        public static XygeniScannerService GetInstance(ILogger logger)
        {
            if (_instance == null)
            {                
                if (logger == null) throw new ArgumentNullException(nameof(logger));
                _instance = new XygeniScannerService(logger);
            }
            return _instance;
        }

        public bool IsScannerRunning() => _scannerRunning;
        public int? GetExitCode() => _exitCode;
        public List<ScanResult> GetScans() => _scans;

        public bool HasQueuedScanners()
        {
            return _scannerQueue.Count > 0 || _activeScannerCount > 0;
        }

        /// <summary>The configured full scan, or with <paramref name="only"/> just those scan types (the findings of the others are kept).</summary>
        public async Task RunAnalysisAsync(string sourceFolder, string xygeniScannerPath, IReadOnlyList<string> only = null)
        {
            _exitCode = null;
            bool fullScan = only == null || only.Count == 0;
            string onlyNote = fullScan ? "" : string.Join(",", only) + " - ";

            var timestamp = DateTime.Now;

            // Keep only last 5 scans
            if (_scans.Count > 5)
            {
                _scans.RemoveAt(0);
            }

            _logger.Log("");
            _logger.Log("=================================================");
            _logger.Log($"  Running scan on source folder: {sourceFolder}");

            var runArgs = BuildScanArgs(ScanConfig.Load(sourceFolder), incremental: false, only: fullScan ? null : WithCompanions(only));
            var scanTypes = GetScanTypes(runArgs);
            var currentScan = new ScanResult { Timestamp = timestamp, Status = "running", IssuesFound = null, Summary = onlyNote.Trim(), ScanTypes = scanTypes, FullScan = fullScan };
            _scans.Add(currentScan);
            OnChanged();

            try
            {
                int exitCode = await RunAnalysisCommandAsync(sourceFolder, xygeniScannerPath, _logger, runArgs);

                _logger.Log("  Scanner finished");

                _scans.Remove(currentScan);

                var totalTime = (DateTime.Now - timestamp).TotalSeconds;
                _scans.Add(new ScanResult {
                    Timestamp = timestamp,
                    Status = "completed",
                    IssuesFound = null,
                    Summary = $"{onlyNote}Duration: {totalTime:F2}s{LicenseNote(exitCode)}",
                    ScanTypes = scanTypes,
                    FullScan = fullScan
                });

                _exitCode = exitCode;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error running scanner");

                _exitCode = 1;
                _scans.Remove(currentScan);
                _scans.Add(new ScanResult {
                    Timestamp = timestamp,
                    Status = "failed",
                    IssuesFound = null,
                    Summary = onlyNote.Trim(),
                    ScanTypes = scanTypes,
                    FullScan = fullScan
                });
            }
            finally
            {
                OnChanged();
            }
        }

        public Task<int> RunAnalysisCommandAsync(string sourceFolder, string xygeniInstallPath, ILogger logger) =>
            RunAnalysisCommandAsync(sourceFolder, xygeniInstallPath, logger, BuildScanArgs(ScanConfig.Load(sourceFolder), incremental: false));

        private async Task<int> RunAnalysisCommandAsync(string sourceFolder, string xygeniInstallPath, ILogger logger, string[] runArgs)
        {
            // args include -d sourceFolder.

            var args = new List<string>(runArgs);
            args.Add("-d");
            args.Add(sourceFolder);

            // The output json report is saved to the metadata folder

            var projectMetadataFolder = await XygeniConfigurationService.GetInstance().GetMetadataFolderAsyncForProject();

            return await CallScannerAsync(xygeniInstallPath, args, logger, projectMetadataFolder);
        }

        public async Task RunIncrementalAnalysisAsync(string sourceFolder, string xygeniScannerPath)
        {
            _exitCode = null;

            var timestamp = DateTime.Now;

            if (_scans.Count > 5)
            {
                _scans.RemoveAt(0);
            }

            _logger.Log("");
            _logger.Log("=================================================");
            _logger.Log($"  Running incremental scan on source folder: {sourceFolder}");

            var runArgs = BuildScanArgs(ScanConfig.Load(sourceFolder), incremental: true);
            var scanTypes = GetScanTypes(runArgs);
            var currentScan = new ScanResult { Timestamp = timestamp, Status = "running", IssuesFound = null, Summary = "incremental", ScanTypes = scanTypes };
            _scans.Add(currentScan);
            OnChanged();

            try
            {
                int exitCode = await RunIncrementalAnalysisCommandAsync(sourceFolder, xygeniScannerPath, _logger, runArgs);

                _logger.Log("  Incremental scanner finished");

                _scans.Remove(currentScan);

                var totalTime = (DateTime.Now - timestamp).TotalSeconds;
                _scans.Add(new ScanResult
                {
                    Timestamp = timestamp,
                    Status = "completed",
                    IssuesFound = null,
                    Summary = $"Incremental - Duration: {totalTime:F2}s{LicenseNote(exitCode)}",
                    ScanTypes = scanTypes
                });

                _exitCode = exitCode;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error running incremental scanner");

                _exitCode = 1;
                _scans.Remove(currentScan);
                _scans.Add(new ScanResult
                {
                    Timestamp = timestamp,
                    Status = "failed",
                    IssuesFound = null,
                    Summary = "incremental",
                    ScanTypes = scanTypes
                });
            }
            finally
            {
                OnChanged();
            }
        }

        public Task<int> RunIncrementalAnalysisCommandAsync(string sourceFolder, string xygeniInstallPath, ILogger logger) =>
            RunIncrementalAnalysisCommandAsync(sourceFolder, xygeniInstallPath, logger, BuildScanArgs(ScanConfig.Load(sourceFolder), incremental: true));

        private async Task<int> RunIncrementalAnalysisCommandAsync(string sourceFolder, string xygeniInstallPath, ILogger logger, string[] runArgs)
        {
            var args = new List<string>(runArgs);
            args.Add("-d");
            args.Add(sourceFolder);

            var projectMetadataFolder = await XygeniConfigurationService.GetInstance().GetMetadataFolderAsyncForProject();

            return await CallScannerAsync(xygeniInstallPath, args, logger, projectMetadataFolder);
        }

        public async Task RunRectifyScaCommandAsync(string filePath, string dependency, string xygeniInstallPath, ILogger logger)
        {
            var args = new List<string>(_runRectifyScaArgs);
            args.Add("--file-path");
            args.Add(filePath);
            args.Add("--dependency");
            args.Add(dependency);
            
            await CallScannerAsync(xygeniInstallPath, args, logger, Path.GetDirectoryName(filePath));
        }

        public async Task RunRectifySastCommandAsync(string filePath, string detector, string line, string xygeniInstallPath, ILogger logger)
        {
            var args = new List<string>(_runRectifySastArgs);
            args.Add("--file-path");
            args.Add(filePath);
            args.Add("--detector");
            args.Add(detector);
            args.Add("--line");
            args.Add(line);

            await CallScannerAsync(xygeniInstallPath, args, logger, Path.GetDirectoryName(filePath));
        }

        public async Task RunRectifyQualityCommandAsync(string filePath, string detector, string line, string xygeniInstallPath, ILogger logger)
        {
            var args = new List<string>(_runRectifyQualityArgs);
            args.Add("--file-path");
            args.Add(filePath);
            args.Add("--detector");
            args.Add(detector);
            args.Add("--line");
            args.Add(line);

            await CallScannerAsync(xygeniInstallPath, args, logger, Path.GetDirectoryName(filePath));
        }

        public async Task RunRectifyAiCommandAsync(string filePath, string detector, string line, string xygeniInstallPath, ILogger logger)
        {
            var args = new List<string>(_runRectifyAiArgs);
            args.Add("--file-path");
            args.Add(filePath);
            args.Add("--detector");
            args.Add(detector);
            args.Add("--line");
            args.Add(line);

            await CallScannerAsync(xygeniInstallPath, args, logger, Path.GetDirectoryName(filePath));
        }

        public async Task RunAiExplainCommandAsync(string issueJson, string outputFile, string xygeniInstallPath, ILogger logger)
        {
            string outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
            string inputJsonPath = Path.Combine(
                string.IsNullOrEmpty(outputDir) ? Path.GetTempPath() : outputDir,
                $"ai-explain-input-{Guid.NewGuid():N}.json");
            File.WriteAllText(inputJsonPath, issueJson ?? string.Empty);

            try
            {
                var args = new List<string>
                {
                    "util",
                    "ai-explain",
                    "--issue-json-file", inputJsonPath,
                    "-f", outputFile
                };
                await CallScannerAsync(xygeniInstallPath, args, logger, Path.GetTempPath());
            }
            finally
            {
                try { File.Delete(inputJsonPath); } catch { /* best-effort cleanup */ }
            }
        }

        // Returns the scanner exit code of a run accepted as successful (see IsSuccessfulExit); throws otherwise.
        private async Task<int> CallScannerAsync(string xygeniInstallPath, List<string> args, ILogger logger, string workingDir)
        {
            if (_scannerRunning)
            {
                throw new Exception("Scanner is already running");
            }
            _scannerRunning = true;

            try
            {
                return await ExecuteScannerCallAsync(xygeniInstallPath, args, logger, workingDir);
            }
            finally
            {
                _scannerRunning = false;
            }
        }

        private async Task<int> ExecuteScannerCallAsync(string xygeniInstallPath, List<string> args, ILogger logger, string workingDir)
        {
            return await Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(xygeniInstallPath))
                {
                    throw new Exception("Xygeni scanner path not configured");
                }

                string scannerScriptPath = GetScannerScriptPath(xygeniInstallPath);
                
                // Prepare Environment Variables
                var env = new Dictionary<string, string>();
                await GetEnvVariables(env);

                // Prepare Process
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell", // Assuming Windows
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDir ?? Path.GetTempPath()
                };

                // Scanner global options go before the command (xygeni <global> scan ...); args keeps the command
                // first, so the isScanCommand check below still sees it. (xygeni/tech-support#378)
                List<string> globalArgs = _configurationService.GetScannerGlobalOptions();
                List<string> blockedArgs = ScannerGlobalOptions.BlockedIn(_configurationService.GetAdditionalGlobalOptions());
                if (blockedArgs.Count > 0)
                {
                    _logger.Log($"  Ignoring scanner options {string.Join(" ", blockedArgs)}: -q/--quiet hide the scanner output the extension reads; the API token comes from the Xygeni configuration.");
                }
                var commandArgs = globalArgs.Concat(args).ToList();

                // Add arguments carefully
                // TS: ["-NoProfile","-ExecutionPolicy", "Bypass", "-File", scannerScriptPath, ...args]
                string psPrefix = $"-NoProfile -ExecutionPolicy Bypass -File \"{scannerScriptPath}\"";

                // Append other args wrapping in quotes if needed
                startInfo.Arguments = psPrefix + string.Concat(commandArgs.Select(arg => $" \"{arg}\""));

                // Add Env Vars
                foreach (var kvp in env)
                {
                    if (startInfo.EnvironmentVariables.ContainsKey(kvp.Key))
                        startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                    else
                        startInfo.EnvironmentVariables.Add(kvp.Key, kvp.Value);
                }

                _logger.Log($"  Xygeni Working dir: {startInfo.WorkingDirectory}");
                _logger.Log($"  Running scanner command: {startInfo.FileName} {startInfo.Arguments}");

                using (var process = new Process { StartInfo = startInfo })
                {
                    bool sawCertificateError = false;
                    process.OutputDataReceived += (sender, e) => 
                    { 
                        if (e.Data == null) return;
                        _logger.Log(StripAnsiEscapeSequences(e.Data));
                        if (ScannerGlobalOptions.IsCertificateError(e.Data)) sawCertificateError = true;
                    };
                    process.ErrorDataReceived += (sender, e) => 
                    { 
                        if (e.Data == null) return;
                        _logger.Log(StripAnsiEscapeSequences(e.Data));
                        if (ScannerGlobalOptions.IsCertificateError(e.Data)) sawCertificateError = true;
                    };

                    DateTime startedAtUtc = DateTime.UtcNow;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool exited = process.WaitForExit(TimeoutMs);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        throw new Exception("Scanner process timeout");
                    }
                    // The timed wait does not drain the asynchronous output handlers; this one does (the process has exited).
                    process.WaitForExit();

                    int exitCode = process.ExitCode;
                    bool isScanCommand = args.Count > 0 && args[0] == "scan";
                    if (!IsSuccessfulExit(exitCode, isScanCommand, startInfo.WorkingDirectory, startedAtUtc))
                    {
                        if (sawCertificateError && !globalArgs.Contains(ScannerGlobalOptions.SkipSslVerify))
                        {
                            SkipSslVerifyPrompt.Suggest(_logger);
                        }
                        throw new Exception($"Scanner process failed with exit code {exitCode}");
                    }
                    return exitCode;
                }
            });
        }

        // `scan` succeeds with 0, with >= 128 (issues found) and with 127 when the licensed scan types
        // still wrote their reports in this run (127 with no report = licence missing or expired).
        // `util` commands (rectify, ai-explain) only succeed with 0. (visual-studio-extension#15)
        private bool IsSuccessfulExit(int exitCode, bool isScanCommand, string reportDir, DateTime startedAtUtc)
        {
            if (exitCode == 0) return true;
            if (!isScanCommand) return false;
            if (exitCode >= AnyIssueFoundExitCode) return true;
            if (exitCode == LicenseErrorExitCode && HasReportWrittenSince(reportDir, startedAtUtc))
            {
                _logger.Log("  Some scan types are not licensed and were skipped; the licensed ones completed (see the LICENSE ERROR lines above).");
                return true;
            }
            return false;
        }

        private static bool HasReportWrittenSince(string reportDir, DateTime startedAtUtc)
        {
            if (string.IsNullOrEmpty(reportDir) || !Directory.Exists(reportDir)) return false;
            return Directory.EnumerateFiles(reportDir, $"*.{XygeniCommands.ReportSuffix}")
                .Any(reportPath => File.GetLastWriteTimeUtc(reportPath) >= startedAtUtc);
        }

        private static string LicenseNote(int exitCode)
        {
            return exitCode == LicenseErrorExitCode ? " - some scan types are not licensed and were skipped" : "";
        }

        private async Task GetEnvVariables(Dictionary<string, string> env)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string apiUrl = _configurationService.GetUrl();
            env["XYGENI_URL"] = apiUrl;

            string token = _configurationService.GetToken();
            if (!string.IsNullOrEmpty(token))
            {
                env["XYGENI_TOKEN"] = token;
            }

            ApplyProxyEnvironmentVariables(env, _configurationService.GetProxySettings());
        }

        private void ApplyProxyEnvironmentVariables(Dictionary<string, string> env, ProxySettings proxySettings)
        {
            if (proxySettings == null || string.IsNullOrWhiteSpace(proxySettings.Host))
            {
                return;
            }

            string protocol = string.IsNullOrWhiteSpace(proxySettings.Protocol) ? "http" : proxySettings.Protocol.Trim();
            string host = proxySettings.Host.Trim();
            string portPart = proxySettings.Port.HasValue ? $":{proxySettings.Port.Value}" : string.Empty;
            string credentials = string.Empty;

            if (!string.IsNullOrWhiteSpace(proxySettings.Username))
            {
                string username = Uri.EscapeDataString(proxySettings.Username.Trim());
                string password = Uri.EscapeDataString(proxySettings.Password ?? string.Empty);
                credentials = $"{username}:{password}@";
            }

            string proxyUrl = $"{protocol}://{credentials}{host}{portPart}";

            env["HTTP_PROXY"] = proxyUrl;
            env["HTTPS_PROXY"] = proxyUrl;

            if (!string.IsNullOrWhiteSpace(proxySettings.NonProxyHosts))
            {
                env["NO_PROXY"] = proxySettings.NonProxyHosts.Trim();
            }

            SetEnvIfPresent(env, "PROXY_PROTOCOL", proxySettings.Protocol);
            SetEnvIfPresent(env, "PROXY_HOST", proxySettings.Host);
            SetEnvIfPresent(env, "PROXY_AUTH", proxySettings.Authentication);
            SetEnvIfPresent(env, "PROXY_USERNAME", proxySettings.Username);
            SetEnvIfPresent(env, "PROXY_PASSWORD", proxySettings.Password);
            if (proxySettings.Port.HasValue)
            {
                env["PROXY_PORT"] = proxySettings.Port.Value.ToString();
            }
        }

        private void SetEnvIfPresent(Dictionary<string, string> env, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                env[key] = value.Trim();
            }
        }

        private string GetScannerScriptPath(string xygeniScannerPath)
        {
            return Path.Combine(xygeniScannerPath, "xygeni.ps1");
        }

        private static IReadOnlyList<string> GetScanTypes(string[] scanArgs)
        {
            const string runPrefix = "--run=";
            string runArg = scanArgs.FirstOrDefault(arg => arg.StartsWith(runPrefix, StringComparison.Ordinal));
            if (runArg == null)
            {
                return null;
            }

            return runArg.Substring(runPrefix.Length)
                .Split(',')
                .Select(scanType => scanType.Trim())
                .Where(scanType => scanType.Length > 0)
                .ToArray();
        }

        private string StripAnsiEscapeSequences(string text)
        {
            // Regex to strip ANSI escape codes
            return Regex.Replace(text, @"\x1B\[[^@-~]*[@-~]", String.Empty);
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
