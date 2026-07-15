using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Contracts;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public abstract class RuleDefinitionMark
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; }

    public abstract IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}

public abstract class RuleDefinitionPropagate
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; }

    public abstract IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks);
}

public abstract class RuleDefinitionPropose
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

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

public abstract class RuleDefinitionLift
{
    public virtual IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
        new[] { RoslynCpgCapability.Default };

    public abstract string RuleId { get; }

    public virtual string GroupKey => RuleId;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; }

    public abstract IEnumerable<LiftedMarkRecord> Lift(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks);
}
