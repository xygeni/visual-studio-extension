using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.IO;
using System.Linq;
using EnvDTE80;
using EnvDTE;
using System.Collections.Generic;

namespace vs2026_plugin.Services
{
    public class XygeniConfigurationService
    {
        private const string CollectionPath = "XygeniConfiguration";
        private const string ApiUrlKey = "ApiUrl";
        private const string TokenKey = "ApiToken";
        private const string MetadataFolderKey = ".xygenidata";

        private readonly SettingsManager _settingsManager;
        private static XygeniConfigurationService _instance;
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;

        // cached data
        private string _metadataFolder;
        private string _projectName;
        private string _rootDirectory;

        private XygeniConfigurationService(IServiceProvider serviceProvider, ILogger logger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _serviceProvider = serviceProvider;
            _settingsManager = new ShellSettingsManager(serviceProvider);
            _logger = logger;
        }

        // Constructor for testing
        public XygeniConfigurationService(SettingsManager settingsManager, ILogger logger)
        {
            _settingsManager = settingsManager;
            _logger = logger;
        }

        public static XygeniConfigurationService GetInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("XygeniConfigurationService has not been initialized. Call GetInstance(IServiceProvider) first.");
            }
            return _instance;
        }

        public static XygeniConfigurationService GetInstance(IServiceProvider serviceProvider, ILogger logger)
        {
            if (_instance == null)
            {
                if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
                _instance = new XygeniConfigurationService(serviceProvider, logger);
            }
            return _instance;
        }

        public string GetUrl()
        {
            var store = _settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);
            return store.CollectionExists(CollectionPath) ? store.GetString(CollectionPath, ApiUrlKey, string.Empty) : string.Empty;
        }

        public string GetToken()
        {
            var store = _settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);
            return store.CollectionExists(CollectionPath) ? store.GetString(CollectionPath, TokenKey, string.Empty) : string.Empty;
        }

        public void SaveUrl(string url)
        {
            var store = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!store.CollectionExists(CollectionPath))
            {
                store.CreateCollection(CollectionPath);
            }
            store.SetString(CollectionPath, ApiUrlKey, url);
        }

        public void SaveToken(string token)
        {
            var store = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!store.CollectionExists(CollectionPath))
            {
                store.CreateCollection(CollectionPath);
            }
            store.SetString(CollectionPath, TokenKey, token);
        }

        public void ClearCache() {
            _metadataFolder = null;
            _projectName = null;
            _rootDirectory = null;
        }

        // resolve metadata path (reports by project)
        public async Task<string> GetMetadataFolderAsyncForProject() {
           
            if (_metadataFolder != null) {
                return _metadataFolder;
            }

            string baseDir = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);              

            var projectName = await GetProjectName();

            string extensionDir = Path.Combine(
                baseDir,
                MetadataFolderKey,
                projectName);
            
            Directory.CreateDirectory(extensionDir);

            _metadataFolder = extensionDir;
            return extensionDir;
        }

        // resolve project name
        public async Task<string> GetProjectName() {
            if (_projectName != null) {
                return _projectName;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = this.getDTE();
            var solution = dte.Solution;
            var projects = this.GetProjects();

            if(solution != null && solution.IsOpen && !string.IsNullOrEmpty(solution.FullName))
            {
                _projectName = Path.GetFileNameWithoutExtension(solution.FullName);
                return _projectName;
            }
            
            _projectName = projects.Item(1)?.Name ?? "unknown";
            return _projectName;
        }

        // resolve Source path
        public async Task<string> GetRootDirectoryAsync()
        {
            if (_rootDirectory != null) {
                return _rootDirectory;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = this.getDTE();
            var solution = dte.Solution;
            var projects = this.GetProjects();

            string solutionPath = string.Empty;

            if (await this.IsSolutionWithProjectsAsync(solution, projects) || await this.IsFolderAsync(solution, projects))
            {
                solutionPath = solution.FullName;

                if (!File.GetAttributes(solutionPath).HasFlag(FileAttributes.Directory))
                {
                    solutionPath = Directory.GetParent(solutionPath).FullName;
                }

                var projectsList = new List<Project>();
                foreach (var aProject in projects)
                {
                    var project = aProject as Project;

                    projectsList.Add(project);
                }
    
                var projectFolders = await this.GetSolutionProjectsFromDteAsync(projectsList);

                // solution with projects, get parent path
                if (projectFolders != null && projectFolders.Count > 0)
                {
                    solutionPath = this.FindRootDirectoryForSolutionProjects(solutionPath, projectFolders);
                }
            }

            else if (await this.IsFlatProjectOrWebSiteAsync(solution, projects))
            {
                // no solution, read first project parent path
                string projectPath = solution.Projects.Item(1).FullName;

                solutionPath = Directory.GetParent(projectPath).FullName;
            }

            else {
                var ivsSolution = await ServiceProvider.GetGlobalServiceAsync(typeof(SVsSolution)) as IVsSolution;
                if (ivsSolution != null)
                {
                    ivsSolution.GetSolutionInfo(
                        out string solutionDir,
                        out string solutionFile,
                        out string userOptsFile);

                    // Project Folder case:
                    // solutionFile == null or empty
                    // solutionDir == root folder
                    string projectRoot;

                    if (!string.IsNullOrEmpty(solutionFile))
                    {
                        // Regular solution
                        projectRoot = Path.GetDirectoryName(solutionFile);
                    }
                    else
                    {
                        // Open Folder
                        projectRoot = solutionDir;
                    }

                    if (!string.IsNullOrEmpty(projectRoot))
                    {
                        projectRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar);

                        solutionPath = Path.GetFileName(projectRoot);
                    }
                }
            }

            _rootDirectory = solutionPath;
            return solutionPath;
        }

        private DTE getDTE() {
            return (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
        }

        public Projects GetProjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return this.getDTE().Solution.Projects;
        }


        
        public string FindRootDirectoryForSolutionProjects(string rootDir, IList<string> projectDirs)
        {
            if (rootDir == null || string.IsNullOrEmpty(rootDir))
            {
                return null;
            }

            if (projectDirs.All(dir => dir.StartsWith(rootDir)))
            {
                return rootDir;
            }

            string newRootDir = Directory.GetParent(rootDir)?.FullName;

            return this.FindRootDirectoryForSolutionProjects(newRootDir, projectDirs);
        }
        

        private async Task<IList<string>> GetSolutionProjectsFromDteAsync(IList<Project> projects)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var projectFolders = new List<string>();

            try
            {
                foreach (var project in projects)
                {
                    if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder)
                    {
                        string slnPaht = this.getDTE().Solution.FullName;

                        var innerProjects = await this.GetSolutionFolderProjectsAsync(project);
                        var innerProjectPaths = await this.GetSolutionProjectsFromDteAsync(innerProjects);

                        projectFolders.AddRange(innerProjectPaths);
                    }
                    else
                    {
                        projectFolders.Add(await this.GetDteProjectPathAsync(project));
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error on get all project paths from dte");
            }

            return projectFolders
                .Where(str => !string.IsNullOrEmpty(str))
                .Distinct()
                .ToList();
        }


        private async System.Threading.Tasks.Task<string> GetDteProjectPathAsync(Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (string.IsNullOrEmpty(project.FullName))
            {
                return null;
            }

            string projectPath = this.GetExistingDirectoryPath(new FileInfo(project.FullName).DirectoryName);

            return Directory.Exists(projectPath) ? projectPath : null;
        }

        private string GetExistingDirectoryPath(string path) => Directory.Exists(path) ? path : Directory.GetParent(path).FullName;

        private async System.Threading.Tasks.Task<IList<Project>> GetSolutionFolderProjectsAsync(Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var projects = new List<Project>();

            var count = project.ProjectItems.Count;

            for (var i = 1; i <= count; i++)
            {
                var item = project.ProjectItems.Item(i).SubProject;
                var subProject = item as Project;

                if (subProject != null)
                {
                    projects.Add(subProject);
                }
            }

            return projects;
        }

    
        
        private async Task<bool> IsFlatProjectOrWebSiteAsync(Solution solution, Projects projects)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return string.IsNullOrEmpty(solution.FullName) && solution.IsDirty && projects.Count > 0;
        }

        private async Task<bool> IsFolderAsync(Solution solution, Projects projects)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return !string.IsNullOrEmpty(solution.FullName) && !solution.IsDirty && projects.Count == 0;
        }

        private async Task<bool> IsSolutionWithProjectsAsync(Solution solution, Projects projects)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return !string.IsNullOrEmpty(solution.FullName) && !solution.IsDirty && projects.Count > 0;
        }




    }
}
