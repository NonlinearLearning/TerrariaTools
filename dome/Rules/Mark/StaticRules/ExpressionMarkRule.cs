using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Rules.Dome;
using TerrariaTools.Rules.Dome.Mark;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules
{
    /// <summary>
    /// 语句标记规则（复杂表达式标记部分）。
    /// 负责对语句中的表达式进行深度标记（如 k && l || m）。
    /// 遵循“最小生存原则”和传播权限。
    /// </summary>
    public class ExpressionMarkRule
    {
        public string Name => "语句复杂表达式标记规则";

        public StatementSyntax MarkStatement(StatementSyntax statement)
        {
            if (statement == null) return null;

            // 1. 对语句内的表达式进行递归标记处理
            return MarkExpressionsInStatement(statement);
        }

        /// <summary>
        /// 遍历语句中的所有表达式，应用“向上感染”逻辑进行标记。
        /// </summary>
        private StatementSyntax MarkExpressionsInStatement(StatementSyntax statement)
        {
            // 使用自定义的 Rewriter 自底向上地处理所有表达式节点
            return (StatementSyntax)new ExpressionMarkRewriter().Visit(statement);
        }

        /// <summary>
        /// 表达式标记重写器：负责自底向上地标记表达式树。
        /// 遵循用户定义的传播权限：
        /// 1. 标识符 (Identifier) 是初始感染源 (如果带有 SourceAnnotationKind)。
        /// 2. 逻辑与 (&&) 是强感染源，允许向上传播标记。
        /// 3. 逻辑或 (||) 是隔离区，阻断标记向上传播（除非两边都坏了）。
        /// </summary>
        private class ExpressionMarkRewriter : CSharpSyntaxRewriter
        {
            /// <summary>
            /// 判断一个节点是否具有“感染性”（即是否允许将其标记传播给父节点）。
            /// </summary>
            private bool IsInfectious(ExpressionSyntax node)
            {
                var core = Unwrap(node);
                // 1. 标识符始终具有初始感染力（它是标记的源头）
                if (core is IdentifierNameSyntax) return true;

                // 2. 逻辑与 (&&) 表达式具有向上传播能力
                if (core.IsKind(SyntaxKind.LogicalAndExpression)) return true;

                // 3. 括号表达式的感染性取决于其内部内容 (Unwrap 已处理)

                // 4. 其他类型（如 LogicalOrExpression）默认不具传播性 (Isolated)
                return false;
            }

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                // 1. 先递归处理子节点（自底向上）
                var left = (ExpressionSyntax)Visit(node.Left);
                var right = (ExpressionSyntax)Visit(node.Right);

                var newNode = node.Update(left, node.OperatorToken, right);

                bool isLeftMarked = IsMarked(left);
                bool isRightMarked = IsMarked(right);

                bool shouldMark = false;

                // 2. 应用传播规则：
                // 如果任一子节点被标记，且该子节点具有“感染性”，则本节点被标记。
                if (isLeftMarked && IsInfectious(left)) shouldMark = true;
                if (isRightMarked && IsInfectious(right)) shouldMark = true;

                // 3. 兜底逻辑：如果左右两边都坏了，不管类型如何，本节点必然标记为坏死
                if (isLeftMarked && isRightMarked) shouldMark = true;

                if (shouldMark)
                {
                    return MarkNode(newNode, "Propagated");
                }

                return newNode;
            }

            private ExpressionSyntax Unwrap(ExpressionSyntax node)
            {
                while (node is ParenthesizedExpressionSyntax p) node = p.Expression;
                return node;
            }

            public override SyntaxNode VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
            {
                var expression = (ExpressionSyntax)Visit(node.Expression);
                var newNode = node.Update(node.OpenParenToken, expression, node.CloseParenToken);

                // 括号节点透明传播内部标记
                if (IsMarked(expression))
                {
                    return MarkNode(newNode, "PropagatedFromInner");
                }
                return newNode;
            }

            public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            {
                var operand = (ExpressionSyntax)Visit(node.Operand);
                var newNode = node.Update(node.OperatorToken, operand);
                if (IsMarked(operand)) return MarkNode(newNode, "PropagatedFromOperand");
                return newNode;
            }

            public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
            {
                var operand = (ExpressionSyntax)Visit(node.Operand);
                var newNode = node.Update(operand, node.OperatorToken);
                if (IsMarked(operand)) return MarkNode(newNode, "PropagatedFromOperand");
                return newNode;
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                // 移除硬编码，检查是否已有标记
                if (IsMarked(node))
                {
                    return node;
                }
                return base.VisitIdentifierName(node);
            }

            private bool IsMarked(SyntaxNode node)
            {
                return node.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any() ||
                       node.GetAnnotations(RuleConstants.SourceAnnotationKind).Any();
            }

            private T MarkNode<T>(T node, string reason) where T : SyntaxNode
            {
                return node.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, reason));
            }
        }
    }
}
