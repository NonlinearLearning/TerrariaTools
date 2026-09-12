using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis.MethodLinkage;
using NLISSN.Core.Marking;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 基于最小调用图可达性，命中从入口点不可达的方法声明。
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreachableMethodDeletion)]
public sealed class UnreachableMethodMarkRule : RuleDefinitionMark
{
    private static readonly RuleFactKind UnreachableMethodFactKind = UnreachableMethodFacts.Marked;

    private static readonly RuleProducesContract UnreachableMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreachableMethodFactKind)
      });

    /// 规则稳定标识。

    public override string RuleId { get; } = "mark.unreachable-method";

    public override RuleProducesContract Produces => UnreachableMethodProduces;

    /// 规则的人类可读名称。
    public override string Name { get; } = "Match unreachable methods by graph reachability";

    /// 标记阶段允许产出的语法节点种类。
    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    // 从入口点沿调用图寻找可达方法，并把剩余方法声明标记为不可达删除候选。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        var unreachableMethods = context.Runtime.GetOrCreateCompilationCache(
          context.SemanticModel.Compilation,
          static compilation => MethodLinkageAnalysis.Create(compilation)).UnreachableMethods;

        foreach (var method in context.EnumerateMethodDeclarations(root))
        {
            if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            if (!unreachableMethods.Contains(Canonicalize(methodSymbol)))
            {
                continue;
            }

            yield return new MarkRecord(
              RuleId,
              method,
              null,
              CreateMethodGraphNode(methodSymbol, method),
              "Method is unreachable from the discovered entry point.",
              FactKind: UnreachableMethodFactKind);
        }
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
