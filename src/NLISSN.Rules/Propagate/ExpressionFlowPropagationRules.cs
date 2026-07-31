using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把赋值右侧的原子命中迁移到左值，供后续符号引用和声明宿主规则继续沿“被写入的位置”扩散。
public sealed class AssignmentLeftValuePropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleProducesContract AssignmentTargetProduces = new(new[]
    {
        new RuleProducedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactPorts.FlowAssignmentTarget)
    });

    public override string CapabilityId { get; } = "propagate.target.assignment-left-value";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-ASSIGN-LHS-001";

    public override RuleProducesContract Produces => AssignmentTargetProduces;

    public override string Name { get; } = "Propagate s-object marks from assignment right values to left values";

    // 把右值上的命中提升到赋值左值，后续规则只沿被写入的位置继续传播。
    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            foreach (var ancestor in expression.Ancestors())
            {
                if (ancestor is AssignmentExpressionSyntax assignmentExpression &&
                    assignmentExpression.Right.Span.Contains(expression.Span))
                {
                    yield return new PropagatedMarkRecord(
                      RuleId,
                      MarkRecordFactory.Create(
                        RuleId,
                        assignmentExpression.Left,
                        "Assignment right value is marked; propagate mark to assignment left value."),
                      seedMark,
                      1);
                    break;
                }
            }
        }
    }
}

/// 把初始化表达式上的命中收束到变量声明点，避免后续规则直接依赖易碎的子表达式位置。
public sealed class DefinitionInitializerPropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleSemanticTag LocalDefinitionSemanticTag =
      RuleFactPorts.FlowLocalDefinition;

    private static readonly RuleProducesContract LocalDefinitionProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.VariableDeclarator },
          LocalDefinitionSemanticTag)
      });

    public override string CapabilityId { get; } = "propagate.target.definition-initializer";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-DECL-INIT-001";

    public override RuleProducesContract Produces => LocalDefinitionProduces;

    public override string Name { get; } = "Propagate s-object marks from definition initializers to declarators";

    // 把初始化表达式上的命中收束到变量 declarator，稳定后续局部定义传播入口。
    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            foreach (var ancestor in expression.Ancestors())
            {
                if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                    equalsValueClause.Value.Span.Contains(expression.Span) &&
                    equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
                {
                    yield return new PropagatedMarkRecord(
                      RuleId,
                      MarkRecordFactory.Create(
                        RuleId,
                        variableDeclarator,
                        "Definition initializer is marked; propagate mark to defined left value.",
                        RuleOutputKind.LocalDefinitionFromInitializer,
                        LocalDefinitionSemanticTag),
                      seedMark,
                      1);
                    break;
                }
            }
        }
    }
}

/// 仅从已收束到局部定义点的标记继续传播到同一可执行作用域内的引用，
/// 避免把 s-object 的局部事实泛化成跨作用域删除结论。
public sealed class SymbolReferencePropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleSemanticTag LocalDefinitionSemanticTag =
      RuleFactPorts.FlowLocalDefinition;

    private static readonly RuleProducesContract SymbolReferenceProduces = new(new[]
    {
        new RuleProducedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference)
    });

    private static readonly RuleConsumesContract LocalDefinitionConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.VariableDeclarator },
          LocalDefinitionSemanticTag)
      });

    public override string CapabilityId { get; } = "propagate.target.symbol-reference";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-SYMBOL-001";

    public override RuleConsumesContract Consumes => LocalDefinitionConsumes;

    public override RuleProducesContract Produces => SymbolReferenceProduces;

    public override string Name { get; } = "Propagate s-object marks from marked definitions to symbol references";

    // 仅把已收束到定义点的局部事实继续传播到同一作用域内的符号引用。
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
        var root = context.Root;
        foreach (var reference in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var referencedSymbol = ResolveReferencedSymbol(context, reference);
            if (referencedSymbol is null ||
                !markedSymbols.TryGetValue(referencedSymbol, out var markedDefinition) ||
                markedDefinition.ExecutableScope is null ||
                !ReferenceEquals(markedDefinition.ExecutableScope, FindContainingExecutableScope(reference)) ||
                !knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked definition.",
                semanticTag: RuleFactPorts.FlowSymbolReference),
              markedDefinition.SourceMark,
              1);
        }
    }

    /// 只接受前序“初始化器 -> 定义点”传播产物，确保符号引用传播从稳定的局部定义出发。
    private static Dictionary<ISymbol, MarkedLocalDefinition> BuildMarkedLocalDefinitions(IPropagationRuleContext context, IReadOnlyList<MarkRecord> marks)
    {
        var symbols = new Dictionary<ISymbol, MarkedLocalDefinition>(SymbolEqualityComparer.Default);
        foreach (var mark in marks)
        {
            if (!IsInitializerDefinitionMark(mark))
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

    private static bool IsInitializerDefinitionMark(MarkRecord mark)
    {
        return mark.SyntaxNode is VariableDeclaratorSyntax &&
          mark.OutputKind == RuleOutputKind.LocalDefinitionFromInitializer;
    }

    private static ISymbol? ResolveDeclaredLocalSymbol(IPropagationRuleContext context, SyntaxNode node)
    {
        var symbol = node is VariableDeclaratorSyntax variableDeclarator
          ? context.SemanticModel.GetDeclaredSymbol(variableDeclarator)
          : null;

        if (symbol is ILocalSymbol)
        {
            return symbol;
        }

        return null;
    }

    private static ISymbol? ResolveReferencedSymbol(IPropagationRuleContext context, IdentifierNameSyntax identifierName)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(identifierName).Symbol;
        return symbol is ILocalSymbol or IParameterSymbol ? symbol : null;
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
}
