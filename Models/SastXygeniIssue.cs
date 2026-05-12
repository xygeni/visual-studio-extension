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
        public List<CodeFlow> CodeFlows { get; set; }

        /// <summary>
        /// Verbatim JSON node from the scanner report. Preserved so the AI Explain
        /// CLI can be fed the exact shape it expects (--issue-json).
        /// </summary>
        public string RawJson { get; set; }

        public SastXygeniIssue()
        {
            CodeFlows = new List<CodeFlow>();
        }

        public bool HasCodeFlow => CodeFlows != null && CodeFlows.Count > 0;

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

        public override string GetCodeFlowTab()
        {
            if (!HasCodeFlow) return string.Empty;
            return "<div id='tab-btn-4' class='tab' onclick='showTab(4)'>CODE FLOW</div>";
        }

        public override string GetAiExplainTab()
        {
            // AI Explain is offered for every SAST issue. The CLI call only fires on tab click.
            return "<div id='tab-btn-5' class='tab' onclick='activateAiTab()'>AI EXPLANATION</div>";
        }
    }
}
