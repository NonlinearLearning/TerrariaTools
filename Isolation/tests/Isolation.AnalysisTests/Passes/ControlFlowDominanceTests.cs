using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class ControlFlowDominanceTests : IDisposable
{
    private readonly string tempDirectory;

    public ControlFlowDominanceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-dominance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_buildsMethodEntryExitAndDominanceEdges()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "DominanceSample.cs"),
            """
            namespace Demo;

            public sealed class Flow
            {
                public int Run(int value)
                {
                    int x = value + 1;
                    if (x > 0)
                    {
                        return x;
                    }

                    return 0;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode methodNode = Assert.Single(graph.GetNodes(CpgNodeKind.Method).Where(node => Has(node, "Name", "Run")));
        CpgNode localNode = Assert.Single(graph.GetNodes(CpgNodeKind.Local).Where(node => Has(node, "Name", "x")));
        CpgNode ifNode = Assert.Single(graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => Has(node, "ControlStructureType", "IF")));
        CpgNode methodReturnNode = Assert.Single(graph.GetNodes(CpgNodeKind.MethodReturn));

        Assert.Contains(graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Cfg), edge => edge.TargetId == localNode.Id);
        Assert.Contains(graph.GetOutgoingEdges(localNode.Id, CpgEdgeKind.Cfg), edge => edge.TargetId == ifNode.Id);

        IEnumerable<CpgNode> returnNodes = graph.GetNodes(CpgNodeKind.ControlStructure)
            .Where(node => Has(node, "ControlStructureType", "RETURN"));
        Assert.All(returnNodes, returnNode =>
            Assert.Contains(graph.GetOutgoingEdges(returnNode.Id, CpgEdgeKind.Cfg), edge => edge.TargetId == methodReturnNode.Id));

        Assert.Contains(graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Dominates), edge => edge.TargetId == localNode.Id);
        Assert.Contains(graph.GetOutgoingEdges(methodReturnNode.Id, CpgEdgeKind.PostDominates), edge => returnNodes.Any(node => node.Id == edge.TargetId));
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
