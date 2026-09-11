using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Contracts;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;

namespace NLISSN.Rules;

public interface IRuleDefinition
{
    string CapabilityId { get; }

    string RuleId { get; }

    RuleNodeId NodeId { get; }

    IReadOnlyList<RuleDependency> Dependencies { get; }

    IReadOnlyList<RuleOutputKind> ProducedOutputs { get; }
}

public abstract class RuleDefinitionMark : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Mark, RuleId);

    public virtual IReadOnlyList<RuleDependency> Dependencies =>
      RuleGraphDependencyCatalog.GetDependencies(this, RuleKind.Mark, Array.Empty<RuleDependency>());

    public virtual IReadOnlyList<RuleOutputKind> ProducedOutputs => new[] { RuleOutputKind.SeedMark };

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; }

    // 在当前 mark 区域内产出规则直接命中的原子种子标记。
    public abstract IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}

public abstract class RuleDefinitionPropagate : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Propagate, RuleId);

    public virtual IReadOnlyList<RuleDependency> Dependencies =>
      RuleGraphDependencyCatalog.GetDependencies(this, RuleKind.Propagate, Array.Empty<RuleDependency>());

    public virtual IReadOnlyList<RuleOutputKind> ProducedOutputs => new[] { RuleOutputKind.PropagatedMark };

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; }

    // 基于当前规则的种子标记执行语义传播，并返回新的传播标记。
    public abstract IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks);
}

public abstract class RuleDefinitionPropose : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Propose, RuleId);

    public virtual IReadOnlyList<RuleDependency> Dependencies =>
      RuleGraphDependencyCatalog.GetDependencies(this, RuleKind.Propose, Array.Empty<RuleDependency>());

    public virtual IReadOnlyList<RuleOutputKind> ProducedOutputs => new[] { RuleOutputKind.DecisionUnit };

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; }

    public abstract IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; }

    /// <summary>
    /// Declares the verifier contract for structural transformations produced by this rule.
    /// </summary>
    public virtual RuleTransformationContract? TransformationContract => null;

    // 消费种子、传播和提升结果，提出供决策引擎收口的候选决策单元。
    public abstract IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks);
}

public abstract class RuleDefinitionLift : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Lift, RuleId);

    public virtual IReadOnlyList<RuleDependency> Dependencies =>
      RuleGraphDependencyCatalog.GetDependencies(this, RuleKind.Lift, Array.Empty<RuleDependency>());

    public virtual IReadOnlyList<RuleOutputKind> ProducedOutputs => new[] { RuleOutputKind.LiftedMark };

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; }

    // 把已有标记提升到更高层的表达式或结构宿主，生成后续决策可消费的提升结果。
    public abstract IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks);

    public virtual IEnumerable<LiftedMarkRecord> Lift(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        _ = existingLiftedMarks;
        return Lift(context, seedMarks, propagatedMarks);
    }
}
