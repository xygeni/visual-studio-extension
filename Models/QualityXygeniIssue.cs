using System;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// Code Quality issue (quality rules: code smells, complexity, naming,
    /// maintainability, reliability...). Single-location (no taint / code-flow) and
    /// auto-remediable via the scanner 'util rectify --quality'.
    ///
    /// Feeds the "Code Quality" tree group and the issue detail panel
    /// (Issue Details + Code Snippet tabs). See ticket xygeni/xygeni-product-backlog#56.
    /// </summary>
    public class QualityXygeniIssue : SingleLocationXygeniIssue
    {
        public string Language { get; set; }

        /// <summary>Quality dimension (e.g. reliability, maintainability, security).</summary>
        public string QualityCategory { get; set; }

        protected override string GetDetailRowsHtml()
        {
            return Field("Category", QualityCategory) + Field("Language", Language);
        }
    }
}
