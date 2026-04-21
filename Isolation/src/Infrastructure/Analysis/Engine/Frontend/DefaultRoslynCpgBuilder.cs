using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend.Builders;
using Domain.Analysis.Engine.Model;
using Logic.Analysis.Engine.Frontend;
using Logic.Analysis.Engine.Passes;
using Logic.Analysis.Engine.Passes.ControlFlow;
using Infrastructure.Analysis.Engine.Passes;
using Logic.Analysis.Engine.Passes.ControlFlow.Dominance;
using Infrastructure.Analysis.Engine.Passes.ControlFlow;
using Logic.Analysis.Engine.Passes.DataFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Infrastructure.Analysis.Engine.Frontend;

/// <summary>
/// 提供可直接工作的默认 Roslyn CPG Builder。
///
/// 这个类现在只负责前端编排：
/// - 准备共享状态；
/// - 组装声明、语句、表达式 Builder；
/// - 运行阶段二图关系 pass。
/// </summary>
public sealed class DefaultRoslynCpgBuilder : RoslynAstToCpgBuilder
{
    private readonly List<MethodStubDefinition> externalMethodStubs = new();
    private readonly List<ImportDirectiveInfo> pendingImports = new();
    private readonly Dictionary<SyntaxNode, long> nodeIdsBySyntax = new();
    private readonly HashSet<string> referencedTypeFullNames = new(StringComparer.Ordinal);


    public override void Build(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
    }


    public override void Build(CpgGraph graph, RoslynCompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);

        nodeIdsBySyntax.Clear();
        referencedTypeFullNames.Clear();
        externalMethodStubs.Clear();
        pendingImports.Clear();

        new BuildFileNodesPass(context.SyntaxTrees.Select(tree => tree.FilePath)).Run(graph);

        CpgGraphBuilder graphBuilder = new(graph);
        BuilderState state = new(
            graphBuilder,
            context,
            nodeIdsBySyntax,
            referencedTypeFullNames,
            externalMethodStubs);
        PrimitiveBuilder primitiveBuilder = new(state);
        ExpressionBuilder expressionBuilder = new(state, primitiveBuilder);
        StatementBuilder statementBuilder = new(state, primitiveBuilder, expressionBuilder);
        DeclarationBuilder declarationBuilder = new(
            state,
            primitiveBuilder,
            expressionBuilder,
            statementBuilder);

        foreach (SyntaxTree tree in context.SyntaxTrees)
        {
            CpgNode? fileNode = primitiveBuilder.FindFileNode(tree.FilePath);
            if (fileNode is null)
            {
                continue;
            }

            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            CollectImports(tree.FilePath, root, context);
            foreach (BaseTypeDeclarationSyntax typeDeclaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                declarationBuilder.BuildType(typeDeclaration, fileNode);
            }
        }

        new BuildConfigFileCreationPass().Run(graph);
        FrontendStageTwoPipeline.Run(graph, pendingImports, referencedTypeFullNames, externalMethodStubs);
    }

    private void CollectImports(
        string filePath,
        CompilationUnitSyntax root,
        RoslynCompilationContext context)
    {
        SemanticModel semanticModel = context.GetSemanticModel(root.SyntaxTree);
        int order = 1;
        foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes(descendIntoChildren: static _ => true)
                     .OfType<UsingDirectiveSyntax>())
        {
            string importedEntity = ResolveImportedEntity(usingDirective, semanticModel);
            FileLinePositionSpan lineSpan = usingDirective.GetLocation().GetLineSpan();
            pendingImports.Add(ImportDirectiveConventions.CreateImportDirectiveInfo(
                filePath,
                importedEntity,
                usingDirective.Alias?.Name.Identifier.ValueText,
                usingDirective.ToString(),
                order++,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                usingDirective.StaticKeyword != default,
                usingDirective.GlobalKeyword != default));
        }
    }

    private string ResolveImportedEntity(UsingDirectiveSyntax usingDirective, SemanticModel semanticModel)
    {
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(usingDirective.Name);
        ISymbol? symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        return symbol switch
        {
            INamespaceSymbol namespaceSymbol => namespaceSymbol.ToDisplayString(),
            ITypeSymbol typeSymbol => RoslynSymbolFormatter.GetTypeFullName(typeSymbol),
            _ => usingDirective.Name.ToString(),
        };
    }
}
