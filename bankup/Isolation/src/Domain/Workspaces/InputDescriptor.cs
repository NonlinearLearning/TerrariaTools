using Domain.Rules;

namespace Domain.Workspaces;

/// <summary>
/// 表示一次运行的输入语义描述。
/// </summary>
public sealed class InputDescriptor
{
    private InputDescriptor(
        InputOrigin origin,
        WorkspacePath sourcePath,
        RunMode runMode,
        RuleSet ruleSet)
    {
        Origin = origin;
        SourcePathValue = sourcePath;
        RunMode = runMode;
        RuleSet = ruleSet;
    }

    /// <summary>
    /// 获取输入来源。
    /// </summary>
    public InputOrigin Origin { get; }

    /// <summary>
    /// 获取输入路径。
    /// </summary>
    public string SourcePath => SourcePathValue.Value;

    /// <summary>
    /// 获取输入路径值对象。
    /// </summary>
    public WorkspacePath SourcePathValue { get; }

    /// <summary>
    /// 获取运行模式。
    /// </summary>
    public RunMode RunMode { get; }

    /// <summary>
    /// 获取规则集合。
    /// </summary>
    public RuleSet RuleSet { get; }

    /// <summary>
    /// 创建输入描述。
    /// </summary>
    public static InputDescriptor Create(
        InputOrigin origin,
        string sourcePath,
        RunMode runMode,
        RuleSet ruleSet)
    {
        return Create(origin, WorkspacePath.Create(sourcePath), runMode, ruleSet);
    }

    public static InputDescriptor Create(
        InputOrigin origin,
        WorkspacePath sourcePath,
        RunMode runMode,
        RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        return new InputDescriptor(origin, sourcePath, runMode, ruleSet);
    }
}

/// <summary>
/// 表示输入来源类型。
/// </summary>
public enum InputOrigin
{
    Unknown = 0,
    Solution = 1,
    Project = 2,
    Directory = 3,
    SourceFile = 4,
}
