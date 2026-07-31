using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Contracts;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Marking;

public abstract class RuleDefinitionMark : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleInputCardinality InputCardinality => RuleInputCardinality.All;

    public virtual RuleConsumesContract Consumes => RuleConsumesContract.Empty;

    public virtual RuleProducesContract Produces => RuleProducesContract.Empty;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; }

    // 在当前 mark 区域内产出规则直接命中的原子种子标记。
    public abstract IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root);
}
