using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Rules.Dome;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules
{
    /// <summary>
    /// 流程控制语句标记规则。
    /// 负责根据表达式的标记状态，决定是否标记/删除对应的语句（如 If, While, For 等）。
    /// 实现自己的逻辑检查,例如是否存在else分支
    /// 这才是和ExpressionMarkRule分离的原因
    /// </summary>
    public class ControlFlowMarkRule
    {
        public string Name => "流程控制语句标记规则";

        public StatementSyntax MarkStatement(StatementSyntax statement)
        {
            if (statement == null) return null;
            return CheckAndMarkStatement(statement);
        }

        /// <summary>
        /// 检查语句的关键部分（如 If 的 Condition），决定是否标记整个语句。
        /// 只有当关键部分的“根节点”最终被标记，或者执行体为空时，才认为该语句无效。
        /// </summary>
        private StatementSyntax CheckAndMarkStatement(StatementSyntax statement)
        {
            switch (statement)
            {
                case IfStatementSyntax ifStmt:
                    // 核心逻辑：检查 Condition 树的根节点是否被标记
                    if (IsStatementEmpty(ifStmt.Statement) || IsNodeMarked(ifStmt.Condition))
                    {
                        var marked = MarkForDelete(ifStmt);
                        // 携带上下文信息，指导后续的 Rewrite 阶段处理 Else
                        if (ifStmt.Else != null)
                        {
                            string contextInfo = "Context=HasElseBranch";
                            if (ifStmt.Else.Statement is IfStatementSyntax) contextInfo = "Context=HasElseIfBranch";
                            marked = marked.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, contextInfo));
                        }
                        return marked;
                    }
                    return ifStmt;

                case WhileStatementSyntax whileStmt:
                    if (IsStatementEmpty(whileStmt.Statement) || IsNodeMarked(whileStmt.Condition))
                        return MarkForDelete(whileStmt);
                    return whileStmt;

                case DoStatementSyntax doStmt:
                    if (IsStatementEmpty(doStmt.Statement) || IsNodeMarked(doStmt.Condition))
                        return MarkForDelete(doStmt);
                    return doStmt;

                case ForStatementSyntax forStmt:
                    // For 循环：条件根节点被标记，或循环体为空
                    if (IsStatementEmpty(forStmt.Statement) || (forStmt.Condition != null && IsNodeMarked(forStmt.Condition)))
                        return MarkForDelete(forStmt);

                    // 特殊情况：如果初始化或增量部分被标记，通常意味着循环逻辑已破坏
                    if (forStmt.Initializers.Any(IsNodeMarked) || forStmt.Incrementors.Any(IsNodeMarked))
                         return MarkForDelete(forStmt);
                    return forStmt;

                case SwitchStatementSyntax switchStmt:
                    // Switch：表达式根节点被标记，或没有任何 Case
                    if (!switchStmt.Sections.Any() || IsNodeMarked(switchStmt.Expression))
                        return MarkForDelete(switchStmt);
                    return switchStmt;

                case ExpressionStatementSyntax exprStmt:
                    // 普通表达式语句：如果表达式根节点被标记，则删除该语句
                    if (IsNodeMarked(exprStmt.Expression))
                        return MarkForDelete(exprStmt);
                    return exprStmt;

                case ReturnStatementSyntax returnStmt:
                    if (returnStmt.Expression != null && IsNodeMarked(returnStmt.Expression))
                        return MarkForDelete(returnStmt);
                    return returnStmt;

                case LocalDeclarationStatementSyntax localDecl:
                     // 变量声明：如果任何初始化器被标记，认为该声明无效
                     foreach(var v in localDecl.Declaration.Variables)
                     {
                         if(v.Initializer != null && IsNodeMarked(v.Initializer.Value))
                            return MarkForDelete(localDecl);
                     }
                     return localDecl;
            }
            return statement;
        }

        private StatementSyntax MarkForDelete(StatementSyntax stmt)
        {
            return stmt.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, RuleConstants.ActionDelete));
        }

        private bool IsNodeMarked(SyntaxNode node)
        {
            return node != null && (node.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any() ||
                   node.GetAnnotations(RuleConstants.SourceAnnotationKind).Any());
        }

        private bool IsStatementEmpty(StatementSyntax stmt)
        {
            if (stmt is BlockSyntax block) return !block.Statements.Any();
            if (stmt is EmptyStatementSyntax) return true;
            return false;
        }
    }
}
