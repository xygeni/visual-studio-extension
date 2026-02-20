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
                    {Field("Explanation", Explanation)}
                    {Field("Type", Type)}
                    {Where(Branch, null, null)}
                    {Field("Found At", Url)}
                    {Field("Location", File + "[" + BeginLine + "]")}
                    {Field("Found By", Detector)}
                    {GetTags()}                    
                </table>
            </div>";
        }

        public override string GetCodeSnippetHtmlTab()
        {
            return @"<input type=""radio"" name=""tabs"" id=""tab-2""><label for=""tab-2"">CODE SNIPPET</label>";
        }

        
    }
}
