using Microsoft.CodeAnalysis;
using NLISSN.Core.Analysis.MethodLinkage;

namespace NLISSN.Core.Analysis;

/// 分析编译中的普通私有方法，给出候选数量和没有外部引用的最终集合。
public sealed class UnreferencedMethodAnalysis
{
    private UnreferencedMethodAnalysis(
      int candidateMethodCount,
      IReadOnlySet<IMethodSymbol> unreferencedMethods)
    {
        CandidateMethodCount = candidateMethodCount;
        UnreferencedMethods = unreferencedMethods;
    }

    public int CandidateMethodCount { get; }

    public IReadOnlySet<IMethodSymbol> UnreferencedMethods { get; }

    public static UnreferencedMethodAnalysis Create(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var linkage = MethodLinkageAnalysis.Create(compilation);
        return new UnreferencedMethodAnalysis(
          linkage.CandidateMethodCount,
          linkage.UnreferencedPrivateMethods);
    }
}
