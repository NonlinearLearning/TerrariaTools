using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Analysis.FlowSummaries;
using NLISSN.Application;
using Xunit;

namespace RoslynPrototype.Tests.Cpg;

public sealed class FlowSummaryRealCallsiteTests
{
    [Fact]
    public void DefaultCatalog_ResolvesObjectToStringAtARealCallsite()
    {
        const string source = "public sealed class Sample { public string Run(object value) => value.ToString(); }";
        var (compilation, invocations) = Compile(source, "real-object-tostring.cs");
        var invocation = Assert.Single(invocations, candidate => candidate.Syntax.ToString() == "value.ToString()")
            ?? throw new InvalidOperationException("The object ToString invocation was not bound.");

        var result = FlowSummaryResolverFactory.Create(compilation).Resolve(
            invocation,
            FlowSummaryEndpoint.Receiver,
            FlowSummaryEndpoint.Return);

        Assert.Equal(ResolvedCallFlowStatus.Resolved, result.Status);
        Assert.Equal(FlowSummaryResolution.Framework, result.Resolution);
        Assert.Equal(FlowSummaryEndpoint.Receiver, result.Mapping!.Source);
        Assert.Equal(FlowSummaryEndpoint.Return, result.Mapping.Target);
    }

    [Fact]
    public void DefaultCatalog_DoesNotUseMethodNameToResolvePathCombineOrTaskFromResult()
    {
        const string source = """
            using System.IO;
            using System.Threading.Tasks;
            public sealed class Sample
            {
                public string Combine() => Path.Combine("a", "b");
                public Task<int> Wrap() => Task.FromResult(1);
            }
            """;
        var (compilation, invocations) = Compile(source, "real-candidates.cs");
        var resolver = FlowSummaryResolverFactory.Create(compilation);

        Assert.All(invocations, invocation =>
        {
            var result = resolver.Resolve(
                invocation,
                FlowSummaryEndpoint.Parameter(0),
                FlowSummaryEndpoint.Return);
            Assert.Equal(ResolvedCallFlowStatus.Unknown, result.Status);
        });
    }

    [Fact]
    public void DefaultCatalog_DoesNotTreatRoslynSymbolToStringAsObjectToString()
    {
        const string source = """
            using Microsoft.CodeAnalysis;
            public sealed class Sample
            {
                public string Run(IMethodSymbol symbol) => symbol.ToString();
            }
            """;
        var (compilation, invocations) = Compile(source, "real-roslyn-symbol-tostring.cs");
        var invocation = Assert.Single(invocations);
        var resolver = FlowSummaryResolverFactory.Create(compilation);

        var result = resolver.Resolve(
            invocation,
            FlowSummaryEndpoint.Receiver,
            FlowSummaryEndpoint.Return);

        Assert.Equal(
            FlowSummaryMethodKey.From(GetObjectToStringMethod(compilation)),
            FlowSummaryMethodKey.From(invocation.TargetMethod));
        Assert.Equal(
            compilation.GetSpecialType(SpecialType.System_Object)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            NLCPGDefaultFlowSummaries.CreateForCompilation(compilation)
                .Single()
                .ReceiverTypeShape);
        Assert.Equal(ResolvedCallFlowStatus.SignatureMismatch, result.Status);
    }

    [Fact]
    public void DefaultCatalog_LeavesLinqTryGetValueAndFileWriteAsUnknown()
    {
        const string source = """
            using System.Collections.Generic;
            using System.IO;
            using System.Linq;
            public sealed class Sample
            {
                public IEnumerable<int> Query(IEnumerable<int> values) =>
                    values.Where(value => value > 0).Select(value => value + 1);

                public bool Lookup(Dictionary<string, int> values, string key, out int result) =>
                    values.TryGetValue(key, out result);

                public void Write(string path, string value) => File.WriteAllText(path, value);
            }
            """;
        var (compilation, invocations) = Compile(source, "real-excluded-candidates.cs");
        var resolver = FlowSummaryResolverFactory.Create(compilation);

        Assert.All(invocations, invocation =>
        {
            var result = resolver.Resolve(
                invocation,
                FlowSummaryEndpoint.Receiver,
                FlowSummaryEndpoint.Return);
            Assert.Equal(ResolvedCallFlowStatus.Unknown, result.Status);
        });
    }

    private static (CSharpCompilation Compilation, IReadOnlyList<IInvocationOperation> Invocations) Compile(
        string source,
        string path)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var references = new[]
        {
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(System.IO.Path).Assembly.Location,
            typeof(System.Threading.Tasks.Task).Assembly.Location,
            typeof(Dictionary<,>).Assembly.Location,
            typeof(IMethodSymbol).Assembly.Location,
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(node => semanticModel.GetOperation(node))
            .OfType<IInvocationOperation>()
            .ToArray();
        return (compilation, invocations);
    }

    private static IMethodSymbol GetObjectToStringMethod(Compilation compilation)
    {
        return compilation.GetSpecialType(SpecialType.System_Object)
            .GetMembers(nameof(object.ToString))
            .OfType<IMethodSymbol>()
            .Single(method => method.Parameters.Length == 0);
    }
}
