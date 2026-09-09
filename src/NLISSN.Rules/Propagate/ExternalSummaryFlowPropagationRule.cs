using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Contracts;
using NLCPG.Analysis.FlowSummaries;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// <summary>
/// Turns call-site flow summary lookups into provenance facts without expanding
/// the target method or treating a summary as rewrite authorization.
/// </summary>
[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class ExternalSummaryFlowPropagationRule : RuleDefinitionPropagate
{
    public static readonly IReadOnlyList<SyntaxKind> SourceNodeKinds =
        Enum.GetValues<SyntaxKind>()
            .Where(kind => kind != SyntaxKind.None)
            .Distinct()
            .ToArray();

    private static readonly RuleConsumesContract TargetInvocationConsumes = new(new[]
    {
        new RuleConsumedSyntax(
            ExpressionFlowPropagationRuleBase.TargetExpressionNodeKinds,
            RuleFactKind.TargetExpression),
    });

    private static readonly RuleProducesContract ExternalSummaryProduces = new(new[]
    {
        new RuleProducedSyntax(SourceNodeKinds, ExternalSummaryFlowPayload.SemanticTag),
    });

    public override string RuleId { get; } = "propagate.external-summary-flow";

    public override string Name { get; } = "Record external call flow summary provenance";

    public override IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
        new[] { NLCPGCapability.InterproceduralDataFlow };

    public override RuleConsumesContract Consumes => TargetInvocationConsumes;

    public override RuleProducesContract Produces => ExternalSummaryProduces;

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds => SourceNodeKinds;

    public override IEnumerable<PropagatedMarkRecord> Propagate(
        IPropagationRuleContext context,
        IReadOnlyList<MarkRecord> seedMarks)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks
            .Where(mark => mark.SyntaxNode is InvocationExpressionSyntax)
            .OrderBy(mark => mark.SyntaxNode.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(mark => mark.SyntaxNode.SpanStart)
            .ThenBy(mark => mark.SyntaxNode.Span.Length)
            .ThenBy(mark => mark.RuleId, StringComparer.Ordinal))
        {
            if (context.SemanticModel.GetOperation(seedMark.SyntaxNode) is not IInvocationOperation invocation)
            {
                continue;
            }

            foreach (var endpoint in EnumerateInputEndpoints(invocation))
            {
                var flow = context.ResolveCallFlow(
                    invocation,
                    endpoint.Endpoint,
                    FlowSummaryEndpoint.Return);
                flow = NormalizeResolution(flow, endpoint.Endpoint);

                var key = BuildEmissionKey(invocation, endpoint, flow);
                if (!emitted.Add(key))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                    RuleId,
                    MarkRecordFactory.Create(
                        RuleId,
                        endpoint.Syntax,
                        BuildReason(flow, endpoint.Endpoint),
                        semanticTag: ExternalSummaryFlowPayload.SemanticTag),
                    seedMark,
                    1,
                    new ExternalSummaryFlowPayload(
                        flow,
                        endpoint.Endpoint,
                        endpoint.Syntax,
                        invocation.Syntax,
                        FlowSummaryEndpoint.Return));
            }
        }
    }

    private static IReadOnlyList<SummaryInputEndpoint> EnumerateInputEndpoints(
        IInvocationOperation invocation)
    {
        var endpoints = new List<SummaryInputEndpoint>();
        if (invocation.Instance is not null)
        {
            endpoints.Add(new SummaryInputEndpoint(
                FlowSummaryEndpoint.Receiver,
                invocation.Instance.Syntax));
        }

        endpoints.AddRange(invocation.Arguments
            .Where(argument => argument.Parameter is not null)
            .Select(argument => new SummaryInputEndpoint(
                CreateParameterEndpoint(argument.Parameter!),
                argument.Value.Syntax)));

        return endpoints
            .OrderBy(endpoint => EndpointOrder(endpoint.Endpoint.Kind))
            .ThenBy(endpoint => endpoint.Endpoint.ParameterOrdinal)
            .ThenBy(endpoint => endpoint.Syntax.SpanStart)
            .ThenBy(endpoint => endpoint.Syntax.Span.Length)
            .ThenBy(endpoint => endpoint.Syntax.RawKind)
            .ToArray();
    }

    private static FlowSummaryEndpoint CreateParameterEndpoint(IParameterSymbol parameter)
    {
        return parameter.RefKind switch
        {
            RefKind.Ref => FlowSummaryEndpoint.RefParameter(parameter.Ordinal),
            RefKind.Out => FlowSummaryEndpoint.OutParameter(parameter.Ordinal),
            _ => FlowSummaryEndpoint.Parameter(parameter.Ordinal),
        };
    }

    private static int EndpointOrder(FlowSummaryEndpointKind kind) => kind switch
    {
        FlowSummaryEndpointKind.Receiver => 0,
        FlowSummaryEndpointKind.Parameter => 1,
        FlowSummaryEndpointKind.RefParameter => 2,
        FlowSummaryEndpointKind.OutParameter => 3,
        _ => 4,
    };

    private static ResolvedCallFlow NormalizeResolution(
        ResolvedCallFlow flow,
        FlowSummaryEndpoint requestedSource)
    {
        if (flow.Status != ResolvedCallFlowStatus.Resolved ||
            flow.Mapping is { Source: var source, Target: var target } &&
            source == requestedSource && target == FlowSummaryEndpoint.Return)
        {
            return flow;
        }

        return flow with
        {
            Status = flow.Status == ResolvedCallFlowStatus.Resolved
                ? ResolvedCallFlowStatus.SignatureMismatch
                : flow.Status,
            Mapping = flow.Status == ResolvedCallFlowStatus.Resolved
                ? null
                : flow.Mapping,
            RejectionReason = flow.Status == ResolvedCallFlowStatus.Resolved
                ? "The resolver returned a mapping different from the requested input-to-return endpoint pair."
                : flow.RejectionReason,
        };
    }

    private static string BuildEmissionKey(
        IInvocationOperation invocation,
        SummaryInputEndpoint endpoint,
        ResolvedCallFlow flow)
    {
        return string.Join(
            "|",
            invocation.Syntax.SpanStart,
            endpoint.Syntax.SpanStart,
            endpoint.Syntax.Span.Length,
            endpoint.Endpoint.Kind,
            endpoint.Endpoint.ParameterOrdinal,
            flow.Status,
            flow.MethodKey.StableKey,
            flow.Mapping?.Kind,
            flow.Mapping?.Source,
            flow.Mapping?.Target);
    }

    private static string BuildReason(
        ResolvedCallFlow flow,
        FlowSummaryEndpoint source)
    {
        return flow.IsResolved
            ? $"External flow summary resolved {source} to {FlowSummaryEndpoint.Return}."
            : $"External flow summary for {source} was retained as protected evidence: " +
              (flow.RejectionReason ?? flow.Status.ToString());
    }

    private sealed record SummaryInputEndpoint(
        FlowSummaryEndpoint Endpoint,
        SyntaxNode Syntax);
}
