using System.Collections.Generic;

namespace TerrariaTools.Configuration
{
    public class RefactoringSettings
    {
        public string DefaultSolutionPath { get; set; } = string.Empty;
        public List<string> IgnoredFiles { get; set; } = new List<string>();
        public int Parallelism { get; set; } = 4;
        public bool EnableDryRun { get; set; } = false;
    }
}
