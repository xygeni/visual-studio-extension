using System;

namespace vs2026_plugin.Models
{
    public class IacXygeniIssue : AbstractXygeniIssue
    {
        public string Resource { get; set; }
        public string Provider { get; set; }
        public string FoundBy { get; set; }
        public string Branch { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                  {Field("Type", Type)}               
                  {Field("Provider", Provider)}
                  {Where(Branch, null, null)}
                  {Field("Location", File)}
                  {Field("Resource", Resource)}
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
