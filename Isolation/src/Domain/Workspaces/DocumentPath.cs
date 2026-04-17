namespace Domain.Workspaces;

/// <summary>
/// 表示文档路径值对象。
/// </summary>
/// <param name="Value">标准化后的路径。</param>
public readonly record struct DocumentPath(string Value)
{
    /// <summary>
    /// 创建标准化后的文档路径。
    /// </summary>
    /// <param name="rawPath">原始路径。</param>
    /// <returns>标准化后的值对象。</returns>
    public static DocumentPath Create(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);
        string normalized = rawPath.Replace('\\', '/').Trim();
        return new DocumentPath(normalized);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }
}
