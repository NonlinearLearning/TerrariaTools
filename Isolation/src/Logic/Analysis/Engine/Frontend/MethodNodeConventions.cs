using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛方法、参数、返回节点的统一属性装配规则。
/// </summary>
public static class MethodNodeConventions
{
    /// <summary>
    /// 将方法描述写入图节点。
    /// </summary>
    public static void ApplyMethodProperties(CpgNode methodNode, MethodNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(methodNode);
        ArgumentNullException.ThrowIfNull(descriptor);

        methodNode.SetProperty("Name", descriptor.Name);
        methodNode.SetProperty("FullName", descriptor.FullName);
        methodNode.SetProperty("Signature", descriptor.Signature);
        methodNode.SetProperty("ReturnTypeFullName", descriptor.ReturnTypeFullName);
        methodNode.SetProperty("DeclaredSymbolId", descriptor.DeclaredSymbolId);
        methodNode.SetProperty("ContainingTypeFullName", descriptor.ContainingTypeFullName);
        methodNode.SetProperty("IsAbstract", descriptor.IsAbstract);
        methodNode.SetProperty("IsVirtual", descriptor.IsVirtual);
        methodNode.SetProperty("IsOverride", descriptor.IsOverride);
        methodNode.SetProperty("AstParentId", descriptor.AstParentId);
        methodNode.SetProperty("FileName", descriptor.FileName);
    }

    /// <summary>
    /// 将参数顺序属性写入节点。
    /// </summary>
    public static void ApplyMethodParameterOrder(CpgNode parameterNode, string name, int order)
    {
        ArgumentNullException.ThrowIfNull(parameterNode);
        parameterNode.SetProperty("Name", name);
        parameterNode.SetProperty("Index", order);
        parameterNode.SetProperty("Order", order);
    }

    /// <summary>
    /// 将返回节点属性写入节点。
    /// </summary>
    public static void ApplyMethodReturnProperties(CpgNode returnNode, string returnTypeFullName, long astParentId, int order)
    {
        ArgumentNullException.ThrowIfNull(returnNode);
        returnNode.SetProperty("TypeFullName", returnTypeFullName);
        returnNode.SetProperty("TypeDeclFullName", returnTypeFullName);
        returnNode.SetProperty("AstParentId", astParentId);
        returnNode.SetProperty("Order", order);
    }
}

/// <summary>
/// 表示方法节点的稳定属性集合。
/// </summary>
public sealed record MethodNodeDescriptor(
    string Name,
    string FullName,
    string Signature,
    string ReturnTypeFullName,
    string? DeclaredSymbolId,
    string ContainingTypeFullName,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    long AstParentId,
    string FileName);
