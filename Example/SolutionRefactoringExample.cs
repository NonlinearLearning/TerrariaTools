using System;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.Services;

namespace Example
{
    /// <summary>
    /// 演示如何使用 ClassRefactorer 和 MethodRefactorer 对整个解决方案进行大规模重构。
    /// </summary>
    public class SolutionRefactoringExample : ITool
    {
        private readonly SolutionRefactoringManager _manager;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public SolutionRefactoringExample(SolutionRefactoringManager manager, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _manager = manager;
            _settings = settings;
        }

        public string Name => "解决方案重构";
        public string Description => "执行全解决方案级别的重构（类/方法移除、私有化等）。";

        public async Task RunAsync(string? solutionPath = null)
        {
            if (string.IsNullOrEmpty(solutionPath))
            {
                // Try to get from settings first
                solutionPath = _settings.Value.DefaultSolutionPath;

                if (string.IsNullOrEmpty(solutionPath))
                {
                    Console.WriteLine("请输入解决方案路径 (直接回车使用默认):");
                    solutionPath = Console.ReadLine();
                }
            }

            if (string.IsNullOrEmpty(solutionPath))
            {
                Console.WriteLine("错误: 未提供有效的解决方案路径。");
                return;
            }

            Console.WriteLine($"开始解决方案重构流 (目标: {solutionPath})...");

            try
            {
                var progress = new Progress<string>(msg => Console.WriteLine(msg));
                var result = await _manager.ExecuteFullRefactoringAsync(solutionPath, progress);

                if (result.Success)
                {
                    Console.WriteLine("\n重构任务全部完成。");
                }
                else
                {
                    Console.WriteLine($"\n重构过程中发生错误: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n未捕获的异常: {ex.Message}");
            }
        }
    }
}
