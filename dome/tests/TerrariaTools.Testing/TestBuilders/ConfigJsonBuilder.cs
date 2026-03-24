namespace TerrariaTools.Testing.TestBuilders;

public sealed class ConfigJsonBuilder
{
    private string? _command = "run";
    private string? _inputPath = "input.cs";
    private string? _outputPath = "out";
    private IReadOnlyList<string>? _ruleSet;
    private string? _logLevel;
    private string? _loader;
    private bool? _allowFallbackToSourceOnly;
    private string? _seedMemberName;

    public ConfigJsonBuilder WithCommand(string? command)
    {
        _command = command;
        return this;
    }

    public ConfigJsonBuilder WithInputPath(string? inputPath)
    {
        _inputPath = inputPath;
        return this;
    }

    public ConfigJsonBuilder WithOutputPath(string? outputPath)
    {
        _outputPath = outputPath;
        return this;
    }

    public ConfigJsonBuilder WithRuleSet(params string[] ruleSet)
    {
        _ruleSet = ruleSet;
        return this;
    }

    public ConfigJsonBuilder WithLogLevel(string? logLevel)
    {
        _logLevel = logLevel;
        return this;
    }

    public ConfigJsonBuilder WithLoader(string? loader)
    {
        _loader = loader;
        return this;
    }

    public ConfigJsonBuilder WithAllowFallback(bool? allowFallback)
    {
        _allowFallbackToSourceOnly = allowFallback;
        return this;
    }

    public ConfigJsonBuilder WithSeedMemberName(string? seedMemberName)
    {
        _seedMemberName = seedMemberName;
        return this;
    }

    public string Build()
    {
        var values = new List<string>();
        Add(values, "Command", _command);
        Add(values, "InputPath", _inputPath);
        Add(values, "OutputPath", _outputPath);
        if (_ruleSet != null)
        {
            values.Add($"""
                  "RuleSet": [{string.Join(", ", _ruleSet.Select(static value => $"\"{value}\""))}]
                """);
        }

        Add(values, "LogLevel", _logLevel);
        Add(values, "Loader", _loader);
        Add(values, "SeedMemberName", _seedMemberName);
        if (_allowFallbackToSourceOnly.HasValue)
        {
            values.Add($"""  "AllowFallbackToSourceOnly": {(_allowFallbackToSourceOnly.Value ? "true" : "false")}""");
        }

        return "{\n" + string.Join(",\n", values) + "\n}";
    }

    private static void Add(List<string> values, string name, string? value)
    {
        if (value != null)
        {
            values.Add($"""  "{name}": "{value}" """);
        }
    }
}
