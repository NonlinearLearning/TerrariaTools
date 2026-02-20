using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.ConsistentBehaviorGuarantee;
using TerrariaTools.Diagnostics;

namespace Example
{
    /// <summary>
    /// FAQ 示例集合：包含了 FAQ.md 中提到的所有进阶用法和常见场景。
    /// </summary>
    public class FAQ
    {
        /// <summary>
        /// Q: 这种自动重构会破坏代码的行为吗？
        /// A: 使用 DifferentialTester 验证逻辑一致性。
        /// </summary>
        public void VerifyBehaviorParity(RewritingTraceContext traceContext, object data)
        {
            var tester = new DifferentialTester(traceContext);

            // 模拟旧逻辑和新逻辑的执行
            // var originalResult = OldLogic.Calculate(data);
            // var newResult = NewLogic.Calculate(data);
            object originalResult = "Old";
            object newResult = "New";

            // 如果结果不一致，将自动记录详细的诊断信息并返回 false
            if (!tester.Compare(originalResult, newResult, "CalculationParity"))
            {
                Console.WriteLine("[警告] 检测到行为不一致！详细信息已记录到跟踪上下文。");
            }
        }

        /// <summary>
        /// Q: 如何自定义占位符生成逻辑？
        /// A: 继承 ExpressionSimplifier 并重写 TryCreatePlaceholder。
        /// </summary>
        public class MyCustomSimplifier : ExpressionSimplifier
        {
            public MyCustomSimplifier(Func<SyntaxNode, bool> shouldRemove, SemanticModel? model = null)
                : base(shouldRemove, model) { }

            protected override SyntaxNode? TryCreatePlaceholder(SyntaxNode node)
            {
                // 1. 获取该节点在当前语义模型中的预期类型
                var type = GetNodeType(node);

                // 2. 针对特定类型生成自定义占位符（例如自定义的默认对象）
                if (type?.Name == "MyCustomType")
                {
                    return SyntaxFactory.ParseExpression("MyCustomType.Default");
                }

                // 3. 其他类型回退到基类默认逻辑（0, false, null 等）
                return base.TryCreatePlaceholder(node);
            }
        }

        /// <summary>
        /// Q: 如何批量处理多个文件并保持语义模型有效？
        /// A: 使用 ExpressionProcessor.RemoveParts 两阶段模式。
        /// </summary>
        public void BatchRefactoringExample(SyntaxNode root, SemanticModel semanticModel)
        {
            // 1. 定义移除谓词：例如移除所有私有字段的直接访问
            Func<SyntaxNode, bool> predicate = node =>
                node is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(memberAccess).Symbol?.DeclaredAccessibility == Accessibility.Private;

            // 2. 一次性处理：先在当前语义模型下标记，再安全重写
            var newRoot = ExpressionProcessor.RemoveParts(root, predicate, semanticModel);

            Console.WriteLine("批量重构完成，语义模型在整个标记阶段保持稳定。");
        }

        /// <summary>
        /// Q: 如何在重构过程中安全地移除异步调用 (await)？
        /// A: ExpressionSimplifier 会自动识别 await 上下文并处理 Task 返回值。
        /// </summary>
        public async Task AsyncRefactoringExample(Document document)
        {
            var model = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();

            // 示例：移除所有对外部 API 的异步调用
            Func<SyntaxNode, bool> predicate = node =>
                node is AwaitExpressionSyntax awaitExpr &&
                awaitExpr.Expression.ToString().Contains("ExternalApi");

            // RemoveParts 会自动处理：
            // 1. 如果 await 表达式被移除，且其结果未被使用，则直接删除语句。
            // 2. 如果结果被赋值给变量，则生成 Task.FromResult(default) 类似的占位符以维持语法正确。
            var newRoot = ExpressionProcessor.RemoveParts(root!, predicate, model);
        }

        /// <summary>
        /// Q: 在多步转换中，如何跟踪被移动或修改的节点？
        /// A: 使用 SyntaxAnnotation 为节点打上持久标记。
        /// </summary>
        public void AnnotationTrackingExample(SyntaxNode root)
        {
            var myAnnotation = new SyntaxAnnotation("MyRefactoring", "TargetNode");

            // 1. 给目标节点打上标记
            var annotatedRoot = root.ReplaceNode(
                root.DescendantNodes().First(n => n is MethodDeclarationSyntax),
                root.DescendantNodes().First(n => n is MethodDeclarationSyntax).WithAdditionalAnnotations(myAnnotation)
            );

            // 2. 经过一系列转换后，依然可以通过 Annotation 找回该节点，即使它的内容已经改变
            var targetNode = annotatedRoot.GetAnnotatedNodes(myAnnotation).FirstOrDefault();

            if (targetNode != null)
            {
                Console.WriteLine("成功通过 Annotation 追踪到目标节点。");
            }
        }

        /// <summary>
        /// Q: 如何利用“结构传播 (Structural Propagation)”自动清理父节点？
        /// A: 标记内部节点，引擎会自动通过 UpwardMarkCollector 向上冒泡。
        /// </summary>
        public void StructuralCleanupExample(SyntaxNode root, SemanticModel model)
        {
            // 场景：一个 try 块内只有一行代码，而这行代码被标记为移除
            // 引擎会自动识别到 try 块变为空，并根据策略决定是否移除整个 try-catch 结构。

            Func<SyntaxNode, bool> shouldRemove = node =>
                node is ExpressionStatementSyntax stmt && stmt.ToString().Contains("ObsoleteCall");

            // 引擎内部执行逻辑：
            // 1. 标记 ObsoleteCall。
            // 2. UpwardMarkCollector 发现其父节点 Block 变为空。
            // 3. 继续向上发现 TryStatement 失去意义。
            // 4. 最终生成的代码中，整个 TryStatement 会被移除。
            var cleanedRoot = ExpressionProcessor.RemoveParts(root, shouldRemove, model);
        }
    }
}
