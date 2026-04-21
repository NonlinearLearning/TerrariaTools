using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为每个 `METHOD_PARAMETER_IN` 创建对应的 `METHOD_PARAMETER_OUT`。
///
/// 这个 pass 对应 Joern `MethodDecoratorPass.scala`。
/// 参数输出节点用于表达方法调用前后参数值的关系，是后续数据流和更完整 CPG 查询的基础。
/// </summary>
public sealed class BuildMethodDecoratorPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode parameterInNode in builder.Graph.GetNodes(CpgNodeKind.MethodParameterIn).ToArray())
        {
            bool alreadyLinked = builder.Graph
                .GetOutgoingEdges(parameterInNode.Id, CpgEdgeKind.ParameterLink)
                .Any(edge => builder.Graph.GetNode(edge.TargetId).Kind == CpgNodeKind.MethodParameterOut);
            if (alreadyLinked)
            {
                continue;
            }

            if (!parameterInNode.TryGetProperty<long>("AstParentId", out long methodId))
            {
                continue;
            }

            CpgNode parameterOutNode = builder.CreateNode(CpgNodeKind.MethodParameterOut);
            CopyProperty<string>(parameterInNode, parameterOutNode, "Name");
            CopyProperty<string>(parameterInNode, parameterOutNode, "Code");
            CopyProperty<int>(parameterInNode, parameterOutNode, "Order");
            CopyProperty<int>(parameterInNode, parameterOutNode, "Index");
            CopyProperty<string>(parameterInNode, parameterOutNode, "TypeFullName");
            CopyProperty<string>(parameterInNode, parameterOutNode, "TypeDeclFullName");
            CopyProperty<string>(parameterInNode, parameterOutNode, "FileName");
            CopyProperty<int>(parameterInNode, parameterOutNode, "Line");
            CopyProperty<int>(parameterInNode, parameterOutNode, "Column");
            parameterOutNode.SetProperty("AstParentId", methodId);

            builder.AddEdge(parameterInNode.Id, parameterOutNode.Id, CpgEdgeKind.ParameterLink);
        }
    }

    private static void CopyProperty<T>(CpgNode source, CpgNode target, string propertyName)
    {
        if (source.TryGetProperty<T>(propertyName, out T? value))
        {
            target.SetProperty(propertyName, value);
        }
    }
}
