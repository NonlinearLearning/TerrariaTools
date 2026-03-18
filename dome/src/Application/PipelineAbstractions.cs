namespace TerrariaTools.Dome.Application;

using System.Diagnostics;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

/// <summary>
/// 流水线上下文接口。
/// </summary>
public interface IPipelineContext
{
    /// <summary>
    /// 流水线终态信息。
    /// </summary>
    PipelineTerminalState? TerminalState { get; set; }
}

/// <summary>
/// 流水线阶段接口。
/// </summary>
/// <typeparam name="TContext">上下文类型。</typeparam>
public interface IPipelineStage<TContext>
    where TContext : IPipelineContext
{
    /// <summary>
    /// 执行阶段逻辑。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 流水线运行器接口。
/// </summary>
/// <typeparam name="TContext">上下文类型。</typeparam>
public interface IPipelineRunner<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 执行完整流水线。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task RunAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 流水线终态记录。
/// </summary>
/// <param name="Result">运行结果。</param>
/// <param name="StageName">阶段名称。</param>
public sealed record PipelineTerminalState(ApplicationAbstractions.RunResult Result, string? StageName = null);

/// <summary>
/// 流水线阶段跟踪记录。
/// </summary>
/// <param name="StageName">阶段名称。</param>
/// <param name="StageIndex">阶段索引。</param>
/// <param name="Elapsed">耗时。</param>
/// <param name="Failed">是否失败。</param>
public sealed record PipelineStageTrace(string StageName, int StageIndex, TimeSpan Elapsed, bool Failed);

/// <summary>
/// 可跟踪流水线上下文接口。
/// </summary>
internal interface ITrackedPipelineContext : IPipelineContext
{
    /// <summary>
    /// 当前阶段名称。
    /// </summary>
    string? CurrentStageName { get; }

    /// <summary>
    /// 当前阶段索引。
    /// </summary>
    int CurrentStageIndex { get; }

    /// <summary>
    /// 阶段跟踪列表。
    /// </summary>
    IReadOnlyList<PipelineStageTrace> StageTraces { get; }

    /// <summary>
    /// 开始阶段跟踪。
    /// </summary>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    void BeginStage(string stageName, int stageIndex);

    /// <summary>
    /// 完成阶段跟踪。
    /// </summary>
    /// <param name="elapsed">耗时。</param>
    /// <param name="failed">是否失败。</param>
    void CompleteStage(TimeSpan elapsed, bool failed);

    /// <summary>
    /// 确保上下文仍可变更。
    /// </summary>
    /// <param name="action">变更动作描述。</param>
    void EnsureCanMutate(string action);
}

/// <summary>
/// 流水线上下文基类。
/// </summary>
public abstract class PipelineContextBase : ITrackedPipelineContext
{
    private readonly List<PipelineStageTrace> _stageTraces = [];
    private PipelineTerminalState? _terminalState;

    /// <summary>
    /// 流水线终态信息。
    /// </summary>
    public PipelineTerminalState? TerminalState
    {
        get => _terminalState;
        set
        {
            if (value == null)
            {
                _terminalState = null;
                return;
            }

            if (_terminalState != null)
            {
                throw new InvalidOperationException($"Pipeline context is already terminal at stage '{_terminalState.StageName ?? "unknown"}'.");
            }

            _terminalState = value.StageName == null && CurrentStageName != null
                ? value with { StageName = CurrentStageName }
                : value;
        }
    }

    /// <summary>
    /// 当前阶段名称。
    /// </summary>
    public string? CurrentStageName { get; private set; }

    /// <summary>
    /// 当前阶段索引。
    /// </summary>
    public int CurrentStageIndex { get; private set; } = -1;

    /// <summary>
    /// 阶段跟踪列表。
    /// </summary>
    public IReadOnlyList<PipelineStageTrace> StageTraces => _stageTraces;

    /// <summary>
    /// 开始阶段跟踪。
    /// </summary>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    public void BeginStage(string stageName, int stageIndex)
    {
        EnsureCanMutate($"begin stage '{stageName}'");
        CurrentStageName = stageName;
        CurrentStageIndex = stageIndex;
    }

    /// <summary>
    /// 完成阶段跟踪。
    /// </summary>
    /// <param name="elapsed">耗时。</param>
    /// <param name="failed">是否失败。</param>
    public void CompleteStage(TimeSpan elapsed, bool failed)
    {
        if (CurrentStageName == null)
        {
            throw new InvalidOperationException("No current stage is active.");
        }

        _stageTraces.Add(new PipelineStageTrace(CurrentStageName, CurrentStageIndex, elapsed, failed));
    }

    /// <summary>
    /// 确保上下文仍可变更。
    /// </summary>
    /// <param name="action">变更动作描述。</param>
    public void EnsureCanMutate(string action)
    {
        if (_terminalState != null)
        {
            throw new InvalidOperationException($"Pipeline context is terminal and cannot {action}.");
        }
    }
}

/// <summary>
/// 流水线执行观察器接口。
/// </summary>
/// <typeparam name="TContext">上下文类型。</typeparam>
public interface IPipelineExecutionObserver<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 阶段开始回调。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    void OnStageStarted(TContext context, string stageName, int stageIndex);

    /// <summary>
    /// 阶段完成回调。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    /// <param name="elapsed">耗时。</param>
    void OnStageCompleted(TContext context, string stageName, int stageIndex, TimeSpan elapsed);

    /// <summary>
    /// 阶段失败回调。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    /// <param name="elapsed">耗时。</param>
    /// <param name="exception">异常对象。</param>
    void OnStageFailed(TContext context, string stageName, int stageIndex, TimeSpan elapsed, Exception exception);
}

/// <summary>
/// 基于进度上报器的流水线观察器。
/// </summary>
/// <typeparam name="TContext">上下文类型。</typeparam>
public sealed class ProgressReporterPipelineObserver<TContext>(Action<string> report) : IPipelineExecutionObserver<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 处理阶段开始事件。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    public void OnStageStarted(TContext context, string stageName, int stageIndex) => report($"[pipeline] stage-start {stageIndex}:{stageName}");

    /// <summary>
    /// 处理阶段完成事件。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    /// <param name="elapsed">耗时。</param>
    public void OnStageCompleted(TContext context, string stageName, int stageIndex, TimeSpan elapsed) =>
        report($"[pipeline] stage-complete {stageIndex}:{stageName} elapsed={FormatElapsed(elapsed)}");

    /// <summary>
    /// 处理阶段失败事件。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段索引。</param>
    /// <param name="elapsed">耗时。</param>
    /// <param name="exception">异常对象。</param>
    public void OnStageFailed(TContext context, string stageName, int stageIndex, TimeSpan elapsed, Exception exception) =>
        report($"[pipeline] stage-failed {stageIndex}:{stageName} elapsed={FormatElapsed(elapsed)} error={exception.Message}");

    /// <summary>
    /// 格式化阶段耗时文本。
    /// </summary>
    /// <param name="elapsed">耗时。</param>
    /// <returns>格式化后的耗时文本。</returns>
    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{elapsed.TotalMilliseconds:F0}ms";
}

/// <summary>
/// 流水线阶段基类。
/// </summary>
/// <typeparam name="TContext">上下文类型。</typeparam>
public abstract class PipelineStage<TContext> : IPipelineStage<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 阶段名称。
    /// </summary>
    public virtual string StageName => GetType().Name;

    /// <summary>
    /// 执行阶段逻辑。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public abstract Task ExecuteAsync(TContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 读取并校验必需状态。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    /// <param name="value">状态值。</param>
    /// <param name="message">异常消息。</param>
    /// <returns>状态值。</returns>
    protected static TState RequireState<TState>(TState? value, string message)
        where TState : class =>
        value ?? throw new InvalidOperationException(message);

    /// <summary>
    /// 设置成功终态。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="result">运行结果。</param>
    protected static void CompleteSuccess(TContext context, ApplicationAbstractions.RunResult result)
    {
        context.TerminalState = new PipelineTerminalState(result);
    }

    /// <summary>
    /// 设置失败终态。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="failureCode">失败码。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="message">失败消息。</param>
    protected static void SetFailure(TContext context, ModelPrimitives.FailureCode failureCode, string outputPath, string? message)
    {
        context.TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Failure(failureCode, outputPath, message));
    }
}

/// <summary>
/// 通用流水线运行器。
/// </summary>
/// <typeparam name="TContext">上下文类型。</typeparam>
public sealed class PipelineRunner<TContext> : IPipelineRunner<TContext>
    where TContext : class, IPipelineContext
{
    private readonly IReadOnlyList<IPipelineStage<TContext>> _stages;
    private readonly IPipelineExecutionObserver<TContext>? _observer;

    /// <summary>
    /// 初始化流水线运行器。
    /// </summary>
    /// <param name="stages">阶段集合。</param>
    public PipelineRunner(IReadOnlyList<IPipelineStage<TContext>> stages)
        : this(stages, null)
    {
    }

    /// <summary>
    /// 初始化流水线运行器并指定执行观察器。
    /// </summary>
    /// <param name="stages">阶段集合。</param>
    /// <param name="observer">执行观察器。</param>
    public PipelineRunner(IReadOnlyList<IPipelineStage<TContext>> stages, IPipelineExecutionObserver<TContext>? observer)
    {
        _stages = stages;
        _observer = observer;
    }

    /// <summary>
    /// 执行流水线阶段序列。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task RunAsync(TContext context, CancellationToken cancellationToken)
    {
        for (var index = 0; index < _stages.Count; index++)
        {
            if (context.TerminalState != null)
            {
                break;
            }

            var stage = _stages[index];
            var stageName = stage is PipelineStage<TContext> namedStage ? namedStage.StageName : stage.GetType().Name;
            var stopwatch = Stopwatch.StartNew();
            if (context is ITrackedPipelineContext trackedContext)
            {
                trackedContext.BeginStage(stageName, index);
            }

            _observer?.OnStageStarted(context, stageName, index);
            try
            {
                await stage.ExecuteAsync(context, cancellationToken);
                stopwatch.Stop();
                if (context is ITrackedPipelineContext trackedSuccessContext)
                {
                    trackedSuccessContext.CompleteStage(stopwatch.Elapsed, failed: false);
                }

                _observer?.OnStageCompleted(context, stageName, index, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (context is ITrackedPipelineContext trackedFailureContext)
                {
                    trackedFailureContext.CompleteStage(stopwatch.Elapsed, failed: true);
                }

                _observer?.OnStageFailed(context, stageName, index, stopwatch.Elapsed, ex);
                throw;
            }
        }
    }
}
