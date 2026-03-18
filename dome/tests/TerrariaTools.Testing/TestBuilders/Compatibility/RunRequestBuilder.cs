using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native run requests.
/// </summary>
public sealed class RunRequestCompatibilityBuilder
{
    private string _inputPath = "input";
    private string _outputPath = "out";
    private IReadOnlyList<string> _ruleSet = Array.Empty<string>();
    private ModelPrimitives.RunMode _mode = ModelPrimitives.RunMode.Standard;
    private ApplicationAbstractions.WorkspaceLoadOptions _workspaceLoadOptions = ApplicationAbstractions.WorkspaceLoadOptions.Default;

    public RunRequestCompatibilityBuilder WithInputPath(string inputPath)
    {
        _inputPath = inputPath;
        return this;
    }

    public RunRequestCompatibilityBuilder WithOutputPath(string outputPath)
    {
        _outputPath = outputPath;
        return this;
    }

    public RunRequestCompatibilityBuilder WithRuleSet(params string[] ruleSet)
    {
        _ruleSet = ruleSet;
        return this;
    }

    public RunRequestCompatibilityBuilder WithMode(ModelPrimitives.RunMode mode)
    {
        _mode = mode;
        return this;
    }

    public RunRequestCompatibilityBuilder WithWorkspaceLoadOptions(ApplicationAbstractions.WorkspaceLoadOptions workspaceLoadOptions)
    {
        _workspaceLoadOptions = workspaceLoadOptions;
        return this;
    }

    public ApplicationAbstractions.RunRequest Build() => new(_inputPath, _outputPath, _ruleSet, _mode, _workspaceLoadOptions);
}
