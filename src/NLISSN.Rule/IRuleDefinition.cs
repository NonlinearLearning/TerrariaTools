namespace NLISSN.Core.Pipeline;

public interface IRuleDefinition
{
    string CapabilityId { get; }

    string RuleId { get; }

    RuleInputCardinality InputCardinality { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }

}
