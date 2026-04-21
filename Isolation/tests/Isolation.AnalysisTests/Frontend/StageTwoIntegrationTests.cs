using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class StageTwoIntegrationTests : IDisposable
{
    private readonly string tempDirectory;

    public StageTwoIntegrationTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-stage2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_buildsTypeBindingCallGraphAndCfg()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "Sample.cs"),
            """
            namespace Demo;

            public class Base
            {
            }

            public sealed class Sample : Base
            {
                public int Add(int left, int right)
                {
                    var sum = left + right;
                    if (sum > 0)
                    {
                        return Inc(sum);
                    }

                    return 0;
                }

                public int Inc(int value)
                {
                    return value + 1;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode sampleType = Assert.Single(graph.GetNodes(CpgNodeKind.TypeDecl).Where(node => HasPropertyValue(node, "Name", "Sample")));
        CpgNode baseType = Assert.Single(graph.GetNodes(CpgNodeKind.Type).Where(node => HasPropertyValue(node, "FullName", "Demo.Base")));
        Assert.Contains(
            graph.GetOutgoingEdges(sampleType.Id, CpgEdgeKind.InheritsFrom),
            edge => edge.TargetId == baseType.Id);

        CpgNode localSum = Assert.Single(graph.GetNodes(CpgNodeKind.Local).Where(node => HasPropertyValue(node, "Name", "sum")));
        Assert.Equal("int", GetStringProperty(localSum, "TypeFullName"));
        Assert.Contains(
            graph.GetOutgoingEdges(localSum.Id, CpgEdgeKind.EvalType),
            edge => graph.GetNode(edge.TargetId).Kind == CpgNodeKind.Type);

        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Identifier),
            node => HasPropertyValue(node, "Name", "sum") && graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ref).Any(edge => edge.TargetId == localSum.Id));

        CpgNode addMethod = Assert.Single(graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Add")));
        CpgNode incMethod = Assert.Single(graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Inc")));
        CpgNode incCall = Assert.Single(graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Inc")));

        Assert.Contains(
            graph.GetOutgoingEdges(incCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == incMethod.Id);

        CpgNode ifNode = Assert.Single(graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "IF")));
        Assert.Contains(
            graph.GetOutgoingEdges(localSum.Id, CpgEdgeKind.Cfg),
            edge => edge.TargetId == ifNode.Id);

        Assert.NotEmpty(graph.GetOutgoingEdges(addMethod.Id, CpgEdgeKind.Ast));
    }

    [Fact]
    public void CreateGraph_buildsConstructorAndPropertyAccessorMethods()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "AccessorSample.cs"),
            """
            namespace Demo;

            public sealed class Counter
            {
                public Counter(int seed)
                {
                    Value = seed;
                }

                public int Value { get; set; }
            }

            public static class Entry
            {
                public static int Run()
                {
                    var counter = new Counter(3);
                    return counter.Value;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode constructorMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", ".ctor")));
        CpgNode constructorCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", ".ctor")));
        Assert.Contains(
            graph.GetOutgoingEdges(constructorCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == constructorMethod.Id);

        CpgNode getterMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "get_Value")));
        CpgNode setterMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "set_Value")));
        CpgNode getterCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "get_Value")));
        CpgNode setterCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "set_Value")));

        Assert.Equal("int", GetStringProperty(getterMethod, "ReturnTypeFullName"));
        Assert.Contains(
            graph.GetOutgoingEdges(getterCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == getterMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(setterCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == setterMethod.Id);
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.MethodParameterIn),
            node => HasPropertyValue(node, "Name", "value") &&
                    graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).Any(edge => edge.SourceId == setterMethod.Id));
    }

    [Fact]
    public void CreateGraph_buildsSwitchAndTryCfg()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ControlFlowSample.cs"),
            """
            namespace Demo;

            public sealed class Flow
            {
                public int Run(int value)
                {
                    switch (value)
                    {
                        case 1:
                            value = value + 1;
                            break;
                        default:
                            value = value + 2;
                            break;
                    }

                    try
                    {
                        if (value > 0)
                        {
                            throw new System.InvalidOperationException();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        value = 10;
                    }
                    finally
                    {
                        value = value + 1;
                    }

                    return value;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode switchNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "SWITCH")));
        CpgNode tryNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "TRY")));
        CpgNode catchNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "CATCH")));
        CpgNode finallyNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "FINALLY")));

        Assert.NotEmpty(graph.GetOutgoingEdges(switchNode.Id, CpgEdgeKind.Cfg));
        Assert.Contains(
            graph.GetOutgoingEdges(tryNode.Id, CpgEdgeKind.Cfg),
            edge => edge.TargetId == catchNode.Id || edge.TargetId == finallyNode.Id || graph.GetNode(edge.TargetId).Kind == CpgNodeKind.Block);
        Assert.NotEmpty(graph.GetOutgoingEdges(catchNode.Id, CpgEdgeKind.Cfg));
        Assert.NotEmpty(graph.GetOutgoingEdges(finallyNode.Id, CpgEdgeKind.Cfg));
    }

    [Fact]
    public void CreateGraph_buildsLambdaAndMethodReference()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "LambdaSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Sample
            {
                public int Inc(int value)
                {
                    return value + 1;
                }

                public int Run()
                {
                    Func<int, int> projector = Inc;
                    Func<int, int> lambda = x => x + 2;
                    return lambda(projector(1));
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode incMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Inc")));
        CpgNode methodRefNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.MethodRef).Where(node => HasPropertyValue(node, "Name", "Inc")));
        Assert.Contains(
            graph.GetOutgoingEdges(methodRefNode.Id, CpgEdgeKind.MethodRef),
            edge => edge.TargetId == incMethod.Id);

        CpgNode lambdaMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => GetStringProperty(node, "Name").StartsWith("<lambda>", StringComparison.Ordinal)));
        CpgNode lambdaRefNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.MethodRef).Where(node => GetStringProperty(node, "Name").StartsWith("<lambda>", StringComparison.Ordinal)));

        Assert.Contains(
            graph.GetOutgoingEdges(lambdaRefNode.Id, CpgEdgeKind.MethodRef),
            edge => edge.TargetId == lambdaMethod.Id);
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.MethodParameterIn),
            node => HasPropertyValue(node, "Name", "x") &&
                    graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).Any(edge => edge.SourceId == lambdaMethod.Id));
    }

    [Fact]
    public void CreateGraph_buildsCoreExpressionOperatorCalls()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ExpressionSample.cs"),
            """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class Expressions
            {
                public async Task<int> RunAsync(int value, int[] items)
                {
                    int awaited = await Task.FromResult(value);
                    int chosen = awaited > 0 ? awaited : items[0];
                    object boxed = chosen;
                    return (int)boxed;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode awaitCall = Assert.Single(graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "await")));
        CpgNode conditionalCall = Assert.Single(graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "?:")));
        CpgNode castCall = Assert.Single(graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "cast")));
        CpgNode indexCall = Assert.Single(graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "[]")));

        Assert.Equal("int", GetStringProperty(awaitCall, "TypeFullName"));
        Assert.Equal("int", GetStringProperty(conditionalCall, "TypeFullName"));
        Assert.Equal("int", GetStringProperty(castCall, "TargetTypeFullName"));
        Assert.Equal("int", GetStringProperty(indexCall, "TypeFullName"));
        Assert.NotEmpty(graph.GetOutgoingEdges(awaitCall.Id, CpgEdgeKind.Ast));
        Assert.NotEmpty(graph.GetOutgoingEdges(conditionalCall.Id, CpgEdgeKind.Ast));
        Assert.NotEmpty(graph.GetOutgoingEdges(castCall.Id, CpgEdgeKind.Ast));
        Assert.NotEmpty(graph.GetOutgoingEdges(indexCall.Id, CpgEdgeKind.Ast));
    }

    [Fact]
    public void CreateGraph_buildsAdditionalJoernStyleExpressionCalls()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "AdditionalExpressionSample.cs"),
            """
            namespace Demo;

            public sealed class AdditionalExpressions
            {
                public int Value { get; set; }

                public int Run(AdditionalExpressions? other, int value)
                {
                    int maybe = other?.Value ?? 0;
                    string text = $"value:{maybe}";
                    int[] values = new[] { maybe, -value };
                    int current = this.Value;
                    return values[0] + text.Length + current;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "?."));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "formatString"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "arrayInitializer"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Call), node => HasPropertyValue(node, "Name", "-"));
        Assert.Contains(graph.GetNodes(CpgNodeKind.Identifier), node => HasPropertyValue(node, "Name", "this"));
    }

    [Fact]
    public void CreateGraph_buildsUsingAndDoWhileControlFlow()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "UsingDoSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Resource : IDisposable
            {
                public void Dispose()
                {
                }
            }

            public sealed class Flow
            {
                public int Run(int value)
                {
                    using (var resource = new Resource())
                    {
                        value = value + 1;
                    }

                    do
                    {
                        value = value - 1;
                    }
                    while (value > 0);

                    return value;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode tryNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "TRY")));
        CpgNode finallyNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "FINALLY")));
        CpgNode doNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.ControlStructure).Where(node => HasPropertyValue(node, "ControlStructureType", "DO")));
        CpgNode disposeCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Dispose")));

        Assert.Contains(
            graph.GetOutgoingEdges(tryNode.Id, CpgEdgeKind.Cfg),
            edge => graph.GetNode(edge.TargetId).Kind == CpgNodeKind.Block);
        Assert.NotEmpty(graph.GetOutgoingEdges(finallyNode.Id, CpgEdgeKind.Cfg));
        Assert.Contains(
            graph.GetOutgoingEdges(doNode.Id, CpgEdgeKind.Cfg),
            edge => graph.GetNode(edge.TargetId).Kind == CpgNodeKind.Block);
        Assert.NotEmpty(graph.GetIncomingEdges(disposeCall.Id, CpgEdgeKind.Ast));
    }

    [Fact]
    public void CreateGraph_buildsEnumAndRecordMembers()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "EnumRecordSample.cs"),
            """
            namespace Demo;

            public enum Status : byte
            {
                Ready = 1,
                Done = 2
            }

            public sealed record Item(int Id, Status State);
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode statusType = Assert.Single(
            graph.GetNodes(CpgNodeKind.TypeDecl).Where(node => HasPropertyValue(node, "Name", "Status")));
        CpgNode byteType = Assert.Single(
            graph.GetNodes(CpgNodeKind.Type).Where(node => HasPropertyValue(node, "FullName", "byte")));
        Assert.Equal("byte", GetStringProperty(statusType, "AliasTypeFullName"));
        Assert.Contains(
            graph.GetOutgoingEdges(statusType.Id, CpgEdgeKind.AliasOf),
            edge => edge.TargetId == byteType.Id);
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Member),
            node => HasPropertyValue(node, "Name", "Ready") &&
                    HasPropertyValue(node, "TypeFullName", "byte") &&
                    graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).Any(edge => edge.SourceId == statusType.Id));
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Member),
            node => HasPropertyValue(node, "Name", "Id") &&
                    HasPropertyValue(node, "TypeFullName", "int"));
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Member),
            node => HasPropertyValue(node, "Name", "State") &&
                    HasPropertyValue(node, "TypeFullName", "Demo.Status"));
    }

    [Fact]
    public void CreateGraph_buildsLocalFunctionAndAnonymousObjectShape()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "LocalFunctionAnonymousSample.cs"),
            """
            namespace Demo;

            public sealed class Sample
            {
                public int Run(int value)
                {
                    int Twice(int input)
                    {
                        return input * 2;
                    }

                    var shape = new { Count = Twice(value), value };
                    return shape.Count;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode runMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Run")));
        CpgNode localFunction = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "Name", "Twice")));
        CpgNode twiceCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Twice")));
        CpgNode anonymousType = Assert.Single(
            graph.GetNodes(CpgNodeKind.TypeDecl).Where(node => HasPropertyValue(node, "IsAnonymous", "true")));

        CpgEdge localFunctionAstEdge = Assert.Single(graph.GetIncomingEdges(localFunction.Id, CpgEdgeKind.Ast));
        Assert.Contains(
            graph.GetIncomingEdges(localFunctionAstEdge.SourceId, CpgEdgeKind.Ast),
            edge => edge.SourceId == runMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(twiceCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == localFunction.Id);
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Member),
            node => HasPropertyValue(node, "Name", "Count") &&
                    graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).Any(edge => edge.SourceId == anonymousType.Id));
        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Member),
            node => HasPropertyValue(node, "Name", "value") &&
                    graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).Any(edge => edge.SourceId == anonymousType.Id));
    }

    [Fact]
    public void CreateGraph_linksFieldAccessCallToMember()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "FieldAccessSample.cs"),
            """
            namespace Demo;

            public sealed class Counter
            {
                private readonly int value;

                public Counter(int seed)
                {
                    value = seed;
                }

                public int Read()
                {
                    return this.value;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode memberNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Member).Where(node => HasPropertyValue(node, "FullName", "Demo.Counter.value")));
        CpgNode fieldAccessCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node =>
                HasPropertyValue(node, "Name", ".") &&
                HasPropertyValue(node, "FieldFullName", "Demo.Counter.value")));

        Assert.Contains(
            graph.GetOutgoingEdges(fieldAccessCall.Id, CpgEdgeKind.Ref),
            edge => edge.TargetId == memberNode.Id);
    }

    [Fact]
    public void CreateGraph_linksInterfaceDispatchToImplementationMethod()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "InterfaceDispatchSample.cs"),
            """
            namespace Demo;

            public interface IWorker
            {
                int Work(int value);
            }

            public sealed class Worker : IWorker
            {
                public int Work(int value)
                {
                    return value + 1;
                }
            }

            public static class Entry
            {
                public static int Run(IWorker worker, int value)
                {
                    return worker.Work(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode interfaceMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.IWorker.Work(int)")));
        CpgNode implementationMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.Worker.Work(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Work")));

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == interfaceMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == implementationMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksVirtualDispatchToOverrideMethod()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "VirtualDispatchSample.cs"),
            """
            namespace Demo;

            public class BaseWorker
            {
                public virtual int Compute(int value)
                {
                    return value;
                }
            }

            public sealed class FancyWorker : BaseWorker
            {
                public override int Compute(int value)
                {
                    return value + 10;
                }
            }

            public static class Entry
            {
                public static int Run(BaseWorker worker, int value)
                {
                    return worker.Compute(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode baseMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.BaseWorker.Compute(int)")));
        CpgNode overrideMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.FancyWorker.Compute(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Compute")));

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == baseMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == overrideMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksDelegateInvocationToReferencedMethod()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "DelegateDispatchSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Sample
            {
                public int Inc(int value)
                {
                    return value + 1;
                }

                public int Run(int value)
                {
                    Func<int, int> projector = Inc;
                    return projector(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode incMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.Sample.Inc(int)")));
        CpgNode delegateInvokeCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Invoke")));

        Assert.Contains(
            graph.GetOutgoingEdges(delegateInvokeCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == incMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksVirtualDispatchAcrossMultipleInheritanceLevels()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "MultiLevelVirtualDispatchSample.cs"),
            """
            namespace Demo;

            public class RootWorker
            {
                public virtual int Compute(int value)
                {
                    return value;
                }
            }

            public class MidWorker : RootWorker
            {
            }

            public sealed class FinalWorker : MidWorker
            {
                public override int Compute(int value)
                {
                    return value + 100;
                }
            }

            public static class Entry
            {
                public static int Run(RootWorker worker, int value)
                {
                    return worker.Compute(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode rootMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.RootWorker.Compute(int)")));
        CpgNode finalMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.FinalWorker.Compute(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Compute")));

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == rootMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == finalMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksDelegateInvocationStoredInField()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "FieldDelegateDispatchSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Sample
            {
                private readonly Func<int, int> projector;

                public Sample()
                {
                    projector = Inc;
                }

                public int Inc(int value)
                {
                    return value + 1;
                }

                public int Run(int value)
                {
                    return projector(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode incMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.Sample.Inc(int)")));
        CpgNode delegateInvokeCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Invoke")));

        Assert.Contains(
            graph.GetOutgoingEdges(delegateInvokeCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == incMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksDelegateInvocationStoredInProperty()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "PropertyDelegateDispatchSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Sample
            {
                private Func<int, int> Projector { get; set; }

                public Sample()
                {
                    Projector = Inc;
                }

                public int Inc(int value)
                {
                    return value + 1;
                }

                public int Run(int value)
                {
                    return Projector(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode incMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.Sample.Inc(int)")));
        CpgNode delegateInvokeCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Invoke")));

        Assert.Contains(
            graph.GetOutgoingEdges(delegateInvokeCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == incMethod.Id);
    }

    [Fact]
    public void CreateGraph_prefersInternalMethodOverExternalStubForDynamicDispatch()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "InternalPreferredDispatchSample.cs"),
            """
            namespace Demo;

            public class BaseWorker
            {
                public virtual int Compute(int value)
                {
                    return value;
                }
            }

            public sealed class Worker : BaseWorker
            {
                public override int Compute(int value)
                {
                    return value + 1;
                }
            }

            public static class Entry
            {
                public static int Run(BaseWorker worker, int value)
                {
                    return worker.Compute(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Compute")));
        IReadOnlyList<CpgNode> targets = graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => HasPropertyValue(node, "Name", "Compute"))
            .ToList();

        Assert.DoesNotContain(
            targets,
            node => node.TryGetProperty<bool>("IsExternal", out bool isExternal) && isExternal);
        Assert.Contains(
            targets,
            node => HasPropertyValue(node, "FullName", "Demo.Worker.Compute(int)"));
    }

    [Fact]
    public void CreateGraph_linksLambdaDelegateInvocationStoredInProperty()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "PropertyLambdaDelegateDispatchSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Sample
            {
                private Func<int, int> Projector { get; set; }

                public Sample()
                {
                    Projector = value => value + 2;
                }

                public int Run(int value)
                {
                    return Projector(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode lambdaMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => GetStringProperty(node, "Name").StartsWith("<lambda>", StringComparison.Ordinal)));
        CpgNode delegateInvokeCall = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Invoke")));

        Assert.Contains(
            graph.GetOutgoingEdges(delegateInvokeCall.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == lambdaMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksInterfaceDispatchToInheritedImplementationMethod()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "InheritedInterfaceImplementationSample.cs"),
            """
            namespace Demo;

            public interface IWorker
            {
                int Work(int value);
            }

            public class BaseWorker : IWorker
            {
                public virtual int Work(int value)
                {
                    return value + 1;
                }
            }

            public sealed class FinalWorker : BaseWorker
            {
            }

            public static class Entry
            {
                public static int Run(IWorker worker, int value)
                {
                    return worker.Work(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode interfaceMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.IWorker.Work(int)")));
        CpgNode baseMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.BaseWorker.Work(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Work")));

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == interfaceMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == baseMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksInterfaceDispatchToExplicitImplementationMethod()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ExplicitInterfaceImplementationSample.cs"),
            """
            namespace Demo;

            public interface IWorker
            {
                int Work(int value);
            }

            public sealed class Worker : IWorker
            {
                int IWorker.Work(int value)
                {
                    return value + 5;
                }
            }

            public static class Entry
            {
                public static int Run(IWorker worker, int value)
                {
                    return worker.Work(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode interfaceMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.IWorker.Work(int)")));
        CpgNode explicitMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.Worker.Demo.IWorker.Work(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Work")));

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == interfaceMethod.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == explicitMethod.Id);
    }

    [Fact]
    public void CreateGraph_linksDynamicDispatchToInheritedBaseImplementationForDerivedStaticType()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "DerivedStaticTypeInheritedMethodSample.cs"),
            """
            namespace Demo;

            public class BaseWorker
            {
                public virtual int Compute(int value)
                {
                    return value + 1;
                }
            }

            public sealed class FinalWorker : BaseWorker
            {
            }

            public static class Entry
            {
                public static int Run(FinalWorker worker, int value)
                {
                    return worker.Compute(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode baseMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.BaseWorker.Compute(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Compute")));

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == baseMethod.Id);
    }

    [Fact]
    public void CreateGraph_prefersInternalOverrideOverExternalBaseStub()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ExternalBaseOverrideSample.cs"),
            """
            using System.IO;

            namespace Demo;

            public sealed class MyStream : MemoryStream
            {
                public override void WriteByte(byte value)
                {
                    base.WriteByte(value);
                }
            }

            public static class Entry
            {
                public static void Run(MemoryStream stream, byte value)
                {
                    stream.WriteByte(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode overrideMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.MyStream.WriteByte(byte)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node =>
                HasPropertyValue(node, "Name", "WriteByte") &&
                node.TryGetProperty<int>("Line", out int line) &&
                line == 17));
        IReadOnlyList<CpgNode> targets = graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => HasPropertyValue(node, "Name", "WriteByte"))
            .ToList();

        Assert.Contains(targets, node => node.Id == overrideMethod.Id);
        Assert.DoesNotContain(
            targets,
            node => node.TryGetProperty<bool>("IsExternal", out bool isExternal) && isExternal);
    }

    [Fact]
    public void CreateGraph_keepsDefaultInterfaceImplementationAsDynamicDispatchTarget()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "DefaultInterfaceMethodSample.cs"),
            """
            namespace Demo;

            public interface IWorker
            {
                int Work(int value)
                {
                    return value + 1;
                }
            }

            public sealed class Worker : IWorker
            {
            }

            public static class Entry
            {
                public static int Run(IWorker worker, int value)
                {
                    return worker.Work(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode interfaceMethod = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node => HasPropertyValue(node, "FullName", "Demo.IWorker.Work(int)")));
        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "Work")));
        IReadOnlyList<CpgNode> targets = graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => HasPropertyValue(node, "Name", "Work"))
            .ToList();

        Assert.Contains(targets, node => node.Id == interfaceMethod.Id);
        Assert.DoesNotContain(
            targets,
            node => HasPropertyValue(node, "FullName", "Demo.Worker.Work(int)"));
    }

    [Fact]
    public void CreateGraph_keepsExternalStubWhenDynamicDispatchHasNoInternalCandidate()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ExternalDynamicFallbackSample.cs"),
            """
            using System.IO;

            namespace Demo;

            public static class Entry
            {
                public static void Run(Stream stream, byte value)
                {
                    stream.WriteByte(value);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode callNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Call).Where(node => HasPropertyValue(node, "Name", "WriteByte")));
        IReadOnlyList<CpgNode> targets = graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => HasPropertyValue(node, "Name", "WriteByte"))
            .ToList();

        Assert.NotEmpty(targets);
        Assert.Contains(
            targets,
            node => node.TryGetProperty<bool>("IsExternal", out bool isExternal) && isExternal);
    }

    private static bool HasPropertyValue(CpgNode node, string propertyName, string expectedValue)
    {
        return node.TryGetProperty<string>(propertyName, out string? actualValue) &&
               string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
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
