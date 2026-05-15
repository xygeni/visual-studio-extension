using System.Collections.Generic;

namespace vs2026_plugin.Models
{
    public class CodeFlow
    {
        public List<string> Tags { get; set; }
        public List<CodeFlowFrame> Frames { get; set; }

        public CodeFlow()
        {
            Tags = new List<string>();
            Frames = new List<CodeFlowFrame>();
        }
    }
}
