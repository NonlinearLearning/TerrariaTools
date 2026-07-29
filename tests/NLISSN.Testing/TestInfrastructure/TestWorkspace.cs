namespace RoslynPrototype.Testing.TestInfrastructure;

public sealed class TestWorkspace : IDisposable
{
  private bool _disposed;

  internal TestWorkspace(string rootPath)
  {
    RootPath = rootPath;
  }

  public string RootPath { get; }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    if (Directory.Exists(RootPath))
    {
      Directory.Delete(RootPath, recursive: true);
    }
  }
}
