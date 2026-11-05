using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private const string XygeniMCPBaseUrl = "https://get.xygeni.io/latest/mcp-server";
        private const string XygeniMCPLibraryUrl = XygeniMCPBaseUrl + "/xygeni-mcp-server.jar";
        // `<hash>  xygeni-mcp-server.jar` and the version on a single line, published with the JAR (#2381).
        private const string XygeniMCPChecksumUrl = XygeniMCPBaseUrl + "/xygeni-mcp-server.jar.sha256";
        private const string XygeniMCPVersionUrl = XygeniMCPBaseUrl + "/version.txt";
        private const string XygeniMCPLibraryName = "xygeni-mcp-server.jar";
        // Installed version, last remote check (shared by every IDE) and an update waiting for a locked JAR (#2382).
        private const string McpVersionFileName = "version.txt";
        private const string McpLastCheckFileName = "last-check.txt";
        private const string McpPendingLibraryName = "xygeni-mcp-server.jar.pending";
        private const string McpPendingVersionFileName = "version.txt.pending";
        private static readonly TimeSpan McpCheckInterval = TimeSpan.FromHours(24);
        // Folder the MCP server was installed in before #2382, inside each extension folder.
        private const string LegacyMcpLibraryFolder = ".xygeni-mcp";
        // The MCP folder is shared by every Xygeni IDE plugin: the lock keeps two of them from writing the JAR at once.
        private const string McpLockName = ".lock";
        private static readonly TimeSpan McpLockStaleAfter = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan McpLockWaitFor = TimeSpan.FromMinutes(2);

        public bool InstallationRunning { get; private set; }
        public bool IsInstalled { get; private set; }
        public string Status { get; private set; }

        /// <summary>What the last MCP server version check found; shown in the configuration view.</summary>
        public McpUpdateCheck LastMcpUpdateCheck { get; private set; }

        public event EventHandler Changed;

        /// <summary>What an MCP server download left behind: the JAR in place, or a `.pending` copy while a client locks it.</summary>
        public enum McpDownloadOutcome { Installed, Pending }

        public enum McpUpdateStatus { NotInstalled, Skipped, Unavailable, UpToDate, UpdateAvailable }

        /// <summary>Result of <see cref="CheckMcpUpdateAsync"/>; the caller decides what to show (#2382).</summary>
        public sealed class McpUpdateCheck
        {
            public McpUpdateStatus Status { get; set; }
            public string Installed { get; set; }
            public string Available { get; set; }
            public bool UpdateAvailable => Status == McpUpdateStatus.UpdateAvailable;
        }

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

        // The scanner lives in one fixed folder under the user's local app data, like the VS Code extension's and
        // (since 1.6.3) the IntelliJ plugin's: Visual Studio gives every extension version its own folder, so a
        // per-extension `.xygeni` was downloaded again (~250 MB) on each update. An existing complete copy is moved.
        public string GetScannerInstallationDir()
        {
            string fixedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".xygenidata", "scanner");
            MigrateExtensionScanner(fixedDir);
            return fixedDir;
        }

        private const int MinScannerJars = 50;
        private bool _scannerMigrationChecked;

        /// <summary>The .jar files under <c>&lt;dir&gt;\lib</c>; 0 when the folder is missing.</summary>
        private static int CountScannerJars(string dir)
        {
            string lib = Path.Combine(dir, "lib");
            return Directory.Exists(lib) ? Directory.GetFiles(lib, "*.jar").Length : 0;
        }

        private void MigrateExtensionScanner(string fixedDir)
        {
            if (_scannerMigrationChecked) return;
            _scannerMigrationChecked = true;
            try
            {
                if (CountScannerJars(fixedDir) >= MinScannerJars) return;
                // Visual Studio gives every extension version its own folder: the scanner of the previous version
                // is in a sibling of this one, so the siblings are searched too (as for the legacy MCP copies).
                var candidateFolders = new List<string> { _extensionPath };
                string extensionsDir = Path.GetDirectoryName(_extensionPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrEmpty(extensionsDir) && Directory.Exists(extensionsDir)) candidateFolders.AddRange(Directory.GetDirectories(extensionsDir));
                string legacy = candidateFolders.Select(folder => Path.Combine(folder, ".xygeni"))
                    .FirstOrDefault(folder => CountScannerJars(folder) >= MinScannerJars);
                if (legacy == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(fixedDir));
                if (Directory.Exists(fixedDir)) Directory.Delete(fixedDir, true);
                Directory.Move(legacy, fixedDir);
                _logger.Log($"  Scanner moved to the shared folder: {fixedDir}");
            }
            catch (Exception ex)
            {
                // A failed move only costs a download: the installer fills the fixed folder.
                _logger.Log($"  Could not move the scanner to {fixedDir}: {ex.Message}");
            }
        }

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

        /// <summary>The version written next to the installed JAR, or null when the JAR is missing or predates version.txt.</summary>
        public string GetMcpInstalledVersion()
        {
            if (!IsMcpLibraryInstalled()) return null;
            return McpVersion.FromVersionFile(ReadTextOrNull(Path.Combine(GetMcpLibraryInstallationDir(), McpVersionFileName)));
        }

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

                        // A scanner without its libraries cannot even run its updater: fail the install so it is retried.
                        int jars = Directory.Exists(Path.Combine(installPath, "lib")) ? Directory.GetFiles(Path.Combine(installPath, "lib"), "*.jar").Length : 0;
                        if (jars < MinScannerJars) throw new Exception($"Incomplete scanner install: {jars} jar(s) in {Path.Combine(installPath, "lib")}");

                        _logger.Log("");
                        _logger.Log($"      Xygeni Scanner installed successfully ({jars} libraries)");
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
                    string message = $"The Xygeni MCP server moved to {GetMcpLibraryPath()}. Update the path in your AI assistant's MCP configuration.";
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
                    if (await DownloadMcpJarLockedAsync().ConfigureAwait(false) == McpDownloadOutcome.Installed)
                    {
                        _logger.Log($"  Xygeni MCP Library Downloaded to: {installMcpPath}");
                    }
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
            _logger.Log("  Check the MCP Server section of Xygeni Settings to configure it.");
            _logger.Log("============================================================");
            return legacyMcpLibraryDirs.Count > 0;
        }

        /// <summary>Downloads the published MCP server over the installed one (the "Update" action).</summary>
        public async Task<McpDownloadOutcome> UpdateMcpLibraryAsync()
        {
            string installMcpPath = GetMcpLibraryInstallationDir();
            Directory.CreateDirectory(installMcpPath);
            McpDownloadOutcome outcome = McpDownloadOutcome.Installed;
            await WithMcpLockAsync(installMcpPath, async () =>
            {
                outcome = await DownloadMcpJarLockedAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (LastMcpUpdateCheck != null)
            {
                LastMcpUpdateCheck = new McpUpdateCheck
                {
                    Status = outcome == McpDownloadOutcome.Installed ? McpUpdateStatus.UpToDate : McpUpdateStatus.UpdateAvailable,
                    Installed = GetMcpInstalledVersion(),
                    Available = LastMcpUpdateCheck.Available
                };
            }
            OnChanged();
            return outcome;
        }

        /// <summary>
        /// Downloads the published JAR and puts it in place. Runs under the MCP lock. The remote version.txt is
        /// read first and written next to the JAR once it is installed; the download is verified against the
        /// published .sha256 when that file is available, and a mismatch keeps the JAR already installed (#2382).
        /// A running MCP client locks the JAR: the download is then kept as .pending and applied on the next start.
        /// </summary>
        private async Task<McpDownloadOutcome> DownloadMcpJarLockedAsync()
        {
            string installMcpPath = GetMcpLibraryInstallationDir();
            string mcpLibraryPath = GetMcpLibraryPath();
            string remoteVersion = McpVersion.FromVersionFile(await FetchTextAsync(XygeniMCPVersionUrl).ConfigureAwait(false));
            _logger.Log($"  MCP server published version: {remoteVersion ?? "unknown"}");
            _logger.Log($"  Downloading MCP library from: {XygeniMCPLibraryUrl} to: {installMcpPath}");

            // Download next to the JAR and move it into place, so an MCP client never runs a half-written JAR.
            string temporaryPath = mcpLibraryPath + ".tmp";
            try
            {
                await DownloadFileAsync(XygeniMCPLibraryUrl, temporaryPath).ConfigureAwait(false);

                string expectedChecksum = McpVersion.FromChecksumFile(await FetchTextAsync(XygeniMCPChecksumUrl).ConfigureAwait(false));
                if (expectedChecksum == null)
                {
                    _logger.Log($"  MCP server checksum is not available at {XygeniMCPChecksumUrl}; the download is not verified");
                }
                else
                {
                    string actualChecksum = await CalculateChecksumAsync(temporaryPath).ConfigureAwait(false);
                    if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"MCP server checksum mismatch (expected {expectedChecksum}, got {actualChecksum}); the installed copy is kept");
                    }
                    _logger.Log("  MCP server checksum verified");
                }
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
                if (!File.Exists(mcpLibraryPath))
                {
                    TryDeleteFile(temporaryPath);
                    throw;
                }
                // A running MCP client locks the JAR; the copy already installed keeps working and the update
                // waits as .pending until the next start.
                string pendingPath = Path.Combine(installMcpPath, McpPendingLibraryName);
                TryDeleteFile(pendingPath);
                File.Move(temporaryPath, pendingPath);
                WriteVersionFile(Path.Combine(installMcpPath, McpPendingVersionFileName), remoteVersion);
                _logger.Log($"  Could not replace {mcpLibraryPath} ({ex.Message}): an AI assistant is still using it. The update is applied on the next start.");
                return McpDownloadOutcome.Pending;
            }
            WriteVersionFile(Path.Combine(installMcpPath, McpVersionFileName), remoteVersion);
            DiscardPendingMcpUpdate();
            return McpDownloadOutcome.Installed;
        }

        /// <summary>
        /// Compares the installed MCP server with the published one. At most once every 24 h across IDEs unless
        /// <paramref name="force"/>; a .pending download is put in place first when the JAR is no longer locked.
        /// A remote version that cannot be read (no network, not published yet) gives Unavailable and is only logged (#2382).
        /// </summary>
        public async Task<McpUpdateCheck> CheckMcpUpdateAsync(bool force)
        {
            if (!IsMcpLibraryInstalled()) return new McpUpdateCheck { Status = McpUpdateStatus.NotInstalled };
            string installMcpPath = GetMcpLibraryInstallationDir();
            await ApplyPendingMcpUpdateAsync().ConfigureAwait(false);
            if (!force && !IsMcpCheckDue()) return new McpUpdateCheck { Status = McpUpdateStatus.Skipped };

            string available = McpVersion.FromVersionFile(await FetchTextAsync(XygeniMCPVersionUrl).ConfigureAwait(false));
            if (available == null)
            {
                _logger.Log($"  MCP server version not available at {XygeniMCPVersionUrl}; update check skipped");
                return new McpUpdateCheck { Status = McpUpdateStatus.Unavailable };
            }
            WriteTextQuietly(Path.Combine(installMcpPath, McpLastCheckFileName), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            string installed = GetMcpInstalledVersion();
            var check = new McpUpdateCheck
            {
                Status = McpVersion.IsNewer(available, installed) ? McpUpdateStatus.UpdateAvailable : McpUpdateStatus.UpToDate,
                Installed = installed,
                Available = available
            };
            LastMcpUpdateCheck = check;
            _logger.Log($"  MCP server installed: {installed ?? "unknown"}, published: {available}");
            OnChanged();
            return check;
        }

        /// <summary>
        /// Puts a .pending download in place when the JAR is no longer locked, whatever the update mode: the download
        /// already happened, only the file swap was waiting (MCP.md). Nothing to do without a .pending.
        /// </summary>
        public async Task ApplyPendingMcpUpdateAsync()
        {
            if (!IsMcpLibraryInstalled()) return;
            string installMcpPath = GetMcpLibraryInstallationDir();
            if (!File.Exists(Path.Combine(installMcpPath, McpPendingLibraryName))) return;
            bool applied = false;
            await WithMcpLockAsync(installMcpPath, () =>
            {
                applied = ApplyPendingMcpUpdateLocked();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            if (applied)
            {
                // The assistant that locked the JAR may still be running the old version: same note as after an update.
                XygeniConfigurationService.GetInstance().SaveMcpRestartPending(GetMcpInstalledVersion());
                OnChanged();
            }
        }

        private bool ApplyPendingMcpUpdateLocked()
        {
            string installMcpPath = GetMcpLibraryInstallationDir();
            string pendingPath = Path.Combine(installMcpPath, McpPendingLibraryName);
            string pendingVersionPath = Path.Combine(installMcpPath, McpPendingVersionFileName);
            if (!File.Exists(pendingPath)) return false;
            try
            {
                string mcpLibraryPath = GetMcpLibraryPath();
                if (File.Exists(mcpLibraryPath)) File.Replace(pendingPath, mcpLibraryPath, null);
                else File.Move(pendingPath, mcpLibraryPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.Log($"  Pending Xygeni MCP server update not applied yet, an AI assistant is still using the JAR: {ex.Message}");
                return false;
            }
            string pendingVersion = McpVersion.FromVersionFile(ReadTextOrNull(pendingVersionPath));
            WriteVersionFile(Path.Combine(installMcpPath, McpVersionFileName), pendingVersion);
            TryDeleteFile(pendingVersionPath);
            _logger.Log($"  Pending Xygeni MCP server update applied ({pendingVersion ?? "unknown version"})");
            return true;
        }

        private void DiscardPendingMcpUpdate()
        {
            string installMcpPath = GetMcpLibraryInstallationDir();
            TryDeleteFile(Path.Combine(installMcpPath, McpPendingLibraryName));
            TryDeleteFile(Path.Combine(installMcpPath, McpPendingVersionFileName));
        }

        private bool IsMcpCheckDue()
        {
            string content = ReadTextOrNull(Path.Combine(GetMcpLibraryInstallationDir(), McpLastCheckFileName));
            if (content == null || !long.TryParse(content.Trim(), out long lastCheck)) return true;
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastCheck > (long)McpCheckInterval.TotalMilliseconds;
        }

        // GET a small text resource (version, checksum); null on any failure, which is logged and never thrown.
        private async Task<string> FetchTextAsync(string url)
        {
            try
            {
                using (var httpClient = CreateHttpClient())
                {
                    // Tiny files read at startup: fail fast rather than hold anything up.
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    using (var response = await httpClient.GetAsync(url).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Log($"  GET {url}: HTTP {(int)response.StatusCode}");
                            return null;
                        }
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"  GET {url} failed: {ex.Message}");
                return null;
            }
        }

        private static string ReadTextOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private void WriteTextQuietly(string path, string content)
        {
            try { File.WriteAllText(path, content); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.Log($"  Could not write {path}: {ex.Message}");
            }
        }

        // Writes the version next to the JAR; an unknown version removes the file so the next check updates it.
        private void WriteVersionFile(string path, string version)
        {
            if (version == null) TryDeleteFile(path);
            else WriteTextQuietly(path, version + "\n");
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
                    if ((int)response.StatusCode >= 500)
                    {
                        // The server is there but cannot answer (down behind a proxy, overloaded): not a bad token.
                        LastConnectionProblem = $"The Xygeni server is unavailable (HTTP {(int)response.StatusCode}). Try again later";
                        return false;
                    }
                    LastConnectionProblem = null;
                    return response.StatusCode == System.Net.HttpStatusCode.OK;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking Xygeni Token");
                LastConnectionProblem = $"The Xygeni server is unreachable ({ex.Message}). Try again later";
                return false;
            }
        }

        /// <summary>Why the last token check failed when the server, not the token, was the problem; null otherwise.</summary>
        public string LastConnectionProblem { get; private set; }

        private const int PingAttempts = 3;
        private static readonly TimeSpan PingRetryDelay = TimeSpan.FromSeconds(3);

        public async Task<bool> IsValidApiUrlAsync(string xygeniApiUrl)
        {
            // Recomputed on every check: a problem of a previous URL (or before "Allow insecure HTTP") must not stick.
            LastConnectionProblem = null;
            // An http URL to a non-loopback host would send the token in clear: refused before any request (#1438).
            string insecureProblem = InsecureUrl.Problem(xygeniApiUrl, XygeniConfigurationService.GetInstance().GetAllowInsecureHttp());
            if (insecureProblem != null)
            {
                _logger?.Log($"  {insecureProblem}");
                LastConnectionProblem = insecureProblem;
                return false;
            }
            if (InsecureUrl.IsInsecureRemote(xygeniApiUrl))
            {
                _logger?.Log($"  WARNING: {InsecureUrl.Warning} ({xygeniApiUrl}); allowed by the settings");
            }
            string pingUrl = $"{xygeniApiUrl.TrimEnd('/')}/ping";
            // A backend that is starting (a local one, a deploy) answers 5xx or refuses the connection for a few
            // seconds: retry before calling it disconnected, so the user does not have to click to reconnect.
            for (int attempt = 1; attempt <= PingAttempts; attempt++)
            {
                bool transientFailure;
                try
                {
                    using (var httpClient = CreateHttpClient())
                    {
                        var response = await httpClient.GetAsync(pingUrl);
                        // 503 = the server answered but is busy: its /ping reports 503 above a CPU threshold
                        // (PingController.max.cpu). The URL is right; the token check decides.
                        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                        {
                            _logger.Log("The Xygeni server is busy (503 on /ping); going on with the token check");
                            return true;
                        }
                        if (response.StatusCode == System.Net.HttpStatusCode.OK) return true;
                        transientFailure = (int)response.StatusCode >= 500;
                        _logger.Log($"Xygeni API URL check: HTTP {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error checking Xygeni API URL");
                    transientFailure = true;
                }
                if (!transientFailure || attempt == PingAttempts) return false;
                _logger.Log($"Retrying the connection in {PingRetryDelay.TotalSeconds:F0}s ({attempt}/{PingAttempts})");
                await Task.Delay(PingRetryDelay).ConfigureAwait(false);
            }
            return false;
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

        /// <summary>The MCP server was installed, updated or its restart note dismissed: the views repaint (#2382).</summary>
        public void NotifyMcpStateChanged() => OnChanged();

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
