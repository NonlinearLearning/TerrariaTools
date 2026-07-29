namespace NLISSN.Core.Pipeline;

public interface IRuleDefinition
{
    string CapabilityId { get; }

    string RuleId { get; }

    RuleNodeId NodeId { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }

    RuleFactDomain FactDomain { get; }

    RuleTerminalConsumesContract TerminalConsumes { get; }
}
