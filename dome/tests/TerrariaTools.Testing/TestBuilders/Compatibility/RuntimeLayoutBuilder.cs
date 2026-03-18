using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native runtime layout contracts.
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
