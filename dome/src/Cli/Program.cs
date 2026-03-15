using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Cli;

// 解析命令行参数
var parseResult = await DomeCliParser.ParseAsync(args, CancellationToken.None);
if (!parseResult.IsSuccess || (parseResult.Request == null && parseResult.TerrariaRuntimeRunRequest == null && parseResult.TerrariaRuntimeShadowExtractionRequest == null))
{
    Console.Error.WriteLine(parseResult.ErrorMessage ?? DomeCliParser.UsageText);
    return 1;
}

RunResult result;
if (parseResult.TerrariaRuntimeRunRequest != null)
{
    result = await DomeApplicationFactory.CreateDefaultTerrariaRuntimeApplication().RunAsync(parseResult.TerrariaRuntimeRunRequest, CancellationToken.None);
}
else if (parseResult.TerrariaRuntimeShadowExtractionRequest != null)
{
    result = await DomeApplicationFactory.CreateDefaultTerrariaRuntimeShadowExtractionApplication().RunAsync(parseResult.TerrariaRuntimeShadowExtractionRequest, CancellationToken.None);
}
else
{
    result = await DomeApplicationFactory.CreateDefault().RunAsync(parseResult.Request!, CancellationToken.None);
}
if (!result.IsSuccess)
{
    Console.Error.WriteLine(result.Message ?? result.FailureCode.ToString());
    return MapExitCode(result.FailureCode);
}

Console.WriteLine($"Artifacts written to {result.OutputPath}");
return 0;

/// <summary>
/// 将失败代码映射为退出代码。
/// </summary>
/// <param name="failureCode">失败代码。</param>
/// <returns>进程退出代码。</returns>
static int MapExitCode(FailureCode failureCode)
{
    return failureCode switch
    {
        FailureCode.WorkspaceLoadFailed => 2,
        FailureCode.AnalysisFailed => 3,
        FailureCode.PlanCompileFailed => 4,
        FailureCode.RewriteFailed => 5,
        FailureCode.ReportFailed => 6,
        FailureCode.BuildFailed => 7,
        _ => 1
    };
}
