using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// AI Security vulnerability (ticket xygeni/xygeni-product-backlog#1692): prompt injection,
    /// unbounded user content in system prompts, mutable model labels... Single-location
    /// findings, no taint / code-flow. Auto-remediable via the scanner 'util rectify --ai'.
    /// </summary>
    public class AiXygeniIssue : SingleLocationXygeniIssue
    {
        /// <summary>Kind of AI asset the finding is attached to (ai_model, ai_agent, prompt...).</summary>
        public string AssetKind { get; set; }

        /// <summary>Control ids of the standards the finding maps to (LLM01, ASI05...).</summary>
        public List<string> Standards { get; set; }
        public List<string> RedTeamVectors { get; set; }
        public string RemediationHint { get; set; }

        protected override string GetDetailRowsHtml()
        {
            return Field("AI Asset", AssetKind)
                + Field("Standards", JoinList(Standards))
                + Field("Red Team Vectors", JoinList(RedTeamVectors));
        }

        protected override string GetTrailingRowsHtml() => FieldMarkdown("Remediation", RemediationHint);
    }
}
