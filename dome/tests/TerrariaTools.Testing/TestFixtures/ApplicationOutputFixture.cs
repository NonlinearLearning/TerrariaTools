namespace TerrariaTools.Testing.TestFixtures;

public sealed class ApplicationOutputFixture : IDisposable
{
    private readonly TemporaryDirectoryFixture _directories = new();

    public string RootPath => _directories.RootPath;

    public string CreateOutputDirectory(string relativePath) => _directories.CreateDirectory(relativePath);

    public string GetOutputPath(string relativePath) => _directories.GetPath(relativePath);

    public void Dispose()
    {
        _directories.Dispose();
    }
}
