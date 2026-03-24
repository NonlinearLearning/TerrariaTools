namespace TerrariaTools.Testing.TestFixtures;

// 为文件系统测试提供隔离的临时根目录，并在释放时自动清理。
public sealed class TemporaryDirectoryFixture : IDisposable
{
    public TemporaryDirectoryFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "TerrariaTools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetPath(string relativePath) => Path.Combine(RootPath, relativePath);

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
