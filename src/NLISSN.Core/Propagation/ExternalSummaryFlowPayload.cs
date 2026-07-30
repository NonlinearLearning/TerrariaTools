using NLCPG.Analysis.FlowSummaries;

namespace NLISSN.Core.Propagation;

/// <summary>
/// Carries a resolved external summary fact without authorizing a rewrite.
/// </summary>
public sealed record ExternalSummaryFlowPayload(ResolvedCallFlow Flow)
{
    public bool IsParameterToReturn =>
        Flow.Mapping?.Source.Kind == FlowSummaryEndpointKind.Parameter &&
        Flow.Mapping.Target.Kind == FlowSummaryEndpointKind.Return;
}
