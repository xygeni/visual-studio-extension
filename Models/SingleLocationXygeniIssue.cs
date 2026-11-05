using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// Base of the single-location findings (Code Quality, API Security, AI Security): one details
    /// table, a CODE SNIPPET tab only when the location carries an excerpt, and no code flow.
    /// Subclasses only list the rows specific to their scan type.
    /// </summary>
    public abstract class SingleLocationXygeniIssue : AbstractXygeniIssue
    {
        public string Branch { get; set; }

        /// <summary>Rows specific to the scan type, rendered between Type and Where.</summary>
        protected abstract string GetDetailRowsHtml();

        /// <summary>Rows rendered after Found By (remediation advice); none by default.</summary>
        protected virtual string GetTrailingRowsHtml() => "";

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    {Field("Explanation", Explanation)}
                    {Field("Type", Type)}
                    {GetDetailRowsHtml()}
                    {Where(Branch, null, null)}
                    {Field("Location", File)}
                    {Field("Found By", Detector)}
                    {GetTrailingRowsHtml()}
                    {GetTags()}
                </table>
            </div>";
        }

        public override string GetCodeSnippetHtmlTab()
        {
            // A finding whose location carries no excerpt has nothing to show in the tab.
            if (string.IsNullOrEmpty(Code)) return "";
            return @"<input type=""radio"" name=""tabs"" id=""tab-2""><label for=""tab-2"">CODE SNIPPET</label>";
        }

        protected static string JoinList(List<string> values)
        {
            return (values == null || values.Count == 0) ? "" : string.Join(", ", values);
        }
    }
}
