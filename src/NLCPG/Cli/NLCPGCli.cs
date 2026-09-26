using System.Text.Json;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Cli;

/// 提供NLCPG 的命令行入口与局部视图查询能力。
public sealed class NLCPGCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public int Run(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException(
              "NLCPG reads configuration from nlissn.yml and accepts no command-line parameters.");
        }

        return Run(NLCPGYamlConfigurationLoader.LoadFromWorkingDirectory());
    }

    internal int Run(NLCPGYamlConfiguration options)
    {

        var source = options.InputPath is not null
          ? File.ReadAllText(options.InputPath)
          : DefaultSource;
        var filePath = options.InputPath ?? "demo.cs";
        // CLI 自身不持有分析逻辑，只负责编排构图与输出。
        var graph = new NLCPGBuilder().BuildFromSource(source, filePath);

        if (options.LocalView is null)
        {
            WriteGraphStats(graph);
            return 0;
        }

        var anchorMatches = ResolveAnchorMatches(graph, options.LocalView.AnchorSelector);
        if (anchorMatches.Count == 0)
        {
            Console.Error.WriteLine($"No node matched {DescribeAnchor(options.LocalView.AnchorSelector)}.");
            return 1;
        }

        if (anchorMatches.Count > 1)
        {
            Console.Error.WriteLine($"Anchor {DescribeAnchor(options.LocalView.AnchorSelector)} matched multiple nodes:");
            foreach (var node in anchorMatches
              .OrderBy(node => node.NodeId)
              .ThenBy(node => graph.ResolveFullName(node), StringComparer.Ordinal)
              .ThenBy(node => graph.ResolveName(node), StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"- {FormatNode(graph, node)}");
            }

            return 1;
        }

        var localView = graph.ExtractLocalView(
          anchorMatches[0].NodeId!.Value,
          options.LocalView.Hops,
          options.LocalView.Direction,
          options.LocalView.EdgeKinds);
        if (options.JsonOutputPath is not null)
        {
            WriteLocalViewJson(graph, localView, options.JsonOutputPath);
        }

        WriteLocalViewSummary(graph, localView, options.LocalView.Direction, options.LocalView.EdgeKinds);
        return 0;
    }

    private static IReadOnlyList<NLCPGNode> ResolveAnchorMatches(NLCPGGraph graph, NLCPGAnchorSelector selector)
    {
        return selector.Kind switch
        {
            NLCPGAnchorSelectorKind.NodeId => graph.Nodes
              .Where(node => node.NodeId == selector.NodeId)
              .ToList(),
            NLCPGAnchorSelectorKind.FullName => graph.Nodes
              .Where(node => string.Equals(graph.ResolveFullName(node), selector.Value, StringComparison.Ordinal))
              .ToList(),
            NLCPGAnchorSelectorKind.Name => graph.Nodes
              .Where(node => string.Equals(graph.ResolveName(node), selector.Value, StringComparison.Ordinal))
              .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(selector)),
        };
    }

    private static string DescribeAnchor(NLCPGAnchorSelector selector)
    {
        return selector.Kind switch
        {
            NLCPGAnchorSelectorKind.NodeId => $"nodeId '{selector.Value}'",
            NLCPGAnchorSelectorKind.FullName => $"fullName '{selector.Value}'",
            NLCPGAnchorSelectorKind.Name => $"name '{selector.Value}'",
            _ => selector.Value,
        };
    }

    private static void WriteGraphStats(NLCPGGraph graph)
    {
        Console.WriteLine($"Nodes: {graph.Nodes.Count}");
        Console.WriteLine($"Edges: {graph.Edges.Count}");

        foreach (var kind in Enum.GetValues<NLCPGNodeKind>())
        {
            var count = graph.Nodes.Count(node => node.Kind == kind);
            if (count > 0)
            {
                Console.WriteLine($"{kind}: {count}");
            }
        }
    }

    private static void WriteLocalViewJson(NLCPGGraph graph, NLCPGLocalView localView, string outputPath)
    {
        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var payload = new
        {
            Anchor = new
            {
                NodeId = localView.Anchor.NodeId!.Value,
                Kind = localView.Anchor.Kind.ToString(),
                DisplayKind = graph.ResolveDisplayKind(localView.Anchor),
                Name = graph.ResolveName(localView.Anchor),
                FullName = graph.ResolveFullName(localView.Anchor),
                FilePath = graph.ResolveFilePath(localView.Anchor),
                localView.Anchor.SpanStart,
                localView.Anchor.SpanEnd,
            },
            localView.Hops,
            Nodes = localView.Nodes
              .OrderBy(node => node.NodeId)
              .Select(node => new
              {
                  NodeId = node.NodeId!.Value,
                  Kind = node.Kind.ToString(),
                  DisplayKind = graph.ResolveDisplayKind(node),
                  Name = graph.ResolveName(node),
                  FullName = graph.ResolveFullName(node),
                  Signature = graph.ResolveSignature(node),
                  DispatchKind = node.DispatchKind?.ToString(),
                  TypeFullName = graph.ResolveTypeFullName(node),
                  FilePath = graph.ResolveFilePath(node),
                  node.SpanStart,
                  node.SpanEnd,
                  node.IsImplicit,
              }),
            Edges = localView.Edges
              .OrderBy(edge => edge.SourceNodeId)
              .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
              .ThenBy(edge => edge.TargetNodeId)
              .Select(edge => new
              {
                  SourceNodeId = edge.SourceNodeId.Value,
                  Kind = edge.Kind.ToString(),
                  TargetNodeId = edge.TargetNodeId.Value,
              }),
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, JsonOptions));
        Console.WriteLine($"Wrote local view JSON: {outputPath}");
    }

    private static void WriteLocalViewSummary(NLCPGGraph graph, NLCPGLocalView localView, NLCPGViewDirection direction, IReadOnlyCollection<NLCPGEdgeKind>? edgeKinds)
    {
        Console.WriteLine($"Anchor: {FormatNode(graph, localView.Anchor)}");
        Console.WriteLine($"Hops: {localView.Hops}");
        Console.WriteLine($"Direction: {direction}");
        Console.WriteLine($"EdgeKinds: {FormatEdgeKinds(edgeKinds)}");
        Console.WriteLine($"LocalNodes: {localView.Nodes.Count}");
        Console.WriteLine($"LocalEdges: {localView.Edges.Count}");

        foreach (var kindGroup in localView.Nodes
                   .GroupBy(node => node.Kind)
                   .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal))
        {
            Console.WriteLine($"{kindGroup.Key}: {kindGroup.Count()}");
        }

        Console.WriteLine("Nodes");
        foreach (var node in localView.Nodes.OrderBy(node => node.NodeId))
        {
            Console.WriteLine($"- {FormatNode(graph, node)}");
        }

        Console.WriteLine("Edges");
        foreach (var edge in localView.Edges
                   .OrderBy(edge => edge.SourceNodeId)
                   .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
                   .ThenBy(edge => edge.TargetNodeId))
        {
            Console.WriteLine($"- {edge.SourceNodeId} -[{edge.Kind}]-> {edge.TargetNodeId}");
        }
    }

    private static string FormatNode(NLCPGGraph graph, NLCPGNode node)
    {
        var identity = graph.GetDisplayText(node);
        return $"{node.NodeId} | {node.Kind} | {identity}";
    }

    private static string FormatEdgeKinds(IReadOnlyCollection<NLCPGEdgeKind>? edgeKinds)
    {
        return edgeKinds is null || edgeKinds.Count == 0
          ? "all"
          : string.Join(",", edgeKinds.OrderBy(kind => kind.ToString(), StringComparer.Ordinal));
    }


    private const string DefaultSource =
      """
      namespace Demo;

      public sealed class Sample {
        public int Add(int left, int right) {
          var sum = left + right;
          return sum;
        }
      }
      """;

}
