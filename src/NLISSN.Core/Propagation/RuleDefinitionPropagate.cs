using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Contracts;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

public abstract class RuleDefinitionPropagate : IRuleDefinition
{
    public virtual IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.Default };

    public virtual string CapabilityId => RuleId;

    public abstract string RuleId { get; }

    public virtual RuleNodeId NodeId => RuleNodeId.For(RuleKind.Propagate, RuleId);

    public virtual RuleConsumesContract Consumes => RuleConsumesContract.Empty;

    public virtual RuleProducesContract Produces => RuleProducesContract.Empty;

    public virtual RuleFactDomain FactDomain => RuleFactDomain.None;

    public virtual RuleTerminalConsumesContract TerminalConsumes => RuleTerminalConsumesContract.Empty;

    public abstract string Name { get; }

    public abstract IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; }

    // 基于当前规则的种子标记执行语义传播，并返回新的传播标记。
    public abstract IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks);
}
