namespace Domain.Propagation;

/// <summary>
/// 表示闭包根。
/// </summary>
public sealed class ClosureRoot
{
    public ClosureRoot(string className, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        ClassName = className.Trim();
        MemberName = memberName.Trim();
    }

    public string ClassName { get; }

    public string MemberName { get; }
}
