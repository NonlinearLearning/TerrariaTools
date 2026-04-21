using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为缺失的类型补最小类型节点和类型声明节点。
///
/// 这个 pass 参考的是 Joern 的 `TypeDeclStubCreator`。
/// 它的价值在于：即使当前前端还没有把某个类型完整展开，图上也先能有一个
/// 可引用的类型壳子，避免后续类型关系和调用关系彻底断掉。
/// </summary>
public sealed class BuildTypeStubPass : CpgPass
{
    /// <summary>
    /// 初始化类型桩创建 pass。
    /// </summary>
    /// <param name="typeFullNames">需要补桩的类型全名集合。</param>
    public BuildTypeStubPass(IEnumerable<string> typeFullNames)
    {
        TypeFullNames = typeFullNames?.ToArray() ?? throw new ArgumentNullException(nameof(typeFullNames));
    }

    /// <summary>
    /// 获取需要补桩的类型全名集合。
    /// </summary>
    public IReadOnlyList<string> TypeFullNames { get; }


    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (string typeFullName in TypeFullNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            CpgNode typeNode = builder.Graph
                .GetNodes(CpgNodeKind.Type)
                .FirstOrDefault(node => node.TryGetProperty<string>("FullName", out string? current) &&
                                        string.Equals(current, typeFullName, StringComparison.Ordinal))
                ?? CreateTypeNode(builder, typeFullName);

            CpgNode typeDeclNode = builder.Graph
                .GetNodes(CpgNodeKind.TypeDecl)
                .FirstOrDefault(node => node.TryGetProperty<string>("FullName", out string? current) &&
                                        string.Equals(current, typeFullName, StringComparison.Ordinal))
                ?? CreateTypeDeclNode(builder, typeFullName);

            bool hasRelation = builder.Graph
                .GetOutgoingEdges(typeNode.Id, CpgEdgeKind.Ref)
                .Any(edge => edge.TargetId == typeDeclNode.Id);

            if (!hasRelation)
            {
                builder.AddEdge(typeNode.Id, typeDeclNode.Id, CpgEdgeKind.Ref);
            }
        }
    }

    private static CpgNode CreateTypeNode(CpgGraphBuilder builder, string typeFullName)
    {
        CpgNode typeNode = builder.CreateNode(CpgNodeKind.Type);
        typeNode.SetProperty("Name", typeFullName.Split('.').Last());
        typeNode.SetProperty("FullName", typeFullName);
        typeNode.SetProperty("TypeDeclFullName", typeFullName);
        return typeNode;
    }

    private static CpgNode CreateTypeDeclNode(CpgGraphBuilder builder, string typeFullName)
    {
        CpgNode typeDeclNode = builder.CreateNode(CpgNodeKind.TypeDecl);
        typeDeclNode.SetProperty("Name", typeFullName.Split('.').Last());
        typeDeclNode.SetProperty("FullName", typeFullName);
        return typeDeclNode;
    }
}
