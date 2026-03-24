namespace TerrariaTools.Dome.Adapters.Runtime.Process;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;

public sealed class TerrariaRuntimeShadowLayoutFactory
{
    public ApplicationAbstractions.TerrariaRuntimeShadowLayout Create(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request)
    {
        var sourceRootPath = Path.GetDirectoryName(request.SolutionPath)
            ?? throw new InvalidOperationException("TR solution path must have a parent directory.");
        var solutionFileName = Path.GetFileName(request.SolutionPath);
        var workspacePath = Path.Combine(request.OutputRootPath, "workspace");
        var artifactsPath = Path.Combine(request.OutputRootPath, "artifacts");
        var dependencyEnvironmentPath = Path.Combine(request.OutputRootPath, "dependency-env");
        var workspaceSolutionPath = Path.Combine(workspacePath, solutionFileName);

        return new ApplicationAbstractions.TerrariaRuntimeShadowLayout(
            request.SolutionPath,
            sourceRootPath,
            request.OutputRootPath,
            workspacePath,
            artifactsPath,
            dependencyEnvironmentPath,
            workspaceSolutionPath);
    }
}



