using Microsoft.CodeAnalysis;

namespace NLCPG.Analysis.FlowSummaries;

/// <summary>
/// Identifies a method summary independently of source spelling at a call site.
/// </summary>
public sealed record FlowSummaryMethodKey(
    string AssemblyIdentity,
    string ContainingMetadataName,
    string MethodName,
    int GenericArity,
    int ParameterCount,
    IReadOnlyList<string> ParameterNames,
    IReadOnlyList<string> ParameterTypeShapes)
{
    public string StableKey => string.Join(
        "|",
        AssemblyIdentity,
        ContainingMetadataName,
        MethodName,
        GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ParameterCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string.Join(",", ParameterNames),
        string.Join(",", ParameterTypeShapes));

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AssemblyIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(ContainingMetadataName);
        ArgumentException.ThrowIfNullOrWhiteSpace(MethodName);
        if (GenericArity < 0 || ParameterCount < 0 ||
            ParameterNames.Count != ParameterCount ||
            ParameterTypeShapes.Count != ParameterCount)
        {
            throw new ArgumentException($"Flow summary method key '{StableKey}' has an invalid signature shape.");
        }
    }

    public static FlowSummaryMethodKey From(IMethodSymbol method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var normalized = (method.ReducedFrom ?? method).OriginalDefinition;
        var parameters = normalized.Parameters;
        return new FlowSummaryMethodKey(
            normalized.ContainingAssembly.Identity.ToString(),
            GetMetadataName(normalized.ContainingType),
            normalized.MetadataName,
            normalized.Arity,
            parameters.Length,
            parameters.Select(parameter => parameter.Name).ToArray(),
            parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray());
    }

    private static string GetMetadataName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return string.Empty;
        }

        return type.ContainingType is null
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.MetadataName}"
            : $"{GetMetadataName(type.ContainingType)}+{type.MetadataName}";
    }
}

/// <summary>
/// Identifies a method boundary endpoint used by a flow mapping.
/// </summary>
public enum FlowSummaryEndpointKind
{
    Receiver,
    Parameter,
    Return,
    RefParameter,
    OutParameter,
}

public sealed record FlowSummaryEndpoint(FlowSummaryEndpointKind Kind, int ParameterOrdinal = -1)
{
    public static FlowSummaryEndpoint Receiver { get; } = new(FlowSummaryEndpointKind.Receiver);

    public static FlowSummaryEndpoint Return { get; } = new(FlowSummaryEndpointKind.Return);

    public static FlowSummaryEndpoint Parameter(int ordinal) => new(FlowSummaryEndpointKind.Parameter, ordinal);

    public static FlowSummaryEndpoint RefParameter(int ordinal) => new(FlowSummaryEndpointKind.RefParameter, ordinal);

    public static FlowSummaryEndpoint OutParameter(int ordinal) => new(FlowSummaryEndpointKind.OutParameter, ordinal);

    internal void Validate(FlowSummaryMethodKey methodKey)
    {
        if (Kind is FlowSummaryEndpointKind.Parameter or FlowSummaryEndpointKind.RefParameter or FlowSummaryEndpointKind.OutParameter)
        {
            if (ParameterOrdinal < 0 || ParameterOrdinal >= methodKey.ParameterCount)
            {
                throw new InvalidOperationException(
                    $"Flow summary method key '{methodKey.StableKey}' has an invalid endpoint ordinal '{ParameterOrdinal}'.");
            }

            return;
        }

        if (ParameterOrdinal != -1)
        {
            throw new InvalidOperationException(
                $"Flow summary method key '{methodKey.StableKey}' has a non-parameter endpoint with ordinal '{ParameterOrdinal}'.");
        }
    }
}

/// <summary>
/// Defines how a proven endpoint pair is interpreted.
/// </summary>
public enum FlowSummaryMappingKind
{
    Explicit,
    PassThrough,
    Block,
}

public sealed record FlowSummaryMapping(
    FlowSummaryEndpoint Source,
    FlowSummaryEndpoint Target,
    FlowSummaryMappingKind Kind);

/// <summary>
/// Holds all ordered endpoint mappings for one external method.
/// </summary>
public sealed record FlowSummary(FlowSummaryMethodKey MethodKey, IReadOnlyList<FlowSummaryMapping> Mappings);

public enum FlowSummaryResolution
{
    Project,
    User,
    Framework,
    Unknown,
}

public sealed record FlowSummaryLookupResult(FlowSummaryResolution Resolution, FlowSummary? Summary)
{
    public static FlowSummaryLookupResult Unknown { get; } = new(FlowSummaryResolution.Unknown, null);

    public IReadOnlyList<FlowSummaryMapping> Mappings => Summary?.Mappings ?? Array.Empty<FlowSummaryMapping>();
}

public enum ResolvedCallFlowStatus
{
    Resolved,
    Unknown,
    SignatureMismatch,
    Blocked,
}

public sealed record ResolvedCallFlow(
    ResolvedCallFlowStatus Status,
    FlowSummaryResolution Resolution,
    FlowSummaryMethodKey MethodKey,
    FlowSummaryMapping? Mapping,
    string? RejectionReason)
{
    public bool IsResolved => Status == ResolvedCallFlowStatus.Resolved;
}
