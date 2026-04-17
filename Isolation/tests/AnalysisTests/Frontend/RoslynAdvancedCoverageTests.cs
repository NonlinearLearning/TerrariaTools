using Analysis.Core;
using Analysis.Frontend;
using Xunit;

namespace Analysis.Tests.Frontend;

public sealed class RoslynAdvancedCoverageTests : IDisposable
{
    private readonly string tempDirectory;

    public RoslynAdvancedCoverageTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-advanced-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_buildsSwitchExpressionPatternConditionalAccessAndArgumentEdges()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "AdvancedExpressions.cs"),
            """
            namespace Demo;

            public static class Entry
            {
                public static int Echo(int value)
                {
                    return value;
                }

                public static int Run(string? text, int fallback)
                {
                    int length = text?.Length ?? fallback;
                    return length switch
                    {
                        > 0 when text is { Length: > 0 } => Echo(length),
                        _ => 0
                    };
                }
            }
            """);

        CpgGraph graph = CreateGraph();

        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "switchExpression"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "isPattern"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "?."));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "??"));

        CpgNode echoCall = Assert.Single(graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Echo")));
        CpgNode argument = Assert.Single(
            graph.GetOutgoingEdges(echoCall.Id, CpgEdgeKind.Argument)
                .Select(edge => graph.GetNode(edge.TargetId))
                .Where(node => HasPropertyValue(node, "Name", "length")));

        Assert.Equal(1, GetIntProperty(argument, "ArgumentIndex"));
    }

    [Fact]
    public void CreateGraph_resolvesGenericExtensionMethodAndReceiver()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ExtensionCalls.cs"),
            """
            namespace Demo;

            public sealed class Box
            {
                public int Value { get; set; }
            }

            public static class Extensions
            {
                public static T Pick<T>(this T value)
                {
                    return value;
                }
            }

            public static class Entry
            {
                public static Box Run(Box box)
                {
                    return box.Pick();
                }
            }
            """);

        CpgGraph graph = CreateGraph();

        CpgNode extensionMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Pick")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Pick")));

        Assert.Contains(graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call), edge => edge.TargetId == extensionMethod.Id);
        Assert.Contains("Demo.Extensions.Pick", GetStringProperty(callNode, "MethodFullName"));
        Assert.Equal("STATIC_DISPATCH", GetStringProperty(callNode, "DispatchType"));
    }

    private CpgGraph CreateGraph()
    {
        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        return frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });
    }

    private static bool HasPropertyValue(CpgNode node, string propertyName, string expectedValue)
    {
        return node.TryGetProperty<string>(propertyName, out string? actualValue) &&
               string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
    }

    private static int GetIntProperty(CpgNode node, string propertyName)
    {
        return node.TryGetProperty<int>(propertyName, out int value) ? value : 0;
    }

    private static string GetStringProperty(CpgNode node, string propertyName)
    {
        return node.TryGetProperty<string>(propertyName, out string? value) ? value ?? string.Empty : string.Empty;
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
