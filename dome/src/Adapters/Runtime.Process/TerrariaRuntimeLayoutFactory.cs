namespace TerrariaTools.Dome.Adapters.Runtime.Process;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;

public sealed class TerrariaRuntimeLayoutFactory : ITerrariaRuntimeLayoutFactory
{
    public ApplicationAbstractions.TerrariaRuntimeLayout Create(ApplicationAbstractions.TerrariaRuntimeRunRequest request)
    {
        var sourceRootPath = Path.GetDirectoryName(request.SolutionPath)
            ?? throw new InvalidOperationException("TR solution path must have a parent directory.");
        var solutionFileName = Path.GetFileName(request.SolutionPath);
        var dependencyEnvironmentPath = Path.Combine(request.OutputRootPath, "dependency-env");
        var workspacePath = Path.Combine(request.OutputRootPath, "workspace");
        var artifactsPath = Path.Combine(request.OutputRootPath, "artifacts");
        var workspaceSolutionPath = Path.Combine(workspacePath, solutionFileName);

        return new ApplicationAbstractions.TerrariaRuntimeLayout(
            request.SolutionPath,
            sourceRootPath,
            request.OutputRootPath,
            dependencyEnvironmentPath,
            workspacePath,
            artifactsPath,
            workspaceSolutionPath);
    }
}




