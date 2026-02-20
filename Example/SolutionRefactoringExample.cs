using System;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions;

namespace Example
{
    /// <summary>
    /// 演示如何使用 ClassRefactorer 和 MethodRefactorer 对整个解决方案进行大规模重构。
    /// </summary>
    public class SolutionRefactoringExample
    {
        public async Task RunAsync(string solutionPath)
        {
            // 2. 创建加载器
            var loader = new TerrariaTools.Load();

            if (string.IsNullOrEmpty(solutionPath))
            {
                Console.WriteLine("错误: 未提供有效的解决方案路径。");
                return;
            }

            Console.WriteLine($"开始解决方案重构流 (目标: {solutionPath})...");

            try 
            {
                // 3. 执行类级别重构
                // 功能：自动删除解决方案中所有未被引用的类
                Console.WriteLine("[步骤 1] 正在执行类重构 (移除未引用的类)...");
                await ClassRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader);

                // 4. 执行方法级别重构
                // 功能：
                // a) 移除未被引用的方法
                // b) 将仅在类内部引用的公共方法自动私有化 (Privatization)
                Console.WriteLine("[步骤 2] 正在执行方法重构 (移除死代码 & 私有化)...");
                await MethodRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader);

                // 5. 执行基于名称的方法清理 (示例)
                Console.WriteLine("[步骤 3] 正在执行基于名称的方法清理 (移除包含 'Debug' 的方法)...");
                await NameBasedMethodRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader, "Debug");

                // 6. 执行特定条件重构 (示例：移除 netMode 检查)
                Console.WriteLine("[步骤 4] 正在执行条件重构 (移除 netMode == 1)...");
                await ConditionRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader);

                Console.WriteLine("重构任务全部完成。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重构过程中发生错误: {ex.Message}");
            }
        }
    }
}
