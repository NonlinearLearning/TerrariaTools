namespace TerrariaTools.Dome.Adapters.Runtime.Process;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;

// 重写影子工作区源码，使请求闭包之外的成员退化为安全桩实现，
// 而可达成员继续保留原始方法体。
public sealed class TerrariaRuntimeShadowSourceRewriter
{
    public (string RewrittenSource, ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary Summary) Rewrite(
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

    private sealed class ShadowVisitor(
        SemanticModel semanticModel,
        IReadOnlySet<string> preservedMemberIds,
        RewriteSummaryBuilder summary) : CSharpSyntaxRewriter
    {
        // 所有可调用成员遵循同一套保留策略，
        // 因此各语法入口统一委托给同一个重写路径。
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), node.ReturnType);
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), null);
        }

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), node.ReturnType);
        }

        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            return RewriteCallable(node, semanticModel.GetDeclaredSymbol(node), node.Type);
        }

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

        private static bool IsVoid(TypeSyntax typeSyntax)
        {
            return typeSyntax is PredefinedTypeSyntax predefined &&
                   predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
        }

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

        private static AccessorDeclarationSyntax RemoveAccessorImplementation(AccessorDeclarationSyntax node)
        {
            return node
                .WithExpressionBody(null)
                .WithBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

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

        private static ReturnStatementSyntax CreateDefaultReturnStatement()
        {
            return (ReturnStatementSyntax)SyntaxFactory.ParseStatement("return default;");
        }

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

    private sealed class RewriteSummaryBuilder
    {
        private readonly HashSet<string> _preserved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _defaulted = new(StringComparer.Ordinal);
        private readonly HashSet<string> _emptied = new(StringComparer.Ordinal);

        public void AddPreserved(string memberId) => _preserved.Add(memberId);

        public void AddDefaulted(string memberId) => _defaulted.Add(memberId);

        public void AddEmptied(string memberId) => _emptied.Add(memberId);

        public ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary Build()
        {
            return new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(
                _preserved.Count,
                _defaulted.Count,
                _emptied.Count,
                _preserved.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                _defaulted.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                _emptied.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray());
        }
    }
}



