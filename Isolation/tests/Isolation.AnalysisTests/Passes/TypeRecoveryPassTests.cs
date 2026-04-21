using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Logic.Analysis.Engine.Passes;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class TypeRecoveryPassTests : IDisposable
{
    private readonly string tempDirectory;

    public TypeRecoveryPassTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-type-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void CreateGraph_recoversDynamicCallTypeHintsFromAssignedConcreteType()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "DynamicCallSample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
                public int Run()
                {
                    return 1;
                }
            }

            public sealed class Entry
            {
                public int Go()
                {
                    dynamic worker = new Worker();
                    return worker.Run();
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode localNode = Assert.Single(graph.GetNodes(CpgNodeKind.Local).Where(node => Has(node, "Name", "worker")));
        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localPossibleTypes));
        Assert.Contains("Demo.Worker", localPossibleTypes ?? Array.Empty<string>());

        CpgNode? identifierNode = graph.GetNodes(CpgNodeKind.Identifier)
            .FirstOrDefault(node => Has(node, "Name", "worker") &&
                                    node.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes) &&
                                    possibleTypes.Contains("Demo.Worker"));

        Assert.True(identifierNode is not null);

        CpgNode? callNode = graph.GetNodes(CpgNodeKind.Call)
            .FirstOrDefault(node =>
                node.TryGetProperty<string>("Name", out string? name) &&
                !string.IsNullOrWhiteSpace(name) &&
                name.Contains("Run", StringComparison.Ordinal));
        Assert.True(callNode is not null);
        Assert.True(callNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? hintedMethodFullNames));
        Assert.Contains("Demo.Worker.Run()", hintedMethodFullNames ?? Array.Empty<string>());

        CpgNode workerRunMethod = Assert.Single(graph.GetNodes(CpgNodeKind.Method).Where(node => Has(node, "FullName", "Demo.Worker.Run()")));
        Assert.Contains(graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call), edge => edge.TargetId == workerRunMethod.Id);
    }

    [Fact]
    public void CreateGraph_recoversTypesThroughMethodReturnValues()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ReturnRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
                public int Run()
                {
                    return 1;
                }
            }

            public sealed class Entry
            {
                public dynamic GetWorker()
                {
                    return new Worker();
                }

                public int Go()
                {
                    dynamic worker = GetWorker();
                    return worker.Run();
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode getWorkerReturnNode = graph.GetNodes(CpgNodeKind.MethodReturn)
            .First(node =>
                node.TryGetProperty<long>("AstParentId", out long methodId) &&
                graph.GetNode(methodId).TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, "GetWorker", StringComparison.Ordinal));
        Assert.True(getWorkerReturnNode.TryGetProperty<string[]>("PossibleTypes", out string[]? returnPossibleTypes));
        Assert.Contains("Demo.Worker", returnPossibleTypes ?? Array.Empty<string>());

        CpgNode localNode = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "worker"));
        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localPossibleTypes));
        Assert.Contains("Demo.Worker", localPossibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_propagatesArgumentTypesIntoDynamicParametersAndReturns()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ParameterRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Entry
            {
                public dynamic Echo(dynamic value)
                {
                    return value;
                }

                public dynamic Run()
                {
                    dynamic input = new Worker();
                    dynamic output = Echo(input);
                    return output;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode valueParameter = graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .First(node => Has(node, "Name", "value"));
        CpgNode echoCall = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "Echo"));
        CpgNode inputLocal = graph.GetNodes(CpgNodeKind.Local)
            .First(node => Has(node, "Name", "input"));
        CpgNode echoMethod = graph.GetNodes(CpgNodeKind.Method)
            .First(node => Has(node, "Name", "Echo"));
        Assert.True(
            valueParameter.TryGetProperty<string[]>("PossibleTypes", out string[]? parameterTypes),
            $"{DescribeNode(valueParameter)} || {DescribeNode(echoCall)} || {DescribeNode(inputLocal)} || {DescribeNode(echoMethod)}");
        Assert.Contains("Demo.Worker", parameterTypes ?? Array.Empty<string>());

        CpgNode echoReturnNode = graph.GetNodes(CpgNodeKind.MethodReturn)
            .First(node =>
                node.TryGetProperty<long>("AstParentId", out long methodId) &&
                graph.GetNode(methodId).TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, "Echo", StringComparison.Ordinal));
        Assert.True(
            echoReturnNode.TryGetProperty<string[]>("PossibleTypes", out string[]? returnTypes),
            DescribeNode(echoReturnNode));
        Assert.Contains("Demo.Worker", returnTypes ?? Array.Empty<string>());

        CpgNode outputLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "output"));
        Assert.True(outputLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? outputTypes));
        Assert.Contains("Demo.Worker", outputTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversFieldLoadTypesThroughDynamicParameter()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ParameterFieldRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Box
            {
                public Worker Item = new Worker();
            }

            public sealed class Entry
            {
                public dynamic Load(dynamic box)
                {
                    return box.Item;
                }

                public dynamic Run()
                {
                    dynamic result = Load(new Box());
                    return result;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode boxParameter = graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .First(node => Has(node, "Name", "box"));
        Assert.True(boxParameter.TryGetProperty<string[]>("PossibleTypes", out string[]? parameterTypes));
        Assert.Contains("Demo.Box", parameterTypes ?? Array.Empty<string>());

        CpgNode fieldAccessNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node =>
                Has(node, "Name", ".") &&
                node.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes) &&
                possibleTypes.Contains("Demo.Worker"));
        Assert.True(fieldAccessNode.TryGetProperty<string[]>("PossibleTypes", out string[]? fieldTypes));
        Assert.Contains("Demo.Worker", fieldTypes ?? Array.Empty<string>());

        CpgNode loadReturnNode = graph.GetNodes(CpgNodeKind.MethodReturn)
            .First(node =>
                node.TryGetProperty<long>("AstParentId", out long methodId) &&
                graph.GetNode(methodId).TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, "Load", StringComparison.Ordinal));
        Assert.True(loadReturnNode.TryGetProperty<string[]>("PossibleTypes", out string[]? returnTypes));
        Assert.Contains("Demo.Worker", returnTypes ?? Array.Empty<string>());

        CpgNode resultLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "result"));
        Assert.True(resultLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? localTypes));
        Assert.Contains("Demo.Worker", localTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversPropertyGetterTypesThroughDynamicParameter()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ParameterPropertyRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Holder
            {
                public Worker Current { get; set; } = new Worker();
            }

            public sealed class Entry
            {
                public dynamic Load(dynamic holder)
                {
                    return holder.Current;
                }

                public dynamic Run()
                {
                    dynamic result = Load(new Holder());
                    return result;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode holderParameter = graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .First(node => Has(node, "Name", "holder"));
        Assert.True(holderParameter.TryGetProperty<string[]>("PossibleTypes", out string[]? parameterTypes));
        Assert.Contains("Demo.Holder", parameterTypes ?? Array.Empty<string>());

        CpgNode getterCallNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node =>
                Has(node, "Name", ".") &&
                node.TryGetProperty<string>("FieldFullName", out string? fieldFullName) &&
                string.Equals(fieldFullName, "holder.Current", StringComparison.Ordinal));
        Assert.True(
            getterCallNode.TryGetProperty<string[]>("PossibleTypes", out string[]? getterTypes),
            DescribeNode(getterCallNode));
        Assert.Contains("Demo.Worker", getterTypes ?? Array.Empty<string>());

        CpgNode loadReturnNode = graph.GetNodes(CpgNodeKind.MethodReturn)
            .First(node =>
                node.TryGetProperty<long>("AstParentId", out long methodId) &&
                graph.GetNode(methodId).TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, "Load", StringComparison.Ordinal));
        Assert.True(loadReturnNode.TryGetProperty<string[]>("PossibleTypes", out string[]? returnTypes));
        Assert.Contains("Demo.Worker", returnTypes ?? Array.Empty<string>());

        CpgNode resultLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "result"));
        Assert.True(resultLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? localTypes));
        Assert.Contains("Demo.Worker", localTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversPropertySetterTypesThroughDynamicParameter()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ParameterPropertySetterRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Holder
            {
                public Worker Current { get; set; } = new Worker();
            }

            public sealed class Entry
            {
                public dynamic Store(dynamic holder, dynamic worker)
                {
                    holder.Current = worker;
                    return holder;
                }

                public dynamic Run()
                {
                    dynamic result = Store(new Holder(), new Worker());
                    return result.Current;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode holderParameter = graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .First(node => Has(node, "Name", "holder"));
        Assert.True(holderParameter.TryGetProperty<string[]>("PossibleTypes", out string[]? holderTypes));
        Assert.Contains("Demo.Holder", holderTypes ?? Array.Empty<string>());

        CpgNode workerParameter = graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .First(node => Has(node, "Name", "worker"));
        Assert.True(workerParameter.TryGetProperty<string[]>("PossibleTypes", out string[]? workerTypes));
        Assert.Contains("Demo.Worker", workerTypes ?? Array.Empty<string>());

        CpgNode setterCallNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "set_Current"));
        Assert.True(
            setterCallNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? setterHints),
            DescribeNode(setterCallNode));
        Assert.Contains("Demo.Holder.set_Current(Demo.Worker)", setterHints ?? Array.Empty<string>());

        CpgNode resultLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "result"));
        Assert.True(resultLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? resultTypes));
        Assert.Contains("Demo.Holder", resultTypes ?? Array.Empty<string>());

        CpgNode getterCallNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node =>
                Has(node, "Name", ".") &&
                node.TryGetProperty<string>("FieldFullName", out string? fieldFullName) &&
                string.Equals(fieldFullName, "result.Current", StringComparison.Ordinal));
        Assert.True(getterCallNode.TryGetProperty<string[]>("PossibleTypes", out string[]? getterTypes));
        Assert.Contains("Demo.Worker", getterTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_lowersDynamicPropertyCompoundAssignmentToGetterOperatorSetter()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "PropertyCompoundAssignmentSample.cs"),
            """
            namespace Demo;

            public sealed class Counter
            {
                public int Count { get; set; }
            }

            public sealed class Entry
            {
                public dynamic Bump(dynamic counter)
                {
                    counter.Count += 1;
                    return counter.Count;
                }

                public int Run()
                {
                    return Bump(new Counter());
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode counterParameter = graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .First(node => Has(node, "Name", "counter"));
        Assert.True(counterParameter.TryGetProperty<string[]>("PossibleTypes", out string[]? counterTypes));
        Assert.Contains("Demo.Counter", counterTypes ?? Array.Empty<string>());

        CpgNode setterCallNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "set_Count"));
        Assert.True(
            setterCallNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? setterHints),
            DescribeNode(setterCallNode));
        Assert.Contains("Demo.Counter.set_Count(int)", setterHints ?? Array.Empty<string>());

        CpgNode getterCallNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "get_Count"));
        Assert.True(
            getterCallNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? getterHints),
            DescribeNode(getterCallNode));
        Assert.Contains("Demo.Counter.get_Count()", getterHints ?? Array.Empty<string>());

        CpgNode addCallNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "+"));
        Assert.Contains(graph.GetOutgoingEdges(addCallNode.Id, CpgEdgeKind.Ast), edge => edge.TargetId == getterCallNode.Id);

        CpgNode bumpReturnNode = graph.GetNodes(CpgNodeKind.MethodReturn)
            .First(node =>
                node.TryGetProperty<long>("AstParentId", out long methodId) &&
                graph.GetNode(methodId).TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, "Bump", StringComparison.Ordinal));
        Assert.True(bumpReturnNode.TryGetProperty<string[]>("PossibleTypes", out string[]? returnTypes));
        Assert.Contains("int", returnTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversTypesFromFieldAccessLoads()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "FieldRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Holder
            {
                public Worker Current { get; set; } = new Worker();

                public dynamic Load()
                {
                    dynamic worker = this.Current;
                    return worker;
                }
            }

            public sealed class Worker
            {
                public int Run()
                {
                    return 1;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode localNode = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "worker"));
        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localPossibleTypes));
        Assert.Contains("Demo.Worker", localPossibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversTypesFromImportAlias()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ImportAliasSample.cs"),
            """
            using AliasWorker = Demo.Worker;

            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Entry
            {
                public AliasWorker Create()
                {
                    AliasWorker worker = new AliasWorker();
                    return worker;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode importNode = Assert.Single(graph.GetNodes(CpgNodeKind.Import).Where(node => Has(node, "ImportedAs", "AliasWorker")));
        Assert.True(importNode.TryGetProperty<string>("ResolvedImportKind", out string? resolvedImportKind));
        Assert.Equal("TYPE", resolvedImportKind);

        CpgNode workerLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "worker"));
        Assert.True(workerLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("Demo.Worker", possibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversElementTypeForIndexAccess()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "IndexAccessSample.cs"),
            """
            namespace Demo;

            public sealed class Entry
            {
                public int Run()
                {
                    dynamic values = new int[] { 1, 2, 3 };
                    dynamic first = values[0];
                    return first;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode firstLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "first"));
        Assert.True(firstLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? localPossibleTypes));
        Assert.Contains("int", localPossibleTypes ?? Array.Empty<string>());

        CpgNode indexCall = graph.GetNodes(CpgNodeKind.Call).First(node => Has(node, "Name", "[]"));
        Assert.True(indexCall.TryGetProperty<string[]>("PossibleTypes", out string[]? callPossibleTypes));
        Assert.Contains("int", callPossibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recordsMethodReferenceAliasesOnDeclarations()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "MethodRefAliasSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Worker
            {
                public int Inc(int value)
                {
                    return value + 1;
                }

                public Func<int, int> Create()
                {
                    Func<int, int> projector = Inc;
                    return projector;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode projectorLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "projector"));
        Assert.True(projectorLocal.TryGetProperty<string[]>("AliasMethodFullNames", out string[]? aliases));
        Assert.Contains("Demo.Worker.Inc(int)", aliases ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversDynamicCallHintsFromMethodReferenceAlias()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "MethodRefDynamicInvokeSample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Worker
            {
                public int Inc(int value)
                {
                    return value + 1;
                }

                public int Run()
                {
                    dynamic projector = Inc;
                    return projector(1);
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode callNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "Name", "projector"));
        Assert.True(callNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? hintedMethodFullNames));
        Assert.Contains("Demo.Worker.Inc(int)", hintedMethodFullNames ?? Array.Empty<string>());

        CpgNode targetMethod = Assert.Single(graph.GetNodes(CpgNodeKind.Method)
            .Where(node => Has(node, "FullName", "Demo.Worker.Inc(int)")));
        Assert.Contains(graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call), edge => edge.TargetId == targetMethod.Id);
    }

    [Fact]
    public void CreateGraph_recoversReturnTypesFromMethodReferenceAliasCall()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "MethodRefReturnRecoverySample.cs"),
            """
            using System;

            namespace Demo;

            public sealed class Worker
            {
                public int Inc(int value)
                {
                    return value + 1;
                }

                public int Run()
                {
                    dynamic projector = Inc;
                    dynamic result = projector(1);
                    return result;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode resultLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "result"));
        Assert.True(resultLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("int", possibleTypes ?? Array.Empty<string>());

        CpgNode aliasCall = graph.GetNodes(CpgNodeKind.Call).First(node => Has(node, "Name", "projector"));
        Assert.True(aliasCall.TryGetProperty<string[]>("PossibleTypes", out string[]? callPossibleTypes));
        Assert.Contains("int", callPossibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_persistsMemberTypesFromSetterAndFieldAssignments()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "MemberWriteRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Holder
            {
                public dynamic Current { get; set; }
                public dynamic Field;

                public dynamic LoadFromProperty()
                {
                    this.Current = new Worker();
                    dynamic worker = this.Current;
                    return worker;
                }

                public dynamic LoadFromField()
                {
                    this.Field = new Worker();
                    dynamic worker = this.Field;
                    return worker;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode currentMember = graph.GetNodes(CpgNodeKind.Member).First(node => Has(node, "Name", "Current"));
        Assert.True(currentMember.TryGetProperty<string[]>("PossibleTypes", out string[]? propertyTypes));
        Assert.Contains("Demo.Worker", propertyTypes ?? Array.Empty<string>());

        CpgNode fieldMember = graph.GetNodes(CpgNodeKind.Member).First(node => Has(node, "Name", "Field"));
        Assert.True(fieldMember.TryGetProperty<string[]>("PossibleTypes", out string[]? fieldTypes));
        Assert.Contains("Demo.Worker", fieldTypes ?? Array.Empty<string>());

        Assert.Contains(
            graph.GetNodes(CpgNodeKind.Local).Where(node => Has(node, "Name", "worker")),
            node => node.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes) &&
                    possibleTypes.Contains("Demo.Worker"));
    }

    [Fact]
    public void CreateGraph_recoversTypesFromChainedFieldAccessLoads()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "ChainedFieldRecoverySample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
            }

            public sealed class Box
            {
                public Worker Item = new Worker();
            }

            public sealed class Holder
            {
                public Box Current = new Box();

                public dynamic Load()
                {
                    dynamic worker = this.Current.Item;
                    return worker;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode localNode = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "worker"));
        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localPossibleTypes));
        Assert.Contains("Demo.Worker", localPossibleTypes ?? Array.Empty<string>());

        CpgNode itemAccessNode = graph.GetNodes(CpgNodeKind.Call)
            .First(node => Has(node, "FieldFullName", "Demo.Box.Item"));
        Assert.True(itemAccessNode.TryGetProperty<string[]>("PossibleTypes", out string[]? callPossibleTypes));
        Assert.Contains("Demo.Worker", callPossibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void CreateGraph_recoversStaticImportMethodHintsEndToEnd()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "StaticImportRecoverySample.cs"),
            """
            using static Demo.Helpers;

            namespace Demo;

            public static class Helpers
            {
                public static int Make()
                {
                    return 1;
                }
            }

            public sealed class Entry
            {
                public int Run()
                {
                    dynamic result = Make();
                    return result;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        CpgNode importNode = Assert.Single(graph.GetNodes(CpgNodeKind.Import).Where(node => Has(node, "ImportedEntity", "Demo.Helpers")));
        Assert.True(importNode.TryGetProperty<bool>("IsStatic", out bool isStatic));
        Assert.True(isStatic);

        CpgNode callNode = graph.GetNodes(CpgNodeKind.Call).First(node => Has(node, "Name", "Make"));
        Assert.Equal("Demo.Helpers.Make()", callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ? methodFullName : null);
        CpgNode targetMethod = Assert.Single(graph.GetNodes(CpgNodeKind.Method).Where(node => Has(node, "FullName", "Demo.Helpers.Make()")));
        Assert.Contains(graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call), edge => edge.TargetId == targetMethod.Id);

        CpgNode resultLocal = graph.GetNodes(CpgNodeKind.Local).First(node => Has(node, "Name", "result"));
        Assert.True(resultLocal.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("int", possibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeRecoveryPass_assignsDummyReturnTypeForUnknownCalls()
    {
        CpgGraph graph = new();

        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        localNode.SetProperty("Name", "result");
        localNode.SetProperty("TypeFullName", "dynamic");

        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", "MakeUnknown");
        callNode.SetProperty("MethodFullName", "<unknown>");
        callNode.SetProperty("AstParentId", localNode.Id);
        graph.AddEdge(localNode.Id, callNode.Id, CpgEdgeKind.Ast);

        new BuildTypeRecoveryPass().Run(graph);

        Assert.True(callNode.TryGetProperty<string[]>("PossibleTypes", out string[]? callTypes));
        Assert.Contains("MakeUnknown.<returnValue>", callTypes ?? Array.Empty<string>());

        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localTypes));
        Assert.Contains("MakeUnknown.<returnValue>", localTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeRecoveryPass_assignsDummyIndexTypeForUnknownIndexAccess()
    {
        CpgGraph graph = new();

        CpgNode receiverNode = graph.CreateNode(CpgNodeKind.Identifier);
        receiverNode.SetProperty("Name", "items");
        receiverNode.SetProperty("TypeFullName", "dynamic");

        CpgNode indexCallNode = graph.CreateNode(CpgNodeKind.Call);
        indexCallNode.SetProperty("Name", "[]");
        indexCallNode.SetProperty("MethodFullName", "<unknown>");
        graph.AddEdge(indexCallNode.Id, receiverNode.Id, CpgEdgeKind.Ast);

        new BuildTypeRecoveryPass().Run(graph);

        Assert.True(indexCallNode.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("items.<indexAccess>", possibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeRecoveryPass_assignsDummyMemberTypeForUnknownFieldAccess()
    {
        CpgGraph graph = new();

        CpgNode receiverNode = graph.CreateNode(CpgNodeKind.Identifier);
        receiverNode.SetProperty("Name", "box");
        receiverNode.SetProperty("TypeFullName", "dynamic");

        CpgNode fieldAccessNode = graph.CreateNode(CpgNodeKind.Call);
        fieldAccessNode.SetProperty("Name", ".");
        fieldAccessNode.SetProperty("FieldFullName", "box.Item");
        fieldAccessNode.SetProperty("MethodFullName", "<unknown>");
        graph.AddEdge(fieldAccessNode.Id, receiverNode.Id, CpgEdgeKind.Ast);

        new BuildTypeRecoveryPass().Run(graph);

        Assert.True(fieldAccessNode.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("box.<member>(Item)", possibleTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeRecoveryPass_resolvesDummyMemberTypeWhenReceiverTypeBecomesConcrete()
    {
        CpgGraph graph = new();

        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        localNode.SetProperty("Name", "worker");
        localNode.SetProperty("TypeFullName", "dynamic");

        CpgNode boxLocal = graph.CreateNode(CpgNodeKind.Local);
        boxLocal.SetProperty("Name", "box");
        boxLocal.SetProperty("TypeFullName", "Demo.Box");

        CpgNode memberNode = graph.CreateNode(CpgNodeKind.Member);
        memberNode.SetProperty("Name", "Item");
        memberNode.SetProperty("FullName", "Demo.Box.Item");
        memberNode.SetProperty("TypeFullName", "Demo.Worker");

        CpgNode fieldAccessNode = graph.CreateNode(CpgNodeKind.Call);
        fieldAccessNode.SetProperty("Name", ".");
        fieldAccessNode.SetProperty("FieldFullName", "box.Item");
        fieldAccessNode.SetProperty("MethodFullName", "<unknown>");
        fieldAccessNode.SetProperty("PossibleTypes", new[] { "box.<member>(Item)" });
        fieldAccessNode.SetProperty("AstParentId", localNode.Id);
        graph.AddEdge(localNode.Id, fieldAccessNode.Id, CpgEdgeKind.Ast);

        new BuildTypeRecoveryPass().Run(graph);

        Assert.True(fieldAccessNode.TryGetProperty<string[]>("PossibleTypes", out string[]? callTypes));
        Assert.Contains("Demo.Worker", callTypes ?? Array.Empty<string>());
        Assert.DoesNotContain("box.<member>(Item)", callTypes ?? Array.Empty<string>());

        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localTypes));
        Assert.Contains("Demo.Worker", localTypes ?? Array.Empty<string>());
        Assert.DoesNotContain("box.<member>(Item)", localTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeRecoveryPass_resolvesDummyIndexTypeWhenCollectionTypeBecomesConcrete()
    {
        CpgGraph graph = new();

        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        localNode.SetProperty("Name", "first");
        localNode.SetProperty("TypeFullName", "dynamic");

        CpgNode itemsLocal = graph.CreateNode(CpgNodeKind.Local);
        itemsLocal.SetProperty("Name", "items");
        itemsLocal.SetProperty("TypeFullName", "int[]");

        CpgNode receiverNode = graph.CreateNode(CpgNodeKind.Identifier);
        receiverNode.SetProperty("Name", "items");

        CpgNode indexCallNode = graph.CreateNode(CpgNodeKind.Call);
        indexCallNode.SetProperty("Name", "[]");
        indexCallNode.SetProperty("MethodFullName", "<unknown>");
        indexCallNode.SetProperty("PossibleTypes", new[] { "items.<indexAccess>" });
        indexCallNode.SetProperty("AstParentId", localNode.Id);
        graph.AddEdge(localNode.Id, indexCallNode.Id, CpgEdgeKind.Ast);
        graph.AddEdge(indexCallNode.Id, receiverNode.Id, CpgEdgeKind.Ast);
        graph.AddEdge(receiverNode.Id, itemsLocal.Id, CpgEdgeKind.Ref);

        new BuildTypeRecoveryPass().Run(graph);

        Assert.True(indexCallNode.TryGetProperty<string[]>("PossibleTypes", out string[]? callTypes));
        Assert.Contains("int", callTypes ?? Array.Empty<string>());
        Assert.DoesNotContain("items.<indexAccess>", callTypes ?? Array.Empty<string>());

        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? localTypes));
        Assert.Contains("int", localTypes ?? Array.Empty<string>());
        Assert.DoesNotContain("items.<indexAccess>", localTypes ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeRecoveryPass_prunesDummyTypeWhenConcreteTypeAlreadyExists()
    {
        CpgGraph graph = new();

        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        localNode.SetProperty("Name", "result");
        localNode.SetProperty(
            "PossibleTypes",
            new[] { "Make.<returnValue>", "int" });

        new BuildTypeRecoveryPass().Run(graph);

        Assert.True(localNode.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("int", possibleTypes ?? Array.Empty<string>());
        Assert.DoesNotContain("Make.<returnValue>", possibleTypes ?? Array.Empty<string>());
    }

    private static bool Has(CpgNode node, string propertyName, string expected)
    {
        return node.TryGetProperty<string>(propertyName, out string? actual) &&
               string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static string DescribeNode(CpgNode node)
    {
        List<string> parts = new()
        {
            $"Kind={node.Kind}",
            $"Id={node.Id}",
        };

        foreach (string propertyName in new[]
                 {
                     "Name",
                     "FullName",
                     "MethodFullName",
                     "TypeFullName",
                     "TypeDeclFullName",
                     "PossibleTypes",
                     "DynamicTypeHintFullNames",
                     "Index",
                     "Order",
                 })
        {
            if (node.TryGetProperty<string>(propertyName, out string? stringValue))
            {
                parts.Add($"{propertyName}={stringValue}");
                continue;
            }

            if (node.TryGetProperty<string[]>(propertyName, out string[]? arrayValue))
            {
                parts.Add($"{propertyName}=[{string.Join(", ", arrayValue ?? Array.Empty<string>())}]");
                continue;
            }

            if (node.TryGetProperty<int>(propertyName, out int intValue))
            {
                parts.Add($"{propertyName}={intValue}");
            }
        }

        return string.Join("; ", parts);
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
