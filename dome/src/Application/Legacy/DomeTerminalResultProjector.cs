namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

internal static class DomeTerminalResultProjector
{
    internal static ApplicationAbstractions.RunResult Project(ApplicationAbstractions.RunResult result) => result;

    internal static ApplicationAbstractions.RunResult Success(string outputPath, string reportPath) =>
        ApplicationAbstractions.RunResult.Success(outputPath, reportPath);

    internal static ApplicationAbstractions.RunResult Failure(ModelPrimitives.FailureCode failureCode, string outputPath, string? message) =>
        ApplicationAbstractions.RunResult.Failure(failureCode, outputPath, message);
}
