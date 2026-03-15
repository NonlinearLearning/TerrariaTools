namespace TerrariaTools.Dome.Application;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

/// <summary>
/// Terraria 运行时影子源码重写器。
/// </summary>
public sealed class TerrariaRuntimeShadowSourceRewriter
{
    /// <summary>
    /// 重写源码并返回重写结果与摘要。
    /// </summary>
    /// <param name="source">待重写的源码文本。</param>
    /// <param name="semanticModel">语义模型。</param>
    /// <param name="preservedMemberIds">需要保留实现的成员标识集合。</param>
    /// <returns>重写后的源码与摘要。</returns>
    public (string RewrittenSource, TerrariaRuntimeShadowRewriteSummary Summary) Rewrite(
        string source,
        SemanticModel semanticModel,
        IReadOnlySet<string> preservedMemberIds)
    {
        var root = (CompilationUnitSyntax)semanticModel.SyntaxTree.GetRoot();
        var summary = new RewriteSummaryBuilder();
        var visitor = new ShadowVisitor(semanticModel, preservedMemberIds, summary);
        var rewrittenRoot = (CompilationUnitSyntax)visitor.Visit(root)!;
        return (rewrittenRoot.ToFullString(), summary.Build());
    }

    /// <summary>
    /// 影子重写访问器。
    /// </summary>
    private sealed class ShadowVisitor(
        SemanticModel semanticModel,
        IReadOnlySet<string> preservedMemberIds,
        RewriteSummaryBuilder summary) : CSharpSyntaxRewriter
    {
        /// <summary>
        /// 访问并重写方法声明。
        /// </summary>
        /// <param name="node">方法声明语法节点。</param>
        /// <returns>重写后的节点。</returns>
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), node.ReturnType);
        }

        /// <summary>
        /// 访问并重写构造函数声明。
        /// </summary>
        /// <param name="node">构造函数声明语法节点。</param>
        /// <returns>重写后的节点。</returns>
        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), null);
        }

        /// <summary>
        /// 访问并重写运算符声明。
        /// </summary>
        /// <param name="node">运算符声明语法节点。</param>
        /// <returns>重写后的节点。</returns>
        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), node.ReturnType);
        }

        /// <summary>
        /// 访问并重写转换运算符声明。
        /// </summary>
        /// <param name="node">转换运算符声明语法节点。</param>
        /// <returns>重写后的节点。</returns>
        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), node.Type);
        }

        /// <summary>
        /// 访问并重写访问器声明。
        /// </summary>
        /// <param name="node">访问器语法节点。</param>
        /// <returns>重写后的节点。</returns>
        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol == null)
            {
                return base.VisitAccessorDeclaration(node);
            }

            var memberId = MetadataMemberIdBuilder.Build(symbol).Value;
            if (preservedMemberIds.Contains(memberId))
            {
                summary.AddPreserved(memberId);
                return base.VisitAccessorDeclaration(node);
            }

            if (ShouldPreserveDeclarationOnly(symbol))
            {
                summary.AddPreserved(memberId);
                return RemoveAccessorImplementation(node);
            }

            if (node.Keyword.IsKind(SyntaxKind.GetKeyword))
            {
                summary.AddDefaulted(memberId);
                return NormalizeAccessor(node, returnsValue: true);
            }

            summary.AddEmptied(memberId);
            return NormalizeAccessor(node, returnsValue: false);
        }

        /// <summary>
        /// 访问并重写表达式体属性声明。
        /// </summary>
        /// <param name="node">属性声明语法节点。</param>
        /// <returns>重写后的节点。</returns>
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.ExpressionBody == null)
            {
                return base.VisitPropertyDeclaration(node);
            }

            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol?.GetMethod == null)
            {
                return base.VisitPropertyDeclaration(node);
            }

            var memberId = MetadataMemberIdBuilder.Build(symbol.GetMethod).Value;
            if (preservedMemberIds.Contains(memberId))
            {
                summary.AddPreserved(memberId);
                return base.VisitPropertyDeclaration(node);
            }

            if (ShouldPreserveDeclarationOnly(symbol))
            {
                summary.AddPreserved(memberId);
                return node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithAccessorList(
                        SyntaxFactory.AccessorList(
                            SyntaxFactory.SingletonList(
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))));
            }

            summary.AddDefaulted(memberId);
            return node
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List(
                        [
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithBody(SyntaxFactory.Block(CreateDefaultReturnStatement()))
                        ])));
        }

        /// <summary>
        /// 重写可调用成员实现。
        /// </summary>
        /// <typeparam name="T">语法节点类型。</typeparam>
        /// <param name="node">当前语法节点。</param>
        /// <param name="symbol">语义符号。</param>
        /// <param name="returnType">返回值类型语法。</param>
        /// <returns>重写后的语法节点。</returns>
        private T RewriteCallable<T>(T node, ISymbol? symbol, TypeSyntax? returnType)
            where T : SyntaxNode
        {
            if (symbol is not IMethodSymbol methodSymbol)
            {
                return node;
            }

            var memberId = MetadataMemberIdBuilder.Build(methodSymbol).Value;
            if (preservedMemberIds.Contains(memberId))
            {
                summary.AddPreserved(memberId);
                return node;
            }

            if (ShouldPreserveDeclarationOnly(methodSymbol))
            {
                summary.AddPreserved(memberId);
                return RemoveImplementation(node);
            }

            var returnsValue = returnType != null && !IsVoid(returnType);
            if (returnsValue)
            {
                summary.AddDefaulted(memberId);
                return NormalizeCallable(node, methodSymbol, true);
            }

            summary.AddEmptied(memberId);
            return NormalizeCallable(node, methodSymbol, false);
        }

        /// <summary>
        /// 判断类型语法是否为 void。
        /// </summary>
        /// <param name="typeSyntax">类型语法节点。</param>
        /// <returns>若为 void 则返回 <see langword="true"/>。</returns>
        private static bool IsVoid(TypeSyntax typeSyntax)
        {
            return typeSyntax is PredefinedTypeSyntax predefined &&
                   predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
        }

        /// <summary>
        /// 将可调用成员规范化为默认实现。
        /// </summary>
        /// <typeparam name="T">语法节点类型。</typeparam>
        /// <param name="node">可调用成员语法节点。</param>
        /// <param name="methodSymbol">方法语义符号。</param>
        /// <param name="returnsValue">是否返回值类型。</param>
        /// <returns>规范化后的语法节点。</returns>
        private static T NormalizeCallable<T>(T node, IMethodSymbol methodSymbol, bool returnsValue)
            where T : SyntaxNode
        {
            var statements = CreateOutParameterAssignments(methodSymbol);
            if (returnsValue)
            {
                statements.Add(CreateDefaultReturnStatement());
            }

            var block = SyntaxFactory.Block(statements);

            return node switch
            {
                MethodDeclarationSyntax method => (T)(SyntaxNode)method
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(block),
                ConstructorDeclarationSyntax ctor => (T)(SyntaxNode)ctor
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(block),
                OperatorDeclarationSyntax op => (T)(SyntaxNode)op
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(block),
                ConversionOperatorDeclarationSyntax conv => (T)(SyntaxNode)conv
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(block),
                _ => node
            };
        }

        /// <summary>
        /// 移除可调用成员的实现体，仅保留声明。
        /// </summary>
        /// <typeparam name="T">语法节点类型。</typeparam>
        /// <param name="node">可调用成员语法节点。</param>
        /// <returns>移除实现后的语法节点。</returns>
        private static T RemoveImplementation<T>(T node)
            where T : SyntaxNode
        {
            return node switch
            {
                MethodDeclarationSyntax method => (T)(SyntaxNode)method
                    .WithExpressionBody(null)
                    .WithBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                ConstructorDeclarationSyntax ctor => (T)(SyntaxNode)ctor
                    .WithExpressionBody(null)
                    .WithBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                OperatorDeclarationSyntax op => (T)(SyntaxNode)op
                    .WithExpressionBody(null)
                    .WithBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                ConversionOperatorDeclarationSyntax conv => (T)(SyntaxNode)conv
                    .WithExpressionBody(null)
                    .WithBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                _ => node
            };
        }

        /// <summary>
        /// 规范化访问器实现。
        /// </summary>
        /// <param name="node">访问器语法节点。</param>
        /// <param name="returnsValue">是否需要返回默认值。</param>
        /// <returns>规范化后的访问器节点。</returns>
        private static AccessorDeclarationSyntax NormalizeAccessor(AccessorDeclarationSyntax node, bool returnsValue)
        {
            var block = returnsValue
                ? SyntaxFactory.Block(CreateDefaultReturnStatement())
                : SyntaxFactory.Block();
            return node
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithBody(block);
        }

        /// <summary>
        /// 移除访问器实现，仅保留分号声明。
        /// </summary>
        /// <param name="node">访问器语法节点。</param>
        /// <returns>移除实现后的访问器节点。</returns>
        private static AccessorDeclarationSyntax RemoveAccessorImplementation(AccessorDeclarationSyntax node)
        {
            return node
                .WithExpressionBody(null)
                .WithBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        /// <summary>
        /// 判断符号是否应仅保留声明。
        /// </summary>
        /// <param name="symbol">待判断的语义符号。</param>
        /// <returns>需要仅保留声明时返回 <see langword="true"/>。</returns>
        private static bool ShouldPreserveDeclarationOnly(ISymbol symbol)
        {
            if (symbol is IMethodSymbol method && method.IsAbstract)
            {
                return true;
            }

             if (symbol is IMethodSymbol externMethod && externMethod.IsExtern)
            {
                return true;
            }

            return symbol.ContainingType?.TypeKind == TypeKind.Interface;
        }

        /// <summary>
        /// 创建默认返回语句。
        /// </summary>
        /// <returns>返回默认值语句。</returns>
        private static ReturnStatementSyntax CreateDefaultReturnStatement()
        {
            return (ReturnStatementSyntax)SyntaxFactory.ParseStatement("return default;");
        }

        /// <summary>
        /// 为 out 参数创建默认赋值语句集合。
        /// </summary>
        /// <param name="methodSymbol">方法语义符号。</param>
        /// <returns>out 参数默认赋值语句列表。</returns>
        private static List<StatementSyntax> CreateOutParameterAssignments(IMethodSymbol methodSymbol)
        {
            var statements = new List<StatementSyntax>();
            foreach (var parameter in methodSymbol.Parameters)
            {
                if (parameter.RefKind != RefKind.Out)
                {
                    continue;
                }

                statements.Add(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(parameter.Name),
                            SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression))));
            }

            return statements;
        }
    }

    /// <summary>
    /// 影子重写摘要构建器。
    /// </summary>
    private sealed class RewriteSummaryBuilder
    {
        private readonly HashSet<string> _preserved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _defaulted = new(StringComparer.Ordinal);
        private readonly HashSet<string> _emptied = new(StringComparer.Ordinal);

        /// <summary>
        /// 记录保留实现的成员。
        /// </summary>
        /// <param name="memberId">成员标识。</param>
        public void AddPreserved(string memberId) => _preserved.Add(memberId);

        /// <summary>
        /// 记录已默认化实现的成员。
        /// </summary>
        /// <param name="memberId">成员标识。</param>
        public void AddDefaulted(string memberId) => _defaulted.Add(memberId);

        /// <summary>
        /// 记录已清空实现的成员。
        /// </summary>
        /// <param name="memberId">成员标识。</param>
        public void AddEmptied(string memberId) => _emptied.Add(memberId);

        /// <summary>
        /// 构建最终重写摘要。
        /// </summary>
        /// <returns>重写摘要对象。</returns>
        public TerrariaRuntimeShadowRewriteSummary Build()
        {
            return new TerrariaRuntimeShadowRewriteSummary(
                _preserved.Count,
                _defaulted.Count,
                _emptied.Count,
                _preserved.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                _defaulted.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                _emptied.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray());
        }
    }
}
