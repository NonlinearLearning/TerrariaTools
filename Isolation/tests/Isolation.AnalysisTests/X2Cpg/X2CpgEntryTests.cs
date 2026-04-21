using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Layers;
using Logic.Analysis.Engine.X2Cpg;
using Logic.Analysis.Engine.X2Cpg.Utils;
using Domain.Analysis.Engine.Semantic;
using Infrastructure.Analysis.Engine.X2Cpg;
using Xunit;

namespace Isolation.AnalysisTests.X2Cpg;

public sealed class X2CpgEntryTests
{
    [Fact]
    public void Config_derivesPathsAndFlagsLikeJoern()
    {
        X2CpgConfig config = new X2CpgConfig()
            .WithInputPath(".")
            .WithOutputPath("out.cpg.bin")
            .WithIgnoredFiles(new[] { "bin", "obj" })
            .WithIgnoredFilesRegex(".*\\.g\\.cs$")
            .WithSchemaValidation(ValidationMode.Enabled)
            .WithDisableFileContent(false)
            .WithServerMode(true)
            .WithServerTimeoutSeconds(60);

        Assert.True(Path.IsPathFullyQualified(config.InputPath));
        Assert.Equal("out.cpg.bin", config.OutputPath);
        Assert.All(config.IgnoredFiles, path => Assert.True(Path.IsPathFullyQualified(path)));
        Assert.Matches(config.IgnoredFilesRegex, "Demo.g.cs");
        Assert.Equal(ValidationMode.Enabled, config.SchemaValidation);
        Assert.False(config.DisableFileContent);
        Assert.True(config.ServerMode);
        Assert.Equal(TimeSpan.FromSeconds(60), config.ServerTimeout);
    }

    [Fact]
    public void DefaultOverlayCreators_matchJoernCoreOrder()
    {
        string[] names = X2CpgOverlays.DefaultOverlayCreators()
            .Select(layer => layer.OverlayName)
            .ToArray();

        Assert.Equal(
            new[]
            {
                BaseLayer.OverlayNameValue,
                ControlFlowLayer.OverlayNameValue,
                TypeRelationsLayer.OverlayNameValue,
                CallGraphLayer.OverlayNameValue,
            },
            names);
    }

    [Fact]
    public void ApplyDefaultOverlays_recordsCoreOverlayNames()
    {
        CpgGraph graph = new();

        X2CpgOverlays.ApplyDefaultOverlays(graph);

        Assert.Equal(
            new[]
            {
                BaseLayer.OverlayNameValue,
                ControlFlowLayer.OverlayNameValue,
                TypeRelationsLayer.OverlayNameValue,
                CallGraphLayer.OverlayNameValue,
            },
            Overlays.AppliedOverlays(graph));
    }

    [Fact]
    public void GraphFactory_createsEmptyInMemoryCpg()
    {
        CpgGraph graph = X2CpgGraphFactory.CreateEmptyGraph();

        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void UtilityMethods_matchJoernX2CpgBehavior()
    {
        Assert.Equal("value", StringUtils.StripQuotes("\"value\""));
        Assert.Equal("value", StringUtils.StripQuotes("'value'"));

        DirectoryInfo temporaryDirectory = global::Infrastructure.Analysis.Engine.X2Cpg.X2Cpg.WriteCodeToFile("class Demo {}", "analysis-x2cpg-", ".cs");

        Assert.True(temporaryDirectory.Exists);
        Assert.True(File.Exists(Path.Combine(temporaryDirectory.FullName, "Test.cs")));
    }

    [Fact]
    public void Frontend_canCreateCpgWithDefaultOverlays()
    {
        FakeFrontend frontend = new();

        CpgGraph graph = ((IX2CpgFrontend)frontend).CreateCpgWithOverlays(frontend.DefaultConfig);

        Assert.Contains(graph.Nodes, node => node.Kind == CpgNodeKind.File);
        Assert.Contains(CallGraphLayer.OverlayNameValue, Overlays.AppliedOverlays(graph));
    }

    private sealed class FakeFrontend : IX2CpgFrontend
    {
        public X2CpgConfig DefaultConfig { get; } = new();

        public CpgGraph CreateCpg(X2CpgConfig config)
        {
            CpgGraph graph = global::Infrastructure.Analysis.Engine.X2Cpg.X2Cpg.NewEmptyCpg(config.OutputPath);
            graph.CreateNode(CpgNodeKind.File).SetProperty("Name", "Demo.cs");
            return graph;
        }

        public void Dispose()
        {
        }
    }
}
