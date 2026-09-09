using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis.MethodLinkage;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// 在项目级 Compilation 内查找没有外部引用的普通私有方法声明。
[global::NLISSN.Core.Pipeline.RuleCatalogIgnore]
public sealed class UnreferencedMethodMarkRule : RuleDefinitionMark
{
    private static readonly RuleFactKind UnreferencedMethodFactKind = RuleFactKind.UnreferencedMethod;

    private static readonly RuleProducesContract UnreferencedMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreferencedMethodFactKind)
      });


    public override string RuleId { get; } = "mark.unreferenced-method";

    public override RuleProducesContract Produces => UnreferencedMethodProduces;


    public override string Name { get; } = "Match unreferenced private method declarations";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    // 迭代剔除仍被外部或保留候选引用的方法，只保留真正无引用的私有方法声明。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        if (!IsEnabled(context))
        {
            yield break;
        }

        var unreferencedMethods = FindUnreferencedCandidateMethods(context);
        foreach (var method in context.EnumerateMethodDeclarations(root))
        {
            if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None)
                is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            if (!unreferencedMethods.Contains(Canonicalize(methodSymbol)))
            {
                continue;
            }

            yield return new MarkRecord(
              RuleId,
              method,
              null,
              CreateMethodGraphNode(methodSymbol, method),
              "Private method has no references from methods that remain in the project.",
              FactKind: UnreferencedMethodFactKind);
        }
    }

    private static bool IsEnabled(IMarkRuleContext context)
    {
        return context.DeleteUnreferencedMethods;
    }

    private static IReadOnlySet<IMethodSymbol> FindUnreferencedCandidateMethods(IMarkRuleContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        return context.Runtime.GetOrCreateCompilationCache(
          compilation,
          static cachedCompilation => MethodLinkageAnalysis.Create(cachedCompilation)).UnreferencedPrivateMethods;
    }

    private static IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }

    private static NLCPGNode CreateMethodGraphNode(IMethodSymbol methodSymbol, MethodDeclarationSyntax method)
    {
        return new NLCPGNode(
          Kind: NLCPGNodeKind.Method,
          DisplayKind: nameof(NLCPGNodeKind.Method),
          Name: methodSymbol.Name,
          FullName: methodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
          Signature: methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
          FilePath: method.SyntaxTree.FilePath,
          SpanStart: method.SpanStart,
          SpanEnd: method.Span.End,
          Text: method.ToString());
    }
}
