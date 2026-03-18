using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Cli;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

var parseResult = await DomeCliParser.ParseAsync(args, CancellationToken.None);
if (!parseResult.IsSuccess || parseResult.Request == null)
{
    Console.Error.WriteLine(parseResult.ErrorMessage ?? DomeCliParser.UsageText);
    return 1;
}

var result = await DomeApplicationFactory.CreateDefault().RunAsync(parseResult.Request, CancellationToken.None);
if (!result.IsSuccess)
{
    Console.Error.WriteLine(result.Message ?? result.FailureCode.ToString());
    return MapExitCode(result.FailureCode);
}

Console.WriteLine($"Artifacts written to {result.OutputPath}");
return 0;

static int MapExitCode(ModelPrimitives.FailureCode failureCode) =>
    failureCode switch
    {
        ModelPrimitives.FailureCode.None => 0,
        ModelPrimitives.FailureCode.WorkspaceLoadFailed => 2,
        ModelPrimitives.FailureCode.AnalysisFailed => 3,
        ModelPrimitives.FailureCode.PlanCompileFailed => 4,
        ModelPrimitives.FailureCode.RewriteFailed => 5,
        ModelPrimitives.FailureCode.BuildFailed => 6,
        ModelPrimitives.FailureCode.ReportFailed => 7,
        _ => 1
    };
