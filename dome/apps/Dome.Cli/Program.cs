using TerrariaTools.Dome.Cli;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

var parseResult = await DomeCliParser.ParseAsync(args, CancellationToken.None);
if (!parseResult.IsSuccess || parseResult.Invocation == null)
{
    Console.Error.WriteLine(parseResult.ErrorMessage ?? DomeCliParser.UsageText);
    return 1;
}

var result = await DomeCliApplicationExecutor.CreateDefault().RunAsync(parseResult.Invocation, CancellationToken.None);
if (!result.IsSuccess)
{
    Console.Error.WriteLine(result.Message ?? result.FailureCode.ToString());
    return MapExitCode(result.FailureCode);
}

Console.WriteLine($"Artifacts written to {result.OutputPath}");
return 0;

/// <summary>
/// 将失败代码映射为进程退出码。
/// </summary>
/// <param name="failureCode">失败代码。</param>
/// <returns>对应的退出码。</returns>
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

