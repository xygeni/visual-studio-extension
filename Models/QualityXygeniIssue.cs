using System;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// Code Quality issue (quality rules: code smells, complexity, naming,
    /// maintainability, reliability...). Modelled after <see cref="SastXygeniIssue"/>
    /// but simpler: quality findings are single-location (no taint / code-flow).
    /// Auto-remediable via the scanner 'util rectify --quality'.
    ///
    /// Feeds the "Code Quality" tree group and the issue detail panel
    /// (Issue Details + Code Snippet tabs). See ticket xygeni/xygeni-product-backlog#56.
    /// </summary>
    public class QualityXygeniIssue : AbstractXygeniIssue
    {
        public string Branch { get; set; }
        public string Language { get; set; }

        /// <summary>Quality dimension (e.g. reliability, maintainability, security).</summary>
        public string QualityCategory { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    {Field("Explanation", Explanation)}
                    {Field("Type", Type)}
                    {Field("Category", QualityCategory)}
                    {Field("Language", Language)}
                    {Where(Branch, null, null)}
                    {Field("Location", File)}
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
