using System;
using System.Linq;
using Markdig;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Microsoft.VisualStudio.Shell;
using System.IO;

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
        bool HasLocation { get; }

        int GetSeverityLevel();
        string GetSubtitleLineHtml();
        string GetIssueDetailsHtml();
        string GetCodeSnippetHtml();
        string GetCodeSnippetHtmlTab();
        string GetExplanationShort();
        string GetExplanationHtml();
        string GetRemediationTab();
        string GetRemediationTabContent();
        string GetCodeFlowTab();
        string GetTags();
        string Field(string name, string value);

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

        // A finding anchored to a line of a source file; service/module-scoped API flaws are not.
        public bool HasLocation => !string.IsNullOrEmpty(File) && BeginLine > 0;

        public const string RemediableAuto = "AUTO";
        public const string RemediableManual = "MANUAL";

        // static cache icons as data URIs for WebView2
        public static string BranchIcon;
        public static string CommitIcon;
        public static string UserIcon;
        private static bool _iconsLoaded = false;

        private static string ToDataUri(string filePath)
        {
            if (!System.IO.File.Exists(filePath)) return "";
            byte[] bytes = System.IO.File.ReadAllBytes(filePath);
            string base64 = Convert.ToBase64String(bytes);
            return $"data:image/png;base64,{base64}";
        }

        public static void LoadIcons()
        {
            if (_iconsLoaded) return;
            string baseDir = Path.GetDirectoryName(typeof(AbstractXygeniIssue).Assembly.Location);
            BranchIcon = ToDataUri(Path.Combine(baseDir, "media", "icons", "branch.png"));
            CommitIcon = ToDataUri(Path.Combine(baseDir, "media", "icons", "commit.png"));
            UserIcon = ToDataUri(Path.Combine(baseDir, "media", "icons", "user.png"));
            _iconsLoaded = true;
        }

        private static readonly Dictionary<string, string> texts = new Dictionary<string, string> {
                {"manual_fix", "Manual Fix"},
                {"potential_reachable", "Potential Reachable"},
                {"in-app-code", "In-App Code"},
                {"generic", "Generic"}
            }; 

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

        public virtual string GetCodeFlowTab() => "";

        public virtual string GetSubtitleLineHtml()
        {
            string subtitle = WebUtility.HtmlEncode(CategoryName);
            string encodedType = WebUtility.HtmlEncode(Type);
            if (!string.IsNullOrEmpty(Url))
            {
                subtitle += $" &nbsp;&nbsp; <a href=\"{WebUtility.HtmlEncode(Url)}\" target=\"_blank\">{encodedType}</a>";
            }
            else
            {
                subtitle += $" {encodedType}";
            }
            return subtitle;
        }

        public virtual string GetCodeSnippetHtml()
        {
            if (string.IsNullOrEmpty(Code) || string.IsNullOrEmpty(File)) return "";

            var codeLines = Code.Split('\n');
            string codeSnippet = "";
            for (int i = 0; i < codeLines.Length; i++)
            {
                int lineNumber = BeginLine + i;
                string escapedLine = WebUtility.HtmlEncode(codeLines[i]);
                codeSnippet += $@"
                <tr>
                  <td class=""line-number"">{lineNumber}</td>
                  <td class=""code-line"">{escapedLine}</td>
                </tr>";
            }

            return $@"
              <div id=""tab-content-2"">
                <p class=""file"">{WebUtility.HtmlEncode(File)}</p>
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
            // DisableHtml: the explanation is scanner text that may quote code or file paths.
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
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
            string issueIdJs = ToJsLiteral(Id);
            string kindJs = ToJsLiteral(Kind);
            string fileJs = ToJsLiteral(File);
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
                            issueId: {issueIdJs},
                            kind: {kindJs},
                            file: {fileJs}
                        }}));
                        
                        return false;
                    }}

                    document.getElementById('remediation-form').onsubmit = formSubmitHandler;
                </script>
            </div>";
        }

        public virtual string GetTags() {
            if (Tags is null || Tags.Count == 0) return "";
            return "<tr><th>Tags</th>" +
                   "<td><div class='xy-container-chip'>" + string.Join(" ", Tags.Where(tag => !string.IsNullOrEmpty(tag)).Select(tag => $"<div class='xy-blue-chip'>{WebUtility.HtmlEncode(TagNames(tag))}</div>")) + "</div></td></tr>";
        }

        // Quoted, escaped JS string literal; HTML-escaped too, as it lands inside a <script> block.
        protected static string ToJsLiteral(string value)
        {
            return JsonConvert.ToString(value ?? string.Empty, '\'', StringEscapeHandling.EscapeHtml);
        }

        // Both arguments are rendered as text: scanner strings (titles, paths, endpoints) are never markup.
        // A detail row for scanner text written in markdown (remediation steps); DisableHtml keeps raw markup as text.
        public virtual string FieldMarkdown(string name, string value) {
            if (string.IsNullOrEmpty(value)) return "";
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
            return $"<tr><th>{WebUtility.HtmlEncode(name)}</th><td>{Markdown.ToHtml(value, pipeline)}</td></tr>";
        }

        public virtual string Field(string name, string value) {
            return string.IsNullOrEmpty(value) ? "" : $"<tr><th>{WebUtility.HtmlEncode(name)}</th><td>{WebUtility.HtmlEncode(value)}</td></tr>";
        }

        public virtual string Where(string branch, string commit, string user) {
            LoadIcons();
            string where = "";
            if(!string.IsNullOrEmpty(branch)) where += $"<img alt='Branch' src='{AbstractXygeniIssue.BranchIcon}' width='16' height='16' style='vertical-align:middle;margin-right:4px'></img> {WebUtility.HtmlEncode(branch)}";
            if(!string.IsNullOrEmpty(commit)) where += $"<img alt='Commit' src='{AbstractXygeniIssue.CommitIcon}' width='16' height='16' style='vertical-align:middle;margin-right:4px'></img> {WebUtility.HtmlEncode(commit)}";
            if(!string.IsNullOrEmpty(user)) where += $"<img alt='User' src='{AbstractXygeniIssue.UserIcon}' width='16' height='16' style='vertical-align:middle;margin-right:4px'></img> {WebUtility.HtmlEncode(user)}";
            if(!string.IsNullOrEmpty(where)) where =  $"<tr><th>Where</th><td>{where}</td></tr>";
            return where;
        }

        private string TagNames(string tag) {
            string key = tag?.ToLower() ?? "";
            return texts.ContainsKey(key) ? texts[key] : tag ?? "";
        }
    }

    

}
