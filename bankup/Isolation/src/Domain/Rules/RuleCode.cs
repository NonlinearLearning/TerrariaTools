namespace Domain.Rules;

/// <summary>
/// 表示规则稳定标识。
/// </summary>
public readonly record struct RuleCode : Domain.Common.ISharedKernelType
{
    /// <summary>
    /// 初始化规则标识。
    /// </summary>
    /// <param name="value">规则编码。</param>
    public RuleCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = Normalize(value);
    }

    /// <summary>
    /// 获取规则编码值。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 创建规则标识。
    /// </summary>
    public static RuleCode Create(string value)
    {
        return new RuleCode(value);
    }

    /// <summary>
    /// 返回字符串表示。
    /// </summary>
    public override string ToString()
    {
        return Value;
    }

    /// <summary>
    /// 从字符串隐式转换为规则标识。
    /// </summary>
    public static implicit operator RuleCode(string value)
    {
        return new RuleCode(value);
    }

    /// <summary>
    /// 从规则标识隐式转换为字符串。
    /// </summary>
    public static implicit operator string(RuleCode ruleCode)
    {
        return ruleCode.Value;
    }

    private static string Normalize(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();

        foreach (char current in normalized)
        {
            bool valid = char.IsAsciiLetterOrDigit(current) || current == '-' || current == '.';
            if (!valid)
            {
                throw new ArgumentException("规则编码只能包含小写字母、数字、连字符或点号。", nameof(value));
            }
        }

        return normalized;
    }
}
