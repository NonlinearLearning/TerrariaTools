using Microsoft.CodeAnalysis;

namespace TerrariaTools.Testing.TestFixtures;

public sealed class RoslynWorkspaceFixture : IDisposable
{
    public AdhocWorkspace Workspace { get; } = new();

    public void Dispose()
    {
        Workspace.Dispose();
    }
}
