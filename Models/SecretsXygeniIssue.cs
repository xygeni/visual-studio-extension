using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    public class SecretsXygeniIssue : AbstractXygeniIssue
    {
        public string Hash { get; set; }
        public string Resource { get; set; }
        public string FoundBy { get; set; }
        public string Secret { get; set; }
        public long TimeAdded { get; set; }
        public string Branch { get; set; }
        public string CommitHash { get; set; }
        public string User { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                    {Field("Type", Type)}
                    {Field("Secret", Secret)}
                    {Where(Branch, CommitHash, User)}
                    {Field("Date", TimeAdded.ToString())}
                    {Field("Resource", Resource)}
                    {Field("Location", Url)}                    
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
