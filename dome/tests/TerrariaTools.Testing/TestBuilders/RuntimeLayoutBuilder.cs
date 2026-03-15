using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class RuntimeLayoutBuilder
{
    private string _solutionPath = "input\\TerrariaServer.sln";
    private string _sourceRootPath = "input";
    private string _outputRootPath = "out";
    private string _dependencyEnvironmentPath = "out\\dependency-env";
    private string _workspacePath = "out\\workspace";
    private string _artifactsPath = "out\\artifacts";
    private string _workspaceSolutionPath = "out\\workspace\\TerrariaServer.sln";

    public RuntimeLayoutBuilder WithSolutionPath(string solutionPath)
    {
        _solutionPath = solutionPath;
        return this;
    }

    public RuntimeLayoutBuilder WithSourceRootPath(string sourceRootPath)
    {
        _sourceRootPath = sourceRootPath;
        return this;
    }

    public RuntimeLayoutBuilder WithOutputRootPath(string outputRootPath)
    {
        _outputRootPath = outputRootPath;
        return this;
    }

    public RuntimeLayoutBuilder WithDependencyEnvironmentPath(string dependencyEnvironmentPath)
    {
        _dependencyEnvironmentPath = dependencyEnvironmentPath;
        return this;
    }

    public RuntimeLayoutBuilder WithWorkspacePath(string workspacePath)
    {
        _workspacePath = workspacePath;
        return this;
    }

    public RuntimeLayoutBuilder WithArtifactsPath(string artifactsPath)
    {
        _artifactsPath = artifactsPath;
        return this;
    }

    public RuntimeLayoutBuilder WithWorkspaceSolutionPath(string workspaceSolutionPath)
    {
        _workspaceSolutionPath = workspaceSolutionPath;
        return this;
    }

    public TerrariaRuntimeLayout Build() =>
        new(
            _solutionPath,
            _sourceRootPath,
            _outputRootPath,
            _dependencyEnvironmentPath,
            _workspacePath,
            _artifactsPath,
            _workspaceSolutionPath);
}
