using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为缺失的方法补最小方法桩。
///
/// 这个 pass 对应 Joern 的 `MethodStubCreator` 思路。
/// 当调用图或符号关系提前需要某个方法，但前端还没把它完整建出来时，
/// 方法桩可以先提供一个最小落点，让图保持连通。
/// </summary>
public sealed class BuildMethodStubPass : CpgPass
{
    /// <summary>
    /// 初始化方法桩创建 pass。
    /// </summary>
    /// <param name="methods">需要补桩的方法定义集合。</param>
    public BuildMethodStubPass(IEnumerable<MethodStubDefinition> methods)
    {
        Methods = methods?.ToArray() ?? throw new ArgumentNullException(nameof(methods));
    }

    /// <summary>
    /// 获取需要补桩的方法定义集合。
    /// </summary>
    public IReadOnlyList<MethodStubDefinition> Methods { get; }


    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (MethodStubDefinition method in Methods)
        {
            bool exists = builder.Graph
                .GetNodes(CpgNodeKind.Method)
                .Any(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                             string.Equals(fullName, method.FullName, StringComparison.Ordinal));

            if (exists)
            {
                continue;
            }

            CpgNode methodNode = builder.CreateNode(CpgNodeKind.Method);
            methodNode.SetProperty("Name", method.Name);
            methodNode.SetProperty("FullName", method.FullName);
            methodNode.SetProperty("Signature", method.Signature);
            methodNode.SetProperty("ContainingTypeFullName", string.IsNullOrWhiteSpace(method.ContainingTypeFullName)
                ? GetContainingTypeFullName(method)
                : method.ContainingTypeFullName);
            methodNode.SetProperty("ReturnTypeFullName", string.IsNullOrWhiteSpace(method.ReturnTypeFullName)
                ? GetReturnTypeFullName(method.Signature)
                : method.ReturnTypeFullName);
            methodNode.SetProperty("IsAbstract", method.IsAbstract);
            methodNode.SetProperty("IsVirtual", method.IsVirtual);
            methodNode.SetProperty("IsOverride", method.IsOverride);
            methodNode.SetProperty("IsExternal", method.IsExternal);
        }
    }

    private static string GetContainingTypeFullName(MethodStubDefinition method)
    {
        int separatorIndex = method.FullName.LastIndexOf($".{method.Name}(", StringComparison.Ordinal);
        return separatorIndex > 0 ? method.FullName[..separatorIndex] : string.Empty;
    }

    private static string GetReturnTypeFullName(string signature)
    {
        int separatorIndex = signature.IndexOf(" (", StringComparison.Ordinal);
        return separatorIndex > 0 ? signature[..separatorIndex] : string.Empty;
    }
}

/// <summary>
/// 表示一个待创建的方法桩定义。
/// </summary>
/// <param name="Name">方法短名。</param>
/// <param name="FullName">方法全名。</param>
/// <param name="Signature">方法签名。</param>
/// <param name="IsExternal">是否外部方法。</param>
/// <param name="ContainingTypeFullName">包含类型全名。</param>
/// <param name="ReturnTypeFullName">返回类型全名。</param>
/// <param name="IsAbstract">是否抽象方法。</param>
/// <param name="IsVirtual">是否虚方法。</param>
/// <param name="IsOverride">是否 override。</param>
public sealed record MethodStubDefinition(
    string Name,
    string FullName,
    string Signature,
    bool IsExternal,
    string ContainingTypeFullName,
    string ReturnTypeFullName,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride);
