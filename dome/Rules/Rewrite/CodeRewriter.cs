using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Rules.Dome.Mark.StaticRules;

namespace TerrariaTools.Rules.Dome.Rewrite
{
    /// <summary>
    /// 基于标记的重写器。
    /// 负责解析 TerrariaTools.Rewrite.Metadata 标记并执行相应的代码转换（删除、注释、重置等）。
    /// </summary>
    public class CodeRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// 执行重写
        /// </summary>
        public SyntaxNode Apply(SyntaxNode root)
        {
            return Visit(root);
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (node == null) return null;

            var objectInitializerReset = node.GetAnnotations(ObjectInitializerRule.ResetAnnotationKind).FirstOrDefault();
            if (objectInitializerReset != null && node is AssignmentExpressionSyntax assignmentExpression)
            {
                return RewriteObjectInitializerAssignment(assignmentExpression, objectInitializerReset.Data);
            }

            // 1. 检查是否有重写标记
            var annotations = node.GetAnnotations(RuleConstants.RewriteAnnotationKind).ToList();
            if (annotations.Count > 0)
            {
                var actionData = annotations.First().Data;
                
                if (IsRewriteAction(actionData))
                {
                    return RewriteNodeWithAction(node, actionData);
                }
            }

            return base.Visit(node);
        }

        private bool IsRewriteAction(string data)
        {
            return data.Contains(RuleConstants.ActionDelete) ||
                   data.Contains(RuleConstants.ActionCommentOut) ||
                   data.Contains(RuleConstants.ActionAddReturn);
        }

        private SyntaxNode RewriteNodeWithAction(SyntaxNode node, string metadata)
        {
            if (metadata.Contains(RuleConstants.ActionAddReturn))
            {
                return RewriteMethodWithReturn(node, metadata);
            }

            // 提取动作
            bool isDelete = metadata.Contains(RuleConstants.ActionDelete);
            
            // 1. 生成注释或条件编译指令
            var originalCode = node.ToFullString().TrimEnd();
            string commentContent;
            
            if (isDelete)
            {
                commentContent = $"/* [Deleted: {metadata}]\r\n{originalCode}\r\n*/";
            }
            else
            {
                commentContent = $"/* [Commented: {metadata}]\r\n{originalCode}\r\n*/";
            }
            
            // 2. 处理 StatementSyntax
            if (node is StatementSyntax stmt)
            {
                var emptyStmt = SyntaxFactory.EmptyStatement()
                    .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(commentContent))
                    .WithTrailingTrivia(stmt.GetTrailingTrivia());

                // 3. 检查是否有 Reset 标记
                var resetAnnotation = node.GetAnnotations(RuleConstants.ResetAnnotationKind).FirstOrDefault();
                if (resetAnnotation != null)
                {
                    var defaultValue = resetAnnotation.Data;
                    
                    if (node is ExpressionStatementSyntax exprStmt && exprStmt.Expression is AssignmentExpressionSyntax assign)
                    {
                        var resetStatement = SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                assign.Left, // 复用左侧 (e.g., this.Field)
                                SyntaxFactory.ParseExpression(defaultValue)
                            ))
                            .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed) // 换行
                            .WithTrailingTrivia(stmt.GetTrailingTrivia());

                        // 返回 Block: { EmptyStmt(Comment); ResetStmt; }
                        return SyntaxFactory.Block(emptyStmt, resetStatement);
                    }
                }

                return emptyStmt;
            }
            // 4. 处理 MemberDeclarationSyntax (方法/字段/属性)
            else if (node is MemberDeclarationSyntax member)
            {
                // 对于成员级别的重写，如果标记为删除，直接返回 null
                // 这样该成员将从最终生成的语法树中移除
                return null;
            }

            return base.Visit(node);
        }

        private SyntaxNode RewriteMethodWithReturn(SyntaxNode node, string metadata)
        {
            if (node is not MethodDeclarationSyntax method || method.Body == null)
            {
                return base.Visit(node);
            }

            var defaultValue = TryGetMetadataValue(metadata, "DefaultValue") ?? "default";
            var returnStatement = SyntaxFactory.ReturnStatement(SyntaxFactory.ParseExpression(defaultValue));
            var newBody = method.Body.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(returnStatement));
            return method.WithBody(newBody);
        }

        private SyntaxNode RewriteObjectInitializerAssignment(AssignmentExpressionSyntax assignment, string defaultValue)
        {
            return assignment.WithRight(SyntaxFactory.ParseExpression(defaultValue ?? "default"));
        }

        private static string? TryGetMetadataValue(string metadata, string key)
        {
            foreach (var part in metadata.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.StartsWith(key + "=", StringComparison.Ordinal))
                {
                    return part.Substring(key.Length + 1);
                }
            }

            return null;
        }
    }
}
