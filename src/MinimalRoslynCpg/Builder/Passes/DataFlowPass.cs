using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Diagnostics;

namespace MinimalRoslynCpg.Builder.Passes
{
internal sealed class DataFlowPass : IRoslynCpgPass
{
  internal static DataFlowPass Instance { get; } = new();

  private DataFlowPass()
  {
  }

  public string Name => nameof(DataFlowPass);

  public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
  {
    builder.RunDataFlowPass(context);
  }
}
}

namespace MinimalRoslynCpg.Builder
{
public sealed partial class RoslynCpgBuilder
{
    private sealed class DataFlowPassMetrics
    {
        public long EnumerateMethodBlocksElapsedMilliseconds { get; set; }
        public long EnumerateOrderedOperationsElapsedMilliseconds { get; set; }
        public long CfgSensitiveElapsedMilliseconds { get; set; }
        public long ValueSourceEdgeElapsedMilliseconds { get; set; }
        public long ReturnFlowEdgeElapsedMilliseconds { get; set; }
        public long TerminalFlowEdgeElapsedMilliseconds { get; set; }
        public long CallArgumentAndReturnElapsedMilliseconds { get; set; }
        public long BuildFlowNeighborsElapsedMilliseconds { get; set; }
        public long FixpointElapsedMilliseconds { get; set; }
        public long ReachingDefinitionEdgeElapsedMilliseconds { get; set; }
        public int MethodBlockCount { get; set; }
        public int OrderedOperationCount { get; set; }
    }

    internal void RunDataFlowPass(RoslynCpgBuildContext context)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var metrics = new DataFlowPassMetrics();
        AddReachingDefinitionDataFlow(context.Graph, metrics);
        totalStopwatch.Stop();
        _dataFlowPassTelemetry = new RoslynCpgDataFlowPassTelemetry(
            totalStopwatch.ElapsedMilliseconds,
            metrics.EnumerateMethodBlocksElapsedMilliseconds,
            metrics.EnumerateOrderedOperationsElapsedMilliseconds,
            metrics.CfgSensitiveElapsedMilliseconds,
            metrics.ValueSourceEdgeElapsedMilliseconds,
            metrics.ReturnFlowEdgeElapsedMilliseconds,
            metrics.TerminalFlowEdgeElapsedMilliseconds,
            metrics.CallArgumentAndReturnElapsedMilliseconds,
            metrics.BuildFlowNeighborsElapsedMilliseconds,
            metrics.FixpointElapsedMilliseconds,
            metrics.ReachingDefinitionEdgeElapsedMilliseconds,
            metrics.MethodBlockCount,
            metrics.OrderedOperationCount);
    }

    private void AddReachingDefinitionDataFlow(RoslynCpgGraph graph, DataFlowPassMetrics metrics)
    {
        var enumerateMethodBlocksStopwatch = Stopwatch.StartNew();
        var methodBlocks = _operationNodes.Keys.OfType<IBlockOperation>().Where(IsMethodRootBlock).ToList();
        enumerateMethodBlocksStopwatch.Stop();
        metrics.EnumerateMethodBlocksElapsedMilliseconds = enumerateMethodBlocksStopwatch.ElapsedMilliseconds;
        metrics.MethodBlockCount = methodBlocks.Count;

        foreach (var methodBlock in methodBlocks)
        {
            var orderedOperationsStopwatch = Stopwatch.StartNew();
            var orderedOperations = methodBlock.DescendantsAndSelf().ToList();
            orderedOperationsStopwatch.Stop();
            metrics.EnumerateOrderedOperationsElapsedMilliseconds += orderedOperationsStopwatch.ElapsedMilliseconds;
            metrics.OrderedOperationCount += orderedOperations.Count;

            var cfgSensitiveStopwatch = Stopwatch.StartNew();
            AddCfgSensitiveSymbolDataFlow(methodBlock, orderedOperations, graph, metrics);
            cfgSensitiveStopwatch.Stop();
            metrics.CfgSensitiveElapsedMilliseconds += cfgSensitiveStopwatch.ElapsedMilliseconds;
            foreach (var operation in orderedOperations)
            {
                var operationNode = GetOrCreateOperationNode(operation, graph);

                var valueSourceStopwatch = Stopwatch.StartNew();
                foreach (var sourceOperation in ValueSourceOperations(operation))
                {
                    if (!ReferenceEquals(sourceOperation, operation))
                    {
                        graph.AddEdge(GetOrCreateOperationNode(sourceOperation, graph), operationNode, RoslynCpgEdgeKind.DataFlow);
                    }
                }
                valueSourceStopwatch.Stop();
                metrics.ValueSourceEdgeElapsedMilliseconds += valueSourceStopwatch.ElapsedMilliseconds;

                if (operation is IReturnOperation returnOperation &&
                    returnOperation.ReturnedValue is not null &&
                    _operationOwningMethods.TryGetValue(operation, out var owningMethod))
                {
                    var returnFlowStopwatch = Stopwatch.StartNew();
                    var exitNode = GetOrCreateMethodExitNode(owningMethod, graph);
                    var returnNode = GetOrCreateMethodReturnNode(owningMethod, graph);
                    graph.AddEdge(GetOrCreateOperationNode(returnOperation.ReturnedValue, graph), returnNode, RoslynCpgEdgeKind.DataFlow);
                    graph.AddEdge(returnNode, exitNode, RoslynCpgEdgeKind.DataFlow);
                    graph.AddEdge(operationNode, exitNode, RoslynCpgEdgeKind.DataFlow);
                    returnFlowStopwatch.Stop();
                    metrics.ReturnFlowEdgeElapsedMilliseconds += returnFlowStopwatch.ElapsedMilliseconds;
                }
            }

            if (_operationOwningMethods.TryGetValue(methodBlock, out var flowMethodSymbol))
            {
                var terminalFlowStopwatch = Stopwatch.StartNew();
                var returnNode = GetOrCreateMethodReturnNode(flowMethodSymbol, graph);
                var terminalOperation = methodBlock.Operations.LastOrDefault();
                if (terminalOperation is not null &&
                    !ContainsExplicitReturn(methodBlock) &&
                    !StopsSequentialFlow(terminalOperation))
                {
                    graph.AddEdge(GetOrCreateOperationNode(terminalOperation, graph), returnNode, RoslynCpgEdgeKind.DataFlow);
                }
                terminalFlowStopwatch.Stop();
                metrics.TerminalFlowEdgeElapsedMilliseconds += terminalFlowStopwatch.ElapsedMilliseconds;
            }
        }

        var callArgumentAndReturnStopwatch = Stopwatch.StartNew();
        AddCallArgumentAndReturnDataFlow(graph);
        callArgumentAndReturnStopwatch.Stop();
        metrics.CallArgumentAndReturnElapsedMilliseconds += callArgumentAndReturnStopwatch.ElapsedMilliseconds;
    }

    private void AddCfgSensitiveSymbolDataFlow(IBlockOperation methodBlock, List<IOperation> orderedOperations, RoslynCpgGraph graph, DataFlowPassMetrics metrics)
    {
        var flowNodes = new List<RoslynCpgNode>();
        var definitionFactsByNodeId = new Dictionary<string, DefinitionFact>(StringComparer.Ordinal);
        var usedFactsByNodeId = new Dictionary<string, List<DefinitionFact>>(StringComparer.Ordinal);

        if (_operationOwningMethods.TryGetValue(methodBlock, out var methodSymbol))
        {
            foreach (var parameter in methodSymbol.Parameters)
            {
                var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
                flowNodes.Add(parameterNode);
                definitionFactsByNodeId[parameterNode.Id] = DefinitionFactForParameter(parameter);
            }
        }

        foreach (var operation in orderedOperations)
        {
            var operationNode = GetOrCreateOperationNode(operation, graph);
            flowNodes.Add(operationNode);
            usedFactsByNodeId[operationNode.Id] = UsedFacts(operation).ToList();

            var definedFact = DefinedFact(operation);
            if (definedFact is not null)
            {
                definitionFactsByNodeId[operationNode.Id] = definedFact;
            }
        }

        var flowNodeIds = flowNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var nodesById = flowNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var buildNeighborsStopwatch = Stopwatch.StartNew();
        var predecessors = BuildFlowNeighborsFromCache(flowNodeIds, incoming: true);
        var successors = BuildFlowNeighborsFromCache(flowNodeIds, incoming: false);
        buildNeighborsStopwatch.Stop();
        metrics.BuildFlowNeighborsElapsedMilliseconds += buildNeighborsStopwatch.ElapsedMilliseconds;
        var inSets = flowNodes.ToDictionary(node => node.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var outSets = flowNodes.ToDictionary(node => node.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var worklist = new Queue<string>(flowNodes.Select(node => node.Id));
        var queued = new HashSet<string>(flowNodes.Select(node => node.Id), StringComparer.Ordinal);

        var fixpointStopwatch = Stopwatch.StartNew();
        while (worklist.Count > 0)
        {
            var nodeId = worklist.Dequeue();
            queued.Remove(nodeId);

            var incomingDefinitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var predecessorId in predecessors[nodeId])
            {
                incomingDefinitions.UnionWith(outSets[predecessorId]);
            }

            inSets[nodeId] = incomingDefinitions;
            var updatedOut = ApplyDefinitionTransfer(nodeId, incomingDefinitions, definitionFactsByNodeId);
            if (updatedOut.SetEquals(outSets[nodeId]))
            {
                continue;
            }

            outSets[nodeId] = updatedOut;
            foreach (var successorId in successors[nodeId])
            {
                if (queued.Add(successorId))
                {
                    worklist.Enqueue(successorId);
                }
            }
        }
        fixpointStopwatch.Stop();
        metrics.FixpointElapsedMilliseconds += fixpointStopwatch.ElapsedMilliseconds;

        var reachingDefinitionStopwatch = Stopwatch.StartNew();
        foreach (var operation in orderedOperations)
        {
            var operationNode = GetOrCreateOperationNode(operation, graph);
            foreach (var usedFact in usedFactsByNodeId[operationNode.Id])
            {
                foreach (var reachingDefinitionId in inSets[operationNode.Id])
                {
                    if (!definitionFactsByNodeId.TryGetValue(reachingDefinitionId, out var reachingFact) ||
                        !FactsMatch(reachingFact, usedFact))
                    {
                        continue;
                    }

                    graph.AddEdge(nodesById[reachingDefinitionId], operationNode, RoslynCpgEdgeKind.DataFlow);
                }
            }
        }
        reachingDefinitionStopwatch.Stop();
        metrics.ReachingDefinitionEdgeElapsedMilliseconds += reachingDefinitionStopwatch.ElapsedMilliseconds;
    }

    private Dictionary<string, List<string>> BuildFlowNeighborsFromCache(HashSet<string> flowNodeIds, bool incoming)
    {
        var neighbors = flowNodeIds.ToDictionary(nodeId => nodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var nodeId in flowNodeIds)
        {
            var cachedNeighbors = incoming
              ? GetCachedCfgPredecessors(nodeId)
              : GetCachedCfgSuccessors(nodeId);
            if (cachedNeighbors.Count == 0)
            {
                continue;
            }

            foreach (var neighborNodeId in cachedNeighbors)
            {
                if (flowNodeIds.Contains(neighborNodeId))
                {
                    neighbors[nodeId].Add(neighborNodeId);
                }
            }
        }

        return neighbors;
    }

    private static HashSet<string> ApplyDefinitionTransfer(string nodeId, HashSet<string> incomingDefinitions, Dictionary<string, DefinitionFact> definitionFactsByNodeId)
    {
        var outgoingDefinitions = new HashSet<string>(incomingDefinitions, StringComparer.Ordinal);
        if (!definitionFactsByNodeId.TryGetValue(nodeId, out var definedFact))
        {
            return outgoingDefinitions;
        }

        outgoingDefinitions.RemoveWhere(definitionId =>
          definitionFactsByNodeId.TryGetValue(definitionId, out var priorFact) &&
          FactsConflict(priorFact, definedFact));
        outgoingDefinitions.Add(nodeId);
        return outgoingDefinitions;
    }

    private static bool FactsMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        if (string.Equals(reachingFact.LocationKey, usedFact.LocationKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsContainerMatch(reachingFact, usedFact) || IsPartMatch(reachingFact, usedFact) || IsAliasMatch(reachingFact, usedFact))
        {
            return true;
        }

        if (string.IsNullOrEmpty(reachingFact.BaseKey) || string.IsNullOrEmpty(usedFact.BaseKey))
        {
            return false;
        }

        return string.Equals(reachingFact.BaseKey, usedFact.BaseKey, StringComparison.Ordinal) &&
               string.Equals(reachingFact.LocationKey, usedFact.LocationKey, StringComparison.Ordinal);
    }

    private static bool FactsConflict(DefinitionFact priorFact, DefinitionFact definedFact)
    {
        if (string.Equals(priorFact.LocationKey, definedFact.LocationKey, StringComparison.Ordinal))
        {
            return true;
        }

        var definedRootKey = FactRootKey(definedFact);
        if (!string.IsNullOrEmpty(definedRootKey) &&
            string.Equals(priorFact.BaseKey, definedRootKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (definedFact.Category is "field" or "property" &&
            string.Equals(priorFact.BaseKey, definedFact.BaseKey, StringComparison.Ordinal))
        {
            return IsAliasMatch(priorFact, definedFact) ||
                   string.Equals(priorFact.PathKey, definedFact.PathKey, StringComparison.Ordinal);
        }

        if (definedFact.Category is "call" && priorFact.Category == "call")
        {
            return string.Equals(priorFact.LocationKey, definedFact.LocationKey, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsContainerMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        var reachingRootKey = FactRootKey(reachingFact);
        return !string.IsNullOrEmpty(usedFact.BaseKey) &&
               !string.IsNullOrEmpty(reachingRootKey) &&
               string.Equals(reachingRootKey, usedFact.BaseKey, StringComparison.Ordinal);
    }

    private static bool IsPartMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        var usedRootKey = FactRootKey(usedFact);
        return !string.IsNullOrEmpty(reachingFact.BaseKey) &&
               !string.IsNullOrEmpty(usedRootKey) &&
               string.Equals(reachingFact.BaseKey, usedRootKey, StringComparison.Ordinal);
    }

    private static bool IsAliasMatch(DefinitionFact left, DefinitionFact right)
    {
        return !string.IsNullOrEmpty(left.BaseKey) &&
               !string.IsNullOrEmpty(right.BaseKey) &&
               string.Equals(left.BaseKey, right.BaseKey, StringComparison.Ordinal) &&
               string.Equals(left.PathKey, right.PathKey, StringComparison.Ordinal);
    }

    private static string? FactRootKey(DefinitionFact fact)
    {
        return fact.BaseKey ?? fact.LocationKey;
    }

    private void AddCallArgumentAndReturnDataFlow(RoslynCpgGraph graph)
    {
        foreach (var invocation in _operationNodes.Keys.OfType<IInvocationOperation>())
        {
            var targetMethod = invocation.TargetMethod;
            if (targetMethod is null)
            {
                continue;
            }

            var callSiteNode = FindCallSiteNode(invocation, graph);
            if (callSiteNode is null)
            {
                continue;
            }

            var candidateMethods = ResolveCallTargetCandidates(invocation, targetMethod).ToList();
            var preferredCandidates = PreferCallTargets(candidateMethods, targetMethod, invocation.Instance?.Type).ToList();
            var effectiveTargets = preferredCandidates.Count > 0 ? preferredCandidates : candidateMethods;
            if (effectiveTargets.Count == 0)
            {
                effectiveTargets.Add(targetMethod);
            }

            foreach (var candidateMethod in effectiveTargets.Where(IsInternalMethod).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
            {
                AddArgumentToParameterFlows(invocation, candidateMethod, graph);

                var returnNode = GetOrCreateMethodReturnNode(candidateMethod, graph);
                graph.AddEdge(returnNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            }
        }

        foreach (var propertyReference in _operationNodes.Keys.OfType<IPropertyReferenceOperation>())
        {
            AddPropertyAccessorSummaryDataFlow(propertyReference, graph);
        }
    }

    private void AddPropertyAccessorSummaryDataFlow(IPropertyReferenceOperation propertyReference, RoslynCpgGraph graph)
    {
        AddPropertyGetterSummaryDataFlow(propertyReference, graph);
        AddPropertySetterSummaryDataFlow(propertyReference, graph);
    }

    private void AddPropertyGetterSummaryDataFlow(IPropertyReferenceOperation propertyReference, RoslynCpgGraph graph)
    {
        var getterMethod = propertyReference.Property.GetMethod;
        if (getterMethod is null)
        {
            return;
        }

        var getter = CanonicalMethodSymbol(getterMethod);
        if (!IsInternalMethod(getter))
        {
            return;
        }

        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, getter, graph);
        AddPropertyAccessorParameterFlows(propertyReference, getter, callSiteNode, includesSetterValue: false, setterValue: null, graph);

        var returnNode = GetOrCreateMethodReturnNode(getter, graph);
        graph.AddEdge(returnNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        if (callSiteNode is not null)
        {
            graph.AddEdge(returnNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            graph.AddEdge(callSiteNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private void AddPropertySetterSummaryDataFlow(IPropertyReferenceOperation propertyReference, RoslynCpgGraph graph)
    {
        if (!IsPropertyWrite(propertyReference))
        {
            return;
        }

        var setterMethod = propertyReference.Property.SetMethod;
        if (setterMethod is null)
        {
            return;
        }

        var setter = CanonicalMethodSymbol(setterMethod);
        if (!IsInternalMethod(setter))
        {
            return;
        }

        if (propertyReference.Parent is not ISimpleAssignmentOperation assignment)
        {
            return;
        }

        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, setter, graph);
        AddPropertyAccessorParameterFlows(propertyReference, setter, callSiteNode, includesSetterValue: true, assignment.Value, graph);

        graph.AddEdge(GetOrCreateOperationNode(assignment.Value, graph), propertyNode, RoslynCpgEdgeKind.DataFlow);
        if (callSiteNode is not null)
        {
            graph.AddEdge(callSiteNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private void AddPropertyAccessorParameterFlows(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod, RoslynCpgNode? callSiteNode, bool includesSetterValue, IOperation? setterValue, RoslynCpgGraph graph)
    {
        var parameterIndex = 0;
        if (propertyReference.Instance is not null && accessorMethod.Parameters.Length > parameterIndex)
        {
            var receiverNode = GetOrCreateOperationNode(propertyReference.Instance, graph);
            var receiverParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex += 1;
        }

        foreach (var argument in propertyReference.Arguments)
        {
            if (parameterIndex >= accessorMethod.Parameters.Length || argument.Value is null)
            {
                continue;
            }

            var argumentNode = GetOrCreateOperationNode(argument.Value, graph);
            var argumentParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(argumentNode, argumentParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(argumentNode, argumentParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex += 1;
        }

        if (includesSetterValue &&
            setterValue is not null &&
            parameterIndex < accessorMethod.Parameters.Length)
        {
            var valueNode = GetOrCreateOperationNode(setterValue, graph);
            var setterValueParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(valueNode, setterValueParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(valueNode, setterValueParameterNode, RoslynCpgEdgeKind.DataFlow);
            if (callSiteNode is not null)
            {
                graph.AddEdge(valueNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            }
        }
    }

    private void AddArgumentToParameterFlows(IInvocationOperation invocation, IMethodSymbol candidateMethod, RoslynCpgGraph graph)
    {
        var parameterIndex = 0;

        if (candidateMethod.IsExtensionMethod && invocation.Instance is not null && candidateMethod.Parameters.Length > 0)
        {
            var receiverNode = GetOrCreateOperationNode(invocation.Instance, graph);
            var receiverParameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[0], graph);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex = 1;
        }

        for (var argumentIndex = 0; argumentIndex < invocation.Arguments.Length && parameterIndex < candidateMethod.Parameters.Length; argumentIndex += 1, parameterIndex += 1)
        {
            var argumentValue = invocation.Arguments[argumentIndex].Value;
            if (argumentValue is null)
            {
                continue;
            }

            var argumentNode = GetOrCreateOperationNode(argumentValue, graph);
            var parameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(argumentNode, parameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(argumentNode, parameterNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private RoslynCpgNode? FindCallSiteNode(IInvocationOperation invocationOperation, RoslynCpgGraph graph)
    {
        return _callSiteNodesByInvocation.TryGetValue(invocationOperation, out var callSiteNode)
          ? callSiteNode
          : null;
    }

    private RoslynCpgNode? FindPropertyAccessorCallSiteNode(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod, RoslynCpgGraph graph)
    {
        return _propertyAccessorCallSiteNodesByKey.TryGetValue(
          PropertyAccessorCallSiteKey(propertyReference, accessorMethod),
          out var callSiteNode)
            ? callSiteNode
            : null;
    }

    private static DefinitionFact? DefinedFact(IOperation operation)
    {
        return operation switch
        {
            IVariableDeclaratorOperation declarator => DefinitionFactForSymbol(declarator.Symbol),
            ISimpleAssignmentOperation assignment => DefinitionFactForAssignmentTarget(assignment.Target),
            IInvocationOperation invocation => DefinitionFactForInvocation(invocation),
            IPropertyReferenceOperation propertyReference when IsPropertyRead(propertyReference) =>
              DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance),
            _ => null,
        };
    }

    private static IEnumerable<IOperation> ValueSourceOperations(IOperation operation)
    {
        switch (operation)
        {
            case IVariableDeclaratorOperation declarator when declarator.Initializer?.Value is { } initializerValue:
                yield return initializerValue;
                break;
            case ISimpleAssignmentOperation assignment:
                yield return assignment.Value;
                break;
            case IInvocationOperation invocation:
                if (invocation.Instance is not null)
                {
                    yield return invocation.Instance;
                }

                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Value is not null)
                    {
                        yield return argument.Value;
                    }
                }

                break;
            case IReturnOperation returnOperation when returnOperation.ReturnedValue is not null:
                yield return returnOperation.ReturnedValue;
                break;
        }
    }

    private static IEnumerable<DefinitionFact> UsedFacts(IOperation operation)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            switch (descendant)
            {
                case ILocalReferenceOperation localReference:
                    yield return DefinitionFactForSymbol(localReference.Local);
                    break;
                case IParameterReferenceOperation parameterReference:
                    yield return DefinitionFactForParameter(parameterReference.Parameter);
                    break;
                case IFieldReferenceOperation fieldReference:
                    yield return DefinitionFactForField(fieldReference.Field, fieldReference.Instance);
                    break;
                case IPropertyReferenceOperation propertyReference:
                    yield return DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance);
                    break;
            }
        }
    }

    private static DefinitionFact DefinitionFactForAssignmentTarget(IOperation target)
    {
        return target switch
        {
            ILocalReferenceOperation localReference => DefinitionFactForSymbol(localReference.Local),
            IParameterReferenceOperation parameterReference => DefinitionFactForParameter(parameterReference.Parameter),
            IFieldReferenceOperation fieldReference => DefinitionFactForField(fieldReference.Field, fieldReference.Instance),
            IPropertyReferenceOperation propertyReference => DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance),
            _ => new DefinitionFact(target.Kind.ToString(), null, "unknown"),
        };
    }

    private static DefinitionFact DefinitionFactForInvocation(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        var locationKey = targetMethod is null
          ? $"call:{invocation.Syntax.SpanStart}:{invocation.Syntax.Span.End}"
          : $"call:{ComposeMethodFullName(targetMethod)}";
        return new DefinitionFact(locationKey, ReceiverKey(invocation.Instance), "call", ComposeInvocationPathKey(invocation));
    }

    private static DefinitionFact DefinitionFactForSymbol(ISymbol symbol)
    {
        return new DefinitionFact(SymbolId(symbol), null, symbol.Kind.ToString(), SymbolId(symbol));
    }

    private static DefinitionFact DefinitionFactForParameter(IParameterSymbol parameter)
    {
        return new DefinitionFact(SymbolId(parameter), null, "parameter", SymbolId(parameter));
    }

    private static DefinitionFact DefinitionFactForField(IFieldSymbol field, IOperation? instance)
    {
        var baseKey = ReceiverKey(instance);
        var locationKey = baseKey is null
          ? $"field:{SymbolId(field)}"
          : $"field:{baseKey}.{field.Name}";
        return new DefinitionFact(locationKey, baseKey, "field", field.Name);
    }

    private static DefinitionFact DefinitionFactForProperty(IPropertySymbol property, IOperation? instance)
    {
        var baseKey = ReceiverKey(instance);
        var propertyKey = property.Parameters.Length == 0
          ? property.Name
          : $"{property.Name}:{ComposePropertySignature(property)}";
        var locationKey = baseKey is null
          ? $"property:{SymbolId(property)}"
          : $"property:{baseKey}.{propertyKey}";
        return new DefinitionFact(locationKey, baseKey, "property", propertyKey);
    }

    private static string ComposeInvocationPathKey(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        if (targetMethod is not null)
        {
            return ComposeMethodLookupKey(targetMethod);
        }

        return $"invoke:{invocation.Syntax.SpanStart}:{invocation.Syntax.Span.End}";
    }

    private static bool IsPropertyRead(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent is not ISimpleAssignmentOperation assignment ||
               !ReferenceEquals(assignment.Target, propertyReference);
    }

    private static bool IsPropertyWrite(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent is ISimpleAssignmentOperation assignment &&
               ReferenceEquals(assignment.Target, propertyReference);
    }

    private static string? ReceiverKey(IOperation? instance)
    {
        if (instance is null)
        {
            return null;
        }

        return instance switch
        {
            IInstanceReferenceOperation instanceReference => ComposeTypeFullName(instanceReference.Type),
            ILocalReferenceOperation localReference => $"local:{SymbolId(localReference.Local)}",
            IParameterReferenceOperation parameterReference => $"param:{SymbolId(parameterReference.Parameter)}",
            IFieldReferenceOperation fieldReference => DefinitionFactForField(fieldReference.Field, fieldReference.Instance).LocationKey,
            IPropertyReferenceOperation propertyReference => DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance).LocationKey,
            _ => $"op:{instance.Kind}:{instance.Syntax.SpanStart}:{instance.Syntax.Span.End}",
        };
    }
}
}
