namespace TerrariaTools.Testing.TestFixtures;

/// <summary>
/// 临时目录测试夹具。
/// </summary>
public sealed class TemporaryDirectoryFixture : IDisposable
{
    /// <summary>
    /// 初始化临时目录夹具并创建根目录。
    /// </summary>
    public TemporaryDirectoryFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "TerrariaTools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>
    /// 获取临时根目录路径。
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// 在根目录下创建子目录。
    /// </summary>
    /// <param name="relativePath">相对路径。</param>
    /// <returns>创建后的绝对路径。</returns>
    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 获取根目录下的目标路径。
    /// </summary>
    /// <param name="relativePath">相对路径。</param>
    /// <returns>目标绝对路径。</returns>
    public string GetPath(string relativePath) => Path.Combine(RootPath, relativePath);

    /// <summary>
    /// 释放资源并删除临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
