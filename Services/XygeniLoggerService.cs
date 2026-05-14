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
            // Callers may invoke this from background threads (e.g. continuations
            // after Task.Run / awaited HTTP calls). _outputPane.Activate() is a
            // COM call that requires the UI thread, so marshal there explicitly.
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _outputPane.Activate();
            });
        }
    }

}