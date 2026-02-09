using System;
using Markdig;
using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    public interface IXygeniIssue
    {
        string Id { get; set; }
        string Type { get; set; }
        string Detector { get; set; }
        string Tool { get; set; }
        string Kind { get; set; }
        string Severity { get; set; }
        string Confidence { get; set; }
        string Category { get; set; }
        string CategoryName { get; set; }
        string File { get; set; }
        int BeginLine { get; set; }
        int EndLine { get; set; }
        int BeginColumn { get; set; }
        int EndColumn { get; set; }
        string Code { get; set; }
        List<string> Tags { get; set; }
        string Explanation { get; set; }
        string Url { get; set; }
        string RemediableLevel { get; set; }

        int GetSeverityLevel();
        string GetSubtitleLineHtml();
        string GetIssueDetailsHtml();
        string GetCodeSnippetHtml();
        string GetCodeSnippetHtmlTab();
        string GetExplanationShort();
        string GetExplanationHtml();
        string GetRemediationTab();
        string GetRemediationTabContent();
    }

    public abstract class AbstractXygeniIssue : IXygeniIssue
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Detector { get; set; }
        public string Tool { get; set; }
        public string Kind { get; set; }
        public string Severity { get; set; }
        public string Confidence { get; set; }
        public string Category { get; set; }
        public string CategoryName { get; set; }
        public string File { get; set; }
        public int BeginLine { get; set; }
        public int EndLine { get; set; }
        public int BeginColumn { get; set; }
        public int EndColumn { get; set; }
        public string Code { get; set; }
        public List<string> Tags { get; set; }
        public string Explanation { get; set; }
        public string Url { get; set; }
        public string RemediableLevel { get; set; }

        public const string RemediableAuto = "AUTO";
        public const string RemediableManual = "MANUAL";

        protected AbstractXygeniIssue()
        {
            Tags = new List<string>();
        }

        public int GetSeverityLevel()
        {
            switch (Severity?.ToLower())
            {
                case "critical": return 0;
                case "high": return 1;
                case "medium": return 2;
                case "low": return 3;
                case "info": return 4;
                default: return 5;
            }
        }

        public abstract string GetIssueDetailsHtml();
        public abstract string GetCodeSnippetHtmlTab();

        public virtual string GetSubtitleLineHtml()
        {
            string subtitle = CategoryName;
            if (!string.IsNullOrEmpty(Url))
            {
                subtitle += $" &nbsp;&nbsp; <a href=\"{Url}\" target=\"_blank\">{Type}</a>";
            }
            else
            {
                subtitle += $" {Type}";
            }
            return subtitle;
        }

        public virtual string GetCodeSnippetHtml()
        {
            if (string.IsNullOrEmpty(Code)) return "";

            var codeLines = Code.Split('\n');
            string codeSnippet = "";
            for (int i = 0; i < codeLines.Length; i++)
            {
                int lineNumber = BeginLine + i + 1;
                string escapedLine = codeLines[i].Replace("<", "&lt;").Replace(">", "&gt;");
                codeSnippet += $@"
                <tr>
                  <td class=""line-number"">{lineNumber}</td>
                  <td class=""code-line"">{escapedLine}</td>
                </tr>";
            }

            return $@"
              <div id=""tab-content-2"">
                <p class=""file"">{(string.IsNullOrEmpty(File) ? "" : File)}</p>
                <table class=""code-snippet-table"">
                  <tbody>
                    {codeSnippet}
                  </tbody>
                </table>
              </div>";
        }

        public virtual string GetField(string field)
        {
            if(string.IsNullOrEmpty(field)) return "";
            return field;
        }

        public virtual string GetExplanationShort()
        {
            if (string.IsNullOrEmpty(Explanation)) return "";

            var length = Explanation.Length;
            if (length > 30) return Explanation.Substring(0, 30) + "...";
            return Explanation;
        }

        public virtual string GetExplanationHtml()
        {
            if(string.IsNullOrEmpty(Explanation)) return "";
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            return Markdown.ToHtml(Explanation, pipeline);
        }

        public virtual string GetRemediationTab()
        {
            if (!RemediableAuto.Equals(RemediableLevel)) return "";
            return $"<div id='tab-btn-3' class='tab' onclick='showTab(3)'>FIX IT</div>";
        }

        public virtual string GetRemediationTabContent()
        {
            if (!RemediableAuto.Equals(RemediableLevel)) return "";
            return  $@"
            <div id='content-3' class='tab-content'>
                <p>XYGENI AGENT - REMEDIATE ISSUE</p>
                <form id='remediation-form'>
                    <p>This vulnerability is fixable. Run Xygeni Agent to get fixed version preview.</p>
                    <br>
                    <div id='rem-buttons'></div>
                </form>
                <script>
                    window.addEventListener('load', function() {{
                        document.getElementById('rem-buttons').innerHTML = 
                            '<button id=\""rem-preview-button\"" type=\""submit\"" class=\""xy-button\"">Remediate with Xygeni Agent</button>';
                    }});

                    function formSubmitHandler(e) {{
                        e.preventDefault();
                        
                        document.getElementById('rem-buttons').innerHTML = 
                            '<button id=\""rem-processing-button\"" type=\""submit\"" disabled class=\""xy-button\"">Processing...</button>';
                        
                        chrome.webview.postMessage(JSON.stringify({{
                            command: 'remediate',
                            issueId: '{Id}',
                            kind: '{Kind}',
                            file: '{File}'
                        }}));
                        
                        return false;
                    }}

                    document.getElementById('remediation-form').onsubmit = formSubmitHandler;
                </script>
            </div>";
        }
    }

}
