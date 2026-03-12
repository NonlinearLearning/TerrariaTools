using System;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// Code Dependency Analyzer definitions.
    /// </summary>
    public static class CodeDependencyAnalyzer
    {
        /// <summary>
        /// Analysis Mode.
        /// </summary>
        public enum AnalysisMode
        {
            /// <summary>Standard mode: Static analysis only.</summary>
            Standard,
            /// <summary>Aggressive mode: Only reachable code from entry points.</summary>
            Aggressive,
            /// <summary>Entry point only: Only identify entry points.</summary>
            EntryOnly
        }
    }
}
