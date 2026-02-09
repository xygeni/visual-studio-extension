using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    public class SecretsXygeniIssue : AbstractXygeniIssue
    {
        public string Hash { get; set; }
        public string Resource { get; set; }
        public string FoundBy { get; set; }
        public string Secret { get; set; }
        public long TimeAdded { get; set; }
        public string Branch { get; set; }
        public string CommitHash { get; set; }
        public string User { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    <tr><th>Type</th><td>{Type}</td></tr>
                    <tr><th>Detector</th><td>{Detector}</td></tr>
                    <tr><th>Resource</th><td>{Resource}</td></tr>
                    <tr><th>File</th><td>{File}</td></tr>
                    <tr><th>Line</th><td>{BeginLine + 1}</td></tr>
                </table>
            </div>";
        }

        public override string GetCodeSnippetHtmlTab()
        {
            return @"<input type=""radio"" name=""tabs"" id=""tab-2""><label for=""tab-2"">CODE SNIPPET</label>";
        }
    }
}
