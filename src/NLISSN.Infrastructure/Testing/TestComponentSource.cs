namespace NLISSN.Infrastructure.Testing;

public sealed record TestComponentSource
{
    public TestComponentSource(string relativePath, string text)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative source path is required.", nameof(relativePath));
        }

        var normalizedPath = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(relativePath) ||
            normalizedPath.Split('/').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Component source paths must be relative and cannot traverse a parent directory.", nameof(relativePath));
        }

        ArgumentNullException.ThrowIfNull(text);
        RelativePath = normalizedPath;
        Text = text;
    }

    public string RelativePath { get; }

    public string Text { get; }
}
