using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using vs2026_plugin.Services;
using System.IO;
using Task = System.Threading.Tasks.Task;



namespace vs2026_plugin
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(vs2026_pluginPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(UI.Window.XygeniConfigurationToolView))]
    [ProvideToolWindow(typeof(UI.Window.XygeniExplorerToolWindow), Style = VsDockStyle.Linked, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(UI.Window.IssueDetailsToolWindow), Style = VsDockStyle.MDI)]
    public sealed class vs2026_pluginPackage : AsyncPackage
    {
        /// <summary>
        /// vs2026_pluginPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "b441d6b9-1770-4351-826d-479748bb2ff9";

        public static vs2026_pluginPackage Instance { get; private set; }

        private IVsOutputWindowPane _outputPane;
        public ILogger Logger { get; private set; }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Initialize Output Channel
            Guid generalPaneGuid = new Guid("D0E6C712-4B6A-4A73-9095-2BB6E30D42A9");
                        
            // 1. Get the Output Window service
            IVsOutputWindow outWindow = await GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;

            IVsOutputWindowPane generalPane;
            int hr = outWindow.GetPane(ref generalPaneGuid, out generalPane);

            if (ErrorHandler.Failed(hr) || generalPane == null)
            {
                outWindow.CreatePane(ref generalPaneGuid, "Xygeni Output", 1, 1);
                outWindow.GetPane(ref generalPaneGuid, out generalPane);
            }

            // Initialize Logger
            Logger = new XygeniOutputLogger(generalPane);
            Logger.Show();            
            Logger.Log("Xygeni Extension Initializing...");

            // Initialize Installer Service
            string extensionPath = Path.GetDirectoryName(typeof(vs2026_pluginPackage).Assembly.Location);
            XygeniConfigurationService.GetInstance(ServiceProvider.GlobalProvider, Logger);
            XygeniInstallerService.GetInstance(extensionPath, Logger);
            XygeniScannerService.GetInstance(Logger);
            XygeniIssueService.GetInstance(Logger);
            IssueDetailsService.GetInstance(this, Logger);

            await Commands.XygeniSettingsCommand.InitializeAsync(this);
            await Commands.XygeniExplorerCommand.InitializeAsync(this);            
            Logger.Log("Xygeni Extension Initialized Successfully");
            
            var initEvents = new InitEvents(this, Logger);
            initEvents.registerEvents();
        }
        
        // When project/solution is loaded, install scanner and read issues
        public void OnWorkspaceReady()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Logger.Log("");
            Logger.Log("  Opening project/solution");

            XygeniConfigurationService.GetInstance().ClearCache();

            // READ ISSUES                
            XygeniIssueService.GetInstance().ReadIssuesAsync();

            // Install Scanner
            Commands.XygeniCommands.InstallScanner();

            // Close Issue Details Window
            IssueDetailsService.GetInstance().CloseIssueDetailsWindow();

        }

        
    }


    // EVENTS HANDLER

    public class InitEvents : IVsSolutionEvents7, IVsSolutionEvents3, IVsSolutionEvents
    {
        private readonly vs2026_pluginPackage _package;
        public ILogger _logger { get; private set; }

        private uint _solutionEventsCookie;
        private IVsSolution _solution;


        public InitEvents(vs2026_pluginPackage package, ILogger logger)
        {
            _package = package;
            _logger = logger;
        }

        public async Task registerEvents()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solution = await ServiceProvider.GetGlobalServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if(_solution != null)
            {
                _solution.AdviseSolutionEvents(this, out _solutionEventsCookie);   
            }  
        }

        public void Dispose()
        {
            _solution.UnadviseSolutionEvents(_solutionEventsCookie);
        }

        // Events
        public void OnAfterOpenFolder(string pszFolderPath) {
            _package.OnWorkspaceReady(); 
            return;
        }  

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) {
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution) {
            return VSConstants.S_OK;
        }

        
        public void OnBeforeCloseFolder(string pszFolderPath)        
        {
            return;
        }
        public void OnQueryCloseFolder(string folder, ref int cancel)        
        {
            return;
        }
        public void OnAfterCloseFolder(string folder)        
        {
            return;
        }
        public void OnAfterLoadAllDeferredProjects()        
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _package.OnWorkspaceReady();
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)        
        {
            return VSConstants.S_OK;
        }

        // Required no-op implementations
        
        public virtual int OnAfterMergeSolution(object pUnkReserved) => VSConstants.S_OK;
        public virtual int OnAfterCloseSolution(object pUnkReserved) => VSConstants.S_OK;
        public virtual int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;
        public virtual int OnBeforeOpenSolution(string pszSolutionFilename) => VSConstants.S_OK;
        
        public virtual int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
        public virtual int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy fRemoved) => VSConstants.S_OK;

        // Add these missing methods

        public virtual int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
        public virtual int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
        public virtual int OnQueryOpenSolution(string pszSolutionFilename) => VSConstants.S_OK;
        public virtual int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoved, ref int pfCancel) => VSConstants.S_OK;
        public virtual int OnQueryLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterRenameSolution(object pUnkReserved) => VSConstants.S_OK;
        public virtual int OnAfterRenameProject(IVsHierarchy pHierarchy, string pszNewName) => VSConstants.S_OK;
        public virtual int OnAfterRemoveFromSolution(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterAddProjectToSolution(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterSccStatusChanged(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterSccProjectRenamed(IVsHierarchy pHierarchy, string pszNewName) => VSConstants.S_OK;
        public virtual int OnAfterSccProjectAdded(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterSccProjectRemoved(IVsHierarchy pHierarchy) => VSConstants.S_OK;

        public virtual int OnBeforeClosingChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnBeforeOpeningChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterClosingChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterOpeningChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;
        public virtual int OnAfterSccStatusChangedEx(IVsHierarchy pHierarchy, uint dwCookie) => VSConstants.S_OK;
        public virtual int OnAfterSccProjectRenamedEx(IVsHierarchy pHierarchy, string pszNewName, uint dwCookie) => VSConstants.S_OK;
        public virtual int OnAfterSccProjectAddedEx(IVsHierarchy pHierarchy, uint dwCookie) => VSConstants.S_OK;
        public virtual int OnAfterSccProjectRemovedEx(IVsHierarchy pHierarchy, uint dwCookie) => VSConstants.S_OK;
    

        
    }


}
