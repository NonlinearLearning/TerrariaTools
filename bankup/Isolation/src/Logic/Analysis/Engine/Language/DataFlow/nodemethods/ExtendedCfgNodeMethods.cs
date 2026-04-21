namespace Logic.Analysis.Engine.Language.DataFlow.Nodemethods;

/// <summary>
/// 数据流 CFG 节点方法入口。
///
/// 对应 Joern `dataflowengineoss/language/nodemethods/ExtendedCfgNodeMethods.scala`。
/// C# 版主要通过 `ExtendedCfgNode` 实例方法暴露能力，因此这里保留命名入口。
/// </summary>
public static class ExtendedCfgNodeMethods
{
    /// <summary>
    /// 返回当前扩展节点本身，便于调用方按 Joern 文件名定位能力。
    /// </summary>
    public static ExtendedCfgNode Self(ExtendedCfgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node;
    }
}
