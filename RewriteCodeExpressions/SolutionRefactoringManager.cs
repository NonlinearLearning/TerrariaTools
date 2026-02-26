using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TerrariaTools.Services;
using TerrariaTools.Configuration;
using TerrariaTools.RewriteCodeExpressions;

namespace TerrariaTools.RewriteCodeExpressions
{
    public class SolutionRefactoringManager
    {
        private readonly IWorkspaceLoader _loader;
        private readonly RefactoringSettings _settings;

        public SolutionRefactoringManager(IWorkspaceLoader loader, IOptions<RefactoringSettings> settings)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<SolutionRefactoringResult> ExecuteFullRefactoringAsync(string solutionPath, IProgress<string>? externalProgress = null)
        {
            var result = new SolutionRefactoringResult();

            if (string.IsNullOrEmpty(solutionPath))
            {
                result.Success = false;
                result.Error = "Solution path cannot be empty.";
                return result;
            }

            // 组合进度报告器：既更新结果日志，又通过外部回调（如果存在）实时输出
            var progress = new Progress<string>(message =>
            {
                result.AddLog(message);
                externalProgress?.Report(message);
            });

            try
            {
                result.AddLog($"Starting solution refactoring for: {solutionPath}");

                // 1. Class Refactoring
                result.AddLog("Executing Class Refactoring (Removing unreferenced classes)...");
                var classStats = await ClassRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, _loader, _settings, progress);
                result.TotalDeletedClasses = classStats.TotalDeletedClasses;
                result.AddLog($"Class Refactoring completed. Deleted {classStats.TotalDeletedClasses} classes.");

                // 2. Method Refactoring
                result.AddLog("Executing Method Refactoring (Removing dead code & Privatization)...");
                // Note: Other refactorers haven't been updated to accept settings yet, so we don't pass them.
                var methodStats = await MethodRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, _loader, progress);
                result.TotalDeletedMethods = methodStats.TotalDeletedMethods;
                result.TotalPrivatizedMethods = methodStats.TotalPrivatizedMethods;
                result.TotalBodyClearedMethods = methodStats.TotalBodyClearedMethods;
                result.AddLog($"Method Refactoring completed. Deleted: {methodStats.TotalDeletedMethods}, Privatized: {methodStats.TotalPrivatizedMethods}, Cleared: {methodStats.TotalBodyClearedMethods}.");

                // 3. Name-based Cleanup
                result.AddLog("Executing Name-based Method Cleanup (Removing 'Debug' methods)...");
                var nameBasedStats = await NameBasedMethodRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, _loader, "Debug", progress);
                result.TotalDeletedMethods += nameBasedStats.TotalDeletedMethods;
                result.TotalBodyClearedMethods += nameBasedStats.TotalBodyClearedMethods;
                result.AddLog($"Name-based Cleanup completed. Additional Deleted: {nameBasedStats.TotalDeletedMethods}, Cleared: {nameBasedStats.TotalBodyClearedMethods}.");

                // 4. Condition Refactoring
                result.AddLog("Executing Condition Refactoring (Removing netMode == 1)...");
                var conditionStats = await ConditionRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, _loader, progress);
                result.TotalRefactoredFiles = conditionStats.TotalChangedFiles;
                result.AddLog($"Condition Refactoring completed. Modified {conditionStats.TotalChangedFiles} files.");

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.AddLog($"Error: {ex.Message}");
            }

            return result;
        }
    }
}
