using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// A finding that can carry code flows (SAST vulnerabilities, malware evidences): the details
    /// render its CODE FLOW tab and AI Explain sends its raw report JSON to the scanner.
    /// </summary>
    public interface ICodeFlowIssue
    {
        List<CodeFlow> CodeFlows { get; }
        bool HasCodeFlow { get; }

        /// <summary>Verbatim JSON node from the scanner report, fed to `util ai-explain --issue-json-file`.</summary>
        string RawJson { get; }
    }
}
