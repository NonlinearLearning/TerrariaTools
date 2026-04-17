using Domain.Propagation;

namespace Domain.Rewrite;

/// <summary>
/// 表示最小运行闭包结果。
/// </summary>
public sealed class RuntimeClosure
{
    private readonly List<string> memberNames = new();
    private readonly List<ReferenceMapping> referenceMappings = new();

    private RuntimeClosure(
        string className,
        string rootMethodName,
        string closureClassName,
        string sourceCode)
    {
        ClassName = className;
        RootMethodName = rootMethodName;
        ClosureClassName = closureClassName;
        SourceCode = sourceCode;
        Root = new ClosureRoot(className, rootMethodName);
        IntegrityStatus = ClosureIntegrityStatus.Unknown;
    }

    /// <summary>
    /// 获取原始类型名称。
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// 获取根方法名称。
    /// </summary>
    public string RootMethodName { get; }

    /// <summary>
    /// 获取闭包类型名称。
    /// </summary>
    public string ClosureClassName { get; }

    /// <summary>
    /// 获取闭包源码。
    /// </summary>
    public string SourceCode { get; }

    /// <summary>
    /// 获取闭包根。
    /// </summary>
    public ClosureRoot Root { get; }

    /// <summary>
    /// 获取闭包完整性状态。
    /// </summary>
    public ClosureIntegrityStatus IntegrityStatus { get; private set; }

    /// <summary>
    /// 获取成员名称集合。
    /// </summary>
    public IReadOnlyCollection<string> MemberNames => memberNames.AsReadOnly();

    /// <summary>
    /// 获取引用映射集合。
    /// </summary>
    public IReadOnlyCollection<ReferenceMapping> ReferenceMappings => referenceMappings.AsReadOnly();

    /// <summary>
    /// 创建最小运行闭包结果。
    /// </summary>
    public static RuntimeClosure Create(
        string className,
        string rootMethodName,
        string closureClassName,
        string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootMethodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(closureClassName);
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new RuntimeClosure(
            className.Trim(),
            rootMethodName.Trim(),
            closureClassName.Trim(),
            sourceCode);
    }

    /// <summary>
    /// 增加成员名称。
    /// </summary>
    public void AddMember(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (memberNames.Contains(memberName, StringComparer.Ordinal))
        {
            return;
        }

        memberNames.Add(memberName.Trim());
    }

    /// <summary>
    /// 增加引用映射。
    /// </summary>
    public void AddReferenceMapping(ReferenceMapping referenceMapping)
    {
        ArgumentNullException.ThrowIfNull(referenceMapping);

        if (referenceMappings.Any(item =>
                string.Equals(item.SourceReference, referenceMapping.SourceReference, StringComparison.Ordinal) &&
                string.Equals(item.TargetReference, referenceMapping.TargetReference, StringComparison.Ordinal)))
        {
            return;
        }

        referenceMappings.Add(referenceMapping);
    }

    /// <summary>
    /// 标记完整性状态。
    /// </summary>
    public void MarkIntegrity(ClosureIntegrityStatus integrityStatus)
    {
        IntegrityStatus = integrityStatus;
    }
}
