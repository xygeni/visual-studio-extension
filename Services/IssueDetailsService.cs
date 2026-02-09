using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.PlatformUI;
using vs2026_plugin.Models;
using vs2026_plugin.UI.Control;
using vs2026_plugin.UI.Window;
using System.Linq;
using Newtonsoft.Json;
using System.Windows.Media;

namespace vs2026_plugin.Services
{
    public class IssueDetailsService
    {
        private static IssueDetailsService _instance;
        private readonly AsyncPackage _package;
        private readonly ILogger _logger;

        private IssueDetailsService(AsyncPackage package, ILogger logger)
        {
            _package = package;
            _logger = logger;
        }

        public static IssueDetailsService GetInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("IssueDetailsService has not been initialized");
            }
            return _instance;
        }

        public static IssueDetailsService GetInstance(AsyncPackage package = null, ILogger logger = null)
        {
            if (_instance == null && package != null)
            {
                _instance = new IssueDetailsService(package, logger);
            }
            return _instance;
        }

        public async Task CloseIssueDetailsWindow()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ToolWindowPane window = await _package.FindToolWindowAsync(typeof(IssueDetailsToolWindow), 0, true, _package.DisposalToken);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            windowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
        }

        public async Task ShowIssueDetailsAsync(IXygeniIssue issue)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ToolWindowPane window = await _package.FindToolWindowAsync(typeof(IssueDetailsToolWindow), 0, true, _package.DisposalToken);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

            if (window.Content is IssueDetailsControl control)
            {
                var html = GenerateHtml(issue);
                control.NavigateToString(html);
                
                control.WebMessageReceived -= Control_WebMessageReceived;
                control.WebMessageReceived += Control_WebMessageReceived;
            }
        }

        private void Control_WebMessageReceived(object sender, string message)
        {
             ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try 
                {
                    IssueDetailsMessage msg = JsonConvert.DeserializeObject<IssueDetailsMessage>(message);
                    
                    if (msg.Command == "openFile")
                    {
                        // Find the issue and open the file
                        var issueService = XygeniIssueService.GetInstance();
                        var issue = issueService?.FindIssueById(msg.IssueId);
                        if (issue != null)
                        {
                            await OpenFileAsync(issue);
                        }
                    }
                    else if (msg.Command == "remediate")
                    {
                        HandleRemediationView(msg);
                    }
                }
                catch(Exception ex)
                {
                    _logger?.Error(ex, "Error handling web message");
                }
            });
        }

        private async Task OpenFileAsync(IXygeniIssue issue)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
             try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null && !string.IsNullOrEmpty(issue.File))
                {
                    string filePath = issue.File;
                    if (!Path.IsPathRooted(filePath))
                    {
                        string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                        if (!string.IsNullOrEmpty(rootDir))
                        {
                            filePath = Path.Combine(rootDir, filePath);
                        }
                    }

                    if (File.Exists(filePath))
                    {
                        var window = dte.ItemOperations.OpenFile(filePath);
                        if (window != null && issue.BeginLine > 0)
                        {
                            var selection = (EnvDTE.TextSelection)dte.ActiveDocument.Selection;
                            selection.GotoLine(issue.BeginLine, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error opening file");
            }
        }

        private void HandleRemediationView(IssueDetailsMessage message)
        {
            _logger?.Log($"Remediation requested for {message.IssueId}");

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    string scannerPath = XygeniInstallerService.GetInstance().GetScannerInstallationDir();
                    var remediationService = RemediationService.GetInstance(_logger);
                    var fixData = await remediationService.LaunchRemediationPreviewAsync(message.Kind, message.IssueId, message.File, scannerPath);

                    if (fixData == null)
                    {
                        return;
                    }

                    string leftFilePath = message.File;
                    OpenDiffView(fixData, leftFilePath);
                }
                catch (Exception ex)
                {
                    _logger?.Log("Error during remediation preview " + ex.Message);
                }
            });
        }

        private void OpenDiffView(FixData fixData, string leftFilePath)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                if (fixData != null && fixData.TempFile != null)
                {
                    _logger?.Log($"Remediation preview generated: {fixData.TempFile}");

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (!Path.IsPathRooted(leftFilePath))
                    {
                        string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                        if (!string.IsNullOrEmpty(rootDir))
                        {
                            leftFilePath = Path.Combine(rootDir, leftFilePath);
                        }
                    }

                    if (File.Exists(leftFilePath) && File.Exists(fixData.TempFile))
                    {
                        var previewService = await _package.GetServiceAsync(typeof(SVsPreviewChangesService)) as IVsPreviewChangesService;
                        if (previewService != null && false)
                        {
                            var engine = new RemediationPreviewEngine(leftFilePath, fixData.TempFile, _logger);
                            previewService.PreviewChanges(engine);
                        }
                        else
                        {
                            _logger?.Log("IVsPreviewChangesService not found, falling back to IVsDifferenceService");
                            var diffService = await _package.GetServiceAsync(typeof(SVsDifferenceService)) as IVsDifferenceService;
                            diffService?.OpenComparisonWindow(leftFilePath, fixData.TempFile);

                            OpenApplyChangesDialog();
                        }
                    }
                    else
                    {
                        _logger?.Log($"One of the files does not exist: {leftFilePath} or {fixData.TempFile}");
                    }
                }
            });
        }

        #region Preview Changes Implementation

        // see https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivspreviewchangesengine?view=visualstudiosdk-2022
        private class RemediationPreviewEngine : IVsPreviewChangesEngine
        {
            private readonly string _originalFile;
            private readonly string _tempFile;
            private readonly ILogger _logger;
            private RemediationPreviewList _changesList;

            public RemediationPreviewEngine(string originalFile, string tempFile, ILogger logger)
            {
                _originalFile = originalFile;
                _tempFile = tempFile;
                _logger = logger;
            }

            public int GetTextViewDescription(out string pbstrTextViewDescription)
            {
                pbstrTextViewDescription = "Remediation preview";
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetConfirmation(out string pbstrConfirmed)
            {
                pbstrConfirmed = "Review and apply remediation changes.";
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetDescription(out string pbstrDescription)
            {
                pbstrDescription = "Remediation provided by Xygeni.";
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetRootChangesList(out object ppIChangesList)
            {
                if (_changesList == null)
                {
                    _changesList = new RemediationPreviewList(_originalFile, _tempFile, _logger);
                }
                ppIChangesList = _changesList;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int ApplyChanges()
            {
                if (_changesList != null && _changesList.ApplyRequested)
                {
                    try
                    {
                        File.Copy(_tempFile, _originalFile, true);
                        _logger?.Log($"Remediation applied to {_originalFile}");
                        return Microsoft.VisualStudio.VSConstants.S_OK;
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "Failed to apply remediation");
                    }
                }
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetHelpContext(out string pdwHelpContext)
            {
                pdwHelpContext = "https://docs.xygeni.com/docs/visual-studio-extension";
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetTitle(out string pbstrTitle)
            {
                pbstrTitle = "Preview Changes - Xygeni Remediation";
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetWarning(out string pbstrWarning, out int pfWarningLevel)
            {
                pbstrWarning = "";
                pfWarningLevel = 0;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }
        }

        private class RemediationPreviewList : IVsPreviewChangesList
        {
            private readonly RemediationPreviewChange _change;
            public bool ApplyRequested => _change.IsApplied;

            public RemediationPreviewList(string originalFile, string tempFile, ILogger logger)
            {
                _change = new RemediationPreviewChange(this, originalFile, tempFile, logger);
            }

            public int GetCount(out uint pcItems)
            {
                pcItems = 1;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }
            public int GetItemCount(out uint pcItems)
            {
                pcItems = 1;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }
            
            public int GetListChanges(ref uint pcChanges, VSTREELISTITEMCHANGE[] prgListChanges)
            {
                pcChanges = 1;
                prgListChanges = new VSTREELISTITEMCHANGE[1];
                prgListChanges[0].index = 0; //ulong
                prgListChanges[0].grfChange = 0; //no changes??
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            

            public int GetDescriptionText(uint index, out string pbstrDescriptionText)
            {
                pbstrDescriptionText = "Apply remediation to " + Path.GetFileName(_change.OriginalFile);
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetDisplayData(uint index, VSTREEDISPLAYDATA[] pData)
            {
                pData[0].Image = pData[0].SelectedImage = 0; // Default icon
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetExpandable(uint index, out int pfExpandable)
            {
                pfExpandable = 0;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetExpandedList(uint index, out int pcChildren, out IVsLiteTreeList ppChildren)
            {
                pcChildren = 0;
                ppChildren = null;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetText(uint index, VSTREETEXTOPTIONS tto, out string pbstrText)
            {
                pbstrText = Path.GetFileName(_change.OriginalFile);
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetTipText(uint index, VSTREETOOLTIPTYPE tto, out string pbstrTipText)
            {
                pbstrTipText = _change.OriginalFile;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }
            

            public int GetAttributes(uint index, out uint pAttributes)
            {
                pAttributes = 0;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int LocateExpandedList(IVsLiteTreeList pChild, out uint iItem)
            {
                iItem = 0;
                return Microsoft.VisualStudio.VSConstants.E_NOTIMPL;
            }

            public int ToggleState(uint index, out uint ptscr)
            {
                _change.IsApplied = !_change.IsApplied;
                ptscr = (uint)(_change.IsApplied ? _VSTREESTATECHANGEREFRESH.TSCR_CURRENT : _VSTREESTATECHANGEREFRESH.TSCR_NONE);
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetFlags(out uint pdwFlags)
            {
                pdwFlags = 0;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int OnClose(VSTREECLOSEACTIONS[] dwActions)
            {
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int OnRequestSource(uint index, object pIUnknownTextView)
            {
                pIUnknownTextView = null;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int UpdateCounter(out uint pCurUpdate, out uint pgrfChanges)
            {
                pCurUpdate = 0;
                pgrfChanges = 0;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }
        }

        private class RemediationPreviewChange
        {
            public string OriginalFile { get; }
            private readonly string _tempFile;
            private readonly ILogger _logger;
            private readonly RemediationPreviewList _parent;
            public bool IsApplied { get; set; } = true;

            public RemediationPreviewChange(RemediationPreviewList parent, string originalFile, string tempFile, ILogger logger)
            {
                _parent = parent;
                OriginalFile = originalFile;
                _tempFile = tempFile;
                _logger = logger;
            }

            public int GetDescription(out string pbstrDescription)
            {
                pbstrDescription = "Remediation changes for " + Path.GetFileName(OriginalFile);
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int GetTitle(out string pbstrTitle)
            {
                pbstrTitle = Path.GetFileName(OriginalFile);
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            public int OnClick(out int pfHandled)
            {
                pfHandled = 0;
                return Microsoft.VisualStudio.VSConstants.S_OK;
            }

            
        }

        #endregion

        private void OpenApplyChangesDialog()
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                var dialog = new ApplyChangesDialog();
                dialog.ShowDialog();
                
                if (dialog.SaveClicked)
                {
                    _logger?.Log("Apply changes dialog: Save clicked");
                }
                else
                {
                    _logger?.Log("Apply changes dialog: Reject clicked");
                }
            });
        }

        private string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public string GetThemeColors()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                
                // Get Visual Studio theme colors using VSColorTheme
                var backgroundColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                var foregroundColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
                var borderColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBorderColorKey);
                var linkColor = VSColorTheme.GetThemedColor(EnvironmentColors.ControlLinkTextColorKey);
                var accentColor = VSColorTheme.GetThemedColor(EnvironmentColors.SystemHighlightColorKey);
                var inactiveSelectionColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowButtonInactiveGlyphColorKey);
                
                // Convert System.Drawing.Color to WPF Color
                var bgColor = Color.FromArgb(backgroundColor.A, backgroundColor.R, backgroundColor.G, backgroundColor.B);
                var fgColor = Color.FromArgb(foregroundColor.A, foregroundColor.R, foregroundColor.G, foregroundColor.B);
                var bdColor = Color.FromArgb(borderColor.A, borderColor.R, borderColor.G, borderColor.B);
                var lnColor = Color.FromArgb(linkColor.A, linkColor.R, linkColor.G, linkColor.B);
                var acColor = Color.FromArgb(accentColor.A, accentColor.R, accentColor.G, accentColor.B);
                var inColor = Color.FromArgb(inactiveSelectionColor.A, inactiveSelectionColor.R, inactiveSelectionColor.G, inactiveSelectionColor.B);
                
                // Convert to hex
                return $@"
                    --vs-background: {ColorToHex(bgColor)};
                    --vs-foreground: {ColorToHex(fgColor)};
                    --vs-border: {ColorToHex(bdColor)};
                    --vs-link: {ColorToHex(lnColor)};
                    --vs-accent: {ColorToHex(acColor)};
                    --vs-inactive-selection: {ColorToHex(inColor)};
                ";
            }
            catch
            {
                // Fallback to default colors if theme colors can't be retrieved
                return @"
                    --vs-background: #1E1E1E;
                    --vs-foreground: #D4D4D4;
                    --vs-border: #3F3F46;
                    --vs-link: #3794FF;
                    --vs-accent: #007ACC;
                    --vs-inactive-selection: #3A3D41;
                ";
            }
        }

        public string GetEmptyStateHtml()
        {
            string themeColors = GetThemeColors();
            string css = $@"
                :root {{
                    {themeColors}
                }}
                body {{ 
                    font-family: 'Segoe UI', sans-serif; 
                    padding: 0; 
                    margin: 0; 
                    color: var(--vs-foreground); 
                    background-color: var(--vs-background);
                    display: flex;
                    justify-content: center;
                    align-items: center;
                    height: 100vh;
                    overflow: hidden;
                }}
                .message {{
                    font-size: 14px;
                    opacity: 0.6;
                }}
            ";

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset=""UTF-8"">
                    <style>{css}</style>
                </head>
                <body>
                    <div class='message'>no issue selected</div>
                </body>
                </html>";
        }

        private string GenerateHtml(IXygeniIssue issue)
        {
            try 
            {
               // Get current VS theme colors
               string themeColors = GetThemeColors();
               
               // Basic CSS style using VS theme colors
               string css = $@"
                    :root {{
                        {themeColors}
                    }}
                    
                    body {{ font-family: 'Segoe UI', sans-serif; padding: 0; margin: 0; color: var(--vs-foreground); background-color: var(--vs-background); }}
                    .header {{ padding: 20px; background-color: var(--vs-background); border-bottom: 1px solid var(--vs-border); }}
                    .title-row {{ display: flex; align-items: center; margin-bottom: 10px; }}
                    .severity-icon {{  margin-right: 10px; padding: 0px 5px 2px 5px; border-radius: 5px; display: inline-block; }}
                    .severity-critical {{ background-color: #ff0000; }}
                    .severity-high {{ background-color: #fa9f4fff; }}
                    .severity-medium {{ background-color: #ffa500; }}
                    .severity-low {{ background-color: #f7f787ff; color: #000000; }}
                    .severity-info {{ background-color: #ccecf7ff; color: #000000; }}
                    
                    h1 {{ margin: 0; font-size: 18px; font-weight: 600; }}
                    .subtitle {{ font-size: 13px; opacity: 0.8; margin-bottom: 10px; }}
                    .file-link {{ font-family: Consolas, monospace; font-size: 12px; cursor: pointer; color: var(--vs-link); }}
                    .file-link:hover {{ text-decoration: underline; }}
                    
                    .tabs {{ display: flex; border-bottom: 1px solid var(--vs-border); background-color: var(--vs-background); }}
                    .tab {{ padding: 10px 15px; cursor: pointer; opacity: 0.7; border-bottom: 2px solid transparent; font-size: 12px; text-transform: uppercase; }}
                    .tab.active {{ opacity: 1; border-bottom-color: var(--vs-accent); font-weight: 600; }}
                    
                    .content-area {{ padding: 20px; }}
                    .tab-content {{ display: none; }}
                    .tab-content.active {{ display: block; }}
                    
                    table {{ width: 100%; border-collapse: collapse; font-size: 13px; }}
                    th {{ text-align: left; padding: 5px; width: 120px; opacity: 0.7; font-weight: normal; }}
                    td {{ padding: 5px; }}
                    
                    .code-snippet-table {{ font-family: Consolas, monospace; }}
                    .line-number {{ color: var(--vs-foreground); opacity: 0.5; text-align: right; padding-right: 10px; width: 30px; user-select: none; }}
                    .code-line {{ white-space: pre-wrap; }}
                    
                    .explanation {{ line-height: 1.5; margin-top: 10px; }}
                    
                    /* Markdown-like styling for explanation */
                    .explanation h1, .explanation h2, .explanation h3 {{ font-size: 14px; margin-top: 15px; margin-bottom: 5px; }}
                    .explanation p {{ margin-bottom: 10px; }}
                    .explanation code {{ font-family: Consolas, monospace; background-color: rgba(128,128,128,0.1); padding: 2px 4px; border-radius: 3px; }}
                    .explanation pre {{ background-color: rgba(128,128,128,0.1); padding: 10px; border-radius: 3px; overflow-x: auto; }}
                    
                    /* Button styling */
                    .xy-button {{ 
                        background-color: var(--vs-accent); 
                        color: #ffffff; 
                        border: none; 
                        padding: 8px 16px; 
                        font-size: 13px; 
                        cursor: pointer; 
                        border-radius: 3px; 
                        font-family: 'Segoe UI', sans-serif;
                        transition: background-color 0.2s;
                    }}
                    .xy-button:hover:not(:disabled) {{ 
                        background-color: #005a9e; 
                    }}
                    .xy-button:disabled {{ 
                        opacity: 0.6; 
                        cursor: not-allowed; 
                    }}
               ";
               
               string severityClass = $"severity-{issue.Severity?.ToLower() ?? "info"}";
               
               // Construct HTML
               return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset=""UTF-8"">
                    <style>{css}</style>
                    <script>
                        function showTab(id) {{
                            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
                            document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
                            document.getElementById('tab-btn-' + id).classList.add('active');
                            document.getElementById('content-' + id).classList.add('active');
                        }}
                        
                        function openFile() {{
                             chrome.webview.postMessage(JSON.stringify({{ command: 'openFile', issueId: '{issue.Id}' }}));
                        }}
                    </script>
                </head>
                <body>
                    <div class='header'>
                        <div class='title-row'>
                            <h1>Xygeni {issue.CategoryName} Issue</h1>
                        </div>
                        <div class='title-row'>
                            <div class='severity-icon {severityClass}'>{issue.Severity}</div> 
                            <div>{issue.Explanation.Substring(0, 30) + "..."}</div> 
                        </div>
                        <div class='subtitle'>
                           {issue.GetSubtitleLineHtml()}
                        </div>
                        <div class='file-link' onclick='openFile()'>
                            {issue.File}:{issue.BeginLine}
                        </div>
                    </div>
                    
                    <div class='tabs'>
                        <div id='tab-btn-1' class='tab active' onclick='showTab(1)'>DETAILS</div>
                        <div id='tab-btn-2' class='tab' onclick='showTab(2)'>CODE</div>
                         <!-- Add Remediation tab if needed -->
                         {issue.GetRemediationTab()}
                    </div>
                    
                    <div class='content-area'>
                        <div id='content-1' class='tab-content active'>
                            {issue.GetIssueDetailsHtml()}
                            
                            <div class='explanation'>
                                <h3>Explanation</h3>
                                {issue.GetExplanationHtml()}
                            </div>
                        </div>
                        
                        <div id='content-2' class='tab-content'>
                            {issue.GetCodeSnippetHtml()}
                        </div>

                        {issue.GetRemediationTabContent()}
                    </div>
                </body>
                </html>";
            }
            catch(Exception ex)
            {
                string css = $@"
                    :root {{
                        {GetThemeColors()}
                    }}
                    
                    body {{ font-family: 'Segoe UI', sans-serif; padding: 0; margin: 0; color: var(--vs-foreground); background-color: var(--vs-background); }}
                    .header {{ padding: 20px; background-color: var(--vs-background); border-bottom: 1px solid var(--vs-border); }}
                   ";
                return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset=""UTF-8"">
                    <style>{css}</style>
                    </script>
                </head>
                <body>
                    <div class='header'>
                        <h1>No details available</h1>
                    </div>
                </body>
                </html>";
            }
        }
    }

    public class IssueDetailsMessage
    {
        public string Command { get; set; }
        public string IssueId { get; set; }
        public string Kind { get; set; }
        public string File { get; set; }
    }
}
