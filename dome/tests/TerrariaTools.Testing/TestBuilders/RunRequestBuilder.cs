using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class RunRequestBuilder
{
    private string _inputPath = "input";
    private string _outputPath = "out";
    private IReadOnlyList<string> _ruleSet = Array.Empty<string>();
    private RunMode _mode = RunMode.Standard;
    private WorkspaceLoadOptions _workspaceLoadOptions = WorkspaceLoadOptions.Default;

    public RunRequestBuilder WithInputPath(string inputPath)
    {
        _inputPath = inputPath;
        return this;
    }

    public RunRequestBuilder WithOutputPath(string outputPath)
    {
        _outputPath = outputPath;
        return this;
    }

    public RunRequestBuilder WithRuleSet(params string[] ruleSet)
    {
        _ruleSet = ruleSet;
        return this;
    }

    public RunRequestBuilder WithMode(RunMode mode)
    {
        _mode = mode;
        return this;
    }

    public RunRequestBuilder WithWorkspaceLoadOptions(WorkspaceLoadOptions workspaceLoadOptions)
    {
        _workspaceLoadOptions = workspaceLoadOptions;
        return this;
    }

    public RunRequest Build() => new(_inputPath, _outputPath, _ruleSet, _mode, _workspaceLoadOptions);
}
