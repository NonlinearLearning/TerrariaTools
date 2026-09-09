using NLCPG.Analysis.FlowSummaries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests.Cpg;

public sealed class FlowSummaryResolverTests
{
    [Fact]
    public void Constructor_ProjectLayerContainsDuplicateStableKey_ThrowsStableValidationError()
    {
        var first = CreateLegacySummary();
        var duplicate = first with
        {
            Sources = new[] { NLCPGFlowSummaryEndpoint.Receiver },
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new NLCPGFlowSummaryRegistry(new[] { first, duplicate }));

        Assert.Equal(
            $"Flow summary source 'Project' contains duplicate method key '{first.StableKey}'.",
            exception.Message);
    }

    [Fact]
    public void Resolve_MultiMappingProjectSummary_TakesPrecedenceAndPreservesMappingOrder()
    {
        var key = new FlowSummaryMethodKey(
            "Demo.Assembly",
            "Demo.Helpers",
            "Map",
            GenericArity: 0,
            ParameterCount: 2,
            ParameterNames: new[] { "source", "destination" },
            ParameterTypeShapes: new[] { "System.String", "System.String" });
        var framework = new FlowSummary(
            key,
            new[]
            {
                new FlowSummaryMapping(
                    FlowSummaryEndpoint.Parameter(0),
                    FlowSummaryEndpoint.Return,
                    FlowSummaryMappingKind.Explicit),
            });
        var user = framework with
        {
            Mappings = new[]
            {
                new FlowSummaryMapping(
                    FlowSummaryEndpoint.Parameter(1),
                    FlowSummaryEndpoint.Return,
                    FlowSummaryMappingKind.Explicit),
            },
        };
        var project = framework with
        {
            Mappings = new[]
            {
                new FlowSummaryMapping(
                    FlowSummaryEndpoint.Receiver,
                    FlowSummaryEndpoint.Parameter(1),
                    FlowSummaryMappingKind.Explicit),
                new FlowSummaryMapping(
                    FlowSummaryEndpoint.Parameter(0),
                    FlowSummaryEndpoint.Return,
                    FlowSummaryMappingKind.PassThrough),
            },
        };
        var registry = new NLCPGFlowSummaryRegistry(
            projectSummaries: new[] { project },
            userSummaries: new[] { user },
            frameworkSummaries: new[] { framework });

        var resolved = registry.Resolve(key);

        Assert.Equal(FlowSummaryResolution.Project, resolved.Resolution);
        Assert.Same(project, resolved.Summary);
        Assert.Equal(project.Mappings, resolved.Mappings);
        Assert.Equal(FlowSummaryResolution.Unknown, registry.Resolve(key with { MethodName = "Missing" }).Resolution);
    }

    [Fact]
    public void DefaultFrameworkCatalog_BindsObjectToStringFromCurrentCompilation()
    {
        var (compilation, invocation) = CreateObjectToStringInvocation("FlowSummaryDefaultCatalog");
        var methodKey = FlowSummaryMethodKey.From(invocation.TargetMethod);
        var summaries = NLCPGDefaultFlowSummaries.CreateForCompilation(compilation);

        var summary = Assert.Single(summaries);
        Assert.Equal(methodKey.StableKey, summary.MethodKey.StableKey);
        Assert.Equal(
            invocation.TargetMethod.ContainingAssembly.Identity.ToString(),
            summary.MethodKey.AssemblyIdentity);

        var resolver = new CallFlowResolver(new NLCPGFlowSummaryRegistry(
            projectSummaries: null,
            userSummaries: null,
            frameworkSummaries: summaries));
        var resolved = resolver.Resolve(
            invocation,
            FlowSummaryEndpoint.Receiver,
            FlowSummaryEndpoint.Return);

        Assert.Equal(ResolvedCallFlowStatus.Resolved, resolved.Status);
        Assert.Equal(FlowSummaryResolution.Framework, resolved.Resolution);
        Assert.Equal(FlowSummaryMappingKind.Explicit, resolved.Mapping!.Kind);
    }

    [Fact]
    public void DefaultFrameworkCatalog_UsesTheBoundReferenceIdentityForEachCompilation()
    {
        var first = CreateObjectToStringInvocation("FlowSummaryReferenceOne");
        var second = CreateObjectToStringInvocation("FlowSummaryReferenceTwo");

        var firstSummary = Assert.Single(
            NLCPGDefaultFlowSummaries.CreateForCompilation(first.Compilation));
        var secondSummary = Assert.Single(
            NLCPGDefaultFlowSummaries.CreateForCompilation(second.Compilation));

        Assert.Equal(
            first.Invocation.TargetMethod.ContainingAssembly.Identity.ToString(),
            firstSummary.MethodKey.AssemblyIdentity);
        Assert.Equal(
            second.Invocation.TargetMethod.ContainingAssembly.Identity.ToString(),
            secondSummary.MethodKey.AssemblyIdentity);
        Assert.Equal(firstSummary.MethodKey.StableKey, secondSummary.MethodKey.StableKey);
    }

    [Fact]
    public void DefaultFrameworkCatalog_LeavesUndeclaredMethodsUnknown()
    {
        var (compilation, invocation) = CreateObjectToStringInvocation("FlowSummaryUnknownCatalog");
        var registry = new NLCPGFlowSummaryRegistry(
            projectSummaries: null,
            userSummaries: null,
            frameworkSummaries: NLCPGDefaultFlowSummaries.CreateForCompilation(compilation));

        var resolved = registry.Resolve(FlowSummaryMethodKey.From(invocation.TargetMethod) with
        {
            MethodName = "Missing"
        });

        Assert.Equal(FlowSummaryResolution.Unknown, resolved.Resolution);
        Assert.Empty(resolved.Mappings);
    }

    [Fact]
    public void Registry_RejectsDuplicateCompleteKeysWithinEverySourceLayer()
    {
        var (compilation, invocation) = CreateObjectToStringInvocation("FlowSummaryDuplicateCatalog");
        var summary = Assert.Single(NLCPGDefaultFlowSummaries.CreateForCompilation(compilation));

        foreach (var source in new[] { "Project", "User", "Framework" })
        {
            var exception = Assert.Throws<InvalidOperationException>(() => source switch
            {
                "Project" => new NLCPGFlowSummaryRegistry(
                    new[] { summary, summary }, null, null),
                "User" => new NLCPGFlowSummaryRegistry(
                    null, new[] { summary, summary }, null),
                _ => new NLCPGFlowSummaryRegistry(
                    null, null, new[] { summary, summary }),
            });

            Assert.Equal(
                $"Flow summary source '{source}' contains duplicate method key '{summary.MethodKey.StableKey}'.",
                exception.Message);
        }
    }

    [Fact]
    public void Resolve_ExtensionMethodWithNamedRefArgument_UsesNormalizedMethodKeyAndExactEndpointKinds()
    {
        const string source = """
            namespace Demo;
            public static class Extensions
            {
                public static T Map<T>(this string receiver, T value, ref T destination)
                {
                    destination = value;
                    return value;
                }
            }
            public sealed class Consumer
            {
                public void Run()
                {
                    string receiver = string.Empty;
                    int destination = 0;
                    _ = receiver.Map(value: 5, destination: ref destination);
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, path: "flow-summary.cs");
        var compilation = CSharpCompilation.Create(
            "FlowSummaryContract",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocationSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = Assert.IsAssignableFrom<IInvocationOperation>(semanticModel.GetOperation(invocationSyntax));
        var key = FlowSummaryMethodKey.From(invocation.TargetMethod);
        var summary = new FlowSummary(
            key,
            new[]
            {
                new FlowSummaryMapping(
                    FlowSummaryEndpoint.Parameter(1),
                    FlowSummaryEndpoint.RefParameter(2),
                    FlowSummaryMappingKind.Explicit),
            });
        var resolver = new CallFlowResolver(new NLCPGFlowSummaryRegistry(
            projectSummaries: new[] { summary },
            userSummaries: null,
            frameworkSummaries: null));

        var resolved = resolver.Resolve(
            invocation,
            FlowSummaryEndpoint.Parameter(1),
            FlowSummaryEndpoint.RefParameter(2));
        var mismatch = resolver.Resolve(
            invocation,
            FlowSummaryEndpoint.Parameter(1),
            FlowSummaryEndpoint.OutParameter(2));

        Assert.Equal(ResolvedCallFlowStatus.Resolved, resolved.Status);
        Assert.Equal(FlowSummaryResolution.Project, resolved.Resolution);
        Assert.Equal(key.StableKey, resolved.MethodKey.StableKey);
        Assert.Equal(ResolvedCallFlowStatus.SignatureMismatch, mismatch.Status);
    }

    [Fact]
    public void BuildFromSource_ExternalResolvedSummary_ProjectsLabeledBridge()
    {
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
            CallFlowResolver = new ToStringFlowResolver(),
        };
        var graph = new NLCPGBuilder(options).BuildFromSource(
            "public sealed class Sample { public string Run(object value) => value.ToString(); }",
            "summary-external.cs");

        var edge = Assert.Single(graph.Edges.Where(candidate =>
            candidate.Kind == NLCPGEdgeKind.InterproceduralDataFlow &&
            candidate.StructuredLabel?.InterproceduralBridgeKind == NLCPGInterproceduralBridgeKind.SummaryMapping));

        Assert.Equal(FlowSummaryResolution.Project, edge.StructuredLabel!.FlowSummaryResolution);
        Assert.Equal(FlowSummaryEndpointKind.Receiver, edge.StructuredLabel.FlowSummarySource!.Kind);
        Assert.Equal(FlowSummaryEndpointKind.Return, edge.StructuredLabel.FlowSummaryTarget!.Kind);
    }

    [Fact]
    public void BuildFromSource_SummaryCallSiteBudget_RecordsStableTruncation()
    {
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
            CallFlowResolver = new TwoMappingToStringFlowResolver(),
            FlowSummaryOptions = new NLCPGFlowSummaryOptions(MaxMappingsPerCallSite: 1),
        };
        var builder = new NLCPGBuilder(options);

        var graph = builder.BuildFromSource(
            "public sealed class Sample { public string Run(object value) => value.ToString(); }",
            "summary-budget.cs");

        Assert.Equal(1, builder.LastFlowSummaryMetrics.ResolvedMappings);
        Assert.Equal(1, builder.LastFlowSummaryMetrics.TruncatedMappings);
        Assert.Single(graph.Edges.Where(edge =>
            edge.StructuredLabel?.InterproceduralBridgeKind == NLCPGInterproceduralBridgeKind.SummaryMapping));
    }

    [Fact]
    public void BuildFromSource_SummaryBridge_DopOneAndSixteenProduceIdenticalStableEdges()
    {
        const string source = "public sealed class Sample { public string Run(object value) => value.ToString(); }";
        var sequential = BuildSummaryGraph(source, 1);
        var parallel = BuildSummaryGraph(source, 16);

        Assert.Equal(GetStableEdges(sequential), GetStableEdges(parallel));
    }

    private static NLCPGGraph BuildSummaryGraph(string source, int degreeOfParallelism)
    {
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = degreeOfParallelism,
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
            CallFlowResolver = new ToStringFlowResolver(),
        };
        return new NLCPGBuilder(options).BuildFromSource(source, "summary-dop.cs");
    }

    private static IReadOnlyList<string> GetStableEdges(NLCPGGraph graph)
    {
        return graph.Edges
            .OrderBy(edge => edge.SourceNodeId)
            .ThenBy(edge => edge.Kind)
            .ThenBy(edge => edge.TargetNodeId)
            .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
            .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
            .ToArray();
    }

    private class ToStringFlowResolver : ICallFlowResolver
    {
        public ResolvedCallFlow Resolve(
            IInvocationOperation invocation,
            FlowSummaryEndpoint source,
            FlowSummaryEndpoint target)
        {
            return ResolveAll(invocation).Single();
        }

        public virtual IReadOnlyList<ResolvedCallFlow> ResolveAll(IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name != "ToString")
            {
                return Array.Empty<ResolvedCallFlow>();
            }

            var key = FlowSummaryMethodKey.From(invocation.TargetMethod);
            var mapping = new FlowSummaryMapping(
                FlowSummaryEndpoint.Receiver,
                FlowSummaryEndpoint.Return,
                FlowSummaryMappingKind.Explicit);
            return new[]
            {
                new ResolvedCallFlow(
                    ResolvedCallFlowStatus.Resolved,
                    FlowSummaryResolution.Project,
                    key,
                    mapping,
                    null),
            };
        }
    }

    private sealed class TwoMappingToStringFlowResolver : ToStringFlowResolver
    {
        public override IReadOnlyList<ResolvedCallFlow> ResolveAll(IInvocationOperation invocation)
        {
            var resolved = base.ResolveAll(invocation);
            if (resolved.Count == 0)
            {
                return resolved;
            }

            var first = resolved[0];
            return new[]
            {
                first,
                first with
                {
                    Mapping = new FlowSummaryMapping(
                        FlowSummaryEndpoint.Receiver,
                        FlowSummaryEndpoint.Receiver,
                        FlowSummaryMappingKind.Explicit),
                },
            };
        }
    }

    private static NLCPGFlowSummary CreateLegacySummary()
    {
        return new NLCPGFlowSummary(
            "Demo.Assembly",
            "Demo.Helpers",
            "Map",
            0,
            new[] { NLCPGFlowSummaryEndpoint.Parameter(0) },
            NLCPGFlowSummaryEndpoint.Return);
    }

    private static (CSharpCompilation Compilation, IInvocationOperation Invocation) CreateObjectToStringInvocation(
        string assemblyName)
    {
        const string source = "public sealed class Sample { public string Run(object value) => value.ToString(); }";
        var tree = CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var invocationSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = Assert.IsAssignableFrom<IInvocationOperation>(
            compilation.GetSemanticModel(tree).GetOperation(invocationSyntax));
        return (compilation, invocation);
    }
}
