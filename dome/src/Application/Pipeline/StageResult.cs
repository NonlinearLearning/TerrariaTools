using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 封装阶段型辅助服务的执行结果。
/// </summary>
/// <typeparam name="T">成功时返回的值类型。</typeparam>
/// <param name="IsSuccess">指示阶段是否成功。</param>
/// <param name="Value">成功时的返回值。</param>
/// <param name="FailureCode">失败时的失败代码。</param>
/// <param name="Message">失败消息。</param>
public sealed record StageResult<T>(
    bool IsSuccess,
    T? Value,
    ModelPrimitives.FailureCode FailureCode,
    string? Message)
{
    /// <summary>
    /// 创建一个成功的阶段结果。
    /// </summary>
    /// <param name="value">阶段返回值。</param>
    /// <returns>表示成功的阶段结果。</returns>
    public static StageResult<T> Success(T value) => new(true, value, ModelPrimitives.FailureCode.None, null);

    /// <summary>
    /// 创建一个失败的阶段结果。
    /// </summary>
    /// <param name="failureCode">失败代码。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>表示失败的阶段结果。</returns>
    public static StageResult<T> Failure(ModelPrimitives.FailureCode failureCode, string message) => new(false, default, failureCode, message);
}
