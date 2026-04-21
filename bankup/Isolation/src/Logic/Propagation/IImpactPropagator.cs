namespace Logic.Propagation;

/// <summary>
/// 定义传播构造能力。
/// </summary>
public interface IImpactPropagator
{
    /// <summary>
    /// 构造传播结果。
    /// </summary>
    /// <param name="input">传播输入。</param>
    /// <returns>传播结果。</returns>
    PropagationResolution Propagate(PropagationBuildInput input);
}
