using NLCPG.Configuration;
using NLCPG.Contracts;
using NLCPG.Model;
using YamlDotNet.RepresentationModel;

namespace NLCPG.Cli;

internal sealed record NLCPGYamlConfiguration(
  string? InputPath,
  NLCPGLocalViewConfiguration? LocalView,
  string? JsonOutputPath);

internal sealed record NLCPGLocalViewConfiguration(
  NLCPGAnchorSelector AnchorSelector,
  int Hops,
  NLCPGViewDirection Direction,
  IReadOnlyCollection<NLCPGEdgeKind>? EdgeKinds);

internal sealed record NLCPGAnchorSelector(
  NLCPGAnchorSelectorKind Kind,
  string Value,
  NodeId? NodeId);

internal enum NLCPGAnchorSelectorKind
{
    NodeId,
    FullName,
    Name,
}

internal static class NLCPGYamlConfigurationLoader
{
    private static readonly IReadOnlySet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "schemaVersion", "tool", "runId", "input", "nlcpg"
    };

    private static readonly IReadOnlySet<string> InputKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "path"
    };

    private static readonly IReadOnlySet<string> NlcpGKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "view", "output"
    };

    private static readonly IReadOnlySet<string> ViewKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "mode", "anchor", "hops", "direction", "edgeKinds"
    };

    private static readonly IReadOnlySet<string> AnchorKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "nodeId", "fullName", "name"
    };

    private static readonly IReadOnlySet<string> OutputKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "json"
    };

    internal static NLCPGYamlConfiguration LoadFromWorkingDirectory()
    {
        return Load(UnifiedYamlDocument.LoadFromWorkingDirectory());
    }

    internal static NLCPGYamlConfiguration Load(UnifiedYamlDocument document)
    {
        RequireSchemaAndTool(document.Root);
        UnifiedYamlDocument.EnsureAllowed(document.Root, "configuration", RootKeys);

        var input = UnifiedYamlDocument.GetMapping(document.Root, "input", "input");
        string? inputPath = null;
        if (input is not null)
        {
            UnifiedYamlDocument.EnsureAllowed(input, "input", InputKeys);
            var configuredPath = UnifiedYamlDocument.GetScalar(input, "path", "input");
            if (configuredPath is not null)
            {
                inputPath = UnifiedYamlDocument.ResolvePath(document.Directory, configuredPath);
                if (!File.Exists(inputPath))
                {
                    throw new ArgumentException($"Input file does not exist: {inputPath}");
                }
            }
        }

        var nlcpg = UnifiedYamlDocument.RequireMapping(document.Root, "nlcpg", "configuration");
        UnifiedYamlDocument.EnsureAllowed(nlcpg, "nlcpg", NlcpGKeys);
        var view = ParseView(nlcpg);
        var output = ParseOutput(nlcpg, document.Directory, view is not null);
        return new NLCPGYamlConfiguration(inputPath, view, output);
    }

    private static NLCPGLocalViewConfiguration? ParseView(YamlMappingNode nlcpg)
    {
        var view = UnifiedYamlDocument.GetMapping(nlcpg, "view", "nlcpg");
        if (view is null)
        {
            return null;
        }

        UnifiedYamlDocument.EnsureAllowed(view, "nlcpg.view", ViewKeys);
        var mode = UnifiedYamlDocument.GetScalar(view, "mode", "nlcpg.view") ?? "stats";
        if (string.Equals(mode, "stats", StringComparison.OrdinalIgnoreCase))
        {
            if (UnifiedYamlDocument.GetMapping(view, "anchor", "nlcpg.view") is not null)
            {
                throw new ArgumentException("nlcpg.view.anchor requires nlcpg.view.mode: local.");
            }

            return null;
        }

        if (!string.Equals(mode, "local", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported nlcpg.view.mode: {mode}");
        }

        var anchor = UnifiedYamlDocument.RequireMapping(view, "anchor", "nlcpg.view");
        var selector = ParseAnchor(anchor);
        var hops = ParseNonNegativeInt(view, "hops", "nlcpg.view", 1);
        var direction = ParseDirection(
          UnifiedYamlDocument.GetScalar(view, "direction", "nlcpg.view") ?? "both");
        var edgeKinds = ParseEdgeKinds(view);
        return new NLCPGLocalViewConfiguration(selector, hops, direction, edgeKinds);
    }

    private static string? ParseOutput(YamlMappingNode nlcpg, string directory, bool hasLocalView)
    {
        var output = UnifiedYamlDocument.GetMapping(nlcpg, "output", "nlcpg");
        if (output is null)
        {
            return null;
        }

        UnifiedYamlDocument.EnsureAllowed(output, "nlcpg.output", OutputKeys);
        var json = UnifiedYamlDocument.GetScalar(output, "json", "nlcpg.output");
        if (json is not null && !hasLocalView)
        {
            throw new ArgumentException("nlcpg.output.json requires nlcpg.view.mode: local.");
        }

        return json is null ? null : UnifiedYamlDocument.ResolvePath(directory, json);
    }

    private static NLCPGAnchorSelector ParseAnchor(YamlMappingNode anchor)
    {
        UnifiedYamlDocument.EnsureAllowed(anchor, "nlcpg.view.anchor", AnchorKeys);
        var nodeId = UnifiedYamlDocument.GetScalar(anchor, "nodeId", "nlcpg.view.anchor");
        var fullName = UnifiedYamlDocument.GetScalar(anchor, "fullName", "nlcpg.view.anchor");
        var name = UnifiedYamlDocument.GetScalar(anchor, "name", "nlcpg.view.anchor");
        var count = new[] { nodeId, fullName, name }.Count(value => value is not null);
        if (count != 1)
        {
            throw new ArgumentException(
              "Exactly one nlcpg.view.anchor selector is required: nodeId, fullName, or name.");
        }

        if (nodeId is not null)
        {
            var parsed = UnifiedYamlDocument.ParseInt(nodeId, "nlcpg.view.anchor.nodeId");
            if (parsed < 0)
            {
                throw new ArgumentException("nlcpg.view.anchor.nodeId must be non-negative.");
            }

            return new NLCPGAnchorSelector(
              NLCPGAnchorSelectorKind.NodeId,
              nodeId,
              new NodeId((uint)parsed));
        }

        return fullName is not null
          ? new NLCPGAnchorSelector(NLCPGAnchorSelectorKind.FullName, fullName, null)
          : new NLCPGAnchorSelector(NLCPGAnchorSelectorKind.Name, name!, null);
    }

    private static IReadOnlyCollection<NLCPGEdgeKind>? ParseEdgeKinds(YamlMappingNode view)
    {
        var sequence = UnifiedYamlDocument.GetSequence(view, "edgeKinds", "nlcpg.view");
        if (sequence is null || sequence.Children.Count == 0)
        {
            return null;
        }

        var edgeKinds = new HashSet<NLCPGEdgeKind>();
        foreach (var child in sequence.Children)
        {
            if (child is not YamlScalarNode scalar || scalar.Value is null ||
                !Enum.TryParse<NLCPGEdgeKind>(scalar.Value, true, out var edgeKind))
            {
                throw new ArgumentException("nlcpg.view.edgeKinds must contain valid NLCPG edge kind names.");
            }

            edgeKinds.Add(edgeKind);
        }

        return edgeKinds;
    }

    private static NLCPGViewDirection ParseDirection(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "both" => NLCPGViewDirection.Both,
            "in" or "incoming" => NLCPGViewDirection.Incoming,
            "out" or "outgoing" => NLCPGViewDirection.Outgoing,
            _ => throw new ArgumentException($"Unsupported nlcpg.view.direction: {value}"),
        };
    }

    private static int ParseNonNegativeInt(
      YamlMappingNode mapping,
      string key,
      string path,
      int defaultValue)
    {
        var value = UnifiedYamlDocument.GetScalar(mapping, key, path);
        if (value is null)
        {
            return defaultValue;
        }

        var parsed = UnifiedYamlDocument.ParseInt(value, $"{path}.{key}");
        if (parsed < 0)
        {
            throw new ArgumentException($"{path}.{key} must be non-negative.");
        }

        return parsed;
    }

    private static void RequireSchemaAndTool(YamlMappingNode root)
    {
        var schemaVersion = UnifiedYamlDocument.RequireScalar(root, "schemaVersion", "configuration");
        if (UnifiedYamlDocument.ParseInt(schemaVersion, "schemaVersion") != 3)
        {
            throw new ArgumentException("NLCPG requires unified configuration schemaVersion: 3.");
        }

        var tool = UnifiedYamlDocument.RequireScalar(root, "tool", "configuration");
        if (!string.Equals(tool, "nlcpg", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Configuration tool '{tool}' does not match NLCPG; expected 'nlcpg'.");
        }
    }
}
