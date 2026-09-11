using Microsoft.CodeAnalysis;
using NLCPG.Model;
using NLISSN.Core.Lifting;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Marking;

/// <summary>
/// Retains legacy provenance for compatibility outside graph routing.
/// Structural conclusions are carried by <see cref="Lifting.LiftedMarkRecord"/>.
/// </summary>
public enum RuleOutputKind
{
    SeedMark,
    PropagatedMark,
    LiftedMark,
    LocalDefinitionFromInitializer,
    LocalDefinitionFromObjectCreation,
    LocalReference,
    ExpressionHost,
    IfStructure,
    SwitchStructure,
    DecisionUnit
}

/// 表示规则在标记阶段产出的一条直接命中记录。
public sealed record MarkRecord
{
    public MarkRecord(
      string ruleId,
      SyntaxNode syntaxNode,
      SyntaxAnnotation? annotation,
      NLCPGNode? primaryGraphNode,
      string reason,
      RuleOutputKind? OutputKind = null,
      RuleSemanticTag? SemanticTag = null,
      RuleEvidenceOrigin Origins = RuleEvidenceOrigin.None,
      RuleFactKind? FactKind = null,
      FactCapability Capability = FactCapability.Unknown,
      FactCertainty Certainty = FactCertainty.Available,
      FactProvenance? Provenance = null,
      string? SourceTreeVersion = null)
    {
        RuleId = ruleId;
        SyntaxNode = syntaxNode;
        Annotation = annotation;
        PrimaryGraphNode = primaryGraphNode;
        Reason = reason;
        this.OutputKind = OutputKind;
        _semanticTag = SemanticTag;
        this.Origins = Origins;
        _factKind = FactKind;
        this.Capability = Capability;
        this.Certainty = Certainty;
        this.Provenance = Provenance ?? FactProvenance.ForMark(ruleId);
        this.SourceTreeVersion = SourceTreeVersion ?? FactIdentity.ForSourceTree(syntaxNode);
    }

    /// 产生这条标记的规则标识。
    public string RuleId { get; init; }

    /// 规则命中的语法节点。
    public SyntaxNode SyntaxNode { get; init; }

    /// 为后续传播、决策或改写绑定到语法树上的注解。
    public SyntaxAnnotation? Annotation { get; init; }

    /// 与当前语法节点对齐的主图节点。
    public NLCPGNode? PrimaryGraphNode { get; init; }

    /// 说明本次命中的原因，供调试和结果输出使用。
    public string Reason { get; init; }

    public RuleOutputKind? OutputKind { get; init; }

    /// 兼容旧 artifact 和测试规则的显示名称；内建路由使用 FactKind。
    public RuleSemanticTag? SemanticTag
    {
        get => _semanticTag ?? (FactKind is { } knownKind
          ? RuleFactKindDescriptor.ToSemanticTag(knownKind)
          : null);
        init => _semanticTag = value;
    }

    /// 内建事实的受控身份；自定义测试事实可以只保留 SemanticTag。
    public RuleFactKind? FactKind
    {
        get => RuleFactKindDescriptor.Resolve(_factKind, _semanticTag);
        init => _factKind = value;
    }

    public RuleEvidenceOrigin Origins { get; init; }

    /// The typed capability of this observation; it is not rewrite authority.
    public FactCapability Capability { get; init; }

    /// Certainty of the observation. Unknown certainty is fail-closed downstream.
    public FactCertainty Certainty { get; init; }

    /// Structured provenance retained across stage boundaries.
    public FactProvenance? Provenance { get; init; }

    /// Identity of the source syntax tree used for this fact.
    public string SourceTreeVersion { get; init; }

    private RuleSemanticTag? _semanticTag;

    private RuleFactKind? _factKind;
}
