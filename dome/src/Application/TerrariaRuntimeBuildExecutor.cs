namespace TerrariaTools.Dome.Application;

using System.Diagnostics;
using TerrariaTools.Dome.Core;

/// <summary>
/// TR 运行时进程执行器接口。
/// </summary>
public interface ITerrariaRuntimeProcessRunner
{
    /// <summary>
    /// 异步执行进程。
    /// </summary>
    /// <param name="fileName">可执行文件名。</param>
    /// <param name="arguments">命令参数。</param>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="onStandardOutput">标准输出回调。</param>
    /// <param name="onStandardError">标准错误回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>进程执行结果。</returns>
    Task<TerrariaRuntimeProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken);
}

/// <summary>
/// TR 运行时构建执行器接口。
/// </summary>
public interface ITerrariaRuntimeBuildExecutor
{
    /// <summary>
    /// 执行构建。
    /// </summary>
    /// <param name="layout">运行时目录布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>构建摘要。</returns>
    Task<TerrariaRuntimeBuildSummary> ExecuteAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken);
}

/// <summary>
/// TR 运行时进度上报接口。
/// </summary>
public interface ITerrariaRuntimeProgressReporter
{
    /// <summary>
    /// 上报进度消息。
    /// </summary>
    /// <param name="message">消息内容。</param>
    void Report(string message);
}

/// <summary>
/// Dome 进度上报接口。
/// </summary>
public interface IDomeProgressReporter
{
    /// <summary>
    /// 上报进度消息。
    /// </summary>
    /// <param name="message">消息内容。</param>
    void Report(string message);
}

/// <summary>
/// 控制台进度上报器。
/// </summary>
public sealed class ConsoleTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter, IDomeProgressReporter
{
    /// <inheritdoc />
    public void Report(string message)
    {
        Console.WriteLine(message);
    }
}

/// <summary>
/// 空实现的 Dome 进度上报器。
/// </summary>
public sealed class NullDomeProgressReporter : IDomeProgressReporter
{
    /// <summary>
    /// 获取单例实例。
    /// </summary>
    public static NullDomeProgressReporter Instance { get; } = new();

    /// <inheritdoc />
    public void Report(string message)
    {
    }
}

/// <summary>
/// 默认 TR 运行时进程执行器。
/// </summary>
public sealed class TerrariaRuntimeProcessRunner : ITerrariaRuntimeProcessRunner
{
    /// <inheritdoc />
    public async Task<TerrariaRuntimeProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new List<string>();
        var standardError = new List<string>();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null)
            {
                return;
            }

            standardOutput.Add(eventArgs.Data);
            onStandardOutput?.Invoke(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null)
            {
                return;
            }

            standardError.Add(eventArgs.Data);
            onStandardError?.Invoke(eventArgs.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return new TerrariaRuntimeProcessResult(
            process.ExitCode,
            string.Join(Environment.NewLine, standardOutput),
            string.Join(Environment.NewLine, standardError));
    }
}

/// <summary>
/// TR 运行时构建执行器。
/// </summary>
public sealed class TerrariaRuntimeBuildExecutor(ITerrariaRuntimeProcessRunner processRunner) : ITerrariaRuntimeBuildExecutor
{
    /// <summary>
    /// 使用默认进程执行器初始化构建执行器。
    /// </summary>
    public TerrariaRuntimeBuildExecutor()
        : this(new TerrariaRuntimeProcessRunner())
    {
    }

    /// <inheritdoc />
    public async Task<TerrariaRuntimeBuildSummary> ExecuteAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        var arguments = $"build \"{layout.WorkspaceSolutionPath}\" --no-restore -m";
        progressReporter.Report($"[tr-run] 开始编译解决方案：dotnet {arguments}");
        var processResult = await processRunner.RunAsync(
            "dotnet",
            arguments,
            layout.WorkspacePath,
            progressReporter.Report,
            progressReporter.Report,
            cancellationToken);
        var summary = new TerrariaRuntimeBuildSummary(
            processResult.ExitCode == 0,
            processResult.ExitCode,
            $"dotnet {arguments}",
            layout.WorkspacePath,
            layout.DependencyEnvironmentPath,
            layout.WorkspaceSolutionPath,
            processResult.StandardOutput,
            processResult.StandardError);
        progressReporter.Report($"[tr-run] 解决方案编译结束，退出码：{summary.BuildExitCode}。");
        return summary;
    }
}
