using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Per-project scan configuration (xygeni/xygeni-product-backlog#415): which scan types the full scan and the
    /// on-save incremental scan run, and the folders excluded from both. Saved in <c>.xygeni/ide.json</c> at the
    /// project root, the same file the other Xygeni IDE plugins read, so a team can commit it and share it.
    /// A missing file or key means the defaults, which are what the plugins always did: every scan type in the
    /// full scan, the file-scoped ones on save, nothing excluded.
    /// </summary>
    public sealed class ScanConfig
    {
        public const string ConfigDir = ".xygeni";
        public const string ConfigFile = "ide.json";

        /// <summary>One row per switch of the Scan configuration section, in the order shown.</summary>
        public sealed class Scanner
        {
            /// <summary>The <c>--run=</c> name the scanner CLI accepts.</summary>
            public string Key { get; }
            public string Label { get; }
            public string Description { get; }
            /// <summary>Icon file under <c>media/icons</c>.</summary>
            public string Icon { get; }
            /// <summary>With <c>--incremental</c> only these scan the changed files; the rest would scan the whole project on every save.</summary>
            public bool OnSave { get; }
            /// <summary>Runs with another scanner and has no switch of its own (suspect deps need the dependencies).</summary>
            public string RunsWith { get; }

            public Scanner(string key, string label, string description, string icon, bool onSave, string runsWith = null)
            {
                Key = key; Label = label; Description = description; Icon = icon; OnSave = onSave; RunsWith = runsWith;
            }
        }

        public static readonly IReadOnlyList<Scanner> Scanners = new[]
        {
            new Scanner("secrets", "Secrets", "Hardcoded credentials and tokens", "secrets.png", onSave: true),
            new Scanner("sast", "SAST", "Vulnerabilities in your own code", "code-sec.png", onSave: true),
            new Scanner("deps", "SCA", "Vulnerable open source dependencies (also suspect dependencies)", "open-source.png", onSave: false),
            new Scanner("iac", "IaC", "Terraform, Kubernetes and CloudFormation flaws", "iac.png", onSave: true),
            new Scanner("misconf", "CI/CD misconfigurations", "Pipeline and SCM settings", "misconf.png", onSave: false),
            new Scanner("malware", "Malware", "Malicious and suspicious packages", "malware.png", onSave: true),
            new Scanner("quality", "Code Quality", "Maintainability and reliability issues", "code-quality.png", onSave: true),
            new Scanner("apisec", "API Security", "Vulnerabilities in your APIs", "api-security.png", onSave: false),
            new Scanner("ai", "AI Security", "OWASP LLM Top 10 risks in your AI code", "ai-security.png", onSave: false),
            new Scanner("suspectdeps", "Suspect dependencies", "Runs with SCA", "open-source.png", onSave: false, runsWith: "deps"),
        };

        /// <summary>The scanners with a switch of their own.</summary>
        public static IEnumerable<Scanner> SwitchableScanners => Scanners.Where(scanner => scanner.RunsWith == null);

        /// <summary>The scan types <c>--incremental</c> can restrict to the changed files.</summary>
        public static IEnumerable<Scanner> OnSaveScanners => Scanners.Where(scanner => scanner.OnSave);

        /// <summary>Every scan type, in the order the <c>--run=</c> list always had (the UI order above is a different one).</summary>
        public static readonly IReadOnlyList<string> AllScanTypes = new[] { "deps", "secrets", "misconf", "iac", "suspectdeps", "sast", "malware", "quality", "apisec", "ai" };

        /// <summary>What the plugins always ran: every type in the full scan, secrets / IaC / SAST / malware on save.</summary>
        public static readonly IReadOnlyList<string> DefaultFullScanTypes = AllScanTypes;
        public static readonly IReadOnlyList<string> DefaultOnSaveScanTypes = new[] { "secrets", "iac", "sast", "malware" };

        /// <summary>Scan types of the full scan, in CLI order. Never empty.</summary>
        public IReadOnlyList<string> Scans { get; }
        /// <summary>Scan types of the on-save incremental scan: a subset of <see cref="Scans"/> among the file-scoped types.</summary>
        public IReadOnlyList<string> OnSave { get; }
        /// <summary>Glob patterns (scanner syntax, <c>**</c> at any depth) added to the scanner's default excludes.</summary>
        public IReadOnlyList<string> Excludes { get; }
        /// <summary>Extra <c>xygeni scan</c> options in canonical form (<c>--name=value</c> / <c>--name</c>), only the ones in <see cref="ScanOptions.Specs"/>.</summary>
        public IReadOnlyList<string> Options { get; }

        private ScanConfig(IReadOnlyList<string> scans, IReadOnlyList<string> onSave, IReadOnlyList<string> excludes, IReadOnlyList<string> options)
        {
            Scans = scans; OnSave = onSave; Excludes = excludes; Options = options;
        }

        public static ScanConfig Defaults() => new ScanConfig(DefaultFullScanTypes, DefaultOnSaveScanTypes, new string[0], new string[0]);

        public bool IsDefault =>
            new HashSet<string>(Scans).SetEquals(DefaultFullScanTypes) && new HashSet<string>(OnSave).SetEquals(DefaultOnSaveScanTypes)
            && Excludes.Count == 0 && Options.Count == 0;

        /// <summary>
        /// Makes a configuration consistent, whatever it was read from: unknown types dropped, CLI order kept, a scanner
        /// that runs with another follows it, on-save limited to the file-scoped types that are also in the full scan,
        /// and an empty full scan falls back to the defaults (the scanner needs at least one type).
        /// </summary>
        public static ScanConfig Normalize(IEnumerable<string> scans, IEnumerable<string> onSave, IEnumerable<string> excludes) =>
            Normalize(scans, onSave, excludes, null);

        public static ScanConfig Normalize(IEnumerable<string> scans, IEnumerable<string> onSave, IEnumerable<string> excludes, IEnumerable<string> options)
        {
            var requested = new HashSet<string>((scans ?? DefaultFullScanTypes).Where(type => AllScanTypes.Contains(type)));
            foreach (var scanner in Scanners.Where(scanner => scanner.RunsWith != null))
            {
                if (requested.Contains(scanner.RunsWith)) requested.Add(scanner.Key); else requested.Remove(scanner.Key);
            }
            var effectiveScans = AllScanTypes.Where(requested.Contains).ToList();
            if (effectiveScans.Count == 0) effectiveScans = DefaultFullScanTypes.ToList();

            var requestedOnSave = new HashSet<string>(onSave ?? DefaultOnSaveScanTypes);
            var onSaveTypes = new HashSet<string>(OnSaveScanners.Select(scanner => scanner.Key));
            var effectiveOnSave = AllScanTypes.Where(type => requestedOnSave.Contains(type) && onSaveTypes.Contains(type) && effectiveScans.Contains(type)).ToList();

            var effectiveExcludes = (excludes ?? new string[0]).Where(value => value != null).Select(value => value.Trim())
                .Where(value => value.Length > 0).Distinct().ToList();
            return new ScanConfig(effectiveScans, effectiveOnSave, effectiveExcludes, ScanOptions.Normalize(options));
        }

        /// <summary>Parses the file content; invalid JSON or a non-object gives the defaults.</summary>
        public static ScanConfig Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return Defaults();
            try
            {
                var root = JsonConvert.DeserializeObject<JToken>(content) as JObject;
                if (root == null) return Defaults();
                return Normalize(StringList(root, "scans"), StringList(root, "onSave"), StringList(root, "excludes"), StringList(root, "options"));
            }
            catch (JsonException)
            {
                return Defaults();
            }
        }

        // A missing key is null (defaults); a key that is not an array of strings is an empty list.
        private static List<string> StringList(JObject root, string key)
        {
            var element = root[key];
            if (element == null) return null;
            var array = element as JArray;
            if (array == null) return new List<string>();
            return array.Where(item => item.Type == JTokenType.String).Select(item => item.Value<string>()).ToList();
        }

        public string ToJson()
        {
            var root = new JObject
            {
                ["scans"] = new JArray(Scans),
                ["onSave"] = new JArray(OnSave),
                ["excludes"] = new JArray(Excludes),
                ["options"] = new JArray(Options)
            };
            return root.ToString(Formatting.Indented) + "\n";
        }

        public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ConfigDir, ConfigFile);

        /// <summary>The configuration of a project: the file when it exists, the defaults otherwise.</summary>
        public static ScanConfig Load(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot)) return Defaults();
            try
            {
                string path = PathFor(projectRoot);
                return Parse(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (IOException) { return Defaults(); }
            catch (UnauthorizedAccessException) { return Defaults(); }
        }

        /// <summary>Writes the configuration; the defaults remove the file instead, so an untouched project leaves nothing behind.</summary>
        public static void Save(string projectRoot, ScanConfig config)
        {
            string path = PathFor(projectRoot);
            var normalized = Normalize(config.Scans, config.OnSave, config.Excludes, config.Options);
            if (normalized.IsDefault)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, normalized.ToJson());
        }

        /// <summary>A copy with <paramref name="type"/> switched in or out of the full scan; on-save follows (it can never exceed the full scan).</summary>
        public ScanConfig WithScanType(string type, bool enabled)
        {
            var scans = enabled ? Scans.Concat(new[] { type }) : Scans.Where(value => value != type);
            return Normalize(scans, OnSave, Excludes, Options);
        }

        /// <summary>A copy with <paramref name="type"/> switched in or out of the on-save scan.</summary>
        public ScanConfig WithOnSaveType(string type, bool enabled)
        {
            var onSave = enabled ? OnSave.Concat(new[] { type }) : OnSave.Where(value => value != type);
            return Normalize(Scans, onSave, Excludes, Options);
        }

        /// <summary>The default on-save types (within the full scan): what Auto Scan on Save runs when nothing was chosen.</summary>
        public ScanConfig WithDefaultOnSave() => Normalize(Scans, DefaultOnSaveScanTypes, Excludes, Options);

        public ScanConfig WithAllScanTypes(bool enabled) => Normalize(enabled ? AllScanTypes : new string[0], OnSave, Excludes, Options);

        public ScanConfig WithExclude(string pattern) => Normalize(Scans, OnSave, Excludes.Concat(new[] { pattern }), Options);

        public ScanConfig WithoutExclude(string pattern) => Normalize(Scans, OnSave, Excludes.Where(value => value != pattern), Options);

        /// <summary>Adds (or replaces, same option) one canonical scan option.</summary>
        public ScanConfig WithScanOption(string option)
        {
            string name = ScanOptions.NameOf(option);
            return Normalize(Scans, OnSave, Excludes, Options.Where(entry => ScanOptions.NameOf(entry) != name).Concat(new[] { option }));
        }

        /// <summary>Removes the option with that name (<c>--ai-review</c>), whatever its value.</summary>
        public ScanConfig WithoutScanOption(string name) =>
            Normalize(Scans, OnSave, Excludes, Options.Where(entry => ScanOptions.NameOf(entry) != name));

        /// <summary>
        /// The <c>--exclude-add=&lt;patterns&gt;</c> argument, or null without excludes. <c>--exclude-add</c> keeps the scanner's
        /// default excludes (node_modules, target, dist…); <c>-e/--exclude</c> would replace them (scanner 6.23+, #2324).
        /// </summary>
        public string ExcludeAddArgument() => Excludes.Count > 0 ? "--exclude-add=" + string.Join(",", Excludes) : null;

        /// <summary>
        /// The exclude pattern of a folder inside the project: its path relative to the root, forward slashes, plus
        /// <c>/**</c> (everything under it). Null for the root itself or a folder outside it.
        /// </summary>
        public static string ExcludePatternForFolder(string projectRoot, string folder)
        {
            if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(folder)) return null;
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            string relative = full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            return relative.Length == 0 ? null : relative + "/**";
        }
    }
}
