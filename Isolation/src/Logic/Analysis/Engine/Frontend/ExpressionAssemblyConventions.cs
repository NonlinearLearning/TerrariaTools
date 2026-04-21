using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛表达式相关节点的属性装配规则。
/// </summary>
public static class ExpressionAssemblyConventions
{
    /// <summary>
    /// 写入标识符节点属性。
    /// </summary>
    public static void ApplyIdentifierProperties(
        CpgNode node,
        string name,
        string code,
        string typeFullName,
        string? referencedSymbolId,
        long astParentId,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("Code", code);
        node.SetProperty("TypeFullName", typeFullName);
        node.SetProperty("TypeDeclFullName", typeFullName);
        node.SetProperty("ReferencedSymbolId", referencedSymbolId);
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
    }

    /// <summary>
    /// 写入字面量节点属性。
    /// </summary>
    public static void ApplyLiteralProperties(
        CpgNode node,
        string name,
        string code,
        string typeFullName,
        long astParentId,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("Code", code);
        node.SetProperty("TypeFullName", typeFullName);
        node.SetProperty("TypeDeclFullName", typeFullName);
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
    }

    /// <summary>
    /// 写入方法引用节点属性。
    /// </summary>
    public static void ApplyMethodRefProperties(
        CpgNode node,
        string name,
        string code,
        string methodFullName,
        string signature,
        string? referencedSymbolId,
        long astParentId,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("Code", code);
        node.SetProperty("MethodFullName", methodFullName);
        node.SetProperty("Signature", signature);
        node.SetProperty("ReferencedSymbolId", referencedSymbolId);
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
    }

    /// <summary>
    /// 写入匿名类型声明节点属性。
    /// </summary>
    public static void ApplyAnonymousTypeDeclProperties(
        CpgNode node,
        string name,
        string fullName,
        string? declaredSymbolId,
        long astParentId,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("FullName", fullName);
        node.SetProperty("TypeFullName", fullName);
        node.SetProperty("DeclaredSymbolId", declaredSymbolId);
        node.SetProperty("IsAnonymous", "true");
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
    }

    /// <summary>
    /// 追加参数序号。
    /// </summary>
    public static void ApplyArgumentIndex(CpgNode node, int argumentIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("ArgumentIndex", argumentIndex);
    }

    /// <summary>
    /// 写入接收者节点属性。
    /// </summary>
    public static void ApplyReceiverIdentifierProperties(CpgNode node, string name, string code, long astParentId, string fileName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", name);
        node.SetProperty("Code", code);
        node.SetProperty("AstParentId", astParentId);
        node.SetProperty("FileName", fileName);
    }

    /// <summary>
    /// 写入强制转换目标类型。
    /// </summary>
    public static void ApplyTargetTypeFullName(CpgNode node, string targetTypeFullName)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("TargetTypeFullName", targetTypeFullName);
    }

    /// <summary>
    /// 写入字段访问元数据。
    /// </summary>
    public static void ApplyFieldAccessProperties(CpgNode node, string fieldFullName, string? referencedSymbolId = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("FieldFullName", fieldFullName);
        if (referencedSymbolId is not null)
        {
            node.SetProperty("ReferencedSymbolId", referencedSymbolId);
        }
    }
}
