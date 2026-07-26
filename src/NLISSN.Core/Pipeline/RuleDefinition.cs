using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Contracts;
using Deletion.Core.Decision;
using Deletion.Core.Lifting;
using Deletion.Core.Marking;
using Deletion.Core.Propagation;

namespace Deletion.Rules;

public interface IRuleDefinition
{
    string CapabilityId { get; }

    string RuleId { get; }
}

public abstract class RuleDefinitionMark : IRuleDefinition
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; }

    public abstract IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}

public abstract class RuleDefinitionPropagate : IRuleDefinition
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; }

    public abstract IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks);
}

public abstract class RuleDefinitionPropose : IRuleDefinition
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; }

    public abstract IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; }

    public abstract IEnumerable<DecisionUnit> Propose(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks);
}

public abstract class RuleDefinitionLift : IRuleDefinition
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; }

    public abstract IEnumerable<LiftedMarkRecord> Lift(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks);
}
