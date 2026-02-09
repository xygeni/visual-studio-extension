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
        private List<IXygeniIssue> _issues = new List<IXygeniIssue>();
        private bool _isReadingIssues = false;

        public event EventHandler IssuesChanged;

        private XygeniIssueService(ILogger logger)
        {
            _logger = logger;
            XygeniScannerService.GetInstance().Changed += (s, e) =>
            {
                ReadIssuesAsync();
            };
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
            return _issues.Where(i => i.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public IXygeniIssue FindIssueById(string id)
        {
            return _issues.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public async Task ReadIssuesAsync()
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
                    _logger.Log("  Issues are already being read, skipping...");
                    return;
                }
                _logger.Log("  Issues report directory: " + workingDir);
                _isReadingIssues = true;
                _issues.Clear();
                
                await ReadScannerOutputAsync(workingDir, suffix);

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
            }
        }

        public async Task ReadScannerOutputAsync(string workingDir, string suffix)
        {
            try
            {
                await ReadSecretsReportAsync(Path.Combine(workingDir, $"secrets.{suffix}"));
                await ReadMisconfReportAsync(Path.Combine(workingDir, $"misconf.{suffix}"));
                await ReadSastReportAsync(Path.Combine(workingDir, $"sast.{suffix}"));
                await ReadIacReportAsync(Path.Combine(workingDir, $"iac.{suffix}"));
                await ReadDepsReportAsync(Path.Combine(workingDir, $"deps.{suffix}"));

                // Sort issues by severity
                _issues = _issues.OrderBy(i => i.GetSeverityLevel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading scanner output:");
                throw;
            }
        }

        public async Task ReadMisconfReportAsync(string filename)
        {
            if (!File.Exists(filename)) return;

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
        }

        public async Task ReadSastReportAsync(string filename)
        {
            if (!File.Exists(filename)) return;

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
        }

        public async Task ReadIacReportAsync(string filename)
        {
            if (!File.Exists(filename)) return;

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
        }

        public async Task ReadDepsReportAsync(string filename)
        {
            if (!File.Exists(filename)) return;

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
        }

        public async Task ReadSecretsReportAsync(string filename)
        {
            if (!File.Exists(filename)) return;

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
