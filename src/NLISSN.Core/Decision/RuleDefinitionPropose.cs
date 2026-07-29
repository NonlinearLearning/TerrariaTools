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

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Propose, RuleId);

    public virtual RuleConsumesContract Consumes => RuleConsumesContract.Empty;

    public virtual RuleProducesContract Produces => RuleProducesContract.Empty;

    public virtual RuleFactDomain FactDomain => RuleFactDomain.None;

    public virtual RuleTerminalConsumesContract TerminalConsumes => RuleTerminalConsumesContract.Empty;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; }

    public abstract IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; }

    // 消费种子、传播和提升结果，提出供决策引擎收口的候选决策单元。
    public abstract IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks);
}
