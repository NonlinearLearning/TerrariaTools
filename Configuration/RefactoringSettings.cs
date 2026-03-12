using System.Collections.Generic;

namespace TerrariaTools.Configuration
{
    public class RefactoringSettings
    {
        public string DefaultSolutionPath { get; set; } = string.Empty;
        public string MessageBufferFilePath { get; set; } = string.Empty;
        public int AnalysisParallelism { get; set; } = 8;
        public List<string> IgnoredFiles { get; set; } = new List<string>();
        public int Parallelism { get; set; } = 4;
        public bool EnableDryRun { get; set; } = false;
        public bool AggressiveMode { get; set; } = false;
        public bool UseHybridRewriteEngine { get; set; } = false;
        public string HybridNamePattern { get; set; } = ".*DummyPattern.*";
        public bool HybridDeleteMatchedMethods { get; set; } = true;
        public bool HybridClearBodyMatchedMethods { get; set; } = false;
        public List<HybridTerrariaConditionSetting> HybridTerrariaConditions { get; set; } = new List<HybridTerrariaConditionSetting>();
    }

    public class HybridTerrariaConditionSetting
    {
        public string SymbolName { get; set; } = string.Empty;
        public string Operator { get; set; } = "EqualsExpression";
        public string Value { get; set; } = string.Empty;
        public bool IsValueLiteral { get; set; } = true;
    }
}
