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
        public async Task RunAsync()
        {
            // 1. 指定解决方案路径
            string solutionPath = @"C:\Path\To\Your\Solution.sln";
            
            // 2. 创建加载器
            var loader = new TerrariaTools.Load();

            Console.WriteLine("开始解决方案重构流...");

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

                Console.WriteLine("重构任务全部完成。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重构过程中发生错误: {ex.Message}");
            }
        }
    }
}
