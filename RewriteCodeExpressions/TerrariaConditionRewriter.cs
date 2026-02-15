/**
 * 功能描述：专门用于处理 Terraria 条件语句重写的重写器。
 * 支持删除指定的特殊表达式（如 netMode == 1），并根据逻辑运算符（&&, ||）调整 if 结构。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTools.RewriteCodeExpressions
{
    public class RewriteCondition
    {
        public string SymbolName { get; set; } = "";
        public SyntaxKind Operator { get; set; } = SyntaxKind.EqualsExpression;
        public string Value { get; set; } = "";
        public bool IsValueLiteral { get; set; } = true;
    }

    /// <summary>
    /// 专门用于处理 Terraria 条件语句重写的重写器。
    /// </summary>
    public class TerrariaConditionRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly List<RewriteCondition> _conditions;
        private bool _inFunction = false;

        /// <summary>
        /// 初始化 TerrariaConditionRewriter
        /// </summary>
        /// <param name="model">语义模型（必须提供）</param>
        /// <param name="conditions">重写规则列表</param>
        public TerrariaConditionRewriter(SemanticModel model, IEnumerable<RewriteCondition> conditions)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _conditions = conditions.ToList();
        }

        /// <summary>
        /// 初始化 TerrariaConditionRewriter (兼容旧构造函数)
        /// </summary>
        public TerrariaConditionRewriter(SemanticModel model, string targetSymbolName = "netMode", int targetValue = 1)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _conditions = new List<RewriteCondition>
            {
                new RewriteCondition
                {
                    SymbolName = targetSymbolName,
                    Operator = SyntaxKind.EqualsExpression,
                    Value = targetValue.ToString(),
                    IsValueLiteral = true
                }
            };
        }

        /// <summary>
        /// 检查给定节点是否匹配目标表达式
        /// </summary>
        private bool IsTargetExpression(SyntaxNode node)
        {
            if (node == null) return false;

            if (node is BinaryExpressionSyntax binary)
            {
                // 移除括号
                var left = RemoveParens(binary.Left);
                var right = RemoveParens(binary.Right);

                var leftSymbol = _model.GetSymbolInfo(left).Symbol;
                var rightSymbol = _model.GetSymbolInfo(right).Symbol;

                foreach (var condition in _conditions)
                {
                    if (MatchesCondition(binary, left, right, leftSymbol, rightSymbol, condition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private ExpressionSyntax RemoveParens(ExpressionSyntax expr)
        {
            while (expr is ParenthesizedExpressionSyntax p)
            {
                expr = p.Expression;
            }
            return expr;
        }

        private bool MatchesCondition(BinaryExpressionSyntax binary, ExpressionSyntax left, ExpressionSyntax right, ISymbol? leftSymbol, ISymbol? rightSymbol, RewriteCondition condition)
        {
            // 检查运算符
            if (!binary.IsKind(condition.Operator)) return false;

            // 检查左边是符号，右边是值
            if (IsSymbolMatch(leftSymbol, condition.SymbolName) && IsValueMatch(right, rightSymbol, condition))
            {
                return true;
            }

            // 检查右边是符号，左边是值 (仅当运算符是交换律兼容时，或者如果不兼容但我们要同时匹配 X != 2 和 2 != X)
            // == 和 != 都是可交换的逻辑判断 (a != b 等价于 b != a)
            if (condition.Operator == SyntaxKind.EqualsExpression || condition.Operator == SyntaxKind.NotEqualsExpression)
            {
                if (IsSymbolMatch(rightSymbol, condition.SymbolName) && IsValueMatch(left, leftSymbol, condition))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSymbolMatch(ISymbol? symbol, string targetName)
        {
            return symbol != null && symbol.Name == targetName;
        }

        private bool IsValueMatch(ExpressionSyntax expr, ISymbol? symbol, RewriteCondition condition)
        {
            if (condition.IsValueLiteral)
            {
                return IsLiteralMatch(expr, condition.Value);
            }
            else
            {
                // 检查是否为标识符（如 whoAmI）
                return symbol != null && symbol.Name == condition.Value;
            }
        }

        private bool IsLiteralMatch(ExpressionSyntax expr, string targetValue)
        {
            if (expr is LiteralExpressionSyntax literal)
            {
                return literal.Token.ValueText == targetValue;
            }
            return false;
        }

        /// <summary>
        /// 检查该节点及其子节点是否包含目标表达式
        /// </summary>
        private bool ContainsTargetExpression(SyntaxNode root)
        {
            // 优化：如果在当前节点中找不到目标表达式，则不进行进一步处理
            return root.DescendantNodes()
                       .OfType<BinaryExpressionSyntax>()
                       .Any(IsTargetExpression);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitMethodDeclaration(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitConstructorDeclaration(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitDestructorDeclaration(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitLocalFunctionStatement(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitOperatorDeclaration(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitConversionOperatorDeclaration(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            if (!ContainsTargetExpression(node)) return node;

            bool previous = _inFunction;
            _inFunction = true;
            try
            {
                return base.VisitAccessorDeclaration(node);
            }
            finally
            {
                _inFunction = previous;
            }
        }

        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            if (!_inFunction) return base.VisitIfStatement(node);

            // 1. 访问条件表达式
            var condition = (ExpressionSyntax?)Visit(node.Condition);

            // 如果条件被彻底移除
            if (condition == null)
            {
                // 如果有 else 分支，则尝试提升 else 分支的内容
                if (node.Else != null)
                {
                    var elseStatement = (StatementSyntax?)Visit(node.Else.Statement);
                    if (elseStatement != null)
                    {
                        return elseStatement.WithLeadingTrivia(node.GetLeadingTrivia());
                    }
                }
                // 否则整个 if 结构被删除
                return null;
            }

            // 2. 访问 if 主体和 else 分支
            var statement = (StatementSyntax?)Visit(node.Statement);
            var elseClause = (ElseClauseSyntax?)Visit(node.Else);

            // 只有当条件发生了改变，或者主体发生了改变，才需要更新
            if (condition == node.Condition && statement == node.Statement && elseClause == node.Else)
            {
                return node;
            }

            // 如果主体变为空（且不是原本就为空），且没有 else，则移除整个 if
            if (statement != null && statement is BlockSyntax block && block.Statements.Count == 0)
            {
                bool originallyEmpty = node.Statement is BlockSyntax originalBlock && originalBlock.Statements.Count == 0;
                if (!originallyEmpty && elseClause == null)
                {
                    return null;
                }
            }

            // 3. 更新并返回
            return node.Update(node.IfKeyword, node.OpenParenToken, condition, node.CloseParenToken, statement!, elseClause);
        }

        public override SyntaxNode? VisitElseClause(ElseClauseSyntax node)
        {
            if (!_inFunction) return base.VisitElseClause(node);
            var statement = (StatementSyntax?)Visit(node.Statement);
            if (statement == null || (statement is BlockSyntax block && block.Statements.Count == 0))
            {
                return null;
            }
            return node.Update(node.ElseKeyword, statement);
        }

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (!_inFunction) return base.VisitBinaryExpression(node);

            // 如果是目标表达式，直接返回 null
            if (IsTargetExpression(node))
            {
                return null;
            }

            // 递归访问左右子树
            var left = (ExpressionSyntax?)Visit(node.Left);
            var right = (ExpressionSyntax?)Visit(node.Right);

            // 如果都没有变，直接返回原节点
            if (left == node.Left && right == node.Right)
            {
                return node;
            }

            // 处理逻辑运算符
            if (node.IsKind(SyntaxKind.LogicalAndExpression))
            {
                // && 规则：假设 netMode == 1 是 FALSE
                // A && False -> False
                if (left == null || right == null)
                {
                    return null;
                }
            }
            else if (node.IsKind(SyntaxKind.LogicalOrExpression))
            {
                // || 规则：假设 netMode == 1 是 FALSE
                // A || False -> A
                if (left == null && right == null) return null;
                if (left == null) return right?.WithLeadingTrivia(node.GetLeadingTrivia());
                if (right == null) return left.WithTrailingTrivia(node.GetTrailingTrivia());
            }
            else
            {
                // 对于其他类型的二元表达式（如 a + b），如果任一侧被移除，则整个表达式通常也该被移除
                if (left == null || right == null)
                {
                    return null;
                }
            }

            // 保持原样更新
            return node.Update(left!, node.OperatorToken, right!);
        }

        public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            if (!_inFunction) return base.VisitParenthesizedExpression(node);
            var expression = Visit(node.Expression);
            if (expression == null) return null;

            if (expression is ExpressionSyntax expr)
            {
                // 自动简化括号：如果内部是简单名称或字面量，则去掉括号
                if (expr is IdentifierNameSyntax || expr is LiteralExpressionSyntax)
                {
                    return expr.WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
                }
                return node.WithExpression(expr);
            }
            return null;
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (!_inFunction) return base.VisitExpressionStatement(node);
            var expression = (ExpressionSyntax?)Visit(node.Expression);
            if (expression == null) return null;

            // 如果表达式发生了改变
            if (expression != node.Expression)
            {
                // 如果原本是一个复杂的二元表达式，重写后变成了一个简单的字面量或标识符，则移除该语句
                if (node.Expression is BinaryExpressionSyntax &&
                    expression is LiteralExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax)
                {
                    return null;
                }
            }

            return node.WithExpression(expression);
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (!_inFunction) return base.VisitVariableDeclarator(node);
            if (node.Initializer == null)
            {
                return base.VisitVariableDeclarator(node);
            }

            var newInitializer = (EqualsValueClauseSyntax?)Visit(node.Initializer);

            if (newInitializer == null)
            {
                // 如果初始化器被移除了，保留变量声明但不初始化
                return node;
            }

            return node.Update(node.Identifier, node.ArgumentList, newInitializer);
        }

        public override SyntaxNode? VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            if (!_inFunction) return base.VisitArrowExpressionClause(node);
            var expression = (ExpressionSyntax?)Visit(node.Expression);
            if (expression == null)
            {
                // 如果表达式被移除（例如 netMode == 1），我们需要移除整个箭头表达式子句
                return null;
            }
            return node.Update(node.ArrowToken, expression);
        }

        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            if (!_inFunction) return base.VisitConditionalExpression(node);
            var condition = (ExpressionSyntax?)Visit(node.Condition);

            // 如果条件被移除（即 target expression），我们需要简化三元表达式
            // 假设 netMode == 1 为 false，则取 WhenFalse 分支
            if (condition == null)
            {
                // 访问 WhenFalse 分支，因为我们要保留它
                var simplifiedFalse = (ExpressionSyntax?)Visit(node.WhenFalse);
                return simplifiedFalse?.WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
            }

            var whenTrue = (ExpressionSyntax?)Visit(node.WhenTrue);
            var whenFalse = (ExpressionSyntax?)Visit(node.WhenFalse);

            // 如果分支被移除，通常意味着整个表达式也该被移除或简化
            if (whenTrue == null || whenFalse == null)
            {
                return null;
            }

            return node.Update(condition, node.QuestionToken, whenTrue, node.ColonToken, whenFalse);
        }

        public override SyntaxNode? VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            if (!_inFunction) return base.VisitEqualsValueClause(node);
            var value = (ExpressionSyntax?)Visit(node.Value);
            if (value == null)
            {
                return null; // 移除整个 EqualsValueClause
            }
            return node.Update(node.EqualsToken, value);
        }
    }
}
