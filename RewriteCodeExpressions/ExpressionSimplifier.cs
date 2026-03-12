using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaTools.Diagnostics;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 表达式简化器（旧版）：通过直接遍历语法树并替换标记节点来实现代码简化。
    /// 该类已逐渐被 PipelineExpressionSimplifier 取代，但仍保留用于兼容性测试和简单场景。
    /// </summary>
    public class ExpressionSimplifier : CSharpSyntaxRewriter
    {
        protected readonly Func<SyntaxNode, bool> _predicate;
        protected readonly SemanticModel? _model;
        protected readonly HashSet<SyntaxNode> _nodesToMark;
        protected readonly RewritingTraceContext _traceContext;
        protected readonly SyntaxGenerator _generator;

        /// <summary>
        /// 初始化 ExpressionSimplifier 的新实例。
        /// </summary>
        /// <param name="predicate">判断节点是否应被简化的谓词。</param>
        /// <param name="model">可选的语义模型，用于生成更准确的占位符。</param>
        /// <param name="nodesToMark">可选的预标记节点集合。</param>
        /// <param name="traceContext">用于记录重写过程的追踪上下文。</param>
        public ExpressionSimplifier(
            Func<SyntaxNode, bool> predicate,
            SemanticModel? model = null,
            HashSet<SyntaxNode>? nodesToMark = null,
            RewritingTraceContext? traceContext = null)
        {
            _predicate = predicate ?? (n => false);
            _model = model;
            _nodesToMark = nodesToMark ?? new HashSet<SyntaxNode>();
            _traceContext = traceContext ?? new RewritingTraceContext();

            // 使用 AdhocWorkspace 获取 Generator，或者如果 model 为空，提供一个默认的
            var workspace = new AdhocWorkspace();
            _generator = SyntaxGenerator.GetGenerator(workspace, LanguageNames.CSharp);
        }

        /// <summary>
        /// 访问并处理语法节点。
        /// </summary>
        /// <param name="node">要访问的语法节点。</param>
        /// <returns>处理后的语法节点。</returns>
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null) return null;

            // 1. 检查节点是否被标记为需要简化
            if (_predicate(node) || _nodesToMark.Contains(node))
            {
                var placeholder = TryCreatePlaceholder(node);
                if (placeholder != null)
                {
                    _traceContext.AddDiagnostic(node, "Original ExpressionSimplifier: Replaced with placeholder", placeholder);
                    return placeholder;
                }

                _traceContext.AddDiagnostic(node, "Original ExpressionSimplifier: Removed (no placeholder created)");
                return null;
            }

            return base.Visit(node);
        }

        /// <summary>
        /// 尝试为指定节点创建占位符。可以被子类重写以提供自定义逻辑。
        /// </summary>
        /// <param name="node">要简化的节点。</param>
        /// <returns>生成的占位符节点，如果无法生成则返回 null。</returns>
        protected virtual SyntaxNode? TryCreatePlaceholder(SyntaxNode node)
        {
            return PlaceholderFactory.CreatePlaceholder(node, _model, _generator);
        }

        /// <summary>
        /// 获取语法节点对应的语义类型。
        /// </summary>
        /// <param name="node">语法节点。</param>
        /// <returns>类型符号，如果无法获取则返回 null。</returns>
        protected virtual ITypeSymbol? GetNodeType(SyntaxNode node)
        {
            if (_model == null) return null;
            if (node is ExpressionSyntax expr)
            {
                var typeInfo = _model.GetTypeInfo(expr);
                return typeInfo.ConvertedType ?? typeInfo.Type;
            }
            return null;
        }
    }
}
