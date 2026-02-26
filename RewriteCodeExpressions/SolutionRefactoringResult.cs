using System;
using System.Collections.Generic;

namespace TerrariaTools.RewriteCodeExpressions
{
    public class SolutionRefactoringResult
    {
        public bool Success { get; set; } = true;
        public string Error { get; set; } = string.Empty;
        public int TotalDeletedClasses { get; set; }
        public int TotalDeletedMethods { get; set; }
        public int TotalPrivatizedMethods { get; set; }
        public int TotalBodyClearedMethods { get; set; }
        public int TotalRefactoredFiles { get; set; }
        public List<string> DetailedLogs { get; set; } = new();

        public void AddLog(string message)
        {
            DetailedLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
