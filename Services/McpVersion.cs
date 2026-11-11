using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Version strings of the Xygeni MCP server (xygeni/xygeni-product-backlog#2382).
    /// The published <c>version.txt</c> holds the JAR version on a single line (<c>1.2.2</c>, no <c>v</c> prefix);
    /// the installed one is kept in <c>%USERPROFILE%\.xygeni\mcp\version.txt</c>. A missing or unparseable
    /// installed version counts as outdated, so a JAR installed before the version file existed is updated once.
    /// </summary>
    public static class McpVersion
    {
        private static readonly Regex VersionPattern = new Regex(@"^v?(\d+(?:\.\d+)*)(?:[-+].*)?$", RegexOptions.Compiled);
        private static readonly Regex Sha256Pattern = new Regex(@"^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        /// <summary><c>1.2.3</c> -> [1, 2, 3]. A <c>v</c> prefix and a <c>-SNAPSHOT</c> / <c>+build</c> suffix are ignored. Null when not a version.</summary>
        public static int[] Parse(string raw)
        {
            if (raw == null) return null;
            var match = VersionPattern.Match(raw.Trim());
            if (!match.Success) return null;
            var segments = match.Groups[1].Value.Split('.');
            var parsed = new int[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!int.TryParse(segments[i], out parsed[i])) return null;
            }
            return parsed;
        }

        /// <summary>Numeric, segment by segment; a missing segment counts as 0 (<c>1.2</c> == <c>1.2.0</c>). Unparseable sorts first.</summary>
        public static int Compare(string a, string b)
        {
            var left = Parse(a);
            var right = Parse(b);
            if (left == null || right == null) return (left != null ? 1 : 0) - (right != null ? 1 : 0);
            int length = Math.Max(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                int diff = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
                if (diff != 0) return diff;
            }
            return 0;
        }

        /// <summary>True when <paramref name="remote"/> is a version newer than <paramref name="local"/>; an unknown or invalid local is always outdated.</summary>
        public static bool IsNewer(string remote, string local)
        {
            if (Parse(remote) == null) return false;
            if (Parse(local) == null) return true;
            return Compare(remote, local) > 0;
        }

        /// <summary>The version in a <c>version.txt</c> body: first non-blank line, trimmed, without the <c>v</c> prefix. Null when invalid.</summary>
        public static string FromVersionFile(string content)
        {
            if (content == null) return null;
            string line = content.Split('\n').Select(entry => entry.Trim()).FirstOrDefault(entry => entry.Length > 0);
            if (line == null || Parse(line) == null) return null;
            return line.StartsWith("v") ? line.Substring(1) : line;
        }

        /// <summary>The hash in a <c>sha256sum</c>-style body (<c>&lt;hash&gt;  xygeni-mcp-server.jar</c>), lowercase. Null when it is not one.</summary>
        public static string FromChecksumFile(string content)
        {
            if (content == null) return null;
            string token = content.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return token != null && Sha256Pattern.IsMatch(token) ? token.ToLowerInvariant() : null;
        }

        /// <summary>What to do with the MCP download given the published .sha256 (#2382).</summary>
        public enum ChecksumAction { Verify, InstallUnverified, Skip }

        /// <summary>
        /// Never replaces an installed JAR with an unverified one: an update without a valid checksum keeps the installed
        /// copy and is retried at the next check. A first install goes ahead unverified only when the server answers that
        /// the .sha256 is not published (403/404, the state until #2381 is promoted to get.xygeni.io); a timeout, 5xx or
        /// an unreadable checksum installs nothing.
        /// </summary>
        /// <param name="status">HTTP status of the .sha256 GET, null when there was no answer.</param>
        /// <param name="expected">The hash read from a 200 body, null when it is not one.</param>
        public static ChecksumAction GetChecksumAction(int? status, string expected, bool jarInstalled)
        {
            if (status == 200 && expected != null) return ChecksumAction.Verify;
            if (!jarInstalled && (status == 403 || status == 404)) return ChecksumAction.InstallUnverified;
            return ChecksumAction.Skip;
        }
    }
}
