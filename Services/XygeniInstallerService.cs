using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using vs2026_plugin.Services;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
    public class XygeniInstallerService
    {
        private static XygeniInstallerService _instance;
        private readonly string _extensionPath;
        private readonly ILogger _logger;

        private const string XygeniGetScannerUrl = "https://get.xygeni.io/latest/scanner/";
        private const string XygeniScannerZipName = "xygeni_scanner.zip";
        private const string XygeniScannerZipRootFolder = "xygeni_scanner";
        private const string XygeniScannerChecksumUrl = "https://raw.githubusercontent.com/xygeni/xygeni/main/checksum/latest/xygeni-release.zip.sha256";

        private const string XygeniMCPLibraryUrl = "https://get.xygeni.io/latest/mcp-server/xygeni-mcp-server.jar";
        private const string XygeniMCPLibraryName = "xygeni-mcp-server.jar";

        public bool InstallationRunning { get; private set; }
        public bool IsInstalled { get; private set; }
        public string Status { get; private set; }

        public event EventHandler Changed;

        private XygeniInstallerService(string extensionPath, ILogger logger)
        {
            _extensionPath = extensionPath;
            _logger = logger;
            CheckScannerInstallation();
        }

        public static XygeniInstallerService GetInstance() {
            if (_instance == null) throw new NullReferenceException("XygeniInstallerService instance is not initialized");
            return _instance;
        }

        public static XygeniInstallerService GetInstance(string extensionPath, ILogger logger)
        {
            if (_instance == null)
            {
                _instance = new XygeniInstallerService(extensionPath, logger);
            }
            return _instance;
        }

        public string GetScannerInstallationDir() => Path.Combine(_extensionPath, ".xygeni");

        public string GetMcpLibraryInstallationDir() => Path.Combine(_extensionPath, ".xygeni-mcp");

        public string GetMcpLibraryPath() => Path.Combine(GetMcpLibraryInstallationDir(), XygeniMCPLibraryName);

        public bool CheckScannerInstallation()
        {
            string installPath = GetScannerInstallationDir();
            IsInstalled = Directory.Exists(installPath) &&
                   (File.Exists(Path.Combine(installPath, "xygeni")) ||
                    File.Exists(Path.Combine(installPath, "xygeni.ps1")));
            return IsInstalled;
        }

        public bool IsMcpLibraryInstalled() => File.Exists(GetMcpLibraryPath());

        public async Task InstallAsync(string apiUrl = null, string token = null, bool overrideInstallation = false)
        {

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            Status = "running";
            IsInstalled = false;
            InstallationRunning = true;
            OnChanged();

            _logger.Log("");
            _logger.Log("==================================================");
            _logger.Log("===    Starting Xygeni Scanner Installation    ===");
            _logger.Log("");

            try
            {
                


                string installPath = GetScannerInstallationDir();
                _logger.Log($"    Installing Xygeni at directory: {installPath}");

                string tempDirPath = Path.Combine(Path.GetTempPath(), $"xygeni_installer_{DateTime.Now.Ticks}");
                Directory.CreateDirectory(tempDirPath);

                string zipPath = Path.Combine(tempDirPath, XygeniScannerZipName);
                string scannerUrl = $"{XygeniGetScannerUrl}{XygeniScannerZipName}";

                try
                {
                    // ensure install tasks is not in the UI thread
                    await Task.Run(async () =>
                    {
                        await DownloadFileAsync(scannerUrl, zipPath).ConfigureAwait(false);

                        string checksumPath = zipPath + ".sha256";
                        await DownloadFileAsync(XygeniScannerChecksumUrl, checksumPath).ConfigureAwait(false);

                        string checksumFileContent = await ReadAllTextAsync(checksumPath).ConfigureAwait(false);
                        string expectedChecksum = checksumFileContent.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        string actualChecksum = await CalculateChecksumAsync(zipPath);

                        if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new Exception($"Checksum validation failed. Expected: {expectedChecksum}, Got: {actualChecksum}");
                        }

                        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDirPath));

                        string extractedRoot = Path.Combine(tempDirPath, XygeniScannerZipRootFolder);
                        if (!Directory.Exists(extractedRoot))
                        {
                             throw new Exception($"Expected root folder {XygeniScannerZipRootFolder} not found in the zip file.");
                        }

                        if (Directory.Exists(installPath))
                        {
                            Directory.Delete(installPath, true);
                        }
                        Directory.CreateDirectory(installPath);

                        CopyDirectory(extractedRoot, installPath);

                        _logger.Log("");
                        _logger.Log("      Xygeni Scanner installed successfully ");
                        _logger.Log("==================================================");                                               

                    }).ConfigureAwait(false);

                    // Switch back to UI thread for state updates
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    Status = "success";
                    IsInstalled = true;

                }
                finally
                {
                    if (Directory.Exists(tempDirPath))
                    {
                        try { Directory.Delete(tempDirPath, true); } catch { }
                    }
                    InstallationRunning = false;                    
                    OnChanged();
                }
            }
            catch (Exception ex)
            {
                InstallationRunning = false;
                _logger.Error(ex, "  Installation process failed");
                Status = "error";
                OnChanged();
                throw;
            }
        }

        public async Task DownloadMCPLibraryAsync()
        {
            string installMcpPath = GetMcpLibraryInstallationDir();
            string mcpLibraryPath = GetMcpLibraryPath();

            _logger.Log("");
            _logger.Log("============================================================");

            if (File.Exists(mcpLibraryPath))
            {
                _logger.Log($"  MCP Library already exists at: {installMcpPath}");
                _logger.Log("  Check Xygeni MCP Setup to configure it.");
                _logger.Log("============================================================");
                return;
            }

            if (!Directory.Exists(installMcpPath))
            {
                Directory.CreateDirectory(installMcpPath);
            }

            _logger.Log($"  Downloading MCP library from: {XygeniMCPLibraryUrl} to: {installMcpPath}");
            await DownloadFileAsync(XygeniMCPLibraryUrl, mcpLibraryPath);

            _logger.Log($"  Xygeni MCP Library Downloaded to: {installMcpPath}");
            _logger.Log("  Check Xygeni MCP Setup to configure it.");
            _logger.Log("============================================================");
        }

        public async Task<bool> IsValidTokenAsync(string xygeniApiUrl, string xygeniToken)
        {
            string testApiUrl = $"{xygeniApiUrl.TrimEnd('/')}/language";
            try
            {
                using (var httpClient = CreateHttpClient())
                using (var request = new HttpRequestMessage(HttpMethod.Get, testApiUrl))
                {
                    if (!string.IsNullOrEmpty(xygeniToken))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {xygeniToken}");
                    }
                    var response = await httpClient.SendAsync(request);
                    return response.StatusCode == System.Net.HttpStatusCode.OK;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking Xygeni Token");
                return false;
            }
        }

        public async Task<bool> IsValidApiUrlAsync(string xygeniApiUrl)
        {
            string pingUrl = $"{xygeniApiUrl.TrimEnd('/')}/ping";
            try
            {
                using (var httpClient = CreateHttpClient())
                {
                    var response = await httpClient.GetAsync(pingUrl);
                    return response.StatusCode == System.Net.HttpStatusCode.OK;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking Xygeni API URL");
                return false;
            }
        }

        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using (var httpClient = CreateHttpClient())
            using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
        }

        private async Task<string> CalculateChecksumAsync(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = await Task.Run(() => sha256.ComputeHash(stream));
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (string subdir in Directory.GetDirectories(sourceDir))
            {
                string destSubdir = Path.Combine(destDir, Path.GetFileName(subdir));
                CopyDirectory(subdir, destSubdir);
            }
        }

        private async Task<string> ReadAllTextAsync(string path)
        {
            using (var reader = File.OpenText(path))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            var proxySettings = GetProxySettingsSafe();
            var webProxy = BuildWebProxy(proxySettings);

            if (webProxy != null)
            {
                handler.Proxy = webProxy;
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }

            return new HttpClient(handler, disposeHandler: true);
        }

        private ProxySettings GetProxySettingsSafe()
        {
            try
            {
                return XygeniConfigurationService.GetInstance().GetProxySettings();
            }
            catch
            {
                return null;
            }
        }

        private IWebProxy BuildWebProxy(ProxySettings proxySettings)
        {
            if (proxySettings == null || string.IsNullOrWhiteSpace(proxySettings.Host))
            {
                return null;
            }

            string protocol = string.IsNullOrWhiteSpace(proxySettings.Protocol) ? "http" : proxySettings.Protocol.Trim();
            string host = proxySettings.Host.Trim();
            string proxyUri = proxySettings.Port.HasValue
                ? $"{protocol}://{host}:{proxySettings.Port.Value}"
                : $"{protocol}://{host}";

            var webProxy = new WebProxy(proxyUri);

            if (!string.IsNullOrWhiteSpace(proxySettings.Username))
            {
                webProxy.Credentials = new NetworkCredential(proxySettings.Username.Trim(), proxySettings.Password ?? string.Empty);
            }
            else if (string.Equals(proxySettings.Authentication, "default", StringComparison.OrdinalIgnoreCase))
            {
                webProxy.Credentials = CredentialCache.DefaultCredentials;
            }

            if (!string.IsNullOrWhiteSpace(proxySettings.NonProxyHosts))
            {
                webProxy.BypassList = proxySettings.NonProxyHosts
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return webProxy;
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
