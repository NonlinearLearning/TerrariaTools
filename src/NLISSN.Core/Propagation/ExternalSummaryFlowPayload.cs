using NLCPG.Analysis.FlowSummaries;
using Microsoft.CodeAnalysis;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

/// <summary>
/// Carries a resolved external summary fact without authorizing a rewrite.
/// </summary>
public sealed record ExternalSummaryFlowPayload
{
    public static RuleSemanticTag SemanticTag { get; } = new("FlowSummary.External");

    public ExternalSummaryFlowPayload(
        ResolvedCallFlow flow,
        FlowSummaryEndpoint? sourceEndpoint = null,
        SyntaxNode? sourceSyntax = null,
        SyntaxNode? invocationSyntax = null,
        FlowSummaryEndpoint? targetEndpoint = null)
    {
        Flow = flow ?? throw new ArgumentNullException(nameof(flow));
        SourceEndpoint = sourceEndpoint ?? flow.Mapping?.Source;
        SourceSyntax = sourceSyntax;
        InvocationSyntax = invocationSyntax;
        TargetEndpoint = targetEndpoint ?? flow.Mapping?.Target;
    }

    public ResolvedCallFlow Flow { get; }

    /// <summary>
    /// The invocation input represented by this fact. Rejected resolutions keep
    /// this value even when the resolver could not return a mapping.
    /// </summary>
    public FlowSummaryEndpoint? SourceEndpoint { get; }

    /// <summary>
    /// The syntax node that supplies <see cref="SourceEndpoint"/> at the call site.
    /// </summary>
    public SyntaxNode? SourceSyntax { get; }

    /// <summary>
    /// The invocation that produced this fact, retained for seed protection and diagnostics.
    /// </summary>
    public SyntaxNode? InvocationSyntax { get; }

    /// <summary>
    /// The requested output endpoint. Rejected resolutions retain the requested return endpoint.
    /// </summary>
    public FlowSummaryEndpoint? TargetEndpoint { get; }

    public bool IsInputToReturn
    {
        get
        {
            var source = SourceEndpoint;
            var target = TargetEndpoint;
            return target?.Kind == FlowSummaryEndpointKind.Return &&
                source?.Kind is FlowSummaryEndpointKind.Receiver or
                    FlowSummaryEndpointKind.Parameter or
                    FlowSummaryEndpointKind.RefParameter or
                    FlowSummaryEndpointKind.OutParameter;
        }
    }

    public bool IsParameterToReturn =>
        IsInputToReturn && SourceEndpoint!.Kind == FlowSummaryEndpointKind.Parameter;

    /// <summary>
    /// Indicates that this endpoint must not be treated as an ordinary delete candidate.
    /// This remains true for unknown, mismatched, blocked, and resolved summaries.
    /// </summary>
    public bool ProtectsInput => IsInputToReturn;
}
