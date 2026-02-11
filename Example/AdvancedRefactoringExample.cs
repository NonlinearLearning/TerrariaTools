using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;

namespace Example
{
    /// <summary>
    /// FAQ 示例：演示如何通过继承 ExpressionSimplifier 自定义占位符生成逻辑。
    /// </summary>
    public class CustomPlaceholderSimplifier : ExpressionSimplifier
    {
        public CustomPlaceholderSimplifier(
            Func<SyntaxNode, bool> shouldRemove, 
            SemanticModel? model = null) 
            : base(shouldRemove, model)
        {
        }

        /// <summary>
        /// 重写此方法以针对特定类型或上下文生成自定义的占位符。
        /// </summary>
        protected override ExpressionSyntax? TryCreatePlaceholder(SyntaxNode node)
        {
            // 示例：获取当前节点在语义模型中的预期类型
            var type = GetExpectedType(node);

            // 如果预期类型是特定的自定义类型，返回自定义的默认值表达式
            if (type != null && type.Name == "MyCustomType")
            {
                return SyntaxFactory.ParseExpression("MyCustomType.DefaultValue");
            }

            // 对于其他情况，调用基类逻辑（生成 0, false, null 等）
            return base.TryCreatePlaceholder(node);
        }
    }

    /// <summary>
    /// FAQ 示例：演示如何批量处理多个文件并保持语义一致性。
    /// </summary>
    public class BatchRefactoringExample
    {
        public void Execute(SyntaxNode root, SemanticModel model)
        {
            // 1. 定义移除谓词（例如：移除所有名为 "InternalDebug" 的方法调用）
            Func<SyntaxNode, bool> predicate = node => 
                node is InvocationExpressionSyntax invocation && 
                invocation.Expression.ToString().EndsWith("InternalDebug");

            // 2. 使用 ExpressionProcessor.RemoveParts 一次性处理。
            // 该方法内部会先收集所有需要标记的节点，然后再进行重写，
            // 从而避免在重写过程中因语法树变化导致语义模型失效的问题。
            var newRoot = ExpressionProcessor.RemoveParts(root, predicate, model);

            Console.WriteLine("批量重构完成。");
        }
    }
}
