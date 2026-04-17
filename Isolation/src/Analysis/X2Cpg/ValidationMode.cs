namespace Analysis.X2Cpg;

/// <summary>
/// 控制前端是否在建图早期执行 schema 校验。
///
/// Joern 的 `ValidationMode` 用在 `X2CpgConfig` 中。当前 C# 版还没有完整
/// schema 校验器，所以这里先保留配置语义，让调用方可以稳定表达意图。
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// 不在建图阶段执行 schema 校验。
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// 在建图阶段执行 schema 校验。
    /// </summary>
    Enabled,
}
