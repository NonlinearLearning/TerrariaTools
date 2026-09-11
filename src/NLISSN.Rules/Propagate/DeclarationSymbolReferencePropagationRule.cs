using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把已收束到局部 declarator 的 delete-class 事实继续传播到同一作用域内、且出现在定义之后的引用点。
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class DeclarationSymbolReferencePropagationRule : RuleDefinitionPropagate
{
    private static readonly RuleFactKind LocalDefinitionFactKind =
      RuleFactKind.FlowLocalDefinition;

    private static readonly RuleConsumesContract LocalDefinitionConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.VariableDeclarator },
          LocalDefinitionFactKind)
      });

    private static readonly RuleProducesContract SymbolReferenceProduces = new(
      new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.IdentifierName },
          RuleFactKind.FlowSymbolReference)
      });


    public override string RuleId { get; } = "propagate.type.symbol-reference";

    public override RuleConsumesContract Consumes => LocalDefinitionConsumes;

    public override RuleProducesContract Produces => SymbolReferenceProduces;


    public override string Name { get; } = "Propagate delete-class local declarators to same-scope references";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IdentifierName
      };

    // 只把局部 declarator 之后、且处在同一作用域内的引用点继续传播为删除类事实。
    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var markedSymbols = BuildMarkedLocalDefinitions(context, seedMarks);
        if (markedSymbols.Count == 0)
        {
            yield break;
        }

        var knownKeys = seedMarks
          .Select(mark => BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        foreach (var reference in context.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var referencedSymbol = ResolveReferencedSymbol(context, reference);
            if (referencedSymbol is null ||
                !markedSymbols.TryGetValue(referencedSymbol, out var markedDefinition) ||
                markedDefinition.ExecutableScope is null ||
                !ReferenceEquals(markedDefinition.ExecutableScope, FindContainingExecutableScope(reference)) ||
                reference.SpanStart <= markedDefinition.SourceMark.SyntaxNode.SpanStart ||
                !knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked delete-class local definition.",
                factKind: RuleFactKind.FlowSymbolReference),
              markedDefinition.SourceMark,
              1);
        }
    }

    /// 只认“对象创建 -> 局部定义点”这类前序传播产物，
    /// 防止任意 TypeSyntax 命中直接扩散成局部引用删除事实。
    private static Dictionary<ISymbol, MarkedLocalDefinition> BuildMarkedLocalDefinitions(IPropagationRuleContext context, IReadOnlyList<MarkRecord> marks)
    {
        var symbols = new Dictionary<ISymbol, MarkedLocalDefinition>(SymbolEqualityComparer.Default);
        foreach (var mark in marks)
        {
            if (!IsObjectCreationDefinitionMark(mark))
            {
                continue;
            }

            var symbol = ResolveDeclaredLocalSymbol(context, mark.SyntaxNode);
            if (symbol is null || symbols.ContainsKey(symbol))
            {
                continue;
            }

            symbols.Add(symbol, new MarkedLocalDefinition(mark, FindContainingExecutableScope(mark.SyntaxNode)));
        }

        return symbols;
    }

    private static bool IsObjectCreationDefinitionMark(MarkRecord mark)
    {
        return mark.SyntaxNode is VariableDeclaratorSyntax &&
          mark.OutputKind == RuleOutputKind.LocalDefinitionFromObjectCreation;
    }

    private static ISymbol? ResolveDeclaredLocalSymbol(IPropagationRuleContext context, SyntaxNode node)
    {
        var symbol = node is VariableDeclaratorSyntax variableDeclarator
          ? context.SemanticModel.GetDeclaredSymbol(variableDeclarator)
          : null;

        return symbol is ILocalSymbol ? symbol : null;
    }

    private static ISymbol? ResolveReferencedSymbol(IPropagationRuleContext context, IdentifierNameSyntax identifierName)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(identifierName).Symbol;
        return symbol is ILocalSymbol ? symbol : null;
    }

    private static SyntaxNode? FindContainingExecutableScope(SyntaxNode node)
    {
        return node.AncestorsAndSelf().FirstOrDefault(ancestor =>
          ancestor is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            DestructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            AccessorDeclarationSyntax or
            AnonymousFunctionExpressionSyntax or
            LocalFunctionStatementSyntax);
    }

    private sealed record MarkedLocalDefinition(MarkRecord SourceMark, SyntaxNode? ExecutableScope);

    private static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
