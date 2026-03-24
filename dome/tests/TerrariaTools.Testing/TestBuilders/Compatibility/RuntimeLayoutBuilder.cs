using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// 用于构建测试场景中的 Terraria 运行时布局。
/// 方便按需覆盖各个路径字段。
/// </summary>
public sealed class RuntimeLayoutCompatibilityBuilder
{
    private string _solutionPath = "input\\TerrariaServer.sln";
    private string _sourceRootPath = "input";
    private string _outputRootPath = "out";
    private string _dependencyEnvironmentPath = "out\\dependency-env";
    private string _workspacePath = "out\\workspace";
    private string _artifactsPath = "out\\artifacts";
    private string _workspaceSolutionPath = "out\\workspace\\TerrariaServer.sln";

    public RuntimeLayoutCompatibilityBuilder WithSolutionPath(string solutionPath)
    {
        _solutionPath = solutionPath;
        return this;
    }

    public RuntimeLayoutCompatibilityBuilder WithSourceRootPath(string sourceRootPath)
    {
        _sourceRootPath = sourceRootPath;
        return this;
    }

    public RuntimeLayoutCompatibilityBuilder WithOutputRootPath(string outputRootPath)
    {
        _outputRootPath = outputRootPath;
        return this;
    }

    public RuntimeLayoutCompatibilityBuilder WithDependencyEnvironmentPath(string dependencyEnvironmentPath)
    {
        _dependencyEnvironmentPath = dependencyEnvironmentPath;
        return this;
    }

    public RuntimeLayoutCompatibilityBuilder WithWorkspacePath(string workspacePath)
    {
        _workspacePath = workspacePath;
        return this;
    }

    public RuntimeLayoutCompatibilityBuilder WithArtifactsPath(string artifactsPath)
    {
        _artifactsPath = artifactsPath;
        return this;
    }

    public RuntimeLayoutCompatibilityBuilder WithWorkspaceSolutionPath(string workspaceSolutionPath)
    {
        _workspaceSolutionPath = workspaceSolutionPath;
        return this;
    }

    /// <summary>
    /// 构建运行时布局实例。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeLayout Build() =>
        new(
            _solutionPath,
            _sourceRootPath,
            _outputRootPath,
            _dependencyEnvironmentPath,
            _workspacePath,
            _artifactsPath,
            _workspaceSolutionPath);
}



