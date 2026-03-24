using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class RunRequestCompatibilityBuilder
{
    private string _inputPath = "input";
    private string _outputPath = "out";
    private IReadOnlyList<string> _ruleSet = Array.Empty<string>();
    private ModelPrimitives.RunMode _mode = ModelPrimitives.RunMode.Standard;
    private ApplicationAbstractions.WorkspaceLoadOptions _workspaceLoadOptions = ApplicationAbstractions.WorkspaceLoadOptions.Default;

    /// <summary>
    /// 设置输入路径。
    /// </summary>
    public RunRequestCompatibilityBuilder WithInputPath(string inputPath)
    {
        _inputPath = inputPath;
        return this;
    }

    /// <summary>
    /// 设置输出路径。
    /// </summary>
    public RunRequestCompatibilityBuilder WithOutputPath(string outputPath)
    {
        _outputPath = outputPath;
        return this;
    }

    /// <summary>
    /// 设置规则集。
    /// </summary>
    public RunRequestCompatibilityBuilder WithRuleSet(params string[] ruleSet)
    {
        _ruleSet = ruleSet;
        return this;
    }

    /// <summary>
    /// 设置运行模式。
    /// </summary>
    public RunRequestCompatibilityBuilder WithMode(ModelPrimitives.RunMode mode)
    {
        _mode = mode;
        return this;
    }

    /// <summary>
    /// 设置工作区加载选项。
    /// </summary>
    public RunRequestCompatibilityBuilder WithWorkspaceLoadOptions(ApplicationAbstractions.WorkspaceLoadOptions workspaceLoadOptions)
    {
        _workspaceLoadOptions = workspaceLoadOptions;
        return this;
    }

    /// <summary>
    /// 构建运行请求实例。
    /// </summary>
    public ApplicationAbstractions.RunRequest Build() => new(_inputPath, _outputPath, _ruleSet, _mode, _workspaceLoadOptions);
}



