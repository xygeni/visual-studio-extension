using System;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Plain-http server URLs (xygeni/xygeni-product-backlog#1438). The extension sends the API token as a bearer header
    /// on every request, so over http:// it travels in clear. Rule shared by the four Xygeni IDE plugins: https:// is the
    /// normal case; http:// to a loopback host (localhost, 127.0.0.1, ::1) is allowed silently, the traffic never leaves
    /// the machine; http:// to any other host is refused unless the user opts in with "Allow insecure HTTP", and then the
    /// connection status keeps warning that the token travels in clear.
    /// </summary>
    public static class InsecureUrl
    {
        public const string Warning = "Connected over insecure http: the API token travels in clear";
        public const string Refused = "Insecure URL: the server URL uses http and the API token would travel in clear. "
            + "Use https, or enable \"Allow insecure HTTP\" in Xygeni Settings if this server really has no TLS";

        public static bool IsLoopbackHost(string hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname)) return false;
            string host = hostname.Trim().ToLowerInvariant().TrimStart('[').TrimEnd(']');
            return host == "localhost" || host.EndsWith(".localhost") || System.Text.RegularExpressions.Regex.IsMatch(host, @"^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$") /* the 127/8 block as an address; 127.example.com is a DNS name */
                || host == "::1" || host == "0:0:0:0:0:0:0:1";
        }

        /// <summary>True for an http:// URL whose host is not loopback: the token would leave the machine in clear.</summary>
        public static bool IsInsecureRemote(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri parsed)) return false;
            return parsed.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(parsed.Host);
        }

        /// <summary>Why the URL cannot be used, or null when it can (https, loopback, or insecure but allowed).</summary>
        public static string Problem(string url, bool allowInsecureHttp)
        {
            return IsInsecureRemote(url) && !allowInsecureHttp ? Refused : null;
        }
    }
}
