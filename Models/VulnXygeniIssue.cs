using System;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    public class VulnXygeniIssue : AbstractXygeniIssue
    {
        public bool Virtual { get; set; }
        public string RepositoryType { get; set; }
        public string DisplayFileName { get; set; }
        public string Group { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public List<string> DependencyPaths { get; set; }
        public bool DirectDependency { get; set; }
        public double BaseScore { get; set; }
        public string Versions { get; set; }
        public string PublicationDate { get; set; }
        public List<string> Weakness { get; set; }
        public List<string> References { get; set; }
        public string Vector { get; set; }
        public string Language { get; set; }

        public override string GetIssueDetailsHtml()
        {
            return $@"
            <div id=""tab-content-1"">
                <table>
                
                    {Field("Published", PublicationDate)}
                    {Field("Affecting", !string.IsNullOrEmpty(Group) ? Group + ":" + Name + ":" + Version : (Name + ":" + Version) )}
                    {Field("Versions", Versions)}
                    {Field("File", File)}
                    {Field("Direct Dependency", DirectDependency.ToString())}
                    {Field("Vector", Vector)}

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
