namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;

// 构建影子提取使用的种子驱动可达性切片，
// 同时避免主分析结果契约变得臃肿。
public sealed class SeedClosureAnalyzer : ApplicationAbstractions.ISeedClosureAnalyzer
{
    public ApplicationAbstractions.SeedClosureAnalysisResult Analyze(
        CoreAnalysis.AnalysisOutput analysis,
        string seedMemberName,
        ApplicationAbstractions.SeedClosureAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = analysis.CreateContext();
        var seedNode = ResolveSeedNode(context.FunctionIndex, seedMemberName);
        var reachableMethods = context.MethodCalls
            .GetReachableMethods([seedNode.MemberId])
            .Distinct(new MemberIdEqualityComparer())
            .OrderBy(static memberId => memberId.Value, StringComparer.Ordinal)
            .ToArray();

        var includedDocuments = CollectIncludedDocuments(context.FunctionIndex, reachableMethods, seedNode)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var memberIdsByDocument = BuildMemberIdsByDocument(context.FunctionIndex, reachableMethods, includedDocuments);
        var symbolClosureDocumentCount = includedDocuments.Length;

        return new ApplicationAbstractions.SeedClosureAnalysisResult(
            seedNode,
            includedDocuments,
            reachableMethods,
            memberIdsByDocument,
            symbolClosureDocumentCount);
    }

    private static CoreAnalysis.FunctionNodeRef ResolveSeedNode(CoreAnalysis.FunctionIndex functionIndex, string seedMemberName)
    {
        var normalizedSeed = NormalizeSeed(seedMemberName);
        var nodes = functionIndex.NodesByMemberId.Values;

        var exactMemberMatch = nodes.FirstOrDefault(node =>
            string.Equals(node.MemberId.Value, seedMemberName, StringComparison.Ordinal) ||
            string.Equals(node.MemberId.Value, $"{seedMemberName}()", StringComparison.Ordinal));
        if (exactMemberMatch != null)
        {
            return exactMemberMatch;
        }

        var normalizedMemberMatch = nodes.FirstOrDefault(node =>
            string.Equals(NormalizeSeed(node.MemberId.Value), normalizedSeed, StringComparison.Ordinal) ||
            string.Equals(NormalizeSeed(node.DisplayName), normalizedSeed, StringComparison.Ordinal));
        if (normalizedMemberMatch != null)
        {
            return normalizedMemberMatch;
        }

        var suffixMatch = nodes.FirstOrDefault(node =>
            NormalizeSeed(node.MemberId.Value).EndsWith($".{normalizedSeed}", StringComparison.Ordinal) ||
            NormalizeSeed(node.DisplayName).EndsWith($".{normalizedSeed}", StringComparison.Ordinal));
        if (suffixMatch != null)
        {
            return suffixMatch;
        }

        throw new InvalidOperationException($"Seed member '{seedMemberName}' was not found in the analysis result.");
    }

    private static IEnumerable<string> CollectIncludedDocuments(
        CoreAnalysis.FunctionIndex functionIndex,
        IReadOnlyList<CoreCommon.MemberId> reachableMethods,
        CoreAnalysis.FunctionNodeRef seedNode)
    {
        var includedDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        includedDocuments.Add(seedNode.DocumentPath);

        foreach (var memberId in reachableMethods)
        {
            if (functionIndex.NodesByMemberId.TryGetValue(memberId.Value, out var node))
            {
                includedDocuments.Add(node.DocumentPath);
            }
        }

        return includedDocuments;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildMemberIdsByDocument(
        CoreAnalysis.FunctionIndex functionIndex,
        IReadOnlyList<CoreCommon.MemberId> reachableMethods,
        IReadOnlyCollection<string> includedDocuments)
    {
        var includedDocumentSet = new HashSet<string>(includedDocuments, StringComparer.OrdinalIgnoreCase);
        var memberIdsByDocument = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var memberId in reachableMethods)
        {
            if (!functionIndex.NodesByMemberId.TryGetValue(memberId.Value, out var node) ||
                !includedDocumentSet.Contains(node.DocumentPath))
            {
                continue;
            }

            if (!memberIdsByDocument.TryGetValue(node.DocumentPath, out var members))
            {
                members = new HashSet<string>(StringComparer.Ordinal);
                memberIdsByDocument[node.DocumentPath] = members;
            }

            members.Add(memberId.Value);
        }

        return memberIdsByDocument.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<string>)new HashSet<string>(pair.Value, StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSeed(string value)
    {
        var trimmed = value.Trim();
        var parenIndex = trimmed.IndexOf('(');
        return parenIndex >= 0 ? trimmed[..parenIndex] : trimmed;
    }

    private sealed class MemberIdEqualityComparer : IEqualityComparer<CoreCommon.MemberId>
    {
        public bool Equals(CoreCommon.MemberId x, CoreCommon.MemberId y) =>
            StringComparer.Ordinal.Equals(x.Value, y.Value);

        public int GetHashCode(CoreCommon.MemberId obj) =>
            StringComparer.Ordinal.GetHashCode(obj.Value);
    }
}




