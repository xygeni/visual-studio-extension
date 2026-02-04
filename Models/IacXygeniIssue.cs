using System;

namespace vs2026_plugin.Models
{
    public class IacXygeniIssue : AbstractXygeniIssue
    {
        public string Resource { get; set; }
        public string Provider { get; set; }
        public string FoundBy { get; set; }
        public string Branch { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    <tr><th>Type</th><td>{Type}</td></tr>
                    <tr><th>Detector</th><td>{Detector}</td></tr>
                    <tr><th>Resource</th><td>{Resource}</td></tr>
                    <tr><th>Provider</th><td>{Provider}</td></tr>
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
