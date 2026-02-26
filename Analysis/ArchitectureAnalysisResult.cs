using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTools.Analysis
{
    public class ArchitectureAnalysisResult
    {
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
        public List<List<string>> StrongConnectedComponents { get; set; } = new();
        public List<string> TopologicalSort { get; set; } = new();
        public bool HasCycles => StrongConnectedComponents.Any(s => s.Count > 1);
        public string Error { get; set; } = string.Empty;
    }
}
