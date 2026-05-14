using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Web.WebView2.Core;

namespace vs2026_plugin.UI.Control
{
    public partial class AiExplainDialog : DialogWindow
    {
        private string _pendingHtml;
        private bool _webViewReady;
        private bool _closed;

        public AiExplainDialog(string title, string initialHtml)
        {
            InitializeComponent();
            Title = title ?? "Xygeni AI Explanation";
            _pendingHtml = initialHtml;
            Closed += (s, e) => _closed = true;
            _ = InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "vs2026_plugin_webview2");
                Directory.CreateDirectory(folder);
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
                await webView.EnsureCoreWebView2Async(env);
                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                _webViewReady = true;
                if (!string.IsNullOrEmpty(_pendingHtml))
                {
                    webView.NavigateToString(_pendingHtml);
                    _pendingHtml = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AiExplainDialog WebView2 init failed: " + ex.Message);
            }
        }

        public void SetHtml(string html)
        {
            if (_closed) return;
            try
            {
                if (_webViewReady && webView.CoreWebView2 != null)
                {
                    webView.NavigateToString(html ?? string.Empty);
                }
                else
                {
                    _pendingHtml = html;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AiExplainDialog SetHtml failed: " + ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
