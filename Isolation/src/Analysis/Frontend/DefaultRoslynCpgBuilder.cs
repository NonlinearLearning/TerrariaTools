using Analysis.Core;
using Analysis.Frontend.Builders;
using Analysis.Model;
using Analysis.Passes;
using Analysis.Passes.ControlFlow.Dominance;
using Analysis.Passes.ControlFlow;
using Analysis.Passes.DataFlow;
using Analysis.Semantic.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Analysis.Frontend;

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

    /// <inheritdoc />
    public override void Build(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
    }

    /// <inheritdoc />
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

        RunStageTwoPasses(graph);
    }

    private void RunStageTwoPasses(CpgGraph graph)
    {
        new BuildConfigFileCreationPass().Run(graph);
        new BuildImportsPass(pendingImports).Run(graph);
        new LinkAstPass().Run(graph);
        new BuildContainsEdgesPass().Run(graph);
        new BuildTypeNodePass().Run(graph);
        new BuildTypeStubPass(referencedTypeFullNames).Run(graph);
        new BuildInheritanceFullNamePass().Run(graph);
        new BuildMethodStubPass(externalMethodStubs).Run(graph);
        new BuildParameterIndexCompatPass().Run(graph);
        new BuildMethodDecoratorPass().Run(graph);
        new ResolveTypeRefsPass().Run(graph);
        new EvaluateNodeTypesPass().Run(graph);
        new BuildTypeHierarchyPass().Run(graph);
        new BuildAliasRelationPass().Run(graph);
        new BindIdentifierReferencePass().Run(graph);
        new BuildFieldAccessRelationPass().Run(graph);
        new BuildMethodReferencePass().Run(graph);
        new BuildImportResolverPass().Run(graph);
        new BuildTypeRecoveryPass().Run(graph);
        new BuildTypeHintCallLinkerPass().Run(graph);
        new BuildStaticCallGraphPass().Run(graph);
        new BuildDynamicCallGraphPass().Run(graph);
        new BuildDelegateCallGraphPass().Run(graph);
        new BuildCfgPass().Run(graph);
        new CfgDominatorPass().Run(graph);
        new BuildCdgPass().Run(graph);
        new BuildOssDataFlowPass().Run(graph);

        IReadOnlyList<ValidationViolation> violations = PostFrontendValidator.Validate(graph);
        CpgNode? metaDataNode = graph.GetNodes(CpgNodeKind.MetaData).FirstOrDefault();
        if (metaDataNode is not null)
        {
            metaDataNode.SetProperty("ValidationViolationCount", violations.Count);
        }
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
            string importedAs = usingDirective.Alias?.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(importedAs))
            {
                importedAs = importedEntity.Split('.').LastOrDefault() ?? importedEntity;
            }

            FileLinePositionSpan lineSpan = usingDirective.GetLocation().GetLineSpan();
            pendingImports.Add(new ImportDirectiveInfo(
                filePath,
                importedEntity,
                importedAs,
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
