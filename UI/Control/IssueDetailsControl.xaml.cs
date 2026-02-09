using System;
using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.VisualStudio.Shell;
using vs2026_plugin.Services;
using System.Threading.Tasks;

namespace vs2026_plugin.UI.Control
{
    public partial class IssueDetailsControl : UserControl
    {
        public event EventHandler<string> WebMessageReceived;

        public IssueDetailsControl()
        {
            InitializeComponent();
            InitializeWebView();
        }

        private async Task InitializeWebView()
        {
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "vs2026_plugin_webview2");
                Directory.CreateDirectory(folder);
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
                await webView.EnsureCoreWebView2Async(env);
                
                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

                webView.WebMessageReceived += WebView_WebMessageReceived;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var service = IssueDetailsService.GetInstance();
                if (service != null)
                {
                    webView.NavigateToString(service.GetEmptyStateHtml());
                }
            }
            catch (Exception ex)
            {
                // Handle initialization error (e.g., runtime not installed)
                // For now, log or show a message if possible, or just fail silently regarding UI
                System.Diagnostics.Debug.WriteLine("WebView2 Init Failed: " + ex.Message);
            }
        }

        private void WebView_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                WebMessageReceived?.Invoke(this, message);
            }
            catch { }
        }

        public void NavigateToString(string htmlContent)
        {
            if (webView.CoreWebView2 != null)
            {
                webView.NavigateToString(htmlContent);
            }
        }

        public void PostMessage(string messageJson)
        {
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.PostWebMessageAsJson(messageJson);
            }
        }
    }
}
