using System;
using System.Linq;
using System.Threading.Tasks;
using TerrariaTools.ConsistentBehaviorGuarantee;
using TerrariaTools.Diagnostics;
using TerrariaTools.Services;

namespace Example
{
    /// <summary>
    /// 演示如何使用 DifferentialTester 执行差分测试，以确保重构后的逻辑与原逻辑行为一致。
    /// </summary>
    public class DifferentialTestingWorkflow : ITool
    {
        public string Name => "差分测试工作流";
        public string Description => "验证重构前后代码行为一致性。";

        public Task RunAsync(string? path = null)
        {
            Run();
            return Task.CompletedTask;
        }

        public void Run()
        {
            // 1. 初始化追踪上下文
            var traceContext = new RewritingTraceContext();
            var tester = new DifferentialTester(traceContext);

            // 模拟一些测试输入数据
            var testInputs = new[] { 10, 20, -5, 0, 100 };

            Console.WriteLine("=== 开始差分测试验证 ===");

            foreach (var input in testInputs)
            {
                // 2. 执行旧逻辑 (基准)
                var legacyResult = LegacyCalculator.Calculate(input);

                // 3. 执行新逻辑 (重构后)
                var refactoredResult = NewCalculator.Calculate(input);

                // 4. 自动比对并记录
                // 如果结果不一致，tester 会记录详细的 input/legacy/refactored 状态
                bool isConsistent = tester.Compare(legacyResult, refactoredResult, $"CalcTest_Input_{input}");

                if (isConsistent)
                {
                    Console.WriteLine($"[通过] 输入 {input}: 行为一致。");
                }
                else
                {
                    Console.WriteLine($"[失败] 输入 {input}: 检测到行为差异！详细诊断已记录。");
                }
            }

            // 5. 查看诊断报告 (在实际 UI 或日志中)
            if (traceContext.GetDiagnostics().Any(d => d.Severity == "Error"))
            {
                Console.WriteLine("\n测试发现异常，请检查追踪记录。");
            }
        }

        // 模拟逻辑类
        static class LegacyCalculator
        {
            public static int Calculate(int x) => x > 0 ? x * 2 : 0;
        }

        static class NewCalculator
        {
            // 假设这是一个重构后的版本，逻辑稍有不同（例如边界值处理错误）
            public static int Calculate(int x) => x >= 0 ? x * 2 : 0; 
        }
    }
}
