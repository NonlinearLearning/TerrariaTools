using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.X2Cpg;

/// <summary>
/// 收敛 X2Cpg 生命周期中与图本身相关的纯构造规则。
/// </summary>
public static class X2CpgGraphFactory
{
    /// <summary>
    /// 创建空的内存态 CPG。
    /// </summary>
    public static CpgGraph CreateEmptyGraph()
    {
        return new CpgGraph();
    }
}
