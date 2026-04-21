using Domain.Analysis.Engine.Core;

namespace Infrastructure.Analysis.Engine.Frontend;

/// <summary>
/// 定义 Roslyn 到 CPG 的最小构建器抽象。
///
/// 当前仓库还没有真正接入 Roslyn，所以这里先落抽象而不引入外部包依赖。
/// 这样做的目的不是偷懒，而是把阶段一、二的核心边界先固定下来：
/// - 前端负责准备编译上下文；
/// - Builder 负责把语法/语义事实投影成图节点与图边。
/// </summary>
public abstract class RoslynAstToCpgBuilder
{
    /// <summary>
    /// 将上层准备好的源码事实写入图中。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public abstract void Build(CpgGraph graph);

    /// <summary>
    /// 将带语义上下文的源码事实写入图中。
    ///
    /// 这里保留一个默认实现，是为了兼容当前仓库里还未升级的构建器。
    /// 老构建器仍然可以只实现 <see cref="Build(CpgGraph)"/>；
    /// 新构建器则可以按需覆盖本方法，消费真实 Roslyn 语义。
    /// </summary>
    /// <param name="graph">目标图。</param>
    /// <param name="context">当前项目的 Roslyn 编译上下文。</param>
    public virtual void Build(CpgGraph graph, RoslynCompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Build(graph);
    }
}
