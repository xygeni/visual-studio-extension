namespace vs2026_plugin.Models
{
    public class CodeFlowFrame
    {
        public string Kind { get; set; }
        public string FilePath { get; set; }
        public int BeginLine { get; set; }
        public int EndLine { get; set; }
        public int BeginColumn { get; set; }
        public int EndColumn { get; set; }
        public string Code { get; set; }
        public string Container { get; set; }
        public string InjectPoint { get; set; }
        public string Category { get; set; }
    }
}
