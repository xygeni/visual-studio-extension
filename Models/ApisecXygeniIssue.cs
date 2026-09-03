using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// API Security flaw (ticket xygeni/xygeni-product-backlog#1691). Flaws are scoped to an
    /// endpoint, a module or a service, so most of them carry no source location: File stays
    /// empty, the positional fields stay at 0 and the finding is still listed in the tree.
    /// No AI auto-fix: the scanner has no 'util rectify --apisec'.
    /// </summary>
    public class ApisecXygeniIssue : AbstractXygeniIssue
    {
        public string Branch { get; set; }
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

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    {Field("Explanation", Explanation)}
                    {Field("Type", Type)}
                    {Field("Endpoint", GetEndpoint())}
                    {Field("Module", ModuleName)}
                    {Field("Service", ServiceName)}
                    {Field("OWASP API Top 10", JoinList(OwaspApiTop10))}
                    {Field("CWE", JoinList(Cwes))}
                    {Where(Branch, null, null)}
                    {Field("Location", File)}
                    {Field("Found By", Detector)}
                    {Field("Remediation", Remediation)}
                    {GetTags()}
                </table>
            </div>";
        }

        public override string GetCodeSnippetHtmlTab()
        {
            if (string.IsNullOrEmpty(File)) return "";
            return @"<input type=""radio"" name=""tabs"" id=""tab-2""><label for=""tab-2"">CODE SNIPPET</label>";
        }

        private static string JoinList(List<string> values)
        {
            return (values == null || values.Count == 0) ? "" : string.Join(", ", values);
        }
    }
}
