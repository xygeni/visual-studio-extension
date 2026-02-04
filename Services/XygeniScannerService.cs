using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using vs2026_plugin.Services;
using vs2026_plugin.Commands;

namespace vs2026_plugin.Services
{
   
    public class ScanResult
    {
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } // "running", "completed", "failed"
        public object IssuesFound { get; set; }
        public string Summary { get; set; }
    }

    public class XygeniScannerService
    {
        private static XygeniScannerService _instance;

        // Configuration Keys
        private const string CollectionPath = "XygeniConfiguration";
        private const string ApiUrlKey = "ApiUrl";

        // Constants
        private const int TimeoutMs = 1800000; // 30 minutes

        private readonly string[] _runAnalysisArgs = { 
            "scan", 
            "--run=deps,secrets,misconf,iac,suspectdeps,sast", 
            "-f", "json", 
            "-o", XygeniCommands.ReportSuffix, 
            "--no-upload", 
            "--include-vulnerabilities" 
        };
        
        private readonly string[] _runRectifyScaArgs = { "util", "rectify", "--sca" };
        private readonly string[] _runRectifySastArgs = { "util", "rectify", "--sast" };

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

        public async Task RunAnalysisAsync(string sourceFolder, string xygeniScannerPath)
        {
            _exitCode = null;

            var timestamp = DateTime.Now;

            // Keep only last 5 scans
            if (_scans.Count > 5)
            {
                _scans.RemoveAt(0);
            }

            _logger.Log("");
            _logger.Log("=================================================");
            _logger.Log($"  Running scan on source folder: {sourceFolder}");

            var currentScan = new ScanResult { Timestamp = timestamp, Status = "running", IssuesFound = null, Summary = "" };
            _scans.Add(currentScan);
            OnChanged();

            try
            {
                await RunAnalysisCommandAsync(sourceFolder, xygeniScannerPath, _logger);

                _logger.Log("  Scanner finished");
                
                _scans.Remove(currentScan);
                
                var totalTime = (DateTime.Now - timestamp).TotalSeconds;
                _scans.Add(new ScanResult { 
                    Timestamp = timestamp, 
                    Status = "completed", 
                    IssuesFound = null, 
                    Summary = $"Duration: {totalTime:F2}s" 
                });
                
                _exitCode = 0;
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
                    Summary = "" 
                });
            }
            finally
            {
                OnChanged();
            }
        }

        public async Task RunAnalysisCommandAsync(string sourceFolder, string xygeniInstallPath, ILogger logger)
        {
            // args include -d sourceFolder.
            
            var args = new List<string>(_runAnalysisArgs);
            args.Add("-d");
            args.Add(sourceFolder);
            
            // The output json report is saved to the metadata folder

            var projectMetadataFolder = await XygeniConfigurationService.GetInstance().GetMetadataFolderAsyncForProject();
            
            await CallScannerAsync(xygeniInstallPath, args, logger, projectMetadataFolder);
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

        private async Task CallScannerAsync(string xygeniInstallPath, List<string> args, ILogger logger, string workingDir)
        {
            if (_scannerRunning)
            {
                throw new Exception("Scanner is already running");
            }
            _scannerRunning = true;

            try
            {
                await ExecuteScannerCallAsync(xygeniInstallPath, args, logger, workingDir);
            }
            finally
            {
                _scannerRunning = false;
            }
        }

        private async Task ExecuteScannerCallAsync(string xygeniInstallPath, List<string> args, ILogger logger, string workingDir)
        {
            await Task.Run(async () =>
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

                // Add arguments carefully
                // TS: ["-NoProfile","-ExecutionPolicy", "Bypass", "-File", scannerScriptPath, ...args]
                string psArgs = $"-NoProfile -ExecutionPolicy Bypass -File \"{scannerScriptPath}\"";
                
                // Append other args wrapping in quotes if needed
                foreach(var arg in args)
                {
                    psArgs += $" \"{arg}\"";
                }
                
                startInfo.Arguments = psArgs;

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
                    process.OutputDataReceived += (sender, e) => 
                    { 
                        if (e.Data != null) _logger.Log(StripAnsiEscapeSequences(e.Data)); 
                    };
                    process.ErrorDataReceived += (sender, e) => 
                    { 
                        if (e.Data != null) _logger.Log(StripAnsiEscapeSequences(e.Data)); 
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool exited = process.WaitForExit(TimeoutMs);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        throw new Exception("Scanner process timeout");
                    }

                    if (process.ExitCode != 0 && process.ExitCode <= 128)
                    {
                        throw new Exception($"Scanner process failed with exit code {process.ExitCode}");
                    }
                }
            });
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

            // Note: Proxy settings are not yet implemented in C# side
        }

        private string GetScannerScriptPath(string xygeniScannerPath)
        {
            return Path.Combine(xygeniScannerPath, "xygeni.ps1");
        }

        private string StripAnsiEscapeSequences(string text)
        {
            // Regex to strip ANSI escape codes
            return text; //Regex.Replace(text, @"\x1B\[[^@-~]*[@-~]", String.Empty);
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
