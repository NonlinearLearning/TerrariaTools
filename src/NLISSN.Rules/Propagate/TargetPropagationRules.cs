using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把赋值右侧的原子命中迁移到左值，供后续符号引用和声明宿主规则继续沿“被写入的位置”扩散。
public sealed class SObjectAssignmentLeftValuePropagationRule : SObjectPropagationRuleBase
{
    public override string CapabilityId { get; } = "propagate.target.assignment-left-value";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-ASSIGN-LHS-001";

    public override string Name { get; } = "Propagate s-object marks from assignment right values to left values";

    // 把右值上的命中提升到赋值左值，后续规则只沿被写入的位置继续传播。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
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
public sealed class SObjectDefinitionInitializerPropagationRule : SObjectPropagationRuleBase
{
    private static readonly RuleSemanticTag LocalDefinitionSemanticTag =
      new("SObject.LocalDefinitionFromInitializer");

    private static readonly RuleProducesContract LocalDefinitionProduces =
      RuleStructureContractFactories.CreateVariableDeclaratorProduces(LocalDefinitionSemanticTag);

    public override string CapabilityId { get; } = "propagate.target.definition-initializer";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-DECL-INIT-001";

    public override RuleProducesContract Produces => LocalDefinitionProduces;

    public override string Name { get; } = "Propagate s-object marks from definition initializers to declarators";

    // 把初始化表达式上的命中收束到变量 declarator，稳定后续局部定义传播入口。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
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

/// 为逻辑条件保留旧的宿主提升入口；一旦能构造结构化 payload，就交给更专门的操作数组传播规则处理。
public sealed class SObjectLogicalConditionPropagationRule : SObjectPropagationRuleBase
{
    public override string CapabilityId { get; } = "propagate.target.logical-condition";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-LOGIC-001";

    public override string Name { get; } = "Propagate s-object marks into logical condition hosts";

    // 在没有更强结构化 payload 可用时，把多个逻辑条件命中折叠到旧的逻辑宿主入口。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var targetNames = ParseTargetNames(context);
        if (targetNames.Count == 0)
        {
            yield break;
        }

        var targetNameList = string.Join(",", targetNames);
        var knownKeys = seedMarks
          .Select(mark => BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression ||
                !context.CanAnalyzeLogicalCondition(expression))
            {
                continue;
            }

            LogicalConditionMarkAnalysis analysis;
            try
            {
                analysis = context.AnalyzeLogicalCondition(expression, targetNameList);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.StartsWith(
                  "Could not resolve target symbol",
                  StringComparison.Ordinal))
            {
                continue;
            }

            if (analysis.PreferredMarkedNode is BinaryExpressionSyntax logicalHost &&
                (logicalHost.IsKind(SyntaxKind.LogicalAndExpression) ||
                 logicalHost.IsKind(SyntaxKind.LogicalOrExpression)) &&
                // 结构化 payload 能明确区分可删与保留操作数时，
                // 由 payload 规则接管，避免这里再产出一个语义更弱的通用宿主标记。
                DeleteSObjectPropagationHelpers.TryBuildLogicalHostPayload(
                  context,
                  logicalHost,
                  analysis.Hits.Select(hit => hit.Node)) is not null)
            {
                continue;
            }

            if (ReferenceEquals(analysis.PreferredMarkedNode, expression) ||
                analysis.Hits.Count <= 1 && analysis.OperandGroups.Count == 0 ||
                !knownKeys.Add(BuildNodeKey(analysis.PreferredMarkedNode)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                analysis.PreferredMarkedNode,
                "Logical condition group is marked; lift atomic hits to the logical host."),
              seedMark,
              1);
        }
    }

    private static IReadOnlyList<string> ParseTargetNames(RuleContext context)
    {
        if (!context.TryGetOption("target-name", out var targetName) ||
            string.IsNullOrWhiteSpace(targetName))
        {
            return Array.Empty<string>();
        }

        return targetName
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.Ordinal)
          .ToList();
    }
}

/// 为逻辑与/或宿主补齐“哪些操作数可删、哪些必须保留”的 payload，
/// 让 Propose 阶段直接做短路语义安全的 Replace 决策。
public sealed class SObjectLogicalOperandGroupPropagationRule : SObjectPropagationRuleBase
{
    private static readonly RuleSemanticTag LogicalHostSemanticTag = new("SObject.LogicalHost");

    private static readonly RuleProducesContract LogicalHostProduces =
      RuleStructureContractFactories.CreateLogicalBinaryProduces(LogicalHostSemanticTag);

    public override string CapabilityId { get; } = "propagate.target.logical-operand-group";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-LOGIC-GROUP-001";

    public override string Name { get; } = "Propagate s-object logical operand groups as structured payloads";

    public override RuleProducesContract Produces => LogicalHostProduces;

    // 为逻辑宿主补齐可删与保留操作数集合，让提案阶段直接生成语义安全的 Replace 决策。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var targetNames = DeleteSObjectPropagationHelpers.ParseTargetNames(context);
        if (targetNames.Count == 0)
        {
            yield break;
        }

        var targetNameList = string.Join(",", targetNames);
        var knownKeys = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression ||
                !context.CanAnalyzeLogicalCondition(expression))
            {
                continue;
            }

            LogicalConditionMarkAnalysis analysis;
            try
            {
                analysis = context.AnalyzeLogicalCondition(expression, targetNameList);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.StartsWith(
                  "Could not resolve target symbol",
                  StringComparison.Ordinal))
            {
                continue;
            }

            if (analysis.PreferredMarkedNode is not BinaryExpressionSyntax host ||
                (!host.IsKind(SyntaxKind.LogicalAndExpression) &&
                 !host.IsKind(SyntaxKind.LogicalOrExpression)) ||
                !knownKeys.Add(BuildNodeKey(host)))
            {
                continue;
            }

            var payload = DeleteSObjectPropagationHelpers.TryBuildLogicalHostPayload(
              context,
              host,
              analysis.Hits.Select(hit => hit.Node));
            if (payload is null)
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                host,
                "Logical condition group is marked; propagate removable and surviving operands to the logical host.",
                RuleOutputKind.LogicalHost,
                LogicalHostSemanticTag),
              seedMark,
              1,
              Payload: payload);
        }
    }
}

/// 把零散命中折叠成完整 if / else if / else 结构的完成态 payload，
/// 后续只需要按结构决策，不再重复扫描控制流外壳。
public sealed class SObjectIfStructureCompletionPropagationRule : SObjectPropagationRuleBase
{
    private static readonly RuleSemanticTag IfCompletionSemanticTag = new("SObject.IfCompletion");

    private static readonly RuleProducesContract IfCompletionProduces =
      RuleStructureContractFactories.CreateIfCompletionProduces(IfCompletionSemanticTag);

    public override string CapabilityId { get; } = "propagate.target.if-structure-completion";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-IF-COMPLETE-001";

    public override string Name { get; } = "Propagate s-object if/elseif/else completion state as structured payloads";

    public override RuleProducesContract Produces => IfCompletionProduces;

    // 把分散在 if 结构里的命中折叠成完整完成态 payload，避免提案阶段重复扫描控制结构。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        return DeleteSObjectPropagationHelpers.EnumerateIfStructureCompletionPropagations(
          context,
          seedMarks,
          RuleId,
          IfCompletionSemanticTag);
    }
}

/// 仅从已收束到局部定义点的标记继续传播到同一可执行作用域内的引用，
/// 避免把 s-object 的局部事实泛化成跨作用域删除结论。
public sealed class SObjectSymbolReferencePropagationRule : SObjectPropagationRuleBase
{
    private static readonly RuleSemanticTag LocalDefinitionSemanticTag =
      new("SObject.LocalDefinitionFromInitializer");

    private static readonly RuleConsumesContract LocalDefinitionConsumes =
      RuleStructureContractFactories.CreateVariableDeclaratorConsumes(
        LocalDefinitionSemanticTag,
        RuleInputCardinality.All);

    public override string CapabilityId { get; } = "propagate.target.symbol-reference";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-SYMBOL-001";

    public override RuleConsumesContract Consumes => LocalDefinitionConsumes;

    public override string Name { get; } = "Propagate s-object marks from marked definitions to symbol references";

    // 仅把已收束到定义点的局部事实继续传播到同一作用域内的符号引用。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
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
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked definition."),
              markedDefinition.SourceMark,
              1);
        }
    }

    /// 只接受前序“初始化器 -> 定义点”传播产物，确保符号引用传播从稳定的局部定义出发。
    private static Dictionary<ISymbol, MarkedLocalDefinition> BuildMarkedLocalDefinitions(RuleContext context, IReadOnlyList<MarkRecord> marks)
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

    private static ISymbol? ResolveDeclaredLocalSymbol(RuleContext context, SyntaxNode node)
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

    private static ISymbol? ResolveReferencedSymbol(RuleContext context, IdentifierNameSyntax identifierName)
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
