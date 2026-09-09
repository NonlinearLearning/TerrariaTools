using Microsoft.CodeAnalysis;

namespace NLCPG.Analysis.FlowSummaries;

/// 保存内置摘要以及从当前 Compilation 解析框架符号的声明式目录。
public static class NLCPGDefaultFlowSummaries
{
    private static readonly IReadOnlyList<FrameworkFlowSummaryDeclaration> FrameworkDeclarations =
        new FrameworkFlowSummaryDeclaration[]
        {
            new(
                "System.Object",
                "ToString",
                GenericArity: 0,
                Array.Empty<FrameworkParameterShape>(),
                new[]
                {
                    new FlowSummaryMapping(
                        FlowSummaryEndpoint.Receiver,
                        FlowSummaryEndpoint.Return,
                        FlowSummaryMappingKind.Explicit),
                }),
        };

    // 保留旧版按简化稳定键查询的兼容目录；新版框架摘要从 Compilation 动态构造。
    public static IReadOnlyList<NLCPGFlowSummary> All { get; } = Array.Empty<NLCPGFlowSummary>();

    /// <summary>
    /// Builds framework summaries from symbols owned by the supplied compilation.
    /// </summary>
    public static IReadOnlyList<FlowSummary> CreateForCompilation(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        return FrameworkDeclarations
            .Select(declaration => declaration.TryCreate(compilation))
            .Where(summary => summary is not null)
            .Cast<FlowSummary>()
            .OrderBy(summary => summary.MethodKey.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    // 按稳定键查找内置跨过程流摘要。
    public static bool TryGet(string stableKey, out NLCPGFlowSummary? summary)
    {
        summary = All.FirstOrDefault(candidate => string.Equals(
          candidate.StableKey,
          stableKey,
          StringComparison.Ordinal));
        return summary is not null;
    }

    private sealed record FrameworkParameterShape(string? Name, string? TypeShape);

    private sealed record FrameworkFlowSummaryDeclaration(
        string ContainingMetadataName,
        string MethodName,
        int GenericArity,
        IReadOnlyList<FrameworkParameterShape> Parameters,
        IReadOnlyList<FlowSummaryMapping> Mappings)
    {
        public FlowSummary? TryCreate(Compilation compilation)
        {
            var containingType = compilation.GetTypeByMetadataName(ContainingMetadataName);
            if (containingType is null)
            {
                return null;
            }

            var candidates = containingType.GetMembers(MethodName)
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary)
                .Where(method => method.Arity == GenericArity)
                .Where(method => method.Parameters.Length == Parameters.Count)
                .Where(MatchesParameters)
                .Select(method => method.OriginalDefinition)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .ToArray();
            if (candidates.Length != 1)
            {
                return null;
            }

            return new FlowSummary(
                FlowSummaryMethodKey.From(candidates[0]),
                Mappings,
                candidates[0].ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        private bool MatchesParameters(IMethodSymbol method)
        {
            for (var index = 0; index < Parameters.Count; index++)
            {
                var expected = Parameters[index];
                var actual = method.Parameters[index];
                if (expected.Name is not null &&
                    !string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
                {
                    return false;
                }

                if (expected.TypeShape is not null &&
                    !string.Equals(
                        expected.TypeShape,
                        actual.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
