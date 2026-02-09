using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
    public class RemediationService
    {
        private static RemediationService _instance;
        private readonly ILogger _logger;

        private RemediationService(ILogger logger)
        {
            _logger = logger;
        }

        public static RemediationService GetInstance(ILogger logger = null)
        {
            if (_instance == null)
            {
                if (logger == null)
                {
                    logger = vs2026_pluginPackage.Instance?.Logger;
                }
                
                if (logger == null) throw new Exception("Logger is required");
                
                _instance = new RemediationService(logger);
            }
            return _instance;
        }

        public async Task<FixData> LaunchRemediationPreviewAsync(string kind, string issueId, string fileUri, string xygeniInstallPath)
        {
            // check installer is ready
            var installerService = XygeniInstallerService.GetInstance();
            if (!installerService.IsInstalled)
            {
                _logger.Log("Xygeni is not ready. Please install it first.");
                return null;
            }            


            _logger.Log($"Run remediation for kind {kind}");

            switch (kind)
            {
                case "code_vulnerability":
                    return await PreviewDiffSastRemediationAsync(issueId, fileUri, xygeniInstallPath);
                case "sca_vulnerability":
                    return await PreviewDiffScaRemediationAsync(issueId, fileUri, xygeniInstallPath);
                case "secret":
                case "misconfiguration":
                case "iac_flaw":
                    _logger.Log($"Remediation preview not supported for {kind}");
                    return new FixData { TempFile = null, Explanation = null };
                default:
                    return new FixData { TempFile = null, Explanation = null };
            }
        }

        private async Task<FixData> PreviewDiffSastRemediationAsync(string issueId, string fileUri, string xygeniInstallPath)
        {
            try
            {
                if (string.IsNullOrEmpty(issueId)) return new FixData { TempFile = null, Explanation = null };

                var issueService = XygeniIssueService.GetInstance();
                var issue = issueService.FindIssueById(issueId);
                if (issue == null)
                {
                    _logger.Log($"Issue not found: {issueId}");
                    throw new Exception($"Issue not found: {issueId}");
                }

                // Ensure file path is absolute
                string absoluteFileUri = fileUri;
                if (!Path.IsPathRooted(absoluteFileUri))
                {
                    string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                    if (!string.IsNullOrEmpty(rootDir))
                    {
                        absoluteFileUri = Path.Combine(rootDir, absoluteFileUri);
                    }
                }

                if (!File.Exists(absoluteFileUri))
                {
                    _logger.Log($"File not found: {absoluteFileUri}");
                    throw new FileNotFoundException($"File not found: {absoluteFileUri}");
                }

                // Generate temp folder and copy file
                string tempDir = Path.Combine(Path.GetTempPath(), "xygeni_rem_" + Guid.NewGuid().ToString().Substring(0, 8));
                Directory.CreateDirectory(tempDir);
                
                string fileName = Path.GetFileName(absoluteFileUri);
                string tempFile = Path.Combine(tempDir, fileName);
                File.Copy(absoluteFileUri, tempFile);

                // Call scanner
                var scanner = XygeniScannerService.GetInstance();
                await scanner.RunRectifySastCommandAsync(tempFile, issue.Detector, issue.BeginLine.ToString(), xygeniInstallPath, _logger);

                // Return fix data            
                const string explanation = "No explanation available";
                return new FixData { TempFile = tempFile, Explanation = explanation };
            }
            catch (Exception ex)
            {
                _logger.Log($"Error applying remediation: {ex.Message}");
                throw;
            }
        }

        private async Task<FixData> PreviewDiffScaRemediationAsync(string issueId, string fileUri, string xygeniInstallPath)
        {
            try
            {
                if (string.IsNullOrEmpty(issueId)) return new FixData { TempFile = null, Explanation = null };

                var issueService = XygeniIssueService.GetInstance();
                var vulnIssue = issueService.FindIssueById(issueId) as VulnXygeniIssue;
                if (vulnIssue == null)
                {
                    _logger.Log($"Issue not found: {issueId}");
                    throw new Exception($"Issue not found: {issueId}");
                }

                string dependencyGavt = $"{vulnIssue.Name}:{vulnIssue.Version}:{vulnIssue.Language}";
                if (!string.IsNullOrEmpty(vulnIssue.Group)) dependencyGavt = $"{vulnIssue.Group}:{dependencyGavt}";

                // Ensure file path is absolute
                string absoluteFileUri = fileUri;
                if (!Path.IsPathRooted(absoluteFileUri))
                {
                    string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                    if (!string.IsNullOrEmpty(rootDir))
                    {
                        absoluteFileUri = Path.Combine(rootDir, absoluteFileUri);
                    }
                }

                if (!File.Exists(absoluteFileUri))
                {
                    _logger.Log($"File not found: {absoluteFileUri}");
                    throw new FileNotFoundException($"File not found: {absoluteFileUri}");
                }

                // Generate temp folder and copy file
                string tempDir = Path.Combine(Path.GetTempPath(), "xygeni_rem_" + Guid.NewGuid().ToString().Substring(0, 8));
                Directory.CreateDirectory(tempDir);
                
                string fileName = Path.GetFileName(absoluteFileUri);
                string tempFile = Path.Combine(tempDir, fileName);
                File.Copy(absoluteFileUri, tempFile);

                // Call scanner
                var scanner = XygeniScannerService.GetInstance();
                await scanner.RunRectifyScaCommandAsync(tempFile, dependencyGavt, xygeniInstallPath, _logger);

                _logger.Log($"SCA remediation applied to {fileUri} on {tempFile}. Check changes before save...");

                // Return fix data            
                const string explanation = "No explanation available";
                return new FixData { TempFile = tempFile, Explanation = explanation };
            }
            catch (Exception ex)
            {
                _logger.Log($"Error applying remediation: {ex.Message}");
                throw;
            }
        }
    }
}
