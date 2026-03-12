using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.Services;

namespace Example
{
    /// <summary>
    /// 演示如何结合语义分析执行跨项目的方法重构流。
    /// 场景：找到一个方法的所有引用，并将其替换为默认值或直接移除。
    /// </summary>
    public class SemanticRefactoringWorkflow : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public SemanticRefactoringWorkflow(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "语义重构工作流";
        public string Description => "基于语义分析的跨项目重构";

        public async Task RunAsync(string? solutionPath = null)
        {
            if (string.IsNullOrEmpty(solutionPath))
            {
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

            var solution = await _loader.LoadSolutionAsync(solutionPath);
            if (solution == null) return;

            await ExecuteAsync(solution);
        }

        public async Task ExecuteAsync(Solution solution)
        {
            // 1. 定位目标方法符号 (例如定位所有标记为 [Obsolete] 的方法)
            var project = solution.Projects.First();
            var compilation = await project.GetCompilationAsync();

            // 假设我们要寻找并移除所有对 'OldService.Cleanup' 的调用
            var targetType = compilation?.GetTypeByMetadataName("MyProject.OldService");
            var targetMethod = targetType?.GetMembers("Cleanup").FirstOrDefault() as IMethodSymbol;

            if (targetMethod == null) return;

            Console.WriteLine($"[分析] 发现目标方法: {targetMethod.ToDisplayString()}");

            // 2. 遍历解决方案中的所有文档
            foreach (var doc in solution.Projects.SelectMany(p => p.Documents))
            {
                var model = await doc.GetSemanticModelAsync();
                var root = await doc.GetSyntaxRootAsync();

                if (model == null || root == null) continue;

                // 3. 定义重构策略
                // 仅当方法调用指向我们的目标符号时，才标记为移除
                Func<SyntaxNode, bool> shouldRemove = node =>
                {
                    if (node is InvocationExpressionSyntax invocation)
                    {
                        var symbol = model.GetSymbolInfo(invocation).Symbol;
                        return SymbolEqualityComparer.Default.Equals(symbol, targetMethod);
                    }
                    return false;
                };

                // 4. 执行重构
                // 引擎会自动处理：
                // - 如果调用在 if (Cleanup()) 中，替换为默认布尔值。
                // - 如果调用是独立语句 Cleanup();，则直接删除。
                var newRoot = ExpressionProcessor.RemoveParts(root, shouldRemove, model);

                if (newRoot != root)
                {
                    Console.WriteLine($"[重写] 已优化文档: {doc.Name}");
                    // 实际应用中，这里会调用 solution.WithDocumentSyntaxRoot(...)
                }
            }
        }
    }
}
