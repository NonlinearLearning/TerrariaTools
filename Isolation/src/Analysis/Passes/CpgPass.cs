using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 定义所有 CPG pass 的统一契约。
///
/// pass 的职责不是“重新建一张图”，而是在当前图上补充缺失事实。
/// 这和 Joern 的高层分工是一致的：
/// - 阶段一 pass 补结构完整性；
/// - 阶段二 pass 补语义完整性。
/// </summary>
public abstract class CpgPass
{
    /// <summary>
    /// 在给定图上执行 pass。
    /// </summary>
    /// <param name="graph">要补充事实的图。</param>
    public void Run(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Execute(new CpgGraphBuilder(graph));
    }

    /// <summary>
    /// 执行真正的 pass 逻辑。
    ///
    /// 子类统一通过 builder 修改图，而不是直接操作图本身，
    /// 这样后续如果切换成差量构建模型，抽象层不用重写。
    /// </summary>
    /// <param name="builder">pass 使用的图构建器。</param>
    protected abstract void Execute(CpgGraphBuilder builder);
}
