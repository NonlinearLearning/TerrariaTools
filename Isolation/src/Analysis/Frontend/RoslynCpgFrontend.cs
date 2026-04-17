using Analysis.Core;

namespace Analysis.Frontend;

/// <summary>
/// 表示当前项目里的 Roslyn CPG 前端。
///
/// 这里先不直接依赖 Roslyn 包，而是接收一个抽象的
/// <see cref="RoslynAstToCpgBuilder"/>。
/// 这样做的原因很直接：
/// - 先把主流程边界固定下来；
/// - 再逐步把真实 Roslyn 语义接进来；
/// - 避免在“主流程还没定”的时候就把外部依赖耦合死。
/// </summary>
public sealed class RoslynCpgFrontend : CpgFrontendBase
{
    /// <summary>
    /// 使用默认 Builder 初始化前端。
    /// </summary>
    public RoslynCpgFrontend()
        : this(new DefaultRoslynCpgBuilder())
    {
    }

    /// <summary>
    /// 使用一个构建器初始化前端。
    /// </summary>
    /// <param name="builder">负责把 Roslyn 事实投影到 CPG 的构建器。</param>
    public RoslynCpgFrontend(RoslynAstToCpgBuilder builder, RoslynProjectLoader? projectLoader = null)
    {
        Builder = builder ?? throw new ArgumentNullException(nameof(builder));
        ProjectLoader = projectLoader ?? new RoslynProjectLoader();
    }

    /// <summary>
    /// 获取当前前端使用的 Roslyn 构建器。
    /// </summary>
    public RoslynAstToCpgBuilder Builder { get; }

    /// <summary>
    /// 获取当前前端使用的项目加载器。
    /// </summary>
    public RoslynProjectLoader ProjectLoader { get; }

    /// <inheritdoc />
    protected override void BuildGraphCore(CpgGraph graph, CpgFrontendOptions options)
    {
        RoslynCompilationContext context = ProjectLoader
            .LoadAsync(options.InputPath)
            .GetAwaiter()
            .GetResult();

        Builder.Build(graph, context);
    }
}
