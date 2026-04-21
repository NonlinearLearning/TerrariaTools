using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Model;
using Logic.Analysis.Engine.Passes;
using Microsoft.CodeAnalysis;

namespace Infrastructure.Analysis.Engine.Frontend.Builders;

/// <summary>
/// 提供各类 Builder 共用的底层工具。
///
/// 这里不负责具体语法分支，只负责公共写属性、写位置、记节点这类稳定动作。
/// </summary>
internal sealed class PrimitiveBuilder
{
    private readonly BuilderState state;

    /// <summary>
    /// 初始化工具 Builder。
    /// </summary>
    public PrimitiveBuilder(BuilderState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>
    /// 记住语法节点和图节点的对应关系。
    /// </summary>
    public void RememberNode(SyntaxNode syntaxNode, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(syntaxNode);
        ArgumentNullException.ThrowIfNull(node);
        state.NodeIdsBySyntax[syntaxNode] = node.Id;
    }

    /// <summary>
    /// 统一写入位置信息。
    /// </summary>
    public void SetLocation(CpgNode node, FileLinePositionSpan lineSpan)
    {
        ArgumentNullException.ThrowIfNull(node);
        GraphNodeConventions.SetLocation(
            node,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    /// <summary>
    /// 安全读取字符串属性。
    /// </summary>
    public string GetStringProperty(CpgNode node, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return node.TryGetProperty<string>(propertyName, out string? value) ? value ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// 判断节点属性是否等于目标值。
    /// </summary>
    public bool HasPropertyValue(CpgNode node, string propertyName, string expectedValue)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return node.TryGetProperty<string>(propertyName, out string? actualValue) &&
               string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// 为语法节点生成稳定操作标识。
    /// </summary>
    public OperationId CreateOperationId(SyntaxNode syntaxNode)
    {
        ArgumentNullException.ThrowIfNull(syntaxNode);
        FileLinePositionSpan lineSpan = syntaxNode.GetLocation().GetLineSpan();
        return new OperationId(FrontendGraphConventions.BuildOperationId(
            lineSpan.Path,
            syntaxNode.SpanStart,
            syntaxNode.RawKind));
    }

    /// <summary>
    /// 收集类型的基类和接口全名。
    /// </summary>
    public IReadOnlyCollection<string> GetBaseTypeNames(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return Array.Empty<string>();
        }

        return new[] { typeSymbol.BaseType }
            .Where(type => type is not null)
            .Cast<ITypeSymbol>()
            .Concat(typeSymbol.Interfaces)
            .Select(RoslynSymbolFormatter.GetTypeFullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 为声明类节点写入统一属性。
    /// </summary>
    public void WriteDeclarationProperties(
        CpgNode node,
        ISymbol? declaredSymbol,
        ITypeSymbol? typeSymbol,
        long astParentId,
        CpgNode fileNode)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(fileNode);

        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        NodeAssemblyConventions.ApplyDeclarationProperties(
            node,
            typeFullName,
            RoslynSymbolFormatter.GetSymbolId(declaredSymbol)?.Value,
            astParentId,
            GetStringProperty(fileNode, "FileName"));
        state.ReferencedTypeFullNames.Add(typeFullName);
    }

    /// <summary>
    /// 统一创建控制结构节点。
    /// </summary>
    public CpgNode CreateControlNode(SyntaxNode syntaxNode, long astParentId, string controlType)
    {
        ArgumentNullException.ThrowIfNull(syntaxNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlType);

        CpgNode controlNode = state.GraphBuilder.CreateNode(CpgNodeKind.ControlStructure);
        NodeAssemblyConventions.ApplyControlNodeProperties(controlNode, controlType, astParentId);
        SetLocation(controlNode, syntaxNode.GetLocation().GetLineSpan());
        RememberNode(syntaxNode, controlNode);
        return controlNode;
    }

    /// <summary>
    /// 记录外部方法桩。
    /// </summary>
    public void AddExternalMethodStubIfNeeded(IMethodSymbol? methodSymbol)
    {
        if (methodSymbol is null)
        {
            return;
        }

        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(methodSymbol.ReturnType));

        if (SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingAssembly, state.Context.Compilation.Assembly))
        {
            return;
        }

        state.ExternalMethodStubs.Add(new MethodStubDefinition(
            methodSymbol.Name,
            RoslynSymbolFormatter.GetMethodFullName(methodSymbol),
            RoslynSymbolFormatter.GetMethodSignature(methodSymbol),
            true,
            RoslynSymbolFormatter.GetTypeFullName(methodSymbol.ContainingType),
            RoslynSymbolFormatter.GetTypeFullName(methodSymbol.ReturnType),
            methodSymbol.IsAbstract,
            methodSymbol.IsVirtual,
            methodSymbol.IsOverride));
    }

    /// <summary>
    /// 查找文件节点。
    /// </summary>
    public CpgNode? FindFileNode(string? filePath)
    {
        return state.GraphBuilder.Graph
            .GetNodes(CpgNodeKind.File)
            .FirstOrDefault(node => HasPropertyValue(node, "FileName", filePath ?? string.Empty));
    }
}
