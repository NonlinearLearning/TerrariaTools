using System.Text.Json;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Cli;

/// <summary>
/// 提供最小 Roslyn CPG 的命令行入口与局部视图查询能力。
/// </summary>
public sealed class MinimalRoslynCpgCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 解析参数、构图，并输出图统计或局部视图。
    /// </summary>
    public int Run(string[] args)
    {
        var options = Parse(args);
        if (options.ShowHelp)
        {
            WriteHelp();
            return 0;
        }

        var source = options.InputPath is not null && File.Exists(options.InputPath)
          ? File.ReadAllText(options.InputPath)
          : DefaultSource;
        var filePath = options.InputPath ?? "demo.cs";
        // CLI 自身不持有分析逻辑，只负责编排构图与输出。
        var graph = new RoslynCpgBuilder().BuildFromSource(source, filePath);

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
              .ThenBy(node => node.FullName, StringComparer.Ordinal)
              .ThenBy(node => node.Name, StringComparer.Ordinal))
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
        if (options.JsonOutPath is not null)
        {
            WriteLocalViewJson(localView, options.JsonOutPath);
        }

        WriteLocalViewSummary(graph, localView, options.LocalView.Direction, options.LocalView.EdgeKinds);
        return 0;
    }

    /// <summary>
    /// 将原始命令行参数解析成一个已校验的选项对象。
    /// </summary>
    private static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? inputPath = null;
        string? view = null;
        string? anchorNodeId = null;
        string? anchorFullName = null;
        string? anchorName = null;
        string? jsonOutPath = null;
        var hops = 1;
        var direction = RoslynCpgViewDirection.Both;
        HashSet<RoslynCpgEdgeKind>? edgeKinds = null;
        var showHelp = false;

        for (var index = 0; index < args.Count; index += 1)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--view":
                    view = ReadRequiredValue(args, ref index, "--view");
                    break;
                case "--anchor-node-id":
                    anchorNodeId = ReadRequiredValue(args, ref index, "--anchor-node-id");
                    break;
                case "--anchor-full-name":
                    anchorFullName = ReadRequiredValue(args, ref index, "--anchor-full-name");
                    break;
                case "--anchor-name":
                    anchorName = ReadRequiredValue(args, ref index, "--anchor-name");
                    break;
                case "--hops":
                    hops = ParsePositiveInt(ReadRequiredValue(args, ref index, "--hops"), "--hops");
                    break;
                case "--direction":
                    direction = ParseDirection(ReadRequiredValue(args, ref index, "--direction"));
                    break;
                case "--edge-kinds":
                    edgeKinds = ParseEdgeKinds(ReadRequiredValue(args, ref index, "--edge-kinds"));
                    break;
                case "--json-out":
                    jsonOutPath = ReadRequiredValue(args, ref index, "--json-out");
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option: {arg}");
                    }

                    if (inputPath is not null)
                    {
                        throw new ArgumentException($"Unexpected positional argument: {arg}");
                    }

                    inputPath = arg;
                    break;
            }
        }

        LocalViewOptions? localView = null;
        if (view is not null)
        {
            if (!string.Equals(view, "local", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unsupported view: {view}");
            }

            var selector = BuildAnchorSelector(anchorNodeId, anchorFullName, anchorName);
            localView = new LocalViewOptions(selector, hops, direction, edgeKinds);
        }
        else if (anchorNodeId is not null || anchorFullName is not null || anchorName is not null)
        {
            throw new ArgumentException("Anchor options require --view local.");
        }

        if (jsonOutPath is not null && localView is null)
        {
            throw new ArgumentException("--json-out currently requires --view local.");
        }

        return new CliOptions(inputPath, localView, jsonOutPath, showHelp);
    }

    private static string ReadRequiredValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        var valueIndex = index + 1;
        if (valueIndex >= args.Count)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        index = valueIndex;
        return args[valueIndex];
    }

    private static int ParsePositiveInt(string value, string optionName)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new ArgumentException($"{optionName} must be a non-negative integer.");
        }

        return parsed;
    }

    private static RoslynCpgViewDirection ParseDirection(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "both" => RoslynCpgViewDirection.Both,
            "in" or "incoming" => RoslynCpgViewDirection.Incoming,
            "out" or "outgoing" => RoslynCpgViewDirection.Outgoing,
            _ => throw new ArgumentException($"Unsupported direction: {value}"),
        };
    }

    private static HashSet<RoslynCpgEdgeKind> ParseEdgeKinds(string value)
    {
        var kinds = new HashSet<RoslynCpgEdgeKind>();
        foreach (var rawKind in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<RoslynCpgEdgeKind>(rawKind, ignoreCase: true, out var edgeKind))
            {
                throw new ArgumentException($"Unsupported edge kind: {rawKind}");
            }

            kinds.Add(edgeKind);
        }

        if (kinds.Count == 0)
        {
            throw new ArgumentException("--edge-kinds must include at least one value.");
        }

        return kinds;
    }

    /// <summary>
    /// 解析唯一有效的锚点选择器，并拒绝歧义输入。
    /// </summary>
    private static AnchorSelector BuildAnchorSelector(string? anchorNodeId, string? anchorFullName, string? anchorName)
    {
        var providedCount = 0;
        providedCount += anchorNodeId is null ? 0 : 1;
        providedCount += anchorFullName is null ? 0 : 1;
        providedCount += anchorName is null ? 0 : 1;
        if (providedCount != 1)
        {
            throw new ArgumentException(
              "Exactly one anchor selector is required: --anchor-node-id, --anchor-full-name, or --anchor-name.");
        }

        if (anchorNodeId is not null)
        {
            if (!uint.TryParse(anchorNodeId, out var parsed))
            {
                throw new ArgumentException("--anchor-node-id must be an unsigned integer.");
            }

            return new AnchorSelector(AnchorSelectorKind.NodeId, anchorNodeId, new NodeId(parsed));
        }

        if (anchorFullName is not null)
        {
            return new AnchorSelector(AnchorSelectorKind.FullName, anchorFullName, null);
        }

        return new AnchorSelector(AnchorSelectorKind.Name, anchorName!, null);
    }

    /// <summary>
    /// 按锚点模式在图中查找匹配节点。
    /// </summary>
    private static IReadOnlyList<RoslynCpgNode> ResolveAnchorMatches(RoslynCpgGraph graph, AnchorSelector selector)
    {
        return selector.Kind switch
        {
            AnchorSelectorKind.NodeId => graph.Nodes
              .Where(node => node.NodeId == selector.NodeId)
              .ToList(),
            AnchorSelectorKind.FullName => graph.Nodes
              .Where(node => string.Equals(node.FullName, selector.Value, StringComparison.Ordinal))
              .ToList(),
            AnchorSelectorKind.Name => graph.Nodes
              .Where(node => string.Equals(node.Name, selector.Value, StringComparison.Ordinal))
              .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(selector)),
        };
    }

    private static string DescribeAnchor(AnchorSelector selector)
    {
        return selector.Kind switch
        {
            AnchorSelectorKind.NodeId => $"nodeId '{selector.Value}'",
            AnchorSelectorKind.FullName => $"fullName '{selector.Value}'",
            AnchorSelectorKind.Name => $"name '{selector.Value}'",
            _ => selector.Value,
        };
    }

    private static void WriteGraphStats(RoslynCpgGraph graph)
    {
        Console.WriteLine($"Nodes: {graph.Nodes.Count}");
        Console.WriteLine($"Edges: {graph.Edges.Count}");

        foreach (var kind in Enum.GetValues<RoslynCpgNodeKind>())
        {
            var count = graph.Nodes.Count(node => node.Kind == kind);
            if (count > 0)
            {
                Console.WriteLine($"{kind}: {count}");
            }
        }
    }

    private static void WriteLocalViewJson(RoslynCpgLocalView localView, string outputPath)
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
                localView.Anchor.DisplayKind,
                localView.Anchor.Name,
                localView.Anchor.FullName,
                localView.Anchor.FilePath,
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
                  node.DisplayKind,
                  node.Name,
                  node.FullName,
                  node.Signature,
                  DispatchKind = node.DispatchKind?.ToString(),
                  node.TypeFullName,
                  node.FilePath,
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

    private static void WriteLocalViewSummary(RoslynCpgGraph graph, RoslynCpgLocalView localView, RoslynCpgViewDirection direction, IReadOnlyCollection<RoslynCpgEdgeKind>? edgeKinds)
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

    private static string FormatNode(RoslynCpgGraph graph, RoslynCpgNode node)
    {
        var identity = graph.GetDisplayText(node);
        return $"{node.NodeId} | {node.Kind} | {identity}";
    }

    private static string FormatEdgeKinds(IReadOnlyCollection<RoslynCpgEdgeKind>? edgeKinds)
    {
        return edgeKinds is null || edgeKinds.Count == 0
          ? "all"
          : string.Join(",", edgeKinds.OrderBy(kind => kind.ToString(), StringComparer.Ordinal));
    }

    /// <summary>
    /// 输出当前支持的命令行契约。
    /// </summary>
    private static void WriteHelp()
    {
        Console.WriteLine("MinimalRoslynCpg");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project .\\src\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj [input-path]");
        Console.WriteLine("  dotnet run --project .\\src\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj [input-path] --view local --anchor-node-id <nodeId> [options]");
        Console.WriteLine("  dotnet run --project .\\src\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj [input-path] --view local --anchor-full-name <fullName> [options]");
        Console.WriteLine("  dotnet run --project .\\src\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj [input-path] --view local --anchor-name <name> [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --hops <n>                 Expand local view by n hops. Default: 1.");
        Console.WriteLine("  --direction <both|in|out> Limit traversal direction. Default: both.");
        Console.WriteLine("  --edge-kinds <csv>         Limit traversal to selected edge kinds.");
        Console.WriteLine("  --json-out <path>          Write local view payload to JSON.");
        Console.WriteLine("  --help                     Show this help.");
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

    private sealed record CliOptions(
      string? InputPath,
      LocalViewOptions? LocalView,
      string? JsonOutPath,
      bool ShowHelp);

    private sealed record LocalViewOptions(
      AnchorSelector AnchorSelector,
      int Hops,
      RoslynCpgViewDirection Direction,
      IReadOnlyCollection<RoslynCpgEdgeKind>? EdgeKinds);

    private sealed record AnchorSelector(AnchorSelectorKind Kind, string Value, NodeId? NodeId);

    private enum AnchorSelectorKind
    {
        NodeId,
        FullName,
        Name,
    }
}
