namespace Analysis.Semantic.Validation;

/// <summary>
/// 表示一条图校验违规记录。
/// </summary>
/// <param name="Code">违规代码。</param>
/// <param name="Level">违规级别。</param>
/// <param name="Message">违规消息。</param>
public sealed record ValidationViolation(
    string Code,
    ValidationLevel Level,
    string Message);
