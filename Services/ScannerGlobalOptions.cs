using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Global options of the Xygeni CLI (XygeniScanner.java @Option on the root command, not inherited by
    /// subcommands): they must go between the launcher and the command (`xygeni &lt;global&gt; scan ...`), or
    /// picocli rejects them as unknown options of `scan`. The launcher script also hands them to the Updater,
    /// so --skip-ssl-verify covers the self-update download too. (xygeni/tech-support#378)
    /// </summary>
    public static class ScannerGlobalOptions
    {
        public const string SkipSslVerify = "--skip-ssl-verify";
        public const string SkipUpdate = "--skip-update";
        public const string Verbose = "--verbose";

        // Never reach the scanner: -q/--quiet hide the console output the extension reads (licence lines), and the API
        // key must not be on the command line (the token already goes in the environment, from the Xygeni configuration).
        private static readonly string[] Blocked = { "-q", "--quiet" };
        private const string ApiKey = "--api-key";

        // What a JVM prints when a TLS-intercepting proxy re-signs the traffic with a CA it does not trust.
        private static readonly string[] CertificateErrorMarkers =
        {
            "PKIX path building failed",
            "unable to find valid certification path",
            "SSLHandshakeException"
        };

        /// <summary>
        /// Splits the user's option string like a shell would split words, honouring single and double quotes
        /// (`-cop "a=b c"` → [-cop, a=b c]). No expansion of any kind.
        /// </summary>
        public static List<string> Split(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return tokens;
            }
            var current = new StringBuilder();
            bool inToken = false;
            char? quote = null;
            foreach (char ch in text)
            {
                if (quote.HasValue)
                {
                    if (ch == quote.Value) { quote = null; } else { current.Append(ch); }
                }
                else if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    inToken = true;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    if (inToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }
                }
                else
                {
                    current.Append(ch);
                    inToken = true;
                }
            }
            if (inToken)
            {
                tokens.Add(current.ToString());
            }
            return tokens;
        }

        /// <summary>
        /// The global options to put before the CLI command: the checked options first, then the free-text
        /// ones, without repeating an option and without the blocked ones.
        /// </summary>
        public static List<string> Build(IEnumerable<string> enabledOptions, string additionalOptions)
        {
            List<string> extra = Partition(additionalOptions, true);
            return enabledOptions.Where(option => !extra.Contains(option)).Concat(extra).ToList();
        }

        /// <summary>The options in the free-text setting that are dropped (see Blocked).</summary>
        public static List<string> BlockedIn(string additionalOptions)
        {
            return Partition(additionalOptions, false);
        }

        /// <summary>The free-text options that are kept, or the blocked ones (Blocked; --api-key with its value).</summary>
        private static List<string> Partition(string additionalOptions, bool kept)
        {
            var keptOptions = new List<string>();
            var blockedOptions = new List<string>();
            List<string> tokens = Split(additionalOptions);
            for (int index = 0; index < tokens.Count; index++)
            {
                string token = tokens[index];
                if (Blocked.Contains(token))
                {
                    blockedOptions.Add(token);
                }
                else if (token == ApiKey)
                {
                    blockedOptions.Add(token);
                    index++; // its value
                }
                else if (token.StartsWith(ApiKey + "=", StringComparison.Ordinal))
                {
                    blockedOptions.Add(ApiKey);
                }
                else
                {
                    keptOptions.Add(token);
                }
            }
            return kept ? keptOptions : blockedOptions;
        }

        public static bool IsCertificateError(string output)
        {
            return output != null && CertificateErrorMarkers.Any(marker => output.IndexOf(marker, StringComparison.Ordinal) >= 0);
        }
    }
}
