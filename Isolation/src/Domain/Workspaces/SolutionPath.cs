namespace Domain.Workspaces;

/// <summary>
/// 表示解决方案路径值对象。
/// </summary>
/// <param name="Value">标准化后的解决方案路径。</param>
public readonly record struct SolutionPath(string Value)
{
    /// <summary>
    /// 创建标准化后的解决方案路径。
    /// </summary>
    public static SolutionPath Create(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);
        string normalized = rawPath.Replace('\\', '/').Trim();
        return new SolutionPath(normalized);
    }

    /// <summary>
    /// 获取解决方案所在目录。
    /// </summary>
    public string DirectoryPath
    {
        get
        {
            string? directory = Path.GetDirectoryName(Value);
            return string.IsNullOrWhiteSpace(directory)
                ? string.Empty
                : directory.Replace('\\', '/');
        }
    }

    /// <summary>
    /// 基于解决方案目录解析工作区内路径。
    /// </summary>
    public string ResolveWorkspacePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || string.IsNullOrWhiteSpace(DirectoryPath))
        {
            return normalized;
        }

        return Path.Combine(DirectoryPath, normalized).Replace('\\', '/');
    }


    public override string ToString()
    {
        return Value;
    }
}
