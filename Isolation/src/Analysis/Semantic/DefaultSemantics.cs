using Analysis.Semantic.Flows;

namespace Analysis.Semantic;

/// <summary>
/// 提供默认外部方法数据流语义。
///
/// 对应 Joern `dataflowengineoss/DefaultSemantics.scala`。当前默认规则保持保守：
/// 没有外部规则时只依赖图内 `ReachingDef` 边，不凭空传播未知库调用。
/// </summary>
public static class DefaultSemantics
{
    /// <summary>
    /// 创建默认语义规则集合。
    /// </summary>
    public static ISemantics Create()
    {
        return new NoSemantics();
    }
}
