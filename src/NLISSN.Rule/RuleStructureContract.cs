using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;

namespace NLISSN.Core.Pipeline;

/// <summary>
/// 表示附加到已标记语法节点的语义事实名称。
/// </summary>
public sealed record RuleSemanticTag
{
    public RuleSemanticTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A rule semantic tag cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

/// <summary>
/// Identifies a built-in rule fact. The member name is its canonical identity;
/// the enum's underlying value is process-local and is not a protocol value.
/// </summary>
public enum RuleFactKind : ushort
{
    Unknown,
    TargetExpression,
    TargetTypeSyntax,
    TargetDeclaration,

    FlowLocalDefinition,
    FlowSymbolReference,
    FlowAssignmentTarget,
    FlowLogicalExpression,
    FlowUnaryExpression,
    FlowConditionalExpression,

    RelationDeclarationHost,
    RelationParameterUsage,
    RelationDelegateUsage,
    RelationExtensionUsage,

    LiftExpressionHost,
    LiftLogicalReduction,
    LiftIfStructure,
    LiftSwitchStructure,
    LiftControlStructure,

    InternalOnlyPublicMethodMarked,
    InternalOnlyPublicMethodPropagated,
    InternalOnlyPublicMethodLifted,

    UnusedInterfaceImplementationMarked,
    UnusedInterfaceImplementationPropagated,
    UnusedInterfaceImplementationLifted,

    UnreachableMethodMarked,
    UnreachableMethodPropagated,
    UnreachableMethodLifted,

    UnreferencedMethodMarked,
    UnreferencedMethodPropagated,
    UnreferencedMethodLifted,

    // Retained for compatibility with serialized legacy fact tags.
    UnreachableMethod,
    UnreferencedMethod
}

/// <summary>
/// Groups a fact kind without replacing the specific fact identity.
/// </summary>
public enum RuleFactDomain : byte
{
    Target,
    Flow,
    Relation,
    Lift,
    Global
}

/// <summary>
/// Provides the controlled domain/display mapping and legacy adapter for built-in fact kinds.
/// </summary>
public static class RuleFactKindDescriptor
{
    public static RuleFactDomain GetDomain(RuleFactKind factKind)
    {
        return factKind switch
        {
            RuleFactKind.TargetExpression or RuleFactKind.TargetTypeSyntax or RuleFactKind.TargetDeclaration =>
              RuleFactDomain.Target,
            RuleFactKind.FlowLocalDefinition or RuleFactKind.FlowSymbolReference or
              RuleFactKind.FlowAssignmentTarget or RuleFactKind.FlowLogicalExpression or
              RuleFactKind.FlowUnaryExpression or RuleFactKind.FlowConditionalExpression =>
              RuleFactDomain.Flow,
            RuleFactKind.RelationDeclarationHost or RuleFactKind.RelationParameterUsage or
              RuleFactKind.RelationDelegateUsage or RuleFactKind.RelationExtensionUsage =>
              RuleFactDomain.Relation,
            RuleFactKind.LiftExpressionHost or RuleFactKind.LiftLogicalReduction or
              RuleFactKind.LiftIfStructure or RuleFactKind.LiftSwitchStructure or
              RuleFactKind.LiftControlStructure =>
              RuleFactDomain.Lift,
            RuleFactKind.InternalOnlyPublicMethodMarked or RuleFactKind.InternalOnlyPublicMethodPropagated or
              RuleFactKind.InternalOnlyPublicMethodLifted or
              RuleFactKind.UnusedInterfaceImplementationMarked or
              RuleFactKind.UnusedInterfaceImplementationPropagated or
              RuleFactKind.UnusedInterfaceImplementationLifted or
              RuleFactKind.UnreachableMethodMarked or RuleFactKind.UnreachableMethodPropagated or
              RuleFactKind.UnreachableMethodLifted or RuleFactKind.UnreferencedMethodMarked or
              RuleFactKind.UnreferencedMethodPropagated or RuleFactKind.UnreferencedMethodLifted or
              RuleFactKind.UnreachableMethod or RuleFactKind.UnreferencedMethod =>
              RuleFactDomain.Global,
            _ => throw new ArgumentOutOfRangeException(nameof(factKind), factKind, "Unknown rule fact kind."),
        };
    }

    public static string GetDisplayName(RuleFactKind factKind)
    {
        return factKind switch
        {
            RuleFactKind.TargetExpression => "Target.Expression",
            RuleFactKind.TargetTypeSyntax => "Target.TypeSyntax",
            RuleFactKind.TargetDeclaration => "Target.Declaration",
            RuleFactKind.FlowLocalDefinition => "Flow.LocalDefinition",
            RuleFactKind.FlowSymbolReference => "Flow.SymbolReference",
            RuleFactKind.FlowAssignmentTarget => "Flow.AssignmentTarget",
            RuleFactKind.FlowLogicalExpression => "Flow.LogicalExpression",
            RuleFactKind.FlowUnaryExpression => "Flow.UnaryExpression",
            RuleFactKind.FlowConditionalExpression => "Flow.ConditionalExpression",
            RuleFactKind.RelationDeclarationHost => "Relation.DeclarationHost",
            RuleFactKind.RelationParameterUsage => "Relation.ParameterUsage",
            RuleFactKind.RelationDelegateUsage => "Relation.DelegateUsage",
            RuleFactKind.RelationExtensionUsage => "Relation.ExtensionUsage",
            RuleFactKind.LiftExpressionHost => "Lift.ExpressionHost",
            RuleFactKind.LiftLogicalReduction => "Lift.LogicalReduction",
            RuleFactKind.LiftIfStructure => "Lift.IfStructure",
            RuleFactKind.LiftSwitchStructure => "Lift.SwitchStructure",
            RuleFactKind.LiftControlStructure => "Lift.ControlStructure",
            RuleFactKind.InternalOnlyPublicMethodMarked => "InternalOnlyPublicMethod.Marked",
            RuleFactKind.InternalOnlyPublicMethodPropagated => "InternalOnlyPublicMethod.Propagated",
            RuleFactKind.InternalOnlyPublicMethodLifted => "InternalOnlyPublicMethod.Lifted",
            RuleFactKind.UnusedInterfaceImplementationMarked => "UnusedInterfaceImplementation.Marked",
            RuleFactKind.UnusedInterfaceImplementationPropagated => "UnusedInterfaceImplementation.Propagated",
            RuleFactKind.UnusedInterfaceImplementationLifted => "UnusedInterfaceImplementation.Lifted",
            RuleFactKind.UnreachableMethodMarked => "UnreachableMethod.Marked",
            RuleFactKind.UnreachableMethodPropagated => "UnreachableMethod.Propagated",
            RuleFactKind.UnreachableMethodLifted => "UnreachableMethod.Lifted",
            RuleFactKind.UnreferencedMethodMarked => "UnreferencedMethod.Marked",
            RuleFactKind.UnreferencedMethodPropagated => "UnreferencedMethod.Propagated",
            RuleFactKind.UnreferencedMethodLifted => "UnreferencedMethod.Lifted",
            RuleFactKind.UnreachableMethod => "UnreachableMethod",
            RuleFactKind.UnreferencedMethod => "UnreferencedMethod",
            _ => throw new ArgumentOutOfRangeException(nameof(factKind), factKind, "Unknown rule fact kind."),
        };
    }

    public static RuleSemanticTag ToSemanticTag(RuleFactKind factKind)
    {
        return new RuleSemanticTag(GetDisplayName(factKind));
    }

    public static bool TryGetKind(RuleSemanticTag semanticTag, out RuleFactKind factKind)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);
        factKind = semanticTag.Value switch
        {
            "Target.Expression" => RuleFactKind.TargetExpression,
            "Target.TypeSyntax" => RuleFactKind.TargetTypeSyntax,
            "Target.Declaration" => RuleFactKind.TargetDeclaration,
            "Flow.LocalDefinition" => RuleFactKind.FlowLocalDefinition,
            "Flow.SymbolReference" => RuleFactKind.FlowSymbolReference,
            "Flow.AssignmentTarget" => RuleFactKind.FlowAssignmentTarget,
            "Flow.LogicalExpression" => RuleFactKind.FlowLogicalExpression,
            "Flow.UnaryExpression" => RuleFactKind.FlowUnaryExpression,
            "Flow.ConditionalExpression" => RuleFactKind.FlowConditionalExpression,
            "Relation.DeclarationHost" => RuleFactKind.RelationDeclarationHost,
            "Relation.ParameterUsage" => RuleFactKind.RelationParameterUsage,
            "Relation.DelegateUsage" => RuleFactKind.RelationDelegateUsage,
            "Relation.ExtensionUsage" => RuleFactKind.RelationExtensionUsage,
            "Lift.ExpressionHost" => RuleFactKind.LiftExpressionHost,
            "Lift.LogicalReduction" => RuleFactKind.LiftLogicalReduction,
            "Lift.IfStructure" => RuleFactKind.LiftIfStructure,
            "Lift.SwitchStructure" => RuleFactKind.LiftSwitchStructure,
            "Lift.ControlStructure" => RuleFactKind.LiftControlStructure,
            "InternalOnlyPublicMethod.Marked" => RuleFactKind.InternalOnlyPublicMethodMarked,
            "InternalOnlyPublicMethod.Propagated" => RuleFactKind.InternalOnlyPublicMethodPropagated,
            "InternalOnlyPublicMethod.Lifted" => RuleFactKind.InternalOnlyPublicMethodLifted,
            "UnusedInterfaceImplementation.Marked" => RuleFactKind.UnusedInterfaceImplementationMarked,
            "UnusedInterfaceImplementation.Propagated" => RuleFactKind.UnusedInterfaceImplementationPropagated,
            "UnusedInterfaceImplementation.Lifted" => RuleFactKind.UnusedInterfaceImplementationLifted,
            "UnreachableMethod.Marked" => RuleFactKind.UnreachableMethodMarked,
            "UnreachableMethod.Propagated" => RuleFactKind.UnreachableMethodPropagated,
            "UnreachableMethod.Lifted" => RuleFactKind.UnreachableMethodLifted,
            "UnreferencedMethod.Marked" => RuleFactKind.UnreferencedMethodMarked,
            "UnreferencedMethod.Propagated" => RuleFactKind.UnreferencedMethodPropagated,
            "UnreferencedMethod.Lifted" => RuleFactKind.UnreferencedMethodLifted,
            "UnreachableMethod" => RuleFactKind.UnreachableMethod,
            "UnreferencedMethod" => RuleFactKind.UnreferencedMethod,
            _ => RuleFactKind.Unknown,
        };
        return factKind is not RuleFactKind.Unknown;
    }

    public static RuleFactKind? Resolve(RuleFactKind? factKind, RuleSemanticTag? semanticTag)
    {
        if (semanticTag is not null && TryGetKind(semanticTag, out var mappedKind))
        {
            return mappedKind;
        }

        if (factKind is { } knownKind)
        {
            _ = GetDomain(knownKind);
            return knownKind;
        }

        return null;
    }

    public static bool Matches(
      RuleFactKind? leftFactKind,
      RuleSemanticTag? leftSemanticTag,
      RuleFactKind? rightFactKind,
      RuleSemanticTag? rightSemanticTag)
    {
        var leftKind = Resolve(leftFactKind, leftSemanticTag);
        var rightKind = Resolve(rightFactKind, rightSemanticTag);
        if (leftKind is { } leftKnown && rightKind is { } rightKnown)
        {
            return leftKnown == rightKnown;
        }

        return leftSemanticTag is not null && rightSemanticTag is not null &&
           leftSemanticTag == rightSemanticTag;
    }

}

/// <summary>
/// 标识为某个事实提供依据的证明域，但不参与图路由。
/// </summary>
[Flags]
public enum RuleEvidenceOrigin
{
    None = 0,
    AtomicExpression = 1,
    DeclarationExpression = 2,
    DeclarationType = 4,
    DeclarationName = 8
}

/// <summary>
/// 指定规则如何接受每个已声明语法输入的生产者。
/// </summary>
public enum RuleInputCardinality
{
    All = 0,
    ExactlyOne = 1,
    Optional = 2
}

/// <summary>
/// 声明从上游规则接收的语法种类和语义标签。
/// </summary>
public sealed record RuleConsumedSyntax
{
    public RuleConsumedSyntax(IReadOnlyList<SyntaxKind> syntaxKinds, RuleSemanticTag semanticTag)
    {
        SyntaxKinds = RuleSyntaxContractValidation.ValidateSyntaxKinds(syntaxKinds);
        SemanticTag = semanticTag ?? throw new ArgumentNullException(nameof(semanticTag));
        FactKind = RuleFactKindDescriptor.Resolve(null, SemanticTag);
    }

    public RuleConsumedSyntax(IReadOnlyList<SyntaxKind> syntaxKinds, RuleFactKind factKind)
    {
        SyntaxKinds = RuleSyntaxContractValidation.ValidateSyntaxKinds(syntaxKinds);
        FactKind = RuleFactKindDescriptor.Resolve(factKind, null);
        SemanticTag = RuleFactKindDescriptor.ToSemanticTag(factKind);
    }

    public IReadOnlyList<SyntaxKind> SyntaxKinds { get; }

    public RuleSemanticTag SemanticTag { get; }

    public RuleFactKind? FactKind { get; }
}

/// <summary>
/// 声明由规则输出的语法种类和语义标签。
/// </summary>
public sealed record RuleProducedSyntax
{
    public RuleProducedSyntax(IReadOnlyList<SyntaxKind> syntaxKinds, RuleSemanticTag semanticTag)
    {
        SyntaxKinds = RuleSyntaxContractValidation.ValidateSyntaxKinds(syntaxKinds);
        SemanticTag = semanticTag ?? throw new ArgumentNullException(nameof(semanticTag));
        FactKind = RuleFactKindDescriptor.Resolve(null, SemanticTag);
    }

    public RuleProducedSyntax(IReadOnlyList<SyntaxKind> syntaxKinds, RuleFactKind factKind)
    {
        SyntaxKinds = RuleSyntaxContractValidation.ValidateSyntaxKinds(syntaxKinds);
        FactKind = RuleFactKindDescriptor.Resolve(factKind, null);
        SemanticTag = RuleFactKindDescriptor.ToSemanticTag(factKind);
    }

    public IReadOnlyList<SyntaxKind> SyntaxKinds { get; }

    public RuleSemanticTag SemanticTag { get; }

    public RuleFactKind? FactKind { get; }
}

/// <summary>
/// 管理规则直接接收的语法输入。
/// </summary>
public sealed record RuleConsumesContract(IReadOnlyList<RuleConsumedSyntax> Inputs)
{
    public static RuleConsumesContract Empty { get; } = new(Array.Empty<RuleConsumedSyntax>());
}

/// <summary>
/// 管理规则直接输出的语法结果。
/// </summary>
public sealed record RuleProducesContract(IReadOnlyList<RuleProducedSyntax> Outputs)
{
    public static RuleProducesContract Empty { get; } = new(Array.Empty<RuleProducedSyntax>());
}

/// <summary>
/// 根据语义标签和接收的语法种类匹配直接语法契约。
/// </summary>
public static class RuleSyntaxContractMatcher
{
    public static bool IsCompatible(RuleProducedSyntax producer, RuleConsumedSyntax consumer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(consumer);

        return RuleFactKindDescriptor.Matches(
            producer.FactKind,
            producer.SemanticTag,
            consumer.FactKind,
            consumer.SemanticTag) &&
          producer.SyntaxKinds.All(consumer.SyntaxKinds.Contains);
    }
}

/// <summary>
/// 根据直接语法标签生产者契约验证标记。
/// </summary>
public static class RuleSyntaxContractValidator
{
    public static RuleProducedSyntax RequireProducedMark(
      RuleProducesContract produces,
      MarkRecord mark)
    {
        ArgumentNullException.ThrowIfNull(produces);
        ArgumentNullException.ThrowIfNull(mark);

        var factKind = RuleFactKindDescriptor.Resolve(mark.FactKind, mark.SemanticTag);
        if (factKind is null && mark.SemanticTag is null)
        {
            throw new InvalidOperationException(
              $"Rule '{mark.RuleId}' emitted a mark without a semantic tag.");
        }

        var output = produces.Outputs.FirstOrDefault(candidate =>
          RuleFactKindDescriptor.Matches(
            candidate.FactKind,
            candidate.SemanticTag,
            factKind,
            mark.SemanticTag) &&
          candidate.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind));
        return output ?? throw new InvalidOperationException(
          $"Rule '{mark.RuleId}' emitted a mark that does not match any declared syntax output.");
    }

    public static bool TryGetObservedProducedMark(
      RuleProducesContract produces,
      MarkRecord mark,
      out RuleProducedSyntax? output)
    {
        ArgumentNullException.ThrowIfNull(produces);
        ArgumentNullException.ThrowIfNull(mark);

        output = mark.SemanticTag is null && mark.FactKind is null
          ? null
          : produces.Outputs.FirstOrDefault(candidate =>
            RuleFactKindDescriptor.Matches(
              candidate.FactKind,
              candidate.SemanticTag,
              mark.FactKind,
              mark.SemanticTag) &&
            candidate.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind));
        return output is not null;
    }

    public static RuleProducesContract CreateObservedProduces(
      IReadOnlyList<MarkRecord> marks,
      IReadOnlyList<RuleConsumedSyntax> consumers)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(consumers);

        var outputs = consumers
          .Distinct()
          .Where(input => marks.Any(mark =>
            RuleFactKindDescriptor.Matches(
              input.FactKind,
              input.SemanticTag,
              mark.FactKind,
              mark.SemanticTag) &&
            input.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind)))
          .Select(input => input.FactKind is { } factKind
            ? new RuleProducedSyntax(input.SyntaxKinds, factKind)
            : new RuleProducedSyntax(input.SyntaxKinds, input.SemanticTag))
          .ToList();
        return new RuleProducesContract(outputs);
    }
}

internal static class RuleSyntaxContractValidation
{
    public static IReadOnlyList<SyntaxKind> ValidateSyntaxKinds(IReadOnlyList<SyntaxKind> syntaxKinds)
    {
        ArgumentNullException.ThrowIfNull(syntaxKinds);
        if (syntaxKinds.Count == 0)
        {
            throw new ArgumentException("A syntax contract must declare at least one syntax kind.", nameof(syntaxKinds));
        }

        if (syntaxKinds.Distinct().Count() != syntaxKinds.Count)
        {
            throw new ArgumentException("A syntax contract cannot declare duplicate syntax kinds.", nameof(syntaxKinds));
        }

        return syntaxKinds.ToArray();
    }
}
