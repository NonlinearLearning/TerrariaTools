using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;
using Microsoft.CodeAnalysis;

namespace Infrastructure.Analysis.Engine.Frontend.Builders;

/// <summary>
/// 保存前端拆分后的共享构建状态。
///
/// Joern 的 `AstCreator` 会把很多上下文长期挂在对象上。
/// 这里也做同样的事，但保持最小集合，避免在 4 个 Builder 之间来回传十几个参数。
/// </summary>
internal sealed class BuilderState
{
    private int nextSyntheticMethodOrdinal;

    /// <summary>
    /// 初始化共享状态。
    /// </summary>
    public BuilderState(
        CpgGraphBuilder graphBuilder,
        RoslynCompilationContext context,
        Dictionary<SyntaxNode, long> nodeIdsBySyntax,
        HashSet<string> referencedTypeFullNames,
        List<MethodStubDefinition> externalMethodStubs)
    {
        GraphBuilder = graphBuilder ?? throw new ArgumentNullException(nameof(graphBuilder));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NodeIdsBySyntax = nodeIdsBySyntax ?? throw new ArgumentNullException(nameof(nodeIdsBySyntax));
        ReferencedTypeFullNames = referencedTypeFullNames ?? throw new ArgumentNullException(nameof(referencedTypeFullNames));
        ExternalMethodStubs = externalMethodStubs ?? throw new ArgumentNullException(nameof(externalMethodStubs));
    }

    /// <summary>
    /// 获取图构建器。
    /// </summary>
    public CpgGraphBuilder GraphBuilder { get; }

    /// <summary>
    /// 获取 Roslyn 编译上下文。
    /// </summary>
    public RoslynCompilationContext Context { get; }

    /// <summary>
    /// 获取语法节点到图节点编号的映射。
    /// </summary>
    public Dictionary<SyntaxNode, long> NodeIdsBySyntax { get; }

    /// <summary>
    /// 获取被引用到的类型全名集合。
    /// </summary>
    public HashSet<string> ReferencedTypeFullNames { get; }

    /// <summary>
    /// 获取外部方法桩定义集合。
    /// </summary>
    public List<MethodStubDefinition> ExternalMethodStubs { get; }

    /// <summary>
    /// 生成当前文件内递增的合成方法序号。
    /// </summary>
    public int AllocateSyntheticMethodOrdinal()
    {
        nextSyntheticMethodOrdinal++;
        return nextSyntheticMethodOrdinal;
    }
}
