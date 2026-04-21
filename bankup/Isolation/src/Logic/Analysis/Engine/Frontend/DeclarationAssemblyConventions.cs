using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛类型与成员声明节点的属性装配规则。
/// </summary>
public static class DeclarationAssemblyConventions
{
    /// <summary>
    /// 写入类型声明节点属性。
    /// </summary>
    public static void ApplyTypeDeclProperties(
        CpgNode node,
        long astParentId,
        string fileName,
        string? declaredSymbolId,
        string typeFullName,
        string typeKind,
        IReadOnlyCollection<string> inheritsFromTypeFullNames)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
        node.SetProperty("DeclaredSymbolId", declaredSymbolId);
        node.SetProperty("TypeFullName", typeFullName);
        node.SetProperty("TypeKind", typeKind);
        node.SetProperty("InheritsFromTypeFullNames", inheritsFromTypeFullNames);
    }

    /// <summary>
    /// 写入枚举底层类型。
    /// </summary>
    public static void ApplyEnumAliasType(CpgNode node, string aliasTypeFullName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("AliasTypeFullName", aliasTypeFullName);
    }

    /// <summary>
    /// 写入成员节点名称与全名。
    /// </summary>
    public static void ApplyMemberIdentity(CpgNode node, string name, string fullName, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("FullName", fullName);
        if (!string.IsNullOrWhiteSpace(source))
        {
            node.SetProperty("Source", source);
        }
    }
}
