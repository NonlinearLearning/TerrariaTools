using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Contracts;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Decision;

public abstract class RuleDefinitionPropose : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public abstract string RuleId { get; }

    public virtual RuleInputCardinality InputCardinality => RuleInputCardinality.All;

    public virtual RuleConsumesContract Consumes => RuleConsumesContract.Empty;

    public virtual RuleProducesContract Produces => RuleProducesContract.Empty;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; }

    public abstract IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; }

    // 消费种子、传播和提升结果，提出供决策引擎收口的候选决策单元。
    public abstract IEnumerable<DecisionUnit> Propose(
      IProposeRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks);
}
