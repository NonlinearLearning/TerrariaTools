using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Diagnostics;

namespace TerrariaTools.ConsistentBehaviorGuarantee
{
    /// <summary>
    /// 逻辑单元等价性验证器，基于 Roslyn 语义分析确保逻辑一致。
    /// </summary>
    public class LogicParityVerifier
    {
        private readonly RewritingTraceContext _traceContext;

        public LogicParityVerifier(RewritingTraceContext traceContext)
        {
            _traceContext = traceContext;
        }

        /// <summary>
        /// 验证两个语法节点的逻辑等价性。
        /// </summary>
        /// <param name="originalNode">原始代码节点</param>
        /// <param name="rewrittenNode">重写后的代码节点</param>
        /// <returns>如果语义逻辑等价则返回 true</returns>
        public bool VerifyParity(SyntaxNode originalNode, SyntaxNode rewrittenNode)
        {
            // 规范化：移除所有琐碎内容（注释、空格等）并去除不必要的括号进行对比
            var normalizedOriginal = Unwrap(originalNode.NormalizeWhitespace("", ""));
            var normalizedRewritten = Unwrap(rewrittenNode.NormalizeWhitespace("", ""));

            bool isEquivalent = normalizedOriginal.IsEquivalentTo(normalizedRewritten, topLevel: false);
            
            if (!isEquivalent)
            {
                _traceContext.AddDiagnostic(new RewritingDiagnostic
                {
                    Reason = $"逻辑单元不等价: {originalNode.Kind()} -> {rewrittenNode.Kind()}. \n" +
                              $"原始摘要: {GetNodeSummary(normalizedOriginal)}\n" +
                              $"重写摘要: {GetNodeSummary(normalizedRewritten)}",
                    Severity = "Warning"
                });
            }
            
            return isEquivalent;
        }

        private SyntaxNode Unwrap(SyntaxNode node)
        {
            if (node is ParenthesizedExpressionSyntax parenthesized)
            {
                return Unwrap(parenthesized.Expression);
            }
            
            // 递归处理子节点中的括号
            var parenNodes = node.DescendantNodes().OfType<ParenthesizedExpressionSyntax>().ToList();
            if (parenNodes.Any())
            {
                return node.ReplaceNodes(parenNodes, (oldNode, newNode) => 
                {
                    return Unwrap(oldNode.Expression);
                });
            }
            return node;
        }

        private string GetNodeSummary(SyntaxNode node)
        {
            string text = node.ToFullString();
            if (text.Length > 100)
            {
                return text.Substring(0, 97) + "...";
            }
            return text;
        }
    }
}
