using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    public class SastXygeniIssue : AbstractXygeniIssue
    {
        public string Branch { get; set; }
        public string Cwe { get; set; }
        public List<string> Cwes { get; set; }
        public string Container { get; set; }
        public string Language { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    <tr><th>Explanation</th><td>{Explanation}</td></tr>
                    <tr><th>Type</th><td>{Type}</td></tr>
                    <tr><th>Location</th><td>{File}[{BeginLine + 1}]</td></tr>
                    <tr><th>Found By</th><td>{Detector}</td></tr>
                    <tr><th>CWE</th><td>{Cwe}</td></tr>
                </table>
            </div>";
        }

        public override string GetCodeSnippetHtmlTab()
        {
            return @"<input type=""radio"" name=""tabs"" id=""tab-2""><label for=""tab-2"">CODE SNIPPET</label>";
        }

        
    }
}
