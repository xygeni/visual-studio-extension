using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// AI Security vulnerability (ticket xygeni/xygeni-product-backlog#1692): prompt injection,
    /// unbounded user content in system prompts, mutable model labels... Single-location
    /// findings, no taint / code-flow. No AI auto-fix: the scanner has no 'util rectify --ai'.
    /// </summary>
    public class AiXygeniIssue : AbstractXygeniIssue
    {
        public string Branch { get; set; }

        /// <summary>Kind of AI asset the finding is attached to (ai_model, ai_agent, prompt...).</summary>
        public string AssetKind { get; set; }

        /// <summary>Control ids of the standards the finding maps to (LLM01, ASI05...).</summary>
        public List<string> Standards { get; set; }
        public List<string> RedTeamVectors { get; set; }
        public string RemediationHint { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    {Field("Explanation", Explanation)}
                    {Field("Type", Type)}
                    {Field("AI Asset", AssetKind)}
                    {Field("Standards", JoinList(Standards))}
                    {Field("Red Team Vectors", JoinList(RedTeamVectors))}
                    {Where(Branch, null, null)}
                    {Field("Location", File)}
                    {Field("Found By", Detector)}
                    {Field("Remediation", RemediationHint)}
                    {GetTags()}
                </table>
            </div>";
        }

        public override string GetCodeSnippetHtmlTab()
        {
            return @"<input type=""radio"" name=""tabs"" id=""tab-2""><label for=""tab-2"">CODE SNIPPET</label>";
        }

        private static string JoinList(List<string> values)
        {
            return (values == null || values.Count == 0) ? "" : string.Join(", ", values);
        }
    }
}
