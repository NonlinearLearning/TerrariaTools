using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules
{
    public class StatementMarkRule
    {
        // Removed local constant to use RuleConstants.RewriteAnnotationKind
        
        public SyntaxNode Apply(SyntaxNode root)
        {
            var rewriter = new StatementMarkRewriter();
            return rewriter.Visit(root);
        }

        public StatementSyntax MarkStatement(StatementSyntax statement)
        {
            return (StatementSyntax)Apply(statement);
        }

        private class StatementMarkRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
            {
                if (HasMarkedAnnotation(node.Expression))
                {
                    return node.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Marked"));
                }

                // 另外检查表达式本身是否包含已标记节点 (如果需要，使用 ExpressionMarkRule 逻辑)
                // 目前，假设上一步 (手动标记) 已向表达式添加了注释
                // 或者我们需要向下遍历。

                // 如果输入的根节点已经在标识符上有了注释 (如 RuleEngine 中所示)，
                // 我们需要检查这些注释是否存在于后代节点中。
                if (node.DescendantNodes().Any(n => n.HasAnnotations(RuleConstants.RewriteAnnotationKind)))
                {
                    return node.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Marked"));
                }

                return base.VisitExpressionStatement(node);
            }

            public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
            {
                 if (node.DescendantNodes().Any(n => n.HasAnnotations(RuleConstants.RewriteAnnotationKind)))
                {
                    return node.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Marked"));
                }
                return base.VisitLocalDeclarationStatement(node);
            }

            // 根据需要添加其他语句类型
             public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
            {
                 if (node.Condition.DescendantNodesAndSelf().Any(n => n.HasAnnotations(RuleConstants.RewriteAnnotationKind)))
                {
                     return node.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Marked"));
                }
                return base.VisitIfStatement(node);
            }

             private bool HasMarkedAnnotation(SyntaxNode node)
             {
                 return node.HasAnnotations(RuleConstants.RewriteAnnotationKind);
             }
        }
    }
}
