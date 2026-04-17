using Analysis.Core;
using Analysis.Frontend;
using Analysis.Semantic;
using Xunit;

namespace Analysis.Tests.Passes;

public sealed class DataFlowPassTests : IDisposable
{
    private readonly string tempDirectory;

    public DataFlowPassTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-dataflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_buildsReachingDefEdgesForLocalAssignments()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "LocalFlowSample.cs"),
            """
            namespace Demo;

            public static class Flow
            {
                public static int Run(int input)
                {
                    int value = input;
                    value = value + 1;
                    return value;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode valueLocal = Assert.Single(
            graph.GetNodes(CpgNodeKind.Local).Where(node => Has(node, "Name", "value")));
        CpgNode returnNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => Has(node, "ControlStructureType", "RETURN")));

        Assert.Contains(
            graph.GetIncomingEdges(returnNode.Id, CpgEdgeKind.ReachingDef),
            edge => edge.SourceId == valueLocal.Id && string.Equals(edge.Label, "value", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateGraph_buildsReachingDefEdgesFromParametersToUses()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ParameterFlowSample.cs"),
            """
            namespace Demo;

            public static class Flow
            {
                public static int Run(int input)
                {
                    return input + 1;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode parameterNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.MethodParameterIn).Where(node => Has(node, "Name", "input")));
        CpgNode returnNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => Has(node, "ControlStructureType", "RETURN")));

        Assert.Contains(
            graph.GetIncomingEdges(returnNode.Id, CpgEdgeKind.ReachingDef),
            edge => edge.SourceId == parameterNode.Id && string.Equals(edge.Label, "input", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateGraph_marksDataflowOverlayAfterOssDataFlowPass()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "OverlaySample.cs"),
            """
            namespace Demo;

            public static class Flow
            {
                public static int Run(int input)
                {
                    int value = input;
                    return value;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        Assert.Contains("dataflowOss", Overlays.AppliedOverlays(graph));
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
