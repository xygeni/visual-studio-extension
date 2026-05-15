using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using vs2026_plugin.Services;

namespace vs2026_plugin.Editor
{
    /// <summary>
    /// Watches the VS Running Document Table for save events and, when the user has
    /// enabled "Auto-run incremental scan on save", triggers a debounced incremental
    /// scan of the current solution/folder root.
    /// </summary>
    internal sealed class XygeniDocumentSaveListener : IVsRunningDocTableEvents3, IDisposable
    {
        private const int DebounceMs = 1000;

        private static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".sln", ".slnx", ".csproj", ".vbproj", ".fsproj", ".vcxproj",
            ".filters", ".user", ".suo", ".vsidx", ".pidb", ".db"
        };

        private readonly IVsRunningDocumentTable _rdt;
        private readonly ILogger _logger;
        private uint _cookie;
        private Timer _debounceTimer;
        private readonly object _timerLock = new object();
        private bool _disposed;

        public XygeniDocumentSaveListener(IVsRunningDocumentTable rdt, ILogger logger)
        {
            _rdt = rdt ?? throw new ArgumentNullException(nameof(rdt));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Advise()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0) return;
            int hr = _rdt.AdviseRunningDocTableEvents(this, out _cookie);
            ErrorHandler.ThrowOnFailure(hr);
        }

        public void Unadvise()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie == 0) return;
            try
            {
                _rdt.UnadviseRunningDocTableEvents(_cookie);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to unadvise XygeniDocumentSaveListener");
            }
            _cookie = 0;
        }

        public int OnAfterSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (!XygeniConfigurationService.GetInstance().GetAutoScan())
                {
                    return VSConstants.S_OK;
                }

                int hr = _rdt.GetDocumentInfo(
                    docCookie,
                    out _,
                    out _,
                    out _,
                    out string moniker,
                    out _,
                    out _,
                    out _);

                if (ErrorHandler.Failed(hr) || string.IsNullOrEmpty(moniker))
                {
                    return VSConstants.S_OK;
                }

                string ext = Path.GetExtension(moniker);
                if (!string.IsNullOrEmpty(ext) && IgnoredExtensions.Contains(ext))
                {
                    return VSConstants.S_OK;
                }

                ScheduleDebouncedScan();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "XygeniDocumentSaveListener.OnAfterSave failed");
            }

            return VSConstants.S_OK;
        }

        private void ScheduleDebouncedScan()
        {
            lock (_timerLock)
            {
                if (_disposed) return;

                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(OnDebounceFired, null, Timeout.Infinite, Timeout.Infinite);
                }

                _debounceTimer.Change(DebounceMs, Timeout.Infinite);
            }
        }

        private void OnDebounceFired(object state)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var configService = XygeniConfigurationService.GetInstance();
                    if (!configService.GetAutoScan())
                    {
                        return;
                    }

                    var installer = XygeniInstallerService.GetInstance();
                    if (!installer.IsInstalled)
                    {
                        return;
                    }

                    var license = LicenseService.GetInstance();
                    if (license.LicenseChecked && !license.IsLicenseAvailable)
                    {
                        return;
                    }

                    var scanner = XygeniScannerService.GetInstance();
                    if (scanner.IsScannerRunning())
                    {
                        return;
                    }

                    string rootDir = await configService.GetRootDirectoryAsync();
                    if (string.IsNullOrEmpty(rootDir))
                    {
                        return;
                    }

                    string scannerPath = installer.GetScannerInstallationDir();
                    if (string.IsNullOrEmpty(scannerPath))
                    {
                        return;
                    }

                    _logger.Log("Auto-scan: triggering incremental scan after file save");

                    try
                    {
                        await scanner.RunIncrementalAnalysisAsync(rootDir, scannerPath);
                    }
                    catch (Exception scanEx)
                    {
                        _logger.Error(scanEx, "Auto-scan: incremental scan failed");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Auto-scan execution failed");
                }
            });
        }

        public void Dispose()
        {
            lock (_timerLock)
            {
                _disposed = true;
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }

        // No-op IVsRunningDocTableEvents / 2 / 3 implementations
        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;
        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) => VSConstants.S_OK;
        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame) => VSConstants.S_OK;
        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;
        public int OnBeforeSave(uint docCookie) => VSConstants.S_OK;
    }
}
