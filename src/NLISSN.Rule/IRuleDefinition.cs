namespace NLISSN.Core.Pipeline;

public interface IRuleDefinition
{
    string CapabilityId { get; }

    string RuleId { get; }

    RuleNodeId NodeId { get; }

    RuleInputCardinality InputCardinality { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }

}
