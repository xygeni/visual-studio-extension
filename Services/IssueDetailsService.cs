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

        public static IssueDetailsService GetInstance(AsyncPackage package = null, ILogger logger = null)
        {
            if (_instance == null && package != null)
            {
                _instance = new IssueDetailsService(package, logger);
            }
            return _instance;
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
                
                // Subscribe to events only once if needed, or handle differently
                // For simplicity, we might just re-subscribe or rely on the control to stay alive
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
                    
                         HandleRemediationView(msg);
                    
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
        }

        private string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private string GetThemeColors()
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
                    .severity-icon {{ width: 16px; height: 16px; margin-right: 10px; border-radius: 50%; display: inline-block; }}
                    .severity-critical {{ background-color: #ff0000; }}
                    .severity-high {{ background-color: #ff4500; }}
                    .severity-medium {{ background-color: #ffa500; }}
                    .severity-low {{ background-color: #ffff00; }}
                    .severity-info {{ background-color: #00bfff; }}
                    
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
                            <div class='severity-icon {severityClass}'></div>
                            <h1>{issue.Type}</h1>
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
                    </div>
                    
                    <div class='content-area'>
                        <div id='content-1' class='tab-content active'>
                            {issue.GetIssueDetailsHtml()}
                            
                            <div class='explanation'>
                                <h3>Explanation</h3>
                                {issue.Explanation ?? "No explanation available."}
                            </div>
                        </div>
                        
                        <div id='content-2' class='tab-content'>
                            {issue.GetCodeSnippetHtml()}
                        </div>
                    </div>
                </body>
                </html>";
            }
            catch(Exception ex)
            {
                return $"<html><body><h3>Error generating details</h3><pre>{ex}</pre></body></html>";
            }
        }
    }

    public class IssueDetailsMessage
    {
        public string Command { get; set; }
        public string IssueId { get; set; }
    }
}
