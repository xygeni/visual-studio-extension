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
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.ComponentModelHost;

namespace vs2026_plugin.Services
{
    public class IssueDetailsService
    {
        private static IssueDetailsService _instance;
        private readonly AsyncPackage _package;
        private readonly ILogger _logger;

        private ToolWindowPane _window;

        private const string XYGENI_STATUS_DIFF_VIEW_OPENED = "diff_view_opened";

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

            _window = await _package.FindToolWindowAsync(typeof(IssueDetailsToolWindow), 0, true, _package.DisposalToken);
            if ((null == _window) || (null == _window.Frame))
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            IVsWindowFrame windowFrame = (IVsWindowFrame)_window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

            if (_window.Content is IssueDetailsControl control)
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

                    OpenDiffView(fixData, message.File);
                }
                catch (Exception ex)
                {
                    _logger?.Log("Error during remediation preview " + ex.Message);
                }
            });
        }

        private void OpenDiffView(FixData fixData, string originalFile)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                if (fixData != null && fixData.TempFile != null)
                {
                    _logger?.Log($"Remediation preview generated: {fixData.TempFile}");

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (!Path.IsPathRooted(originalFile))
                    {
                        string rootDir = await XygeniConfigurationService.GetInstance().GetRootDirectoryAsync();
                        if (!string.IsNullOrEmpty(rootDir))
                        {
                            originalFile = Path.Combine(rootDir, originalFile);
                        }
                    }

                    if (File.Exists(originalFile) && File.Exists(fixData.TempFile))
                    {
                        var diffService = await _package.GetServiceAsync(typeof(SVsDifferenceService)) as IVsDifferenceService;
                        
                        // show diff content in a new window  
                        string originalFileName = Path.GetFileName(originalFile);   
                        try {
                            string fixedText = File.ReadAllText(fixData.TempFile);
                            var bufferFactory = await _package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;

                            var textBufferFactory = bufferFactory.GetService<ITextBufferFactoryService>();

                            ITextBuffer rightBuffer = textBufferFactory.CreateTextBuffer(
                                fixedText,
                                textBufferFactory.TextContentType);
                            
                            diffService?.OpenComparisonWindow2(
                                originalFile, 
                                fixData.TempFile,
                                originalFileName + " - Xygeni Fix Preview",
                                "Proposed Fix by Xygeni for " + fixData.IssueTitle,
                                originalFileName + " - Original",
                                "Proposed Fix",
                                null,
                                null,
                                32); // right is a temp file
                        } catch (Exception ex) {
                            _logger?.Log("Error opening diff window " + ex.Message);
                        }

                        // show apply changes dialog
                        OpenApplyChangesDialog(
                            fixData.IssueTitle, 
                            $"Please review the changes to {originalFileName} and apply them to save the fix.",
                            (sender, e) => { 
                                // copy content of fixData.TempFile to originalFile
                                File.Copy(fixData.TempFile, originalFile, true);                                
                            },
                            (sender, e) => { 
                                // nothing to do here
                                
                            });

                        // sent message to IssueDetails webview to update the UI
                        var message = new
                        {
                            Kind = "remediation",
                            Command = XYGENI_STATUS_DIFF_VIEW_OPENED
                        };

                        if(_window != null)
                        {
                            var issueDetailsControl = _window.Content as IssueDetailsControl;
                            issueDetailsControl?.PostMessage(JsonConvert.SerializeObject(message));                                                        
                        }

                    }
                    else
                    {
                        _logger?.Log($"One of the files does not exist: {originalFile} or {fixData.TempFile}");
                    }
                }
            });
        }

        

        private void OpenApplyChangesDialog(string issueTitle, string content, EventHandler saveAction, EventHandler rejectAction)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {                
                try
                {
                    // show a modeless dialog to allow user edit the file and confirm changes
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dialog = new ApplyChangesDialog(issueTitle, content);
                    dialog.SaveClickedEvent += saveAction;
                    dialog.RejectClickedEvent += rejectAction;
                    IVsUIShell uiShell = await _package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                    uiShell.GetDialogOwnerHwnd(out IntPtr hwnd);
                    var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
                    helper.Owner = hwnd;
                    dialog.Show();                     
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error opening ApplyChangesDialog");
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
            ThreadHelper.ThrowIfNotOnUIThread();
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
               ThreadHelper.ThrowIfNotOnUIThread();

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
                    .xy-container-chip {{
                        display: flex;
                        align-items: start;
                        gap: 2px;
                        flex-direction: row;
                        flex-wrap: wrap;
                    }}

                    .xy-blue-chip {{
                        font-size: 10px !important;
                        font-weight: 500;
                        position: relative;
                        border-radius: 7px !important;
                        box-sizing: border-box;
                        background-color: transparent !important;
                        color: #536DF7;
                        border: 1px solid #536DF7;
                        min-height: 22px !important;
                        padding-left: 2px;
                        padding-right: 2px; 
                        padding-top: 5px; 
                        margin-right: 2px;
                        text-wrap: nowrap;
                    }}
               ";
               
               string severityClass = $"severity-{issue.Severity?.ToLower() ?? "info"}";
               string explanation = issue.Explanation.Length > 30 ? issue.Explanation.Substring(0, 30) + "..." : issue.Explanation;
               
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

                        function onMessage(event) {{
                            console.log(event.data);
                            const message = JSON.parse(event.data);
                            if (message.command === '{XYGENI_STATUS_DIFF_VIEW_OPENED}') {{
                                alert('save');
                            }}
                        }}
                        chrome.webview.addEventListener('message', onMessage);
                    </script>
                </head>
                <body>
                    <div class='header'>
                        <div class='title-row'>
                            <h1>Xygeni {issue.CategoryName} Issue</h1>
                        </div>
                        <div class='title-row'>
                            <div class='severity-icon {severityClass}'>{issue.Severity}</div> 
                            <div>{explanation}</div> 
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
                        <h1>No details available {ex.Message}</h1>
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
