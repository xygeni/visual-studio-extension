using System;
using System.Collections.Generic;
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
        // Folder the MCP server was installed in before #2382, inside each extension folder.
        private const string LegacyMcpLibraryFolder = ".xygeni-mcp";
        // The MCP folder is shared by every Xygeni IDE plugin: the lock keeps two of them from writing the JAR at once.
        private const string McpLockName = ".lock";
        private static readonly TimeSpan McpLockStaleAfter = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan McpLockWaitFor = TimeSpan.FromMinutes(2);

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

        // Shared with the other Xygeni IDE plugins and independent of the extension version, so the path the
        // user pasted into their MCP client survives extension updates (#2382).
        public string GetMcpLibraryInstallationDir() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xygeni", "mcp");

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

                // The MCP server was never downloaded before #2382; a reinstall also refreshes it.
                await InstallMcpLibraryAsync(overrideInstallation);
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

        /// <summary>
        /// Installs the MCP library without ever failing the caller. When it replaced a copy inside an
        /// extension folder, the user is told to update the path in their MCP client configuration.
        /// </summary>
        public async Task InstallMcpLibraryAsync(bool force = false)
        {
            try
            {
                bool movedFromLegacyPath = await DownloadMCPLibraryAsync(force).ConfigureAwait(false);
                if (movedFromLegacyPath)
                {
                    string message = $"The Xygeni MCP server moved to {GetMcpLibraryPath()}. Update the path in your MCP client configuration.";
                    _logger.Log(message);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, "Xygeni MCP server",
                        OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "  Error downloading Xygeni MCP library. MCP Server will not be available");
            }
        }

        /// <summary>
        /// Makes sure the MCP library is in the shared folder: downloads it when missing, or always with
        /// <paramref name="force"/> (reinstall). Legacy copies inside extension folders are removed afterwards.
        /// </summary>
        /// <returns>true when a legacy copy was found, so the path in the user's MCP client is stale.</returns>
        public async Task<bool> DownloadMCPLibraryAsync(bool force = false)
        {
            string installMcpPath = GetMcpLibraryInstallationDir();
            string mcpLibraryPath = GetMcpLibraryPath();
            List<string> legacyMcpLibraryDirs = FindLegacyMcpLibraryDirs();

            _logger.Log("");
            _logger.Log("============================================================");

            if (!force && File.Exists(mcpLibraryPath))
            {
                _logger.Log($"  MCP Library already exists at: {installMcpPath}");
            }
            else
            {
                Directory.CreateDirectory(installMcpPath);
                await WithMcpLockAsync(installMcpPath, async () =>
                {
                    // Another IDE may have installed it while this one waited for the lock.
                    if (!force && File.Exists(mcpLibraryPath)) return;

                    _logger.Log($"  Downloading MCP library from: {XygeniMCPLibraryUrl} to: {installMcpPath}");
                    // Download next to the JAR and move it into place, so an MCP client never runs a half-written JAR.
                    string temporaryPath = mcpLibraryPath + ".tmp";
                    try
                    {
                        await DownloadFileAsync(XygeniMCPLibraryUrl, temporaryPath).ConfigureAwait(false);
                    }
                    catch
                    {
                        TryDeleteFile(temporaryPath);
                        throw;
                    }
                    try
                    {
                        if (File.Exists(mcpLibraryPath)) File.Replace(temporaryPath, mcpLibraryPath, null);
                        else File.Move(temporaryPath, mcpLibraryPath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        TryDeleteFile(temporaryPath);
                        // A running MCP client locks the JAR; the copy already installed keeps working.
                        if (!File.Exists(mcpLibraryPath)) throw;
                        _logger.Log($"  Could not replace {mcpLibraryPath} ({ex.Message}). Close your MCP client and reinstall to update it.");
                        return;
                    }
                    _logger.Log($"  Xygeni MCP Library Downloaded to: {installMcpPath}");
                }).ConfigureAwait(false);
            }

            if (!File.Exists(mcpLibraryPath))
            {
                _logger.Log($"  MCP Library not found at: {installMcpPath}. MCP server is not available.");
                _logger.Log("============================================================");
                return false;
            }
            foreach (string legacyDir in legacyMcpLibraryDirs)
            {
                _logger.Log($"  Removing previous MCP library folder: {legacyDir}");
                try
                {
                    Directory.Delete(legacyDir, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // An MCP client still running the old JAR locks it; it is retried on the next start.
                    _logger.Log($"  Could not remove {legacyDir}: {ex.Message}");
                }
            }
            _logger.Log("  Check Xygeni MCP Setup to configure it.");
            _logger.Log("============================================================");
            return legacyMcpLibraryDirs.Count > 0;
        }

        // MCP library folders left by versions before #2382: `.xygeni-mcp` inside this extension folder or inside
        // the folder of another installed version (Visual Studio gives each installed version its own folder).
        private List<string> FindLegacyMcpLibraryDirs()
        {
            var candidateFolders = new List<string> { _extensionPath };
            string extensionsDir = Path.GetDirectoryName(_extensionPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(extensionsDir) && Directory.Exists(extensionsDir))
            {
                candidateFolders.AddRange(Directory.GetDirectories(extensionsDir));
            }

            var legacyDirs = new List<string>();
            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in candidateFolders)
            {
                string legacyDir = Path.GetFullPath(Path.Combine(folder, LegacyMcpLibraryFolder));
                if (seenDirs.Add(legacyDir) && File.Exists(Path.Combine(legacyDir, XygeniMCPLibraryName)))
                {
                    legacyDirs.Add(legacyDir);
                }
            }
            return legacyDirs;
        }

        // Runs `action` holding <mcpDir>/.lock, created exclusively. A lock held by another IDE is waited for;
        // one older than McpLockStaleAfter was left by a crashed process and is taken over.
        private async Task WithMcpLockAsync(string mcpDir, Func<Task> action)
        {
            string lockPath = Path.Combine(mcpDir, McpLockName);
            DateTime waitUntil = DateTime.UtcNow + McpLockWaitFor;
            while (true)
            {
                try
                {
                    using (new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    break;
                }
                catch (IOException)
                {
                    // Gone between the two calls: try again. A missing folder would fail forever: give up.
                    if (!File.Exists(lockPath))
                    {
                        if (!Directory.Exists(mcpDir)) throw;
                        continue;
                    }
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) > McpLockStaleAfter)
                    {
                        TryDeleteFile(lockPath);
                    }
                    else if (DateTime.UtcNow > waitUntil)
                    {
                        throw new IOException($"MCP library is being installed by another IDE ({lockPath})");
                    }
                    else
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }
            }
            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                TryDeleteFile(lockPath);
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try { File.Delete(filePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
