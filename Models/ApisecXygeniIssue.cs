using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// API Security flaw (ticket xygeni/xygeni-product-backlog#1691). Flaws are scoped to an
    /// endpoint, a module or a service; File/BeginLine come from the report's API inventory
    /// (endpoint handler, handler file or OpenAPI spec) and may stay empty, in which case the
    /// finding is still listed in the tree. The tree label (Type) is the machine flawType, like
    /// every other category; the human Title (it embeds the endpoint) is shown in the details.
    /// No AI auto-fix: the scanner has no 'util rectify --apisec'.
    /// </summary>
    public class ApisecXygeniIssue : SingleLocationXygeniIssue
    {
        /// <summary>Human-readable label of the flaw, e.g. "Endpoint reachable without authentication: GET /users".</summary>
        public string Title { get; set; }
        public string EndpointMethod { get; set; }
        public string EndpointPath { get; set; }
        public string ModuleName { get; set; }
        public string ServiceName { get; set; }
        public List<string> OwaspApiTop10 { get; set; }
        public List<string> Cwes { get; set; }
        public string Remediation { get; set; }

        public string GetEndpoint()
        {
            if (string.IsNullOrEmpty(EndpointPath)) return "";
            return string.IsNullOrEmpty(EndpointMethod) ? EndpointPath : $"{EndpointMethod} {EndpointPath}";
        }

        protected override string GetDetailRowsHtml()
        {
            return Field("Title", Title)
                + Field("Endpoint", GetEndpoint())
                + Field("Module", ModuleName)
                + Field("Service", ServiceName)
                + Field("OWASP API Top 10", JoinList(OwaspApiTop10))
                + Field("CWE", JoinList(Cwes));
        }

        protected override string GetTrailingRowsHtml() => FieldMarkdown("Remediation", Remediation);
    }
}
