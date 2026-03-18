namespace TerrariaTools.Testing.TestFixtures;

/// <summary>
/// 应用输出目录测试夹具。
/// </summary>
public sealed class ApplicationOutputFixture : IDisposable
{
    private readonly TemporaryDirectoryFixture _directories = new();

    /// <summary>
    /// 获取根目录路径。
    /// </summary>
    public string RootPath => _directories.RootPath;

    /// <summary>
    /// 创建输出目录。
    /// </summary>
    /// <param name="relativePath">相对路径。</param>
    /// <returns>创建后的绝对路径。</returns>
    public string CreateOutputDirectory(string relativePath) => _directories.CreateDirectory(relativePath);

    /// <summary>
    /// 获取输出路径。
    /// </summary>
    /// <param name="relativePath">相对路径。</param>
    /// <returns>输出绝对路径。</returns>
    public string GetOutputPath(string relativePath) => _directories.GetPath(relativePath);

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _directories.Dispose();
    }
}
