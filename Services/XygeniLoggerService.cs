using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System;

namespace vs2026_plugin.Services
{
    public interface ILogger
    {
        void Log(string message);
        void Error(Exception ex, string message);
        void Show();
    }

    internal class XygeniOutputLogger : ILogger
    {
        private readonly IVsOutputWindowPane _outputPane;
        public XygeniOutputLogger(IVsOutputWindowPane outputPane) { _outputPane = outputPane; }
        public void Log(string message) 
        { 
            string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            _outputPane.OutputStringThreadSafe($"[{timestamp}] {message}{Environment.NewLine}"); 
        }
        public void Error(Exception ex, string message) { 
            Log($"ERROR: {message} - {ex.Message}"); 
            Log($"Stack trace: {ex.ToString()}");
        }
        public void Show() 
        { 
            ThreadHelper.ThrowIfNotOnUIThread();
            _outputPane.Activate(); 
        }
    }

}