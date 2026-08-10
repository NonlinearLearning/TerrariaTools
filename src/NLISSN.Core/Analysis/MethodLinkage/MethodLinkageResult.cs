using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Analysis.MethodLinkage;

/// 编译级方法关联分析的不可变删除候选结果。
public sealed class MethodLinkageResult
{
    internal MethodLinkageResult(
      bool hasErrors,
      int candidateMethodCount,
      IReadOnlySet<IMethodSymbol> unreachableMethods,
      IReadOnlySet<IMethodSymbol> unreferencedPrivateMethods)
    {
        HasErrors = hasErrors;
        CandidateMethodCount = candidateMethodCount;
        UnreachableMethods = unreachableMethods;
        UnreferencedPrivateMethods = unreferencedPrivateMethods;
    }

    public bool HasErrors { get; }

    public int CandidateMethodCount { get; }

    public IReadOnlySet<IMethodSymbol> UnreachableMethods { get; }

    public IReadOnlySet<IMethodSymbol> UnreferencedPrivateMethods { get; }
}
