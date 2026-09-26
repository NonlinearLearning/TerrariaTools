using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NLCPG.Configuration;

public sealed class UnifiedYamlDocument
{
    private static readonly IDeserializer DuplicateKeyChecker = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .WithDuplicateKeyChecking()
      .Build();

    private UnifiedYamlDocument(string path, YamlMappingNode root)
    {
        Path = path;
        Directory = System.IO.Path.GetDirectoryName(path)!
          ?? throw new ArgumentException($"Configuration file has no parent directory: {path}");
        Root = root;
    }

    public string Path { get; }

    public string Directory { get; }

    public YamlMappingNode Root { get; }

    public static UnifiedYamlDocument LoadFromWorkingDirectory()
    {
        return Load(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "nlissn.yml"));
    }

    public static UnifiedYamlDocument Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ArgumentException($"Configuration file does not exist: {fullPath}");
        }

        var yamlText = File.ReadAllText(fullPath);
        try
        {
            DuplicateKeyChecker.Deserialize<object>(yamlText);
            var stream = new YamlStream();
            stream.Load(new StringReader(yamlText));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                throw new ArgumentException("Configuration file must contain one YAML mapping.");
            }

            return new UnifiedYamlDocument(fullPath, root);
        }
        catch (YamlException exception)
        {
            throw new ArgumentException($"Invalid YAML configuration: {exception.Message}", exception);
        }
    }

    public static void EnsureAllowed(
      YamlMappingNode mapping,
      string path,
      IReadOnlySet<string> allowedKeys)
    {
        foreach (var entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
            {
                throw new ArgumentException($"{path} contains a non-scalar property name.");
            }

            if (!allowedKeys.Contains(key.Value))
            {
                throw new ArgumentException($"Unknown configuration property: {path}.{key.Value}");
            }
        }
    }

    public static YamlMappingNode RequireMapping(
      YamlMappingNode parent,
      string key,
      string path)
    {
        return GetMapping(parent, key, path)
          ?? throw new ArgumentException($"Configuration property is required: {path}.{key}");
    }

    public static YamlMappingNode? GetMapping(
      YamlMappingNode parent,
      string key,
      string path)
    {
        var node = GetNode(parent, key, path);
        if (node is null)
        {
            return null;
        }

        return node as YamlMappingNode
          ?? throw new ArgumentException($"Configuration property must be a mapping: {path}.{key}");
    }

    public static YamlSequenceNode? GetSequence(
      YamlMappingNode parent,
      string key,
      string path)
    {
        var node = GetNode(parent, key, path);
        if (node is null)
        {
            return null;
        }

        return node as YamlSequenceNode
          ?? throw new ArgumentException($"Configuration property must be a sequence: {path}.{key}");
    }

    public static string? GetScalar(
      YamlMappingNode parent,
      string key,
      string path)
    {
        var node = GetNode(parent, key, path);
        if (node is null)
        {
            return null;
        }

        return node is YamlScalarNode scalar && scalar.Value is not null
          ? scalar.Value
          : throw new ArgumentException(
              $"Configuration property must be a scalar: {path}.{key} ({node.GetType().FullName}).");
    }

    public static string RequireScalar(
      YamlMappingNode parent,
      string key,
      string path)
    {
        return GetScalar(parent, key, path)
          ?? throw new ArgumentException($"Configuration property is required: {path}.{key}");
    }

    public static string ResolvePath(string directory, string value)
    {
        return System.IO.Path.GetFullPath(
          System.IO.Path.IsPathRooted(value)
            ? value
            : System.IO.Path.Combine(directory, value));
    }

    public static int ParseInt(string value, string path)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Configuration property must be an integer: {path}");
        }

        return parsed;
    }

    public static bool ParseBoolean(string value, string path)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Configuration property must be boolean: {path}");
        }

        return parsed;
    }

    private static YamlNode? GetNode(YamlMappingNode parent, string key, string path)
    {
        var matching = parent.Children.Keys
          .OfType<YamlScalarNode>()
          .SingleOrDefault(node => string.Equals(node.Value, key, StringComparison.Ordinal));
        return matching is null ? null : parent.Children[matching];
    }
}
