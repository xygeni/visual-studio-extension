using System;

namespace vs2026_plugin.Models
{
    public class MisconfXygeniIssue : AbstractXygeniIssue
    {
        public string ToolKind { get; set; }
        public string CurrentBranch { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    {Field("Explanation", Explanation)}
                    {Where(CurrentBranch, null, null)}
                    {Field("Location", File)}
                    {Field("Found By", Detector)}
                    {Field("Tool", ToolKind)}
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
