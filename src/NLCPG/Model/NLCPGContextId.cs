namespace NLCPG.Model;

/// 承载调用点等边执行上下文的稳定标识。
///何意为
public readonly record struct NLCPGContextId(string Value)
{
    // 返回上下文标识的原始字符串值。
    public override string ToString()
    {
        return Value;
    }
}
