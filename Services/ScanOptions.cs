using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Options of <c>xygeni scan</c> the user may add from the IDE (xygeni/xygeni-product-backlog#415, "scanner options").
    ///
    /// A closed list, not free text: Xygeni Settings offers these with autocompletion and refuses anything else, and the
    /// same validation runs when <c>.xygeni\ide.json</c> is read, so a hand-edited file cannot smuggle in an option the
    /// extension manages itself (<c>--run</c>, <c>-o</c>, ...). Taken from ScanCommand and its option groups in
    /// DepsDoctor-CLI (<c>xygeni scan --help</c>). Kept identical in the four Xygeni IDE plugins.
    /// </summary>
    public static class ScanOptions
    {
        public enum Kind { Flag, Enum, List, Text, Path }

        public sealed class Spec
        {
            /// <summary>Long name, with the dashes (<c>--run-languages</c>).</summary>
            public string Name { get; }
            public Kind Kind { get; }
            /// <summary>One line, as in the CLI help.</summary>
            public string Description { get; }
            /// <summary>Enum: the only values accepted. List: suggested items (others are accepted too unless <see cref="Closed"/>).</summary>
            public IReadOnlyList<string> Values { get; }
            /// <summary>List: every item must be one of <see cref="Values"/>.</summary>
            public bool Closed { get; }
            public string Placeholder { get; }

            public Spec(string name, Kind kind, string description, IReadOnlyList<string> values = null, bool closed = false, string placeholder = null)
            {
                Name = name; Kind = kind; Description = description; Values = values ?? new string[0]; Closed = closed; Placeholder = placeholder;
            }
        }

        private static readonly string[] Languages = { "csharp", "go", "html", "java", "javascript", "kotlin", "php", "python", "swift", "typescript" };
        private static readonly string[] Severities = { "critical", "high", "low", "info" };

        /// <summary>The options offered, in the order of the autocompletion.</summary>
        public static readonly IReadOnlyList<Spec> Specs = new[]
        {
            new Spec("--run-languages", Kind.List, "Languages to analyze (SAST, Code Quality); all if not set", Languages, closed: true, placeholder: "java,typescript"),
            new Spec("--skip-languages", Kind.List, "Languages to leave out of the analysis", Languages, closed: true, placeholder: "html"),
            new Spec("--include", Kind.List, "Include patterns, comma-separated (glob, ** at any depth)", placeholder: "src/**"),
            new Spec("--detectors", Kind.Text, "Detectors to include per scan: <scan>=<ids, a severity or all>, | between scans", placeholder: "secrets=high|iac=all"),
            new Spec("--skip-detectors", Kind.Text, "Detectors to leave out per scan, same syntax as --detectors", placeholder: "sast=hardcoded_password"),
            new Spec("--mute", Kind.Path, "Mute descriptor file (.xygeni.mute.yml) or #name of a named profile", placeholder: ".xygeni.mute.yml"),
            new Spec("--standard", Kind.Text, "ID of the compliance standard to check", placeholder: "owasp-top10"),
            new Spec("--ai-review", Kind.Enum, "Which findings an AI agent reviews (needs an AI agent configured)", new[] { "off", "all", "ai-detected" }),
            new Spec("--ai-review-filter", Kind.List, "Findings the AI review looks at: a severity (that one and higher), detector IDs, !<id> to leave one out, or all", Severities, placeholder: "high,!sql_injection"),
            new Spec("--ai-mode", Kind.Enum, "Convention profile of the application before analyzing it (needs an AI agent configured)", new[] { "off", "auto", "stored", "rebuild" }),
            new Spec("--include-license-analysis", Kind.Flag, "Evaluate the licenses of the components (SCA)"),
            new Spec("--conf", Kind.Path, "Scan configuration file template (the filename is prefixed with SCAN.)", placeholder: "conf/custom.yml"),
            new Spec("--custom-detectors-dir", Kind.Path, "Directory with custom detectors", placeholder: ".xygeni/detectors"),
        };

        /// <summary>Options the extension builds itself or that would break the IDE flow, with the reason shown when typed.</summary>
        public static readonly IReadOnlyDictionary<string, string> Managed = new Dictionary<string, string>
        {
            ["--run"] = "chosen with the scan type switches",
            ["--skip"] = "chosen with the scan type switches",
            ["--incremental"] = "set by Auto Scan on Save",
            ["-d"] = "the solution folder is scanned", ["--dir"] = "the solution folder is scanned",
            ["-o"] = "the report location is managed by the extension", ["--output"] = "the report location is managed by the extension",
            ["-f"] = "the extension reads JSON reports", ["--format"] = "the extension reads JSON reports",
            ["--upload"] = "IDE scans are never uploaded", ["--no-upload"] = "IDE scans are never uploaded",
            ["--include-vulnerabilities"] = "always set by the extension",
            ["-e"] = "replaces the scanner defaults: use Excluded folders instead", ["--exclude"] = "replaces the scanner defaults: use Excluded folders instead",
            ["--exclude-add"] = "use Excluded folders", ["--exclude-remove"] = "use Excluded folders",
            ["-n"] = "the project is named by the extension", ["--name"] = "the project is named by the extension",
            ["--branch"] = "the branch is taken from the solution",
            ["-repo"] = "the IDE scans the local solution", ["--repository"] = "the IDE scans the local solution",
            ["--repo-token"] = "the IDE scans the local solution", ["--repo-branch"] = "the IDE scans the local solution",
            ["--api-key"] = "the token comes from the Xygeni configuration",
            ["--never-fail"] = "the extension reads the exit code", ["--fail-on"] = "the extension reads the exit code",
            ["--try-all-scans"] = "the extension reads the exit code", ["--stop-on-errors"] = "the extension reads the exit code",
            ["--secrets-mode"] = "internal", ["--iac-mode"] = "internal", ["--sast-mode"] = "internal", ["--malware-mode"] = "internal",
            ["-b"] = "baselines are a CI concern", ["--baseline"] = "baselines are a CI concern", ["--set-baseline"] = "baselines are a CI concern",
            ["--auto-remediate"] = "opens pull requests: a CI option", ["--pr-comment"] = "comments on pull requests: a CI option", ["--triage"] = "a CI option",
        };

        public sealed class Parsed
        {
            public string Name { get; }
            /// <summary>Null for a bare option.</summary>
            public string Value { get; }
            public Parsed(string name, string value) { Name = name; Value = value; }
        }

        private static readonly Regex Option = new Regex(@"^(-{1,2}[A-Za-z][\w-]*)(?:\s*=\s*|\s+)?(.*)$", RegexOptions.Singleline);

        /// <summary><c>--name</c>, <c>--name=value</c> or <c>--name value</c> (one option per entry); null when it is not an option at all.</summary>
        public static Parsed Parse(string text)
        {
            string trimmed = (text ?? string.Empty).Trim();
            if (!trimmed.StartsWith("-", StringComparison.Ordinal)) return null;
            var match = Option.Match(trimmed);
            if (!match.Success) return null;
            string rest = match.Groups[2].Value.Trim();
            return new Parsed(match.Groups[1].Value, rest.Length == 0 ? null : rest);
        }

        public static Spec SpecOf(string name) => Specs.FirstOrDefault(spec => spec.Name == name);

        /// <summary>The outcome of <see cref="Validate"/>: <see cref="Option"/> is the canonical form when <see cref="Ok"/>, <see cref="Error"/> the reason otherwise.</summary>
        public sealed class Validation
        {
            public bool Ok { get; }
            public string Option { get; }
            public Spec Spec { get; }
            public string Error { get; }
            /// <summary>An option typed without its value yet: not an error to paint in red.</summary>
            public bool Pending { get; }

            private Validation(bool ok, string option, Spec spec, string error, bool pending)
            {
                Ok = ok; Option = option; Spec = spec; Error = error; Pending = pending;
            }

            internal static Validation Accepted(string option, Spec spec) => new Validation(true, option, spec, null, false);
            internal static Validation Refused(string error) => new Validation(false, null, null, error, false);
            internal static Validation Incomplete(string error, Spec spec) => new Validation(false, null, spec, error, true);
        }

        /// <summary>Checks one entry. Refuses what is not in <see cref="Specs"/>, saying why when the extension manages that option itself.</summary>
        public static Validation Validate(string text)
        {
            var parsed = Parse(text);
            if (parsed == null) return Validation.Refused("An option starts with -- (type -- to see them)");
            if (Managed.TryGetValue(parsed.Name, out string managed)) return Validation.Refused($"{parsed.Name} is not available here: {managed}");
            var spec = SpecOf(parsed.Name);
            if (spec == null) return Validation.Refused($"{parsed.Name} is not an option the IDE can pass to the scanner");
            if (spec.Kind == Kind.Flag)
            {
                return parsed.Value == null ? Validation.Accepted(spec.Name, spec) : Validation.Refused($"{spec.Name} takes no value");
            }
            string value = (parsed.Value ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return Validation.Incomplete($"{spec.Name} needs a value" + (spec.Placeholder != null ? $", like {spec.Placeholder}" : ""), spec);
            }
            if (spec.Kind == Kind.Enum && !spec.Values.Contains(value))
            {
                return Validation.Refused($"{spec.Name} is one of {string.Join(", ", spec.Values)}");
            }
            if (spec.Kind == Kind.List)
            {
                var items = value.Split(',').Select(item => item.Trim()).ToList();
                if (items.Any(item => item.Length == 0)) return Validation.Refused($"{spec.Name}: comma-separated values, none empty");
                if (spec.Closed)
                {
                    var unknown = items.Where(item => !spec.Values.Contains(item)).ToList();
                    if (unknown.Count > 0) return Validation.Refused($"{spec.Name}: {string.Join(", ", unknown)} not among {string.Join(", ", spec.Values)}");
                }
                return Validation.Accepted($"{spec.Name}={string.Join(",", items)}", spec);
            }
            if (spec.Kind != Kind.Path && value.Any(char.IsWhiteSpace)) return Validation.Refused($"{spec.Name}: no spaces in the value");
            return Validation.Accepted($"{spec.Name}={value}", spec);
        }

        /// <summary>The option name of a canonical entry (<c>--ai-review=all</c> → <c>--ai-review</c>).</summary>
        public static string NameOf(string entry) => Parse(entry)?.Name ?? entry;

        /// <summary>Keeps the valid entries, in canonical form, one per option (the last one wins), in the order of <see cref="Specs"/>.</summary>
        public static List<string> Normalize(IEnumerable<string> entries)
        {
            var result = new List<string>();
            if (entries == null) return result;
            var byName = new Dictionary<string, string>();
            foreach (string entry in entries)
            {
                if (entry == null) continue;
                var validation = Validate(entry);
                if (validation.Ok) byName[validation.Spec.Name] = validation.Option;
            }
            foreach (var spec in Specs)
            {
                if (byName.TryGetValue(spec.Name, out string option)) result.Add(option);
            }
            return result;
        }

        /// <summary>
        /// What the autocompletion offers for the text typed so far: option names while there is no <c>=</c> yet (the whole
        /// text replaced by <c>--name=</c>, or <c>--name</c> for a flag), the values of an Enum / List option after it.
        /// </summary>
        public static List<string> Suggestions(string text)
        {
            string typed = (text ?? string.Empty).TrimStart();
            var parsed = Parse(typed);
            bool hasSeparator = typed.Contains("=") || typed.Trim().Any(char.IsWhiteSpace);
            var result = new List<string>();
            if (parsed == null || !hasSeparator)
            {
                string prefix = (parsed != null ? parsed.Name : typed).ToLowerInvariant();
                foreach (var spec in Specs)
                {
                    if (prefix.Length == 0 || prefix == "-" || spec.Name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        result.Add(spec.Kind == Kind.Flag ? spec.Name : spec.Name + "=");
                    }
                }
                return result;
            }
            var found = SpecOf(parsed.Name);
            if (found == null || found.Values.Count == 0) return result;
            var items = (parsed.Value ?? string.Empty).Split(',');
            string last = items[items.Length - 1].Trim().ToLowerInvariant();
            var head = items.Take(items.Length - 1).Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
            string baseText = found.Name + "=" + (head.Count == 0 ? "" : string.Join(",", head) + ",");
            foreach (string value in found.Values)
            {
                if (value.StartsWith(last, StringComparison.Ordinal) && !head.Contains(value)) result.Add(baseText + value);
            }
            return result;
        }
    }
}
