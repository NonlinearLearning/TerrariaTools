using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.X2Cpg.Utils;

/// <summary>
/// 提供无需具体前端节点上下文的节点创建辅助。
///
/// 对应 Joern `NodeBuilders.scala`。
/// </summary>
public static class NodeBuilders
{
    public static CpgNode NewLocalNode(CpgGraph graph, string name, string typeFullName)
    {
        CpgNode node = graph.CreateNode(CpgNodeKind.Local);
        node.SetProperty("Name", name);
        node.SetProperty("Code", name);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    public static CpgNode NewIdentifierNode(CpgGraph graph, string name, string typeFullName)
    {
        CpgNode node = graph.CreateNode(CpgNodeKind.Identifier);
        node.SetProperty("Name", name);
        node.SetProperty("Code", name);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    public static CpgNode NewCallNode(
        CpgGraph graph,
        string methodName,
        string? typeDeclFullName,
        string returnTypeFullName,
        string dispatchType,
        IEnumerable<string>? argumentTypes = null)
    {
        string signature = ComposeCallSignature(returnTypeFullName, argumentTypes ?? Array.Empty<string>());
        string methodFullName = ComposeMethodFullName(typeDeclFullName, methodName, signature);
        CpgNode node = graph.CreateNode(CpgNodeKind.Call);
        node.SetProperty("Name", methodName);
        node.SetProperty("MethodFullName", methodFullName);
        node.SetProperty("Signature", signature);
        node.SetProperty("TypeFullName", returnTypeFullName);
        node.SetProperty("DispatchType", dispatchType);
        return node;
    }

    public static CpgNode NewMethodReturnNode(CpgGraph graph, string typeFullName)
    {
        CpgNode node = graph.CreateNode(CpgNodeKind.MethodReturn);
        node.SetProperty("TypeFullName", typeFullName);
        node.SetProperty("Code", "RET");
        node.SetProperty("EvaluationStrategy", "BY_VALUE");
        return node;
    }

    private static string ComposeCallSignature(string returnType, IEnumerable<string> argumentTypes)
    {
        return $"{returnType}({string.Join(",", argumentTypes)})";
    }

    private static string ComposeMethodFullName(string? typeDeclFullName, string name, string signature)
    {
        return string.IsNullOrWhiteSpace(typeDeclFullName)
            ? $"{name}:{signature}"
            : $"{typeDeclFullName}.{name}:{signature}";
    }
}
