using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Cli;

// 解析命令行参数
var parseResult = await DomeCliParser.ParseAsync(args, CancellationToken.None);
if (!parseResult.IsSuccess || parseResult.Request == null)
{
    Console.Error.WriteLine(parseResult.ErrorMessage ?? DomeCliParser.UsageText);
    return 1;
}

// 创建并运行应用程序
var result = await DomeApplicationFactory.CreateDefault().RunAsync(parseResult.Request, CancellationToken.None);
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
        _ => 1
    };
}
