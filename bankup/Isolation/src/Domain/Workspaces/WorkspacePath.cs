namespace Domain.Workspaces;

/// <summary>
/// 表示工作区内输入路径值对象。
/// </summary>
/// <param name="Value">标准化后的路径。</param>
public readonly record struct WorkspacePath(string Value)
{
    /// <summary>
    /// 创建标准化后的路径。
    /// </summary>
    public static WorkspacePath Create(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);
        return new WorkspacePath(rawPath.Replace('\\', '/').Trim());
    }


    public override string ToString()
    {
        return Value;
    }
}
