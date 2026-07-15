using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Model;

namespace RoslynPrototype.Analysis;

/// <summary>
/// Holds run-scoped, thread-safe facts shared by Mark rules for one analysis.
/// </summary>
public sealed class MarkAnalysisSnapshot
{
    private readonly CpgAnalysisContext _analysisContext;
    private readonly IReadOnlyDictionary<GraphBindingKey, RoslynCpgNode> _graphBindings;
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<IReadOnlyList<ExpressionSyntax>>> _atomicCandidates = new();
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<IOperation?>> _operations = new();
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<MarkRegionFacts>> _regions = new();
    private readonly ConcurrentDictionary<TargetMatchKey, Lazy<bool>> _targetMatches = new();
    private readonly ConcurrentDictionary<SliceQueryKey, Lazy<RoslynCpgSliceResult>> _sliceQueries = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _normalizedTargetNames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, MarkRuleTelemetryAccumulator> _ruleTelemetry = new();
    private readonly AsyncLocal<MarkRuleTelemetryScope?> _activeRuleTelemetry = new();
    private long _atomicCandidateIndexHitCount;
    private long _atomicCandidateIndexMissCount;
    private long _operationLookupCacheHitCount;
    private long _operationLookupCacheMissCount;
    private long _graphBindingIndexHitCount;
    private long _graphBindingIndexMissCount;
    private long _regionCacheHitCount;
    private long _regionCacheMissCount;
    private long _targetMatchCacheHitCount;
    private long _targetMatchCacheMissCount;
    private long _sliceQueryCacheHitCount;
    private long _sliceQueryCacheMissCount;

    public MarkAnalysisSnapshot(CpgAnalysisContext analysisContext)
    {
        _analysisContext = analysisContext;
        _graphBindings = BuildGraphBindingIndex(analysisContext.Graph.Nodes);
    }

    public MarkAnalysisTelemetry Telemetry => new(
      Volatile.Read(ref _atomicCandidateIndexHitCount),
      Volatile.Read(ref _atomicCandidateIndexMissCount),
      Volatile.Read(ref _operationLookupCacheHitCount),
      Volatile.Read(ref _operationLookupCacheMissCount),
      Volatile.Read(ref _graphBindingIndexHitCount),
      Volatile.Read(ref _graphBindingIndexMissCount),
      Volatile.Read(ref _regionCacheHitCount),
      Volatile.Read(ref _regionCacheMissCount),
      Volatile.Read(ref _targetMatchCacheHitCount),
      Volatile.Read(ref _targetMatchCacheMissCount),
      Volatile.Read(ref _sliceQueryCacheHitCount),
      Volatile.Read(ref _sliceQueryCacheMissCount),
      _ruleTelemetry
        .OrderBy(entry => entry.Key)
        .Select(entry => entry.Value.CreateTelemetry())
        .ToList());

    public MarkRuleTelemetryScope BeginRuleTelemetry(
      int ruleOrder,
      string ruleId,
      string? groupKey)
    {
        var accumulator = _ruleTelemetry.GetOrAdd(
          ruleOrder,
          _ => new MarkRuleTelemetryAccumulator(ruleOrder, ruleId, groupKey));
        var scope = new MarkRuleTelemetryScope(this, accumulator, _activeRuleTelemetry.Value);
        _activeRuleTelemetry.Value = scope;
        return scope;
    }

    public IReadOnlyList<string> GetNormalizedTargetNames(string? targetName)
    {
        var key = targetName ?? string.Empty;
        return _normalizedTargetNames.GetOrAdd(
          key,
          static value => value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList());
    }

    public bool GetTargetMatch(
      SyntaxNode syntaxNode,
      IReadOnlyList<string> targetNames,
      Func<bool> evaluate)
    {
        var key = new TargetMatchKey(
          syntaxNode,
          string.Join("\u001f", targetNames.OrderBy(name => name, StringComparer.Ordinal)));
        if (_targetMatches.TryGetValue(key, out var existing))
        {
            RecordTargetMatchCacheHit();
            return existing.Value;
        }

        var created = new Lazy<bool>(evaluate, LazyThreadSafetyMode.ExecutionAndPublication);
        var value = _targetMatches.GetOrAdd(key, created);
        if (ReferenceEquals(value, created))
        {
            RecordTargetMatchCacheMiss();
        }
        else
        {
            RecordTargetMatchCacheHit();
        }

        return value.Value;
    }

    public IReadOnlyList<ExpressionSyntax> GetAtomicCandidates(SyntaxNode root)
    {
        if (_atomicCandidates.TryGetValue(root, out var existing))
        {
            RecordAtomicCandidateIndexHit();
            return existing.Value;
        }

        var created = new Lazy<IReadOnlyList<ExpressionSyntax>>(
          () => new AtomicExpressionAnalyzer().Analyze(root),
          LazyThreadSafetyMode.ExecutionAndPublication);
        var value = _atomicCandidates.GetOrAdd(root, created);
        if (ReferenceEquals(value, created))
        {
            RecordAtomicCandidateIndexMiss();
        }
        else
        {
            RecordAtomicCandidateIndexHit();
        }
        return value.Value;
    }

    public IOperation? GetOperation(SyntaxNode syntaxNode)
    {
        if (_operations.TryGetValue(syntaxNode, out var existing))
        {
            RecordOperationLookupCacheHit();
            return existing.Value;
        }

        var created = new Lazy<IOperation?>(
          () => _analysisContext.SemanticModel.GetOperation(syntaxNode),
          LazyThreadSafetyMode.ExecutionAndPublication);
        var value = _operations.GetOrAdd(syntaxNode, created);
        if (ReferenceEquals(value, created))
        {
            RecordOperationLookupCacheMiss();
        }
        else
        {
            RecordOperationLookupCacheHit();
        }
        return value.Value;
    }

    public MarkCodeRegion GetMarkRegion(SyntaxNode anchorNode)
    {
        if (_regions.TryGetValue(anchorNode, out var existing))
        {
            RecordRegionCacheHit();
            return existing.Value.Create(anchorNode);
        }

        var created = new Lazy<MarkRegionFacts>(
          () => MarkRegionFacts.From(new MarkRegionAnalyzer().Analyze(anchorNode, _analysisContext)),
          LazyThreadSafetyMode.ExecutionAndPublication);
        var value = _regions.GetOrAdd(anchorNode, created);
        if (ReferenceEquals(value, created))
        {
            RecordRegionCacheMiss();
        }
        else
        {
            RecordRegionCacheHit();
        }
        return value.Value.Create(anchorNode);
    }

    public bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out RoslynCpgNode? graphNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            graphNode = null;
            RecordGraphBindingIndexMiss();
            return false;
        }

        var key = new GraphBindingKey(filePath, syntaxNode.SpanStart, syntaxNode.Span.End);
        if (_graphBindings.TryGetValue(key, out graphNode))
        {
            RecordGraphBindingIndexHit();
            return true;
        }

        RecordGraphBindingIndexMiss();
        return false;
    }

    public RoslynCpgSliceResult QuerySliceBackward(
      string sinkNodeId,
      RoslynCpgSliceQueryOptions options)
    {
        var key = SliceQueryKey.Create(sinkNodeId, options);
        if (_sliceQueries.TryGetValue(key, out var existing))
        {
            Interlocked.Increment(ref _sliceQueryCacheHitCount);
            return existing.Value;
        }

        var created = new Lazy<RoslynCpgSliceResult>(
          () => new RoslynCpgSliceQuery(_analysisContext.Graph).QueryBackward(sinkNodeId, options),
          LazyThreadSafetyMode.ExecutionAndPublication);
        var value = _sliceQueries.GetOrAdd(key, created);
        if (ReferenceEquals(value, created))
        {
            Interlocked.Increment(ref _sliceQueryCacheMissCount);
        }
        else
        {
            Interlocked.Increment(ref _sliceQueryCacheHitCount);
        }

        return value.Value;
    }

    private void RecordAtomicCandidateIndexHit()
    {
        Interlocked.Increment(ref _atomicCandidateIndexHitCount);
        _activeRuleTelemetry.Value?.RecordAtomicCandidateIndexHit();
    }

    private void RecordAtomicCandidateIndexMiss()
    {
        Interlocked.Increment(ref _atomicCandidateIndexMissCount);
        _activeRuleTelemetry.Value?.RecordAtomicCandidateIndexMiss();
    }

    private void RecordOperationLookupCacheHit()
    {
        Interlocked.Increment(ref _operationLookupCacheHitCount);
        _activeRuleTelemetry.Value?.RecordOperationLookupCacheHit();
    }

    private void RecordOperationLookupCacheMiss()
    {
        Interlocked.Increment(ref _operationLookupCacheMissCount);
        _activeRuleTelemetry.Value?.RecordOperationLookupCacheMiss();
    }

    private void RecordGraphBindingIndexHit()
    {
        Interlocked.Increment(ref _graphBindingIndexHitCount);
        _activeRuleTelemetry.Value?.RecordGraphBindingIndexHit();
    }

    private void RecordGraphBindingIndexMiss()
    {
        Interlocked.Increment(ref _graphBindingIndexMissCount);
        _activeRuleTelemetry.Value?.RecordGraphBindingIndexMiss();
    }

    private void RecordRegionCacheHit()
    {
        Interlocked.Increment(ref _regionCacheHitCount);
        _activeRuleTelemetry.Value?.RecordRegionCacheHit();
    }

    private void RecordRegionCacheMiss()
    {
        Interlocked.Increment(ref _regionCacheMissCount);
        _activeRuleTelemetry.Value?.RecordRegionCacheMiss();
    }

    private void RecordTargetMatchCacheHit()
    {
        Interlocked.Increment(ref _targetMatchCacheHitCount);
        _activeRuleTelemetry.Value?.RecordTargetMatchCacheHit();
    }

    private void RecordTargetMatchCacheMiss()
    {
        Interlocked.Increment(ref _targetMatchCacheMissCount);
        _activeRuleTelemetry.Value?.RecordTargetMatchCacheMiss();
    }

    private void RestoreActiveRuleTelemetry(MarkRuleTelemetryScope? previous)
    {
        _activeRuleTelemetry.Value = previous;
    }

    private static IReadOnlyDictionary<GraphBindingKey, RoslynCpgNode> BuildGraphBindingIndex(
      IEnumerable<RoslynCpgNode> graphNodes)
    {
        var bindings = new Dictionary<GraphBindingKey, RoslynCpgNode>();
        foreach (var node in graphNodes)
        {
            if (node.IsImplicit ||
                string.IsNullOrWhiteSpace(node.FilePath) ||
                node.SpanStart is null ||
                node.SpanEnd is null)
            {
                continue;
            }

            var key = new GraphBindingKey(node.FilePath, node.SpanStart.Value, node.SpanEnd.Value);
            if (!bindings.TryGetValue(key, out var current) ||
                GetBindingPriority(node) < GetBindingPriority(current))
            {
                bindings[key] = node;
            }
        }

        return bindings;
    }

    private static int GetBindingPriority(RoslynCpgNode node)
    {
        return node.Kind switch
        {
            RoslynCpgNodeKind.Method => 0,
            RoslynCpgNodeKind.MethodParameter => 1,
            RoslynCpgNodeKind.CallSite => 2,
            RoslynCpgNodeKind.MemberAccess => 3,
            RoslynCpgNodeKind.Reference => 4,
            RoslynCpgNodeKind.Operation => 5,
            RoslynCpgNodeKind.OpInvocation => 6,
            RoslynCpgNodeKind.OpBinary => 7,
            RoslynCpgNodeKind.OpAssignment => 8,
            RoslynCpgNodeKind.OpLocalReference => 9,
            RoslynCpgNodeKind.OpParameterReference => 10,
            RoslynCpgNodeKind.OpFieldReference => 11,
            RoslynCpgNodeKind.OpPropertyReference => 12,
            RoslynCpgNodeKind.SyntaxNode => 13,
            _ => 14
        };
    }

    private readonly record struct GraphBindingKey(string FilePath, int SpanStart, int SpanEnd);

    private readonly record struct TargetMatchKey(SyntaxNode SyntaxNode, string TargetNames);

    private readonly record struct SliceQueryKey(
      string SinkNodeId,
      string AllowedEdgeKinds,
      int MaxHops,
      int MaxPaths,
      int MaxDefinitions,
      int MaxCallDepth)
    {
        public static SliceQueryKey Create(string sinkNodeId, RoslynCpgSliceQueryOptions options)
        {
            return new SliceQueryKey(
              sinkNodeId,
              string.Join(",", options.AllowedEdgeKinds.OrderBy(kind => kind)),
              options.MaxHops,
              options.MaxPaths,
              options.MaxDefinitions,
              options.MaxCallDepth);
        }
    }

    private sealed record MarkRegionFacts(
      SyntaxNode RegionNode,
      Microsoft.CodeAnalysis.Text.TextSpan Span,
      int NodeCount,
      int ExpressionCount,
      int StatementCount)
    {
        public static MarkRegionFacts From(MarkCodeRegion region)
        {
            return new MarkRegionFacts(
              region.RegionNode,
              region.Span,
              region.NodeCount,
              region.ExpressionCount,
              region.StatementCount);
        }

        public MarkCodeRegion Create(SyntaxNode anchorNode)
        {
            return new MarkCodeRegion(anchorNode, RegionNode, Span, NodeCount, ExpressionCount, StatementCount);
        }
    }

    internal sealed class MarkRuleTelemetryAccumulator
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _candidateMarkCount;
        private long _acceptedMarkCount;
        private long _graphBindingFallbackCount;
        private long _atomicCandidateIndexHitCount;
        private long _atomicCandidateIndexMissCount;
        private long _operationLookupCacheHitCount;
        private long _operationLookupCacheMissCount;
        private long _graphBindingIndexHitCount;
        private long _graphBindingIndexMissCount;
        private long _regionCacheHitCount;
        private long _regionCacheMissCount;
        private long _targetMatchCacheHitCount;
        private long _targetMatchCacheMissCount;

        public MarkRuleTelemetryAccumulator(int ruleOrder, string ruleId, string? groupKey)
        {
            RuleOrder = ruleOrder;
            RuleId = ruleId;
            GroupKey = groupKey;
        }

        public int RuleOrder { get; }

        public string RuleId { get; }

        public string? GroupKey { get; }

        public void RecordCandidateMark() => Interlocked.Increment(ref _candidateMarkCount);

        public void RecordAcceptedMark() => Interlocked.Increment(ref _acceptedMarkCount);

        public void RecordGraphBindingFallback() => Interlocked.Increment(ref _graphBindingFallbackCount);

        public void RecordAtomicCandidateIndexHit() => Interlocked.Increment(ref _atomicCandidateIndexHitCount);

        public void RecordAtomicCandidateIndexMiss() => Interlocked.Increment(ref _atomicCandidateIndexMissCount);

        public void RecordOperationLookupCacheHit() => Interlocked.Increment(ref _operationLookupCacheHitCount);

        public void RecordOperationLookupCacheMiss() => Interlocked.Increment(ref _operationLookupCacheMissCount);

        public void RecordGraphBindingIndexHit() => Interlocked.Increment(ref _graphBindingIndexHitCount);

        public void RecordGraphBindingIndexMiss() => Interlocked.Increment(ref _graphBindingIndexMissCount);

        public void RecordRegionCacheHit() => Interlocked.Increment(ref _regionCacheHitCount);

        public void RecordRegionCacheMiss() => Interlocked.Increment(ref _regionCacheMissCount);

        public void RecordTargetMatchCacheHit() => Interlocked.Increment(ref _targetMatchCacheHitCount);

        public void RecordTargetMatchCacheMiss() => Interlocked.Increment(ref _targetMatchCacheMissCount);

        public void Stop() => _stopwatch.Stop();

        public MarkRuleTelemetry CreateTelemetry()
        {
            return new MarkRuleTelemetry(
              RuleOrder,
              RuleId,
              GroupKey,
              _stopwatch.ElapsedMilliseconds,
              Volatile.Read(ref _candidateMarkCount),
              Volatile.Read(ref _acceptedMarkCount),
              Volatile.Read(ref _graphBindingFallbackCount),
              Volatile.Read(ref _atomicCandidateIndexHitCount),
              Volatile.Read(ref _atomicCandidateIndexMissCount),
              Volatile.Read(ref _operationLookupCacheHitCount),
              Volatile.Read(ref _operationLookupCacheMissCount),
              Volatile.Read(ref _graphBindingIndexHitCount),
              Volatile.Read(ref _graphBindingIndexMissCount),
              Volatile.Read(ref _regionCacheHitCount),
              Volatile.Read(ref _regionCacheMissCount),
              Volatile.Read(ref _targetMatchCacheHitCount),
              Volatile.Read(ref _targetMatchCacheMissCount));
        }
    }

    public sealed class MarkRuleTelemetryScope : IDisposable
    {
        private readonly MarkAnalysisSnapshot _snapshot;
        private readonly MarkRuleTelemetryAccumulator _accumulator;
        private readonly MarkRuleTelemetryScope? _previous;
        private bool _disposed;

        internal MarkRuleTelemetryScope(
          MarkAnalysisSnapshot snapshot,
          MarkRuleTelemetryAccumulator accumulator,
          MarkRuleTelemetryScope? previous)
        {
            _snapshot = snapshot;
            _accumulator = accumulator;
            _previous = previous;
        }

        public void RecordCandidateMark() => _accumulator.RecordCandidateMark();

        public void RecordAcceptedMark() => _accumulator.RecordAcceptedMark();

        public void RecordGraphBindingFallback() => _accumulator.RecordGraphBindingFallback();

        public void RecordAtomicCandidateIndexHit() => _accumulator.RecordAtomicCandidateIndexHit();

        public void RecordAtomicCandidateIndexMiss() => _accumulator.RecordAtomicCandidateIndexMiss();

        public void RecordOperationLookupCacheHit() => _accumulator.RecordOperationLookupCacheHit();

        public void RecordOperationLookupCacheMiss() => _accumulator.RecordOperationLookupCacheMiss();

        public void RecordGraphBindingIndexHit() => _accumulator.RecordGraphBindingIndexHit();

        public void RecordGraphBindingIndexMiss() => _accumulator.RecordGraphBindingIndexMiss();

        public void RecordRegionCacheHit() => _accumulator.RecordRegionCacheHit();

        public void RecordRegionCacheMiss() => _accumulator.RecordRegionCacheMiss();

        public void RecordTargetMatchCacheHit() => _accumulator.RecordTargetMatchCacheHit();

        public void RecordTargetMatchCacheMiss() => _accumulator.RecordTargetMatchCacheMiss();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _accumulator.Stop();
            _snapshot.RestoreActiveRuleTelemetry(_previous);
            _disposed = true;
        }
    }
}

public sealed record MarkAnalysisTelemetry(
  long AtomicCandidateIndexHitCount,
  long AtomicCandidateIndexMissCount,
  long OperationLookupCacheHitCount,
  long OperationLookupCacheMissCount,
  long GraphBindingIndexHitCount,
  long GraphBindingIndexMissCount,
  long RegionCacheHitCount,
  long RegionCacheMissCount,
  long TargetMatchCacheHitCount,
  long TargetMatchCacheMissCount,
  long SliceQueryCacheHitCount,
  long SliceQueryCacheMissCount,
  IReadOnlyList<MarkRuleTelemetry> RuleTelemetry);

public sealed record MarkRuleTelemetry(
  int RuleOrder,
  string RuleId,
  string? GroupKey,
  long ElapsedMilliseconds,
  long CandidateMarkCount,
  long AcceptedMarkCount,
  long GraphBindingFallbackCount,
  long AtomicCandidateIndexHitCount,
  long AtomicCandidateIndexMissCount,
  long OperationLookupCacheHitCount,
  long OperationLookupCacheMissCount,
  long GraphBindingIndexHitCount,
  long GraphBindingIndexMissCount,
  long RegionCacheHitCount,
  long RegionCacheMissCount,
  long TargetMatchCacheHitCount,
  long TargetMatchCacheMissCount);
