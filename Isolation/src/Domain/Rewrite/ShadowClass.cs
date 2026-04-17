using Domain.Propagation;

namespace Domain.Rewrite;

/// <summary>
/// 表示影子类结果。
/// </summary>
public sealed class ShadowClass
{
    private readonly List<string> memberNames = new();
    private readonly List<ReferenceMapping> referenceMappings = new();

    private ShadowClass(string className, string shadowClassName, string sourceCode)
    {
        ClassName = className;
        ShadowClassName = shadowClassName;
        SourceCode = sourceCode;
    }

    /// <summary>
    /// 获取原始类型名称。
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// 获取影子类型名称。
    /// </summary>
    public string ShadowClassName { get; }

    /// <summary>
    /// 获取影子类源码。
    /// </summary>
    public string SourceCode { get; }

    /// <summary>
    /// 获取成员名称集合。
    /// </summary>
    public IReadOnlyCollection<string> MemberNames => memberNames.AsReadOnly();

    /// <summary>
    /// 获取引用映射集合。
    /// </summary>
    public IReadOnlyCollection<ReferenceMapping> ReferenceMappings => referenceMappings.AsReadOnly();

    /// <summary>
    /// 创建影子类结果。
    /// </summary>
    public static ShadowClass Create(string className, string shadowClassName, string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowClassName);
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new ShadowClass(className.Trim(), shadowClassName.Trim(), sourceCode);
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
}
