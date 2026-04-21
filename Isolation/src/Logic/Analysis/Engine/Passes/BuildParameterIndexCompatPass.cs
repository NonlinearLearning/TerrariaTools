using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为参数节点补齐 `Index`。
///
/// 这个 pass 对应 Joern `ParameterIndexCompatPass.scala`。
/// 旧图可能只有 `Order`，没有 `Index`；后续查询和 pass 更适合统一读 `Index`。
/// </summary>
public sealed class BuildParameterIndexCompatPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode parameterNode in builder.Graph.GetNodes(CpgNodeKind.MethodParameterIn))
        {
            bool hasIndex = parameterNode.TryGetProperty<int>("Index", out int index) && index > 0;
            if (hasIndex)
            {
                continue;
            }

            if (parameterNode.TryGetProperty<int>("Order", out int order) && order > 0)
            {
                parameterNode.SetProperty("Index", order);
            }
        }
    }
}
