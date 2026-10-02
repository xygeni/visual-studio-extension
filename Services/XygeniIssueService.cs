using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using vs2026_plugin.Models;
using vs2026_plugin.Services;
using vs2026_plugin.Commands;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Threading;

namespace vs2026_plugin.Services
{
    public class XygeniIssueService
    {
        private static XygeniIssueService _instance;
        private readonly ILogger _logger;
        private readonly XygeniScannerService _scannerService;
        private List<IXygeniIssue> _issues = new List<IXygeniIssue>();
        private bool _isReadingIssues = false;
        private bool _pendingReadIssues = false;
        // Scan types of the reads deferred while another read runs; null = one of them was a full read.
        private IReadOnlyList<string> _pendingScanTypes;

        public event EventHandler IssuesChanged;

        private XygeniIssueService(ILogger logger)
        {
            _logger = logger;
            _scannerService = XygeniScannerService.GetInstance();
            _scannerService.Changed += (s, e) =>
            {
                OnScannerChanged();
            };
        }

        private void OnScannerChanged()
        {
            var latestScan = _scannerService.GetScans()
                .OrderByDescending(scan => scan.Timestamp)
                .FirstOrDefault();

            // Refresh issues only when a scan has ended. A "running" update is emitted at scan start.
            if (latestScan != null && string.Equals(latestScan.Status, "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = ReadIssuesAsync(latestScan?.ScanTypes);
        }

       
        

        public static XygeniIssueService GetInstance() => GetInstance(null);    

        public static XygeniIssueService GetInstance(ILogger logger)
        {
            if (_instance == null)
            {
                if (logger == null) throw new ArgumentNullException(nameof(logger));
                _instance = new XygeniIssueService(logger);
            }
            return _instance;
        }

        public List<IXygeniIssue> GetIssues() => _issues;

        public List<IXygeniIssue> GetIssuesByCategory(string category)
        {
            return _issues.Where(issue => issue.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public IXygeniIssue FindIssueById(string id)
        {
            return _issues.FirstOrDefault(issue => string.Equals(issue.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        // Full read: every report is re-read (startup, or a scan whose types are unknown).
        public Task ReadIssuesAsync() => ReadIssuesAsync(null);

        // scanTypes = the `--run=` list of the scan that just ended: only those reports were rewritten,
        // so only their categories are dropped and re-read; the rest keep their current findings.
        public async Task ReadIssuesAsync(IReadOnlyList<string> scanTypes)
        {
            _logger.Log("");
            _logger.Log("==================================================");
            _logger.Log("  Reading issues...");

            try
            {
                string workingDir = await XygeniConfigurationService.GetInstance().GetMetadataFolderAsyncForProject();
                if (string.IsNullOrEmpty(workingDir))
                {
                    _logger.Log("  Working directory is null or empty, skipping...");
                    return;
                }
                _logger.Log("  Working directory: " + workingDir);
                string suffix = XygeniCommands.ReportSuffix;
                if (_isReadingIssues)
                {
                    _pendingScanTypes = _pendingReadIssues ? MergeScanTypes(_pendingScanTypes, scanTypes) : scanTypes;
                    _pendingReadIssues = true;
                    _logger.Log("  Issues are already being read, scheduling a refresh...");
                    return;
                }
                _logger.Log("  Issues report directory: " + workingDir);
                _isReadingIssues = true;
                if (scanTypes == null)
                {
                    _issues.Clear();
                }
                else
                {
                    _issues.RemoveAll(issue => scanTypes.Any(scanType => string.Equals(CategoryOf(scanType), issue.Category, StringComparison.OrdinalIgnoreCase)));
                }

                await ReadScannerOutputAsync(workingDir, suffix, scanTypes);

                _logger.Log($"  {_issues.Count} issues read.");
                
                OnIssuesChanged();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading issues output");
                OnIssuesChanged();
            }
            finally
            {
                _isReadingIssues = false;
                _logger.Log("==================================================");

                if (_pendingReadIssues)
                {
                    _pendingReadIssues = false;
                    var pendingScanTypes = _pendingScanTypes;
                    _pendingScanTypes = null;
                    _ = ReadIssuesAsync(pendingScanTypes);
                }
            }
        }

        // A full read (null) absorbs the scoped ones; otherwise the deferred reads collapse into one union.
        private static IReadOnlyList<string> MergeScanTypes(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            if (first == null || second == null) return null;
            return first.Union(second, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task ReadScannerOutputAsync(string workingDir, string suffix, IReadOnlyList<string> scanTypes)
        {
            try
            {
                if (ShouldRead("secrets", scanTypes)) await ReadSecretsReportAsync(Path.Combine(workingDir, $"secrets.{suffix}"));
                if (ShouldRead("misconf", scanTypes)) await ReadMisconfReportAsync(Path.Combine(workingDir, $"misconf.{suffix}"));
                if (ShouldRead("sast", scanTypes)) await ReadSastReportAsync(Path.Combine(workingDir, $"sast.{suffix}"));
                if (ShouldRead("quality", scanTypes)) await ReadQualityReportAsync(Path.Combine(workingDir, $"quality.{suffix}"));
                if (ShouldRead("iac", scanTypes)) await ReadIacReportAsync(Path.Combine(workingDir, $"iac.{suffix}"));
                if (ShouldRead("deps", scanTypes)) await ReadDepsReportAsync(Path.Combine(workingDir, $"deps.{suffix}"));
                if (ShouldRead("apisec", scanTypes)) await ReadApisecReportAsync(Path.Combine(workingDir, $"apisec.{suffix}"));
                if (ShouldRead("ai", scanTypes)) await ReadAiReportAsync(Path.Combine(workingDir, $"ai.{suffix}"));

                // Sort issues by severity
                _issues = _issues.OrderBy(issue => issue.GetSeverityLevel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading scanner output:");
                throw;
            }
        }

        // null = full read; otherwise only the reports named by the scan types.
        private static bool ShouldRead(string scanType, IReadOnlyList<string> scanTypes)
        {
            return scanTypes == null || scanTypes.Contains(scanType, StringComparer.OrdinalIgnoreCase);
        }

        // Report files are named after the scan type; the parsers file `deps` findings under "sca".
        private static string CategoryOf(string scanType)
        {
            return string.Equals(scanType, "deps", StringComparison.OrdinalIgnoreCase) ? "sca" : scanType;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadMisconfReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessMisconfReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading misconf output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadSastReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessSastReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading sast output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadQualityReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessQualityReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading quality output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadIacReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessIacReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading iac output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadDepsReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessDepsReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading deps output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadApisecReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessApisecReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading apisec output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadAiReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessAiReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading ai output:");
                throw;
            }
            return Task.CompletedTask;
        }

        // Synchronous file read wrapped in a Task so the report readers share one signature.
        public Task ReadSecretsReportAsync(string filename)
        {
            if (!File.Exists(filename)) return Task.CompletedTask;

            try
            {
                string data = File.ReadAllText(filename);
                var rawData = JsonConvert.DeserializeObject<JObject>(data);
                ProcessSecretsReport(rawData);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading secrets output:");
                throw;
            }
            return Task.CompletedTask;
        }

        private void ProcessMisconfReport(JObject jsonRaw)
        {
            var misconfigurations = jsonRaw["misconfigurations"] as JArray ?? new JArray { jsonRaw["misconfigurations"] };
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var rawMisconf in misconfigurations)
            {
                if (rawMisconf == null || rawMisconf.Type == JTokenType.Null) continue;

                var issue = new MisconfXygeniIssue
                {
                    Id = rawMisconf["issueId"]?.ToString(),
                    Type = rawMisconf["type"]?.ToString(),
                    Detector = rawMisconf["detector"]?.ToString(),
                    Tool = tool,
                    Kind = "misconfiguration",
                    Severity = rawMisconf["severity"]?.ToString(),
                    Confidence = rawMisconf["confidence"]?.ToString() ?? "high",
                    Category = "misconf",
                    CategoryName = "Misconfigurations",
                    ToolKind = rawMisconf["properties"]?["tool_kind"]?.ToString(),
                    File = rawMisconf["location"]?["filepath"]?.ToString(),
                    BeginLine = int.TryParse(rawMisconf["location"]?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                    EndLine = int.TryParse(rawMisconf["location"]?["endLine"]?.ToString(), out int el) ? el : 0,
                    BeginColumn = int.TryParse(rawMisconf["location"]?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                    EndColumn = int.TryParse(rawMisconf["location"]?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                    Code = rawMisconf["location"]?["code"]?.ToString(),
                    Explanation = rawMisconf["explanation"]?.ToString(),
                    Url = rawMisconf["url"]?.ToString(),
                    CurrentBranch = jsonRaw["currentBranch"]?.ToString(),
                    RemediableLevel = "none"
                };
                _issues.Add(issue);
            }
        }

        private void ProcessSastReport(JObject jsonRaw)
        {
            var sast_vuln = jsonRaw["vulnerabilities"] as JArray ?? new JArray { jsonRaw["vulnerabilities"] };
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var raw_vuln in sast_vuln)
            {
                if (raw_vuln == null || raw_vuln.Type == JTokenType.Null) continue;

                var issue = new SastXygeniIssue
                {
                    Id = raw_vuln["issueId"]?.ToString(),
                    Type = raw_vuln["kind"]?.ToString(),
                    Detector = raw_vuln["detector"]?.ToString(),
                    Tool = tool,
                    Kind = "code_vulnerability",
                    Severity = raw_vuln["severity"]?.ToString(),
                    Confidence = raw_vuln["confidence"]?.ToString() ?? "high",
                    Category = "sast",
                    CategoryName = "SAST",
                    File = raw_vuln["location"]?["filepath"]?.ToString(),
                    BeginLine = int.TryParse(raw_vuln["location"]?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                    EndLine = int.TryParse(raw_vuln["location"]?["endLine"]?.ToString(), out int el) ? el : 0,
                    BeginColumn = int.TryParse(raw_vuln["location"]?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                    EndColumn = int.TryParse(raw_vuln["location"]?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                    Code = raw_vuln["location"]?["code"]?.ToString(),
                    Explanation = raw_vuln["explanation"]?.ToString(),
                    Url = raw_vuln["url"]?.ToString(),
                    Tags = raw_vuln["tags"]?.ToObject<List<string>>(),
                    Branch = jsonRaw["currentBranch"]?.ToString(),
                    Cwe = raw_vuln["cwe"]?.ToString(),
                    Cwes = raw_vuln["cwes"]?.ToObject<List<string>>(),
                    Container = raw_vuln["container"]?.ToString(),
                    Language = raw_vuln["language"]?.ToString(),
                    CodeFlows = ParseCodeFlows(raw_vuln["codeFlows"] as JArray),
                    RawJson = raw_vuln.ToString(Formatting.None),
                    RemediableLevel = AbstractXygeniIssue.RemediableAuto
                };
                _issues.Add(issue);
            }
        }

        private void ProcessQualityReport(JObject jsonRaw)
        {
            // Top-level key CONFIRMED against a real quality.<suffix> report: Code Quality reuses
            // the SAST scanner infra, so findings live under `vulnerabilities` (narrow `qualityIssues`
            // fallback only, for forward-compat). See PR #45 and the fixture-backed tests in the
            // sibling plugins (vscode/intellij).
            var quality_items = jsonRaw["vulnerabilities"] as JArray
                ?? jsonRaw["qualityIssues"] as JArray
                ?? new JArray();
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var raw in quality_items)
            {
                if (raw == null || raw.Type == JTokenType.Null) continue;

                var issue = new QualityXygeniIssue
                {
                    Id = raw["issueId"]?.ToString(),
                    Type = raw["kind"]?.ToString() ?? raw["type"]?.ToString() ?? raw["ruleId"]?.ToString(),
                    Detector = raw["detector"]?.ToString(),
                    Tool = tool,
                    Kind = "quality_issue",
                    Severity = raw["severity"]?.ToString() ?? "info",
                    Confidence = raw["confidence"]?.ToString() ?? "high",
                    Category = "quality",
                    CategoryName = "Code Quality",
                    // Real reports carry the quality dimension in `kind` (e.g. "reliability",
                    // "maintainability"); `category`/`properties.category` are only present in
                    // older/other shapes → keep them as the preferred fallbacks (PR #45 fix).
                    QualityCategory = raw["category"]?.ToString()
                        ?? raw["properties"]?["category"]?.ToString()
                        ?? raw["kind"]?.ToString(),
                    File = raw["location"]?["filepath"]?.ToString(),
                    BeginLine = int.TryParse(raw["location"]?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                    EndLine = int.TryParse(raw["location"]?["endLine"]?.ToString(), out int el) ? el : 0,
                    BeginColumn = int.TryParse(raw["location"]?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                    EndColumn = int.TryParse(raw["location"]?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                    Code = raw["location"]?["code"]?.ToString(),
                    Explanation = raw["explanation"]?.ToString() ?? raw["message"]?.ToString(),
                    Url = raw["url"]?.ToString(),
                    Tags = raw["tags"]?.ToObject<List<string>>(),
                    Branch = jsonRaw["currentBranch"]?.ToString(),
                    Language = raw["language"]?.ToString(),
                    RemediableLevel = AbstractXygeniIssue.RemediableAuto
                };
                _issues.Add(issue);
            }
        }

        private void ProcessIacReport(JObject jsonRaw)
        {
            var flaws = jsonRaw["flaws"] as JArray ?? new JArray { jsonRaw["flaws"] };
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var flaw in flaws)
            {
                if (flaw == null || flaw.Type == JTokenType.Null) continue;

                var issue = new IacXygeniIssue
                {
                    Id = flaw["issueId"]?.ToString(),
                    Type = flaw["type"]?.ToString(),
                    Detector = flaw["detector"]?.ToString(),
                    Tool = tool,
                    Kind = "iac_flaw",
                    Severity = flaw["severity"]?.ToString(),
                    Confidence = flaw["confidence"]?.ToString() ?? "high",
                    Resource = flaw["resource"]?.ToString(),
                    Provider = flaw["provider"]?.ToString(),
                    FoundBy = flaw["detector"]?.ToString(),
                    Category = "iac",
                    CategoryName = "IaC",
                    File = flaw["location"]?["filepath"]?.ToString(),
                    BeginLine = int.TryParse(flaw["location"]?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                    EndLine = int.TryParse(flaw["location"]?["endLine"]?.ToString(), out int el) ? el : 0,
                    BeginColumn = int.TryParse(flaw["location"]?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                    EndColumn = int.TryParse(flaw["location"]?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                    Code = flaw["location"]?["code"]?.ToString(),
                    Tags = flaw["tags"]?.ToObject<List<string>>(),
                    Explanation = flaw["explanation"]?.ToString(),
                    Url = flaw["url"]?.ToString(),
                    Branch = jsonRaw["currentBranch"]?.ToString(),
                    RemediableLevel = "none"
                };
                _issues.Add(issue);
            }
        }

        private void ProcessDepsReport(JObject jsonRaw)
        {
            var dependencies = jsonRaw["dependencies"] as JArray ?? new JArray { jsonRaw["dependencies"] };
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var dep in dependencies)
            {
                if (dep == null || dep.Type == JTokenType.Null) continue;
                if (dep["vulnerabilities"] == null) continue;

                foreach (var vuln in dep["vulnerabilities"])
                {
                    var location = dep["paths"]?["locations"]?[0];
                    string publishDateStr = vuln["published"]?.ToString();
                    DateTime publishDate = !string.IsNullOrEmpty(publishDateStr) ? DateTime.Parse(publishDateStr) : DateTime.Now;

                    var issue = new VulnXygeniIssue
                    {
                        Id = vuln["id"]?.ToString(),
                        Type = vuln["id"]?.ToString(),
                        Virtual = dep["virtual"]?.Value<bool>() ?? false,
                        Url = vuln["source"]?["url"]?.ToString(),
                        Detector = vuln["source"]?["name"]?.ToString() ?? "unknown",
                        Tool = tool,
                        Kind = "sca_vulnerability",
                        RepositoryType = dep["repositoryType"]?.ToString(),
                        DisplayFileName = dep["displayFileName"]?.ToString(),
                        Group = dep["group"]?.ToString(),
                        Name = dep["name"]?.ToString(),
                        Version = dep["version"]?.ToString(),
                        DependencyPaths = dep["paths"]?["dependencyPaths"]?.ToObject<List<string>>(),
                        DirectDependency = dep["paths"]?["directDependency"]?.Value<bool>() ?? false,
                        Severity = vuln["severity"]?.ToString(),
                        Confidence = dep["confidence"]?.ToString() ?? "high",
                        Category = "sca",
                        CategoryName = "SCA",
                        File = location?["filepath"]?.ToString() ?? dep["fileName"]?.ToString() ?? dep["displayFileName"]?.ToString(),
                        BeginLine = int.TryParse(location?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                        EndLine = int.TryParse(location?["endLine"]?.ToString(), out int el) ? el : 0,
                        BeginColumn = int.TryParse(location?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                        EndColumn = int.TryParse(location?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                        Code = location?["code"]?.ToString(),
                        Explanation = vuln["description"]?.ToString() ?? "Vulnerability " + vuln["cve"]?.ToString(),
                        Tags = GetTags(dep["tags"], dep["remediable"]?["remediableLevel"]?.ToString()),
                        BaseScore = GetBaseScore(vuln["ratings"] as JArray),
                        Versions = SummarizeVersionRange(vuln["versions"] as JArray),
                        PublicationDate = publishDate.ToString("g"),
                        Weakness = vuln["cwes"]?.ToObject<List<string>>(),
                        References = vuln["references"]?.ToObject<List<string>>(),
                        Vector = GetVector(vuln["ratings"] as JArray),
                        Language = dep["language"]?.ToString(),
                        RemediableLevel = dep["remediable"]?["remediableLevel"]?.ToString() ?? "none"
                    };
                    _issues.Add(issue);
                }
            }
        }

        private void ProcessSecretsReport(JObject jsonRaw)
        {
            var secrets = jsonRaw["secrets"] as JArray ?? new JArray { jsonRaw["secrets"] };
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var rawSecret in secrets)
            {
                if (rawSecret == null || rawSecret.Type == JTokenType.Null) continue;

                var issue = new SecretsXygeniIssue
                {
                    Id = rawSecret["issueId"]?.ToString(),
                    Type = rawSecret["type"]?.ToString(),
                    Hash = rawSecret["hash"]?.ToString(),
                    Detector = rawSecret["detector"]?.ToString(),
                    Tool = tool,
                    Kind = "secret",
                    Severity = rawSecret["severity"]?.ToString(),
                    Confidence = rawSecret["confidence"]?.ToString() ?? "high",
                    Resource = rawSecret["resource"]?.ToString() ?? "",
                    FoundBy = rawSecret["detector"]?.ToString() ?? "",
                    Category = "secrets",
                    CategoryName = "Secrets",
                    File = rawSecret["location"]?["filepath"]?.ToString() ?? "",
                    BeginLine = int.TryParse(rawSecret["location"]?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                    EndLine = int.TryParse(rawSecret["location"]?["endLine"]?.ToString(), out int el) ? el : 0,
                    BeginColumn = int.TryParse(rawSecret["location"]?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                    EndColumn = int.TryParse(rawSecret["location"]?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                    Code = rawSecret["location"]?["code"]?.ToString() ?? "",
                    Explanation = $"Secret of type '{rawSecret["type"]}' detected by '{rawSecret["detector"]}'",
                    Tags = rawSecret["tags"]?.ToObject<List<string>>(),
                    Url = rawSecret["url"]?.ToString() ?? "",
                    Secret = rawSecret["location"]?["secret"]?.ToString() ?? "",
                    TimeAdded = long.TryParse(rawSecret["location"]?["timeAdded"]?.ToString(), out long ta) ? ta : 0,
                    Branch = rawSecret["location"]?["branch"]?.ToString() ?? "",
                    CommitHash = rawSecret["location"]?["commitHash"]?.ToString() ?? "",
                    User = rawSecret["location"]?["user"]?.ToString() ?? "",
                    RemediableLevel = "none"
                };
                _issues.Add(issue);
            }
        }

        private void ProcessApisecReport(JObject jsonRaw)
        {
            // Findings live under `flaws`; the sibling `services` array is the discovered API inventory,
            // which is where the flaws' source positions live (see ResolveApisecLocation).
            var flaws = jsonRaw["flaws"] as JArray ?? new JArray();
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            var handlerByEndpointId = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            var specFileByModuleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IndexApiInventory(jsonRaw["services"] as JArray, handlerByEndpointId, specFileByModuleName);

            foreach (var flaw in flaws)
            {
                if (flaw == null || flaw.Type == JTokenType.Null) continue;

                ResolveApisecLocation(flaw, handlerByEndpointId, specFileByModuleName, out string file, out int line);
                var issue = new ApisecXygeniIssue
                {
                    Id = flaw["issueId"]?.ToString(),
                    // `flawType` is the machine type and the tree label, like every other category; the
                    // human `title` (it embeds the endpoint) goes to the details panel.
                    Type = flaw["flawType"]?.ToString() ?? flaw["title"]?.ToString() ?? "",
                    Title = flaw["title"]?.ToString(),
                    Detector = flaw["detector"]?.ToString(),
                    Tool = tool,
                    Kind = "api_flaw",
                    Severity = flaw["severity"]?.ToString() ?? "info",
                    Confidence = flaw["confidence"]?.ToString() ?? "high",
                    Category = "apisec",
                    CategoryName = "API Security",
                    File = file,
                    BeginLine = line,
                    EndLine = line,
                    BeginColumn = 0,
                    EndColumn = 0,
                    Code = "",
                    Explanation = flaw["explanation"]?.ToString() ?? "",
                    Url = flaw["url"]?.ToString() ?? "",
                    Tags = flaw["tags"]?.ToObject<List<string>>() ?? new List<string>(),
                    Branch = jsonRaw["currentBranch"]?.ToString(),
                    EndpointMethod = flaw["endpointMethod"]?.ToString(),
                    EndpointPath = flaw["endpointPath"]?.ToString(),
                    ModuleName = flaw["moduleName"]?.ToString(),
                    ServiceName = flaw["serviceName"]?.ToString(),
                    OwaspApiTop10 = flaw["owaspApiTop10"]?.ToObject<List<string>>(),
                    Cwes = flaw["cwes"]?.ToObject<List<string>>(),
                    Remediation = flaw["remediation"]?.ToString(),
                    RemediableLevel = "none"
                };
                _issues.Add(issue);
            }
        }

        // services[].modules[].endpoints[]: each endpoint carries its handler {file, line}, keyed here by
        // "<METHOD> <path>" (the flaws' `endpointId`); each module the OpenAPI spec it was read from.
        private static void IndexApiInventory(JArray services, IDictionary<string, JToken> handlerByEndpointId, IDictionary<string, string> specFileByModuleName)
        {
            if (services == null) return;

            foreach (var service in services)
            {
                var modules = service?["modules"] as JArray;
                if (modules == null) continue;

                foreach (var module in modules)
                {
                    string moduleName = module?["name"]?.ToString();
                    string specFile = module?["location"]?["file"]?.ToString();
                    if (!string.IsNullOrEmpty(moduleName) && !string.IsNullOrEmpty(specFile))
                    {
                        specFileByModuleName[moduleName] = specFile;
                    }

                    var endpoints = module?["endpoints"] as JArray;
                    if (endpoints == null) continue;

                    foreach (var endpoint in endpoints)
                    {
                        var handler = endpoint?["handler"];
                        if (handler == null || handler.Type == JTokenType.Null) continue;
                        handlerByEndpointId[$"{endpoint["method"]} {endpoint["path"]}"] = handler;
                    }
                }
            }
        }

        // Resolution order: the flaw's own `location` (rare), its endpoint's handler, its `handler_file`
        // property (service-scoped flaws, no line), the module's OpenAPI spec (no line); else location-less.
        private static void ResolveApisecLocation(JToken flaw, IDictionary<string, JToken> handlerByEndpointId, IDictionary<string, string> specFileByModuleName, out string file, out int line)
        {
            file = flaw["location"]?["filepath"]?.ToString() ?? "";
            line = int.TryParse(flaw["location"]?["beginLine"]?.ToString(), out int locationLine) ? locationLine : 0;
            if (!string.IsNullOrEmpty(file)) return;

            string endpointId = flaw["endpointId"]?.ToString();
            if (string.IsNullOrEmpty(endpointId))
            {
                endpointId = $"{flaw["endpointMethod"]} {flaw["endpointPath"]}";
            }
            if (handlerByEndpointId.TryGetValue(endpointId, out JToken handler))
            {
                file = handler["file"]?.ToString() ?? "";
                line = int.TryParse(handler["line"]?.ToString(), out int handlerLine) ? handlerLine : 0;
                if (!string.IsNullOrEmpty(file)) return;
            }

            string handlerFile = flaw["properties"]?["handler_file"]?.ToString();
            if (!string.IsNullOrEmpty(handlerFile))
            {
                file = handlerFile;
                line = 0;
                return;
            }

            string moduleName = flaw["moduleName"]?.ToString();
            if (!string.IsNullOrEmpty(moduleName) && specFileByModuleName.TryGetValue(moduleName, out string specFile))
            {
                file = specFile;
                line = 0;
            }
        }

        private void ProcessAiReport(JObject jsonRaw)
        {
            // The AI report does not reuse the SAST field names: severity is `severityFloor`, the
            // id is `id`, the detector `detectorId` and the explanation `description`. There is no
            // kind/type either, so the detector id doubles as the finding's label.
            var vulnerabilities = jsonRaw["vulnerabilities"] as JArray ?? new JArray();
            string tool = jsonRaw["metadata"]?["reportProperties"]?["tool.name"]?.ToString();

            foreach (var vulnerability in vulnerabilities)
            {
                if (vulnerability == null || vulnerability.Type == JTokenType.Null) continue;

                var location = vulnerability["location"];
                string detectorId = vulnerability["detectorId"]?.ToString() ?? "";
                var issue = new AiXygeniIssue
                {
                    Id = vulnerability["id"]?.ToString(),
                    Type = detectorId,
                    Detector = detectorId,
                    Tool = tool,
                    Kind = "ia_vulnerability",
                    Severity = vulnerability["severityFloor"]?.ToString() ?? "info",
                    Confidence = vulnerability["confidence"]?.ToString() ?? "high",
                    Category = "ai",
                    CategoryName = "AI Security",
                    File = location?["filepath"]?.ToString() ?? "",
                    BeginLine = int.TryParse(location?["beginLine"]?.ToString(), out int bl) ? bl : 0,
                    EndLine = int.TryParse(location?["endLine"]?.ToString(), out int el) ? el : 0,
                    BeginColumn = int.TryParse(location?["beginColumn"]?.ToString(), out int bc) ? bc : 0,
                    EndColumn = int.TryParse(location?["endColumn"]?.ToString(), out int ec) ? ec : 0,
                    Code = location?["code"]?.ToString() ?? "",
                    Explanation = vulnerability["description"]?.ToString() ?? "",
                    Url = vulnerability["url"]?.ToString() ?? "",
                    Tags = vulnerability["tags"]?.ToObject<List<string>>() ?? new List<string>(),
                    Branch = jsonRaw["currentBranch"]?.ToString(),
                    AssetKind = vulnerability["assetKind"]?.ToString(),
                    Standards = GetStandardControlIds(vulnerability["standards"] as JArray),
                    RedTeamVectors = vulnerability["redTeamVectors"]?.ToObject<List<string>>(),
                    RemediationHint = vulnerability["remediationHint"]?.ToString(),
                    // AI fix via the scanner's `util rectify --ai` (RectifyCommand.java: --ai → runAiRectify).
                    RemediableLevel = AbstractXygeniIssue.RemediableAuto
                };
                _issues.Add(issue);
            }
        }

        // `standards` is a list of {standard, version, controlId}; the control id (LLM01, ASI05...)
        // is what reads well in the detail panel.
        private List<string> GetStandardControlIds(JArray standards)
        {
            var controlIds = new List<string>();
            if (standards == null) return controlIds;

            foreach (var standard in standards)
            {
                // An entry is {std, version, controlId}; tolerate a bare string so one odd entry never drops every AI finding.
                string controlId = standard is JObject standardRef
                    ? standardRef["controlId"]?.ToString() ?? standardRef["std"]?.ToString()
                    : standard?.ToString();
                if (!string.IsNullOrEmpty(controlId)) controlIds.Add(controlId);
            }
            return controlIds;
        }

        private List<CodeFlow> ParseCodeFlows(JArray rawCodeFlows)
        {
            var result = new List<CodeFlow>();
            if (rawCodeFlows == null) return result;

            foreach (var rawFlow in rawCodeFlows)
            {
                if (rawFlow == null || rawFlow.Type == JTokenType.Null) continue;

                var flow = new CodeFlow
                {
                    Tags = rawFlow["tags"]?.ToObject<List<string>>() ?? new List<string>()
                };

                var rawFrames = rawFlow["frames"] as JArray;
                if (rawFrames != null)
                {
                    foreach (var rawFrame in rawFrames)
                    {
                        if (rawFrame == null || rawFrame.Type == JTokenType.Null) continue;

                        var location = rawFrame["location"];
                        var frame = new CodeFlowFrame
                        {
                            Kind = rawFrame["kind"]?.ToString(),
                            Container = rawFrame["container"]?.ToString(),
                            InjectPoint = rawFrame["injectPoint"]?.ToString(),
                            Category = rawFrame["category"]?.ToString(),
                            FilePath = location?["filepath"]?.ToString() ?? rawFrame["filePath"]?.ToString(),
                            BeginLine = int.TryParse((location?["beginLine"] ?? rawFrame["beginLine"])?.ToString(), out int bl) ? bl : 0,
                            EndLine = int.TryParse((location?["endLine"] ?? rawFrame["endLine"])?.ToString(), out int el) ? el : 0,
                            BeginColumn = int.TryParse((location?["beginColumn"] ?? rawFrame["beginColumn"])?.ToString(), out int bc) ? bc : 0,
                            EndColumn = int.TryParse((location?["endColumn"] ?? rawFrame["endColumn"])?.ToString(), out int ec) ? ec : 0,
                            Code = (location?["code"] ?? rawFrame["code"])?.ToString()
                        };
                        flow.Frames.Add(frame);
                    }
                }
                result.Add(flow);
            }

            return result;
        }

        private List<string> GetTags(JToken tags, string remediable)
        {
            var tagsList = tags?.ToObject<List<string>>() ?? new List<string>();
            if (!string.IsNullOrEmpty(remediable))
            {
                tagsList.Add(remediable);
            }
            return tagsList;
        }

        private double GetBaseScore(JArray ratings)
        {
            if (ratings != null && ratings.Count > 0)
            {
                return ratings.Last["score"]?.Value<double>() ?? 0;
            }
            return 0;
        }

        private string GetVector(JArray ratings)
        {
            if (ratings != null && ratings.Count > 0)
            {
                return ratings.Last["vector"]?.ToString() ?? "";
            }
            return "";
        }

        private string SummarizeVersionRange(JArray versions)
        {
            if (versions == null || versions.Count == 0) return "";

            var parts = new List<string>();
            foreach (var v in versions)
            {
                var subParts = new List<string>();
                string start = v["startVersion"]?.ToString();
                if (!string.IsNullOrEmpty(start) && start != "0")
                {
                    bool excluded = v["versionStartExcluded"]?.Value<bool>() ?? false;
                    subParts.Add($">{(excluded ? "" : "=")}{start}");
                }

                string end = v["endVersion"]?.ToString();
                if (!string.IsNullOrEmpty(end) && end != "0")
                {
                    bool excluded = v["versionEndExcluded"]?.Value<bool>() ?? false;
                    subParts.Add($"<{(excluded ? "" : "=")}{end}");
                }
                parts.Add(string.Join(" ", subParts));
            }
            return string.Join(" | ", parts);
        }

        public async Task<string> GetDetectorDocAsync(string url, string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                _logger.Log("  Xygeni token not found, skipping vuln retrieve...");
                throw new Exception("token_not_found");
            }

            using (var client = new System.Net.Http.HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                try
                {
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        if (errorContent.Contains("detector_not_found"))
                        {
                            throw new Exception("detector_not_found");
                        }
                        throw new Exception($"Error reading detector doc: {response.ReasonPhrase}");
                    }
                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error loading issue doc");
                    throw;
                }
            }
        }

        protected virtual void OnIssuesChanged()
        {
            IssuesChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    
}
