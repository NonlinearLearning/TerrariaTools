using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace NLCPG.Analysis.FlowSummaries;

/// <summary>
/// Resolves an explicitly requested endpoint pair from a Roslyn-bound invocation.
/// </summary>
public interface ICallFlowResolver
{
    ResolvedCallFlow Resolve(
        IInvocationOperation invocation,
        FlowSummaryEndpoint source,
        FlowSummaryEndpoint target);

    IReadOnlyList<ResolvedCallFlow> ResolveAll(IInvocationOperation invocation);
}

public sealed class CallFlowResolver : ICallFlowResolver
{
    private readonly NLCPGFlowSummaryRegistry _registry;

    public CallFlowResolver(NLCPGFlowSummaryRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ResolvedCallFlow Resolve(
        IInvocationOperation invocation,
        FlowSummaryEndpoint source,
        FlowSummaryEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var methodKey = FlowSummaryMethodKey.From(invocation.TargetMethod);
        var lookup = _registry.Resolve(methodKey);
        if (lookup.Resolution == FlowSummaryResolution.Unknown)
        {
            return new ResolvedCallFlow(
                ResolvedCallFlowStatus.Unknown,
                lookup.Resolution,
                methodKey,
                null,
                "No flow summary matches the bound invocation method.");
        }

        if (!IsCompatibleWithMethod(invocation.TargetMethod, source) ||
            !IsCompatibleWithMethod(invocation.TargetMethod, target))
        {
            return new ResolvedCallFlow(
                ResolvedCallFlowStatus.SignatureMismatch,
                lookup.Resolution,
                methodKey,
                null,
                "The requested endpoint pair is incompatible with the bound invocation signature.");
        }

        var mapping = lookup.Mappings.FirstOrDefault(candidate =>
            candidate.Source == source && candidate.Target == target);
        if (mapping is null)
        {
            return new ResolvedCallFlow(
                ResolvedCallFlowStatus.SignatureMismatch,
                lookup.Resolution,
                methodKey,
                null,
                "The matched flow summary does not declare the requested endpoint pair.");
        }

        var status = mapping.Kind == FlowSummaryMappingKind.Block
            ? ResolvedCallFlowStatus.Blocked
            : ResolvedCallFlowStatus.Resolved;
        return new ResolvedCallFlow(status, lookup.Resolution, methodKey, mapping, null);
    }

    public IReadOnlyList<ResolvedCallFlow> ResolveAll(IInvocationOperation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var methodKey = FlowSummaryMethodKey.From(invocation.TargetMethod);
        var lookup = _registry.Resolve(methodKey);
        if (lookup.Resolution == FlowSummaryResolution.Unknown)
        {
            return new[] { new ResolvedCallFlow(ResolvedCallFlowStatus.Unknown, lookup.Resolution, methodKey, null, "No flow summary matches the bound invocation method.") };
        }

        return lookup.Mappings
          .Select(mapping => Resolve(invocation, mapping.Source, mapping.Target))
          .ToArray();
    }

    private static bool IsCompatibleWithMethod(IMethodSymbol method, FlowSummaryEndpoint endpoint)
    {
        var normalized = method.ReducedFrom ?? method;
        if (endpoint.Kind is FlowSummaryEndpointKind.Receiver or FlowSummaryEndpointKind.Return)
        {
            return endpoint.ParameterOrdinal == -1;
        }

        if (endpoint.ParameterOrdinal < 0 || endpoint.ParameterOrdinal >= normalized.Parameters.Length)
        {
            return false;
        }

        var refKind = normalized.Parameters[endpoint.ParameterOrdinal].RefKind;
        return endpoint.Kind switch
        {
            FlowSummaryEndpointKind.Parameter => refKind == RefKind.None,
            FlowSummaryEndpointKind.RefParameter => refKind == RefKind.Ref,
            FlowSummaryEndpointKind.OutParameter => refKind == RefKind.Out,
            _ => false,
        };
    }
}
