using Microsoft.CodeAnalysis;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public class CodeAnalysisWorkspaceLoaderTests
{
    [Fact]
    public void CreateWorkspace_SupportsCSharpLanguageServices()
    {
        using var workspace = CodeAnalysisWorkspaceLoader.CreateWorkspace();

        Assert.Contains(LanguageNames.CSharp, workspace.Services.SupportedLanguages);
    }
}
