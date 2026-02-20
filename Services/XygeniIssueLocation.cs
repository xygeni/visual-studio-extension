namespace vs2026_plugin.Services
{
    public sealed class XygeniIssueLocation
    {
        public string OriginalPath { get; set; }
        public string DocumentPath { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public int BeginLine { get; set; }
        public int EndLine { get; set; }
        public int BeginColumn { get; set; }
        public int EndColumn { get; set; }
    }
}
