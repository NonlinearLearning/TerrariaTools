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

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Mark, RuleId);

    public virtual RuleConsumesContract Consumes => RuleConsumesContract.Empty;

    public virtual RuleProducesContract Produces => RuleProducesContract.Empty;

    public virtual RuleFactDomain FactDomain => RuleFactDomain.None;

    public virtual RuleTerminalConsumesContract TerminalConsumes => RuleTerminalConsumesContract.Empty;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; }

    // 在当前 mark 区域内产出规则直接命中的原子种子标记。
    public abstract IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}
