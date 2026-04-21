using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class ControlDependenceTests : IDisposable
{
    private readonly string tempDirectory;

    public ControlDependenceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-cdg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_buildsControlDependenceEdgesForIfBranches()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "CdgSample.cs"),
            """
            namespace Demo;

            public sealed class Flow
            {
                public int Run(int value)
                {
                    int result = 0;
                    if (value > 0)
                    {
                        result = 1;
                    }
                    else
                    {
                        result = 2;
                    }

                    return result;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode ifNode = Assert.Single(graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => Has(node, "ControlStructureType", "IF")));
        CpgNode thenAssignment = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "=") && Has(node, "Code", "result = 1"));
        CpgNode elseAssignment = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "=") && Has(node, "Code", "result = 2"));

        Assert.Contains(graph.GetOutgoingEdges(ifNode.Id, CpgEdgeKind.Cdg), edge => edge.TargetId == thenAssignment.Id);
        Assert.Contains(graph.GetOutgoingEdges(ifNode.Id, CpgEdgeKind.Cdg), edge => edge.TargetId == elseAssignment.Id);
    }

    private static bool Has(CpgNode node, string propertyName, string expected)
    {
        return node.TryGetProperty<string>(propertyName, out string? actual) &&
               string.Equals(actual, expected, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
