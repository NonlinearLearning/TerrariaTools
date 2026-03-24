namespace TerrariaTools.Dome.Application.Pipeline;

using System.Diagnostics;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 定义流水线上下文需要暴露的最小终态能力。
/// </summary>
public interface IPipelineContext
{
    /// <summary>
    /// 获取或设置流水线终态。
    /// </summary>
    PipelineTerminalState? TerminalState { get; set; }
}

/// <summary>
/// 定义单个流水线阶段的执行契约。
/// </summary>
/// <typeparam name="TContext">阶段使用的上下文类型。</typeparam>
public interface IPipelineStage<TContext>
    where TContext : IPipelineContext
{
    /// <summary>
    /// 执行当前阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 定义流水线执行器契约。
/// </summary>
/// <typeparam name="TContext">流水线上下文类型。</typeparam>
public interface IPipelineRunner<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 按顺序执行整条流水线。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task RunAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 表示流水线进入终态时的结果。
/// </summary>
/// <param name="Result">运行结果。</param>
/// <param name="StageName">写入终态的阶段名称。</param>
public sealed record PipelineTerminalState(ModelExecution.RunResult Result, string? StageName = null);

/// <summary>
/// 记录单个阶段的执行轨迹。
/// </summary>
/// <param name="StageName">阶段名称。</param>
/// <param name="StageIndex">阶段序号。</param>
/// <param name="Elapsed">阶段耗时。</param>
/// <param name="Failed">指示阶段是否失败。</param>
public sealed record PipelineStageTrace(string StageName, int StageIndex, TimeSpan Elapsed, bool Failed);

/// <summary>
/// 为可跟踪的流水线上下文补充阶段轨迹能力。
/// </summary>
internal interface ITrackedPipelineContext : IPipelineContext
{
    /// <summary>
    /// 获取当前正在执行的阶段名称。
    /// </summary>
    string? CurrentStageName { get; }

    /// <summary>
    /// 获取当前正在执行的阶段序号。
    /// </summary>
    int CurrentStageIndex { get; }

    /// <summary>
    /// 获取已完成阶段的轨迹集合。
    /// </summary>
    IReadOnlyList<PipelineStageTrace> StageTraces { get; }

    /// <summary>
    /// 标记某个阶段开始执行。
    /// </summary>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    void BeginStage(string stageName, int stageIndex);

    /// <summary>
    /// 记录当前阶段完成。
    /// </summary>
    /// <param name="elapsed">阶段耗时。</param>
    /// <param name="failed">指示阶段是否失败。</param>
    void CompleteStage(TimeSpan elapsed, bool failed);

    /// <summary>
    /// 确保上下文仍允许写入新状态。
    /// </summary>
    /// <param name="action">当前准备执行的写入动作。</param>
    void EnsureCanMutate(string action);
}

/// <summary>
/// 提供带阶段轨迹记录能力的流水线上下文基类。
/// </summary>
public abstract class PipelineContextBase : ITrackedPipelineContext
{
    private readonly List<PipelineStageTrace> _stageTraces = [];
    private PipelineTerminalState? _terminalState;

    /// <summary>
    /// 获取或设置流水线终态。
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
    /// 获取当前正在执行的阶段名称。
    /// </summary>
    public string? CurrentStageName { get; private set; }

    /// <summary>
    /// 获取当前正在执行的阶段序号。
    /// </summary>
    public int CurrentStageIndex { get; private set; } = -1;

    /// <summary>
    /// 获取已记录的阶段轨迹。
    /// </summary>
    public IReadOnlyList<PipelineStageTrace> StageTraces => _stageTraces;

    /// <summary>
    /// 标记某个阶段开始执行。
    /// </summary>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    public void BeginStage(string stageName, int stageIndex)
    {
        EnsureCanMutate($"begin stage '{stageName}'");
        CurrentStageName = stageName;
        CurrentStageIndex = stageIndex;
    }

    /// <summary>
    /// 记录当前阶段完成。
    /// </summary>
    /// <param name="elapsed">阶段耗时。</param>
    /// <param name="failed">指示阶段是否失败。</param>
    public void CompleteStage(TimeSpan elapsed, bool failed)
    {
        if (CurrentStageName == null)
        {
            throw new InvalidOperationException("No current stage is active.");
        }

        _stageTraces.Add(new PipelineStageTrace(CurrentStageName, CurrentStageIndex, elapsed, failed));
    }

    /// <summary>
    /// 确保上下文仍允许继续写入状态。
    /// </summary>
    /// <param name="action">当前准备执行的写入动作。</param>
    public void EnsureCanMutate(string action)
    {
        if (_terminalState != null)
        {
            throw new InvalidOperationException($"Pipeline context is terminal and cannot {action}.");
        }
    }
}

/// <summary>
/// 定义用于观察流水线执行过程的回调。
/// </summary>
/// <typeparam name="TContext">流水线上下文类型。</typeparam>
public interface IPipelineExecutionObserver<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 在阶段开始时触发。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    void OnStageStarted(TContext context, string stageName, int stageIndex);

    /// <summary>
    /// 在阶段成功完成时触发。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    /// <param name="elapsed">阶段耗时。</param>
    void OnStageCompleted(TContext context, string stageName, int stageIndex, TimeSpan elapsed);

    /// <summary>
    /// 在阶段执行失败时触发。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    /// <param name="elapsed">阶段耗时。</param>
    /// <param name="exception">触发失败的异常。</param>
    void OnStageFailed(TContext context, string stageName, int stageIndex, TimeSpan elapsed, Exception exception);
}

/// <summary>
/// 将阶段执行事件格式化为文本进度消息。
/// </summary>
/// <typeparam name="TContext">流水线上下文类型。</typeparam>
public sealed class ProgressReporterPipelineObserver<TContext>(Action<string> report) : IPipelineExecutionObserver<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 记录阶段开始消息。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    public void OnStageStarted(TContext context, string stageName, int stageIndex) => report($"[pipeline] stage-start {stageIndex}:{stageName}");

    /// <summary>
    /// 记录阶段完成消息。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    /// <param name="elapsed">阶段耗时。</param>
    public void OnStageCompleted(TContext context, string stageName, int stageIndex, TimeSpan elapsed) =>
        report($"[pipeline] stage-complete {stageIndex}:{stageName} elapsed={FormatElapsed(elapsed)}");

    /// <summary>
    /// 记录阶段失败消息。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="stageName">阶段名称。</param>
    /// <param name="stageIndex">阶段序号。</param>
    /// <param name="elapsed">阶段耗时。</param>
    /// <param name="exception">触发失败的异常。</param>
    public void OnStageFailed(TContext context, string stageName, int stageIndex, TimeSpan elapsed, Exception exception) =>
        report($"[pipeline] stage-failed {stageIndex}:{stageName} elapsed={FormatElapsed(elapsed)} error={exception.Message}");

    /// <summary>
    /// 将耗时格式化为适合日志输出的文本。
    /// </summary>
    /// <param name="elapsed">阶段耗时。</param>
    /// <returns>格式化后的耗时文本。</returns>
    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{elapsed.TotalMilliseconds:F0}ms";
}

/// <summary>
/// 提供带终态辅助方法的阶段基类。
/// </summary>
/// <typeparam name="TContext">流水线上下文类型。</typeparam>
public abstract class PipelineStage<TContext> : IPipelineStage<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// 获取阶段名称。
    /// </summary>
    public virtual string StageName => GetType().Name;

    /// <summary>
    /// 执行当前阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public abstract Task ExecuteAsync(TContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 要求某个引用状态已存在。
    /// </summary>
    /// <typeparam name="TState">状态值类型。</typeparam>
    /// <param name="value">待检查的状态值。</param>
    /// <param name="message">状态不存在时抛出的消息。</param>
    /// <returns>已验证存在的状态值。</returns>
    protected static TState RequireState<TState>(TState? value, string message)
        where TState : class =>
        value ?? throw new InvalidOperationException(message);

    /// <summary>
    /// 将上下文标记为成功终态。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="result">成功结果。</param>
    protected static void CompleteSuccess(TContext context, ModelExecution.RunResult result)
    {
        context.TerminalState = new PipelineTerminalState(result);
    }

    /// <summary>
    /// 将上下文标记为失败终态。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="failureCode">失败代码。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="message">失败消息。</param>
    protected static void SetFailure(TContext context, ModelPrimitives.FailureCode failureCode, string outputPath, string? message)
    {
        context.TerminalState = new PipelineTerminalState(ModelExecution.RunResult.Failure(failureCode, outputPath, message));
    }
}

/// <summary>
/// 负责按顺序执行流水线阶段。
/// </summary>
/// <typeparam name="TContext">流水线上下文类型。</typeparam>
public sealed class PipelineRunner<TContext> : IPipelineRunner<TContext>
    where TContext : class, IPipelineContext
{
    private readonly IReadOnlyList<IPipelineStage<TContext>> _stages;
    private readonly IPipelineExecutionObserver<TContext>? _observer;

    /// <summary>
    /// 初始化流水线执行器。
    /// </summary>
    /// <param name="stages">按执行顺序排列的阶段集合。</param>
    public PipelineRunner(IReadOnlyList<IPipelineStage<TContext>> stages)
        : this(stages, null)
    {
    }

    /// <summary>
    /// 初始化流水线执行器。
    /// </summary>
    /// <param name="stages">按执行顺序排列的阶段集合。</param>
    /// <param name="observer">可选的执行观察器。</param>
    public PipelineRunner(IReadOnlyList<IPipelineStage<TContext>> stages, IPipelineExecutionObserver<TContext>? observer)
    {
        _stages = stages;
        _observer = observer;
    }

    /// <summary>
    /// 依次执行所有阶段，直到进入终态或阶段全部完成。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
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

/// <summary>
/// Runs a flow assembled on demand for the current pipeline context.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
public sealed class AssembledFlowRunner<TContext>(
    Func<TContext, IReadOnlyList<IPipelineStage<TContext>>> stageFactory,
    IPipelineExecutionObserver<TContext>? observer = null) : IPipelineRunner<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// Builds the active stage list for the provided context and executes it.
    /// </summary>
    /// <param name="context">The pipeline context for this run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the assembled flow finishes.</returns>
    public Task RunAsync(TContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runner = new PipelineRunner<TContext>(stageFactory(context), observer);
        return runner.RunAsync(context, cancellationToken);
    }
}

