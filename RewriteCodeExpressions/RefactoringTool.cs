using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TerrariaTools.Configuration;
using TerrariaTools.Services;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 解决方案重构工具，提供全解决方案级别的代码清理和重构功能。
    /// </summary>
    public class RefactoringTool : ITool
    {
        /// <summary>
        /// 解决方案重构管理器
        /// </summary>
        private readonly SolutionRefactoringManager _manager;

        /// <summary>
        /// 重构设置
        /// </summary>
        private readonly RefactoringSettings _settings;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="manager">解决方案重构管理器实例</param>
        /// <param name="settings">重构设置选项</param>
        public RefactoringTool(SolutionRefactoringManager manager, IOptions<RefactoringSettings> settings)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// 工具名称
        /// </summary>
        public string Name => "解决方案重构工具";

        /// <summary>
        /// 工具描述
        /// </summary>
        public string Description => "执行全解决方案级别的重构（类清理、方法清理、条件重写等）。";

        /// <summary>
        /// 异步运行重构工具
        /// </summary>
        /// <param name="targetPath">可选的目标路径（默认为空，从配置或输入获取）</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task RunAsync(string? targetPath = null)
        {
            // 1. 确定目标路径
            string solutionPath = targetPath;

            if (string.IsNullOrEmpty(solutionPath))
            {
                // 尝试从配置获取
                solutionPath = _settings.DefaultSolutionPath;

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

            Console.WriteLine($"开始解决方案重构任务 (目标: {solutionPath})...");

            try
            {
                // 2. 执行重构
                // 使用 Progress<T> 将内部日志输出到控制台
                var progress = new Progress<string>(msg => Console.WriteLine($"[Refactoring] {msg}"));

                var result = await _manager.ExecuteFullRefactoringAsync(solutionPath, progress);

                // 3. 输出结果
                if (result.Success)
                {
                    Console.WriteLine("\n==========================================");
                    Console.WriteLine("重构任务全部完成！");
                    Console.WriteLine($"总计删除类: {result.TotalDeletedClasses}");
                    Console.WriteLine($"总计删除方法: {result.TotalDeletedMethods}");
                    Console.WriteLine($"总计私有化方法: {result.TotalPrivatizedMethods}");
                    Console.WriteLine($"总计清空方法体: {result.TotalBodyClearedMethods}");
                    Console.WriteLine($"总计修改文件 (条件重写): {result.TotalRefactoredFiles}");
                    if (result.HybridPlanItemCount > 0 || result.HybridExecutedRuleCount > 0 || result.HybridReplacedNodeCount > 0 || result.HybridDeletedNodeCount > 0)
                    {
                        Console.WriteLine($"Hybrid 计划命中: {result.HybridPlanItemCount}");
                        Console.WriteLine($"Hybrid 执行规则: {result.HybridExecutedRuleCount}");
                        Console.WriteLine($"Hybrid 替换节点: {result.HybridReplacedNodeCount}");
                        Console.WriteLine($"Hybrid 删除节点: {result.HybridDeletedNodeCount}");
                    }
                    Console.WriteLine("==========================================\n");
                }
                else
                {
                    Console.WriteLine($"\n[错误] 重构过程中发生错误: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[异常] 未捕获的异常: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
