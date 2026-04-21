using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class RealTerrariaToolsCpgSmokeTests : IDisposable
{
    private readonly string tempDirectory;

    public RealTerrariaToolsCpgSmokeTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-real-terraria-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_buildsCpgForRealTerrariaToolsFiles()
    {
        string repositoryRoot = FindTerrariaToolsRoot();
        string mainSource = Path.Combine(repositoryRoot, "Main.cs");
        if (!File.Exists(mainSource))
        {
            return;
        }

        File.Copy(mainSource, Path.Combine(tempDirectory, "Main.cs"));

        CpgGraph graph = new RoslynCpgFrontend(new DefaultRoslynCpgBuilder())
            .CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode programType = Assert.Single(graph.GetNodes(CpgNodeKind.TypeDecl).Where(node => HasPropertyValue(node, "Name", "Program")));
        Assert.NotEmpty(graph.GetOutgoingEdges(programType.Id, CpgEdgeKind.Ast));

        CpgNode mainMethod = Assert.Single(graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Main")));
        Assert.NotEmpty(graph.GetOutgoingEdges(mainMethod.Id, CpgEdgeKind.Ast));

        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasCodeContaining(node, "MSBuildLocator.RegisterDefaults()"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasCodeContaining(node, "services.BuildServiceProvider()"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasCodeContaining(node, "app.RunAsync(args)"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Local), node => HasPropertyValue(node, "Name", "serviceProvider"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.ControlStructure), node => HasPropertyValue(node, "ControlStructureType", "IF"));

        Assert.True(graph.Edges.Count(edge => edge.Kind == CpgEdgeKind.Call) > 0);
        Assert.True(graph.Edges.Count(edge => edge.Kind == CpgEdgeKind.Cfg) > 0);
        Assert.True(graph.Edges.Count(edge => edge.Kind == CpgEdgeKind.ReachingDef) > 0);
    }

    private static string FindTerrariaToolsRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "TerrariaTools.csproj");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
    }

    private static bool HasPropertyValue(CpgNode node, string propertyName, string expectedValue)
    {
        return node.TryGetProperty<string>(propertyName, out string? actualValue) &&
               string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
    }

    private static bool HasCodeContaining(CpgNode node, string expectedCode)
    {
        return node.TryGetProperty<string>("Code", out string? code) &&
               code?.Contains(expectedCode, StringComparison.Ordinal) is true;
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
