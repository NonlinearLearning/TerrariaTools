using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛常见节点类型的属性装配规则。
/// </summary>
public static class NodeAssemblyConventions
{
    /// <summary>
    /// 写入声明类节点公共属性。
    /// </summary>
    public static void ApplyDeclarationProperties(
        CpgNode node,
        string typeFullName,
        string? declaredSymbolId,
        long astParentId,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("TypeFullName", typeFullName);
        node.SetProperty("TypeDeclFullName", typeFullName);
        node.SetProperty("DeclaredSymbolId", declaredSymbolId);
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
    }

    /// <summary>
    /// 写入控制结构节点属性。
    /// </summary>
    public static void ApplyControlNodeProperties(CpgNode node, string controlType, long astParentId)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", controlType);
        node.SetProperty("ControlStructureType", controlType);
        node.SetProperty("AstParentId", astParentId);
    }

    /// <summary>
    /// 写入块节点属性。
    /// </summary>
    public static void ApplyBlockNodeProperties(CpgNode node, string name, long astParentId)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("AstParentId", astParentId);
    }

    /// <summary>
    /// 写入调用节点属性。
    /// </summary>
    public static void ApplyCallNodeProperties(CpgNode node, CallNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(descriptor);
        node.SetProperty("Name", descriptor.Name);
        node.SetProperty("Code", descriptor.Code);
        node.SetProperty("MethodFullName", descriptor.MethodFullName);
        node.SetProperty("Signature", descriptor.Signature);
        node.SetProperty("TypeFullName", descriptor.TypeFullName);
        node.SetProperty("DispatchType", descriptor.DispatchType);
        node.SetProperty("ReferencedSymbolId", descriptor.ReferencedSymbolId);
        node.SetProperty("OperationId", descriptor.OperationId);
        node.SetProperty("AstParentId", descriptor.AstParentId);
        node.SetProperty("FileName", descriptor.FileName);
    }
}

/// <summary>
/// 表示调用节点的稳定属性集合。
/// </summary>
public sealed record CallNodeDescriptor(
    string Name,
    string Code,
    string MethodFullName,
    string Signature,
    string TypeFullName,
    string DispatchType,
    string? ReferencedSymbolId,
    string OperationId,
    long AstParentId,
    string FileName);
