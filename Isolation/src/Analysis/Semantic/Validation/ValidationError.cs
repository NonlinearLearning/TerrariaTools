namespace Analysis.Semantic.Validation;

/// <summary>
/// 表示图校验失败时抛出的异常。
/// </summary>
public sealed class ValidationError : Exception
{
    /// <summary>
    /// 初始化图校验异常。
    /// </summary>
    /// <param name="message">异常消息。</param>
    public ValidationError(string message)
        : base(message)
    {
    }
}
