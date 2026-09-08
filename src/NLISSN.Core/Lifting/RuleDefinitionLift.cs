using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Contracts;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Lifting;

public abstract class RuleDefinitionLift : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public abstract string RuleId { get; }

    public virtual RuleInputCardinality InputCardinality => RuleInputCardinality.All;

    public virtual RuleConsumesContract Consumes => RuleConsumesContract.Empty;

    public virtual RuleProducesContract Produces => RuleProducesContract.Empty;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; }

    // 把已有标记提升到更高层的表达式或结构宿主，生成后续决策可消费的提升结果。
    public abstract IEnumerable<LiftedMarkRecord> Lift(
      ILiftRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks);

    public virtual IEnumerable<LiftedMarkRecord> Lift(
      ILiftRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        _ = existingLiftedMarks;
        return Lift(context, seedMarks, propagatedMarks);
    }
}
