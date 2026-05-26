using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using vs2026_plugin.Models;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// IDE seat license validation. Mirrors vscode-extension/src/xygeni/service/license.ts:
    ///   - On first call generates a machine fingerprint
    ///     SHA-256(hostname | primaryMac | platform | arch) and persists it under
    ///     LocalApplicationData\.xygenidata\fingerprint.dat.
    ///   - POSTs the fingerprint object to /internal/license/ideaccess with a Bearer
    ///     token. HTTP 200 means the IDE seat is allowed; anything else gates features.
    ///   - On Dispose releases the seat via /internal/license/ideaccess/uninstall.
    /// </summary>
    public class LicenseService : IDisposable
    {
        public const string FingerprintFileName = "fingerprint.dat";
        private const string DataFolder = ".xygenidata";

        private static LicenseService _instance;

        private readonly ILogger _logger;
        private readonly string _fingerprintFilePath;
        private bool _disposed;
        private bool _isLicenseAvailable = false; // fail-closed until first successful check

        public event EventHandler Changed;

        public bool IsLicenseAvailable => _isLicenseAvailable;
        public bool LicenseChecked { get; private set; }

        private LicenseService(ILogger logger)
        {
            _logger = logger;
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string extDir = Path.Combine(baseDir, DataFolder);
            Directory.CreateDirectory(extDir);
            _fingerprintFilePath = Path.Combine(extDir, FingerprintFileName);
        }

        public static LicenseService GetInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("LicenseService has not been initialized.");
            }
            return _instance;
        }

        public static LicenseService GetInstance(ILogger logger)
        {
            if (_instance == null)
            {
                if (logger == null) throw new ArgumentNullException(nameof(logger));
                _instance = new LicenseService(logger);
            }
            return _instance;
        }

        public async Task<bool> IsValidLicenseAsync(string apiUrl, string token)
        {
            if (string.IsNullOrEmpty(apiUrl))
            {
                _logger.Log("Xygeni URL not found, license cannot be checked.");
                SetLicenseAvailable(false);
                return false;
            }
            if (string.IsNullOrEmpty(token))
            {
                _logger.Log("Xygeni token not found, license cannot be checked.");
                SetLicenseAvailable(false);
                return false;
            }

            try
            {
                string fingerprintJson = GetOrCreateFingerprintJson();
                bool ok = await CallInstallLicenseAsync(fingerprintJson, apiUrl, token);
                SetLicenseAvailable(ok);
                if (ok)
                {
                    _logger.Log("Xygeni IDE License is available.");
                }
                else
                {
                    _logger.Log("Xygeni IDE License is NOT available.");
                }
                return ok;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating Xygeni IDE License");
                SetLicenseAvailable(false);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                string apiUrl = TryGetUrl();
                if (string.IsNullOrEmpty(apiUrl)) return;
                if (!File.Exists(_fingerprintFilePath)) return;

                string fingerprintJson = File.ReadAllText(_fingerprintFilePath);
                if (string.IsNullOrEmpty(fingerprintJson)) return;

                // Best-effort, fire-and-forget. Blocking the UI thread on shutdown can
                // trigger VS "is busy" dialogs; if the request does not complete before
                // the process exits the seat will be reclaimed by the backend timeout.
                _ = Task.Run(async () =>
                {
                    try { await CallUninstallLicenseAsync(fingerprintJson, apiUrl); }
                    catch (Exception ex) { _logger?.Error(ex, "Error releasing Xygeni IDE License seat"); }
                });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error releasing Xygeni IDE License seat");
            }
        }

        private void SetLicenseAvailable(bool value)
        {
            LicenseChecked = true;
            if (_isLicenseAvailable == value) return;
            _isLicenseAvailable = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private string GetOrCreateFingerprintJson()
        {
            if (File.Exists(_fingerprintFilePath))
            {
                string existing = File.ReadAllText(_fingerprintFilePath);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }
            }

            var fingerprintObj = BuildFingerprint();
            string json = JsonConvert.SerializeObject(fingerprintObj);
            try
            {
                File.WriteAllText(_fingerprintFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Could not persist machine fingerprint file");
            }
            return json;
        }

        private static object BuildFingerprint()
        {
            string hostname = SafeMachineName();
            string mac = GetPrimaryMac() ?? string.Empty;
            string platform = "win32"; // Visual Studio extension is Windows-only.
            string arch = GetArch();

            string raw = string.Join("|", new[] { hostname, mac, platform, arch });
            string sha = Sha256Hex(raw);

            return new
            {
                hostname = hostname,
                platform = platform,
                mac = mac,
                fingerprint = sha
            };
        }

        private static string SafeMachineName()
        {
            try { return Environment.MachineName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string GetArch()
        {
            try
            {
                // RuntimeInformation.OSArchitecture is available in .NET Framework 4.7.1+.
                return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                    .ToString().ToLowerInvariant();
            }
            catch
            {
                return Environment.Is64BitOperatingSystem ? "x64" : "x86";
            }
        }

        private static string GetPrimaryMac()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
                    .FirstOrDefault(n => !string.IsNullOrEmpty(n.GetPhysicalAddress()?.ToString())
                                         && n.GetPhysicalAddress().ToString() != "000000000000");

                if (nic == null) return null;

                byte[] bytes = nic.GetPhysicalAddress().GetAddressBytes();
                if (bytes == null || bytes.Length == 0) return null;
                return string.Join(":", bytes.Select(b => b.ToString("x2")));
            }
            catch
            {
                return null;
            }
        }

        private static string Sha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private async Task<bool> CallInstallLicenseAsync(string fingerprintJson, string apiUrl, string token)
        {
            string url = $"{apiUrl.TrimEnd('/')}/internal/license/ideaccess";
            using (var httpClient = CreateHttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                request.Content = new StringContent(fingerprintJson, Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Log($"Error response installing Xygeni IDE License: {(int)response.StatusCode}");
                }
                return response.StatusCode == HttpStatusCode.OK;
            }
        }

        private async Task<bool> CallUninstallLicenseAsync(string fingerprintJson, string apiUrl)
        {
            string url = $"{apiUrl.TrimEnd('/')}/internal/license/ideaccess/uninstall";
            using (var httpClient = CreateHttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(fingerprintJson, Encoding.UTF8, "application/json");
                var response = await httpClient.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Log($"Error response uninstalling Xygeni IDE License: {(int)response.StatusCode}");
                }
                return response.StatusCode == HttpStatusCode.OK;
            }
        }

        private string TryGetUrl()
        {
            try { return XygeniConfigurationService.GetInstance().GetUrl(); }
            catch { return null; }
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            var proxySettings = SafeProxySettings();
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
            var client = new HttpClient(handler, disposeHandler: true);
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        private ProxySettings SafeProxySettings()
        {
            try { return XygeniConfigurationService.GetInstance().GetProxySettings(); }
            catch { return null; }
        }

        private static IWebProxy BuildWebProxy(ProxySettings proxySettings)
        {
            if (proxySettings == null || string.IsNullOrWhiteSpace(proxySettings.Host))
            {
                return null;
            }
            string protocol = string.IsNullOrWhiteSpace(proxySettings.Protocol) ? "http" : proxySettings.Protocol.Trim();
            string host = proxySettings.Host.Trim();
            string uri = proxySettings.Port.HasValue
                ? $"{protocol}://{host}:{proxySettings.Port.Value}"
                : $"{protocol}://{host}";

            var webProxy = new WebProxy(uri);
            if (!string.IsNullOrWhiteSpace(proxySettings.Username))
            {
                webProxy.Credentials = new NetworkCredential(
                    proxySettings.Username.Trim(),
                    proxySettings.Password ?? string.Empty);
            }
            else if (string.Equals(proxySettings.Authentication, "default", StringComparison.OrdinalIgnoreCase))
            {
                webProxy.Credentials = CredentialCache.DefaultCredentials;
            }
            if (!string.IsNullOrWhiteSpace(proxySettings.NonProxyHosts))
            {
                var bypass = proxySettings.NonProxyHosts
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
                webProxy.BypassList = bypass;
            }
            return webProxy;
        }
    }
}
