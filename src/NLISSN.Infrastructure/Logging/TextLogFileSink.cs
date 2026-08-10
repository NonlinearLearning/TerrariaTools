using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;

namespace NLISSN.Logging;

public sealed class TextLogFileSink : ITextLogSink, IAsyncDisposable
{
    private const int BatchRecordCapacity = 64;
    private readonly Channel<TextLogWorkItem> _channel;
    private readonly TextWriter _writer;
    private readonly TextLogFormatter _formatter;
    private readonly TextLogFilter _filter;
    private readonly Task _drainTask;
    private readonly object _failureLock = new();
    private readonly object _shutdownLock = new();
    private readonly object _writerDisposeLock = new();
    private Exception? _failure;
    private Task? _shutdownTask;
    private Task? _writerDisposeTask;
    private int _disposeRequested;
    private int _batchCount;
    private int _recordCount;
    private long _writeMilliseconds;

    public TextLogFileSink(string path, TextLogFormatter formatter, TextLogFilter filter)
      : this(path, OpenWriter(path), formatter, filter)
    {
    }

    internal TextLogFileSink(string path, TextWriter writer, TextLogFormatter formatter, TextLogFilter filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(filter);
        Path = path;
        _writer = writer;
        _formatter = formatter;
        _filter = filter;
        _channel = Channel.CreateBounded<TextLogWorkItem>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _drainTask = Task.Run(DrainAsync);
    }

    public string Path { get; }

    public int RecordsWritten => Volatile.Read(ref _recordCount);

    public int BatchesWritten => Volatile.Read(ref _batchCount);

    public long WriteMilliseconds => Interlocked.Read(ref _writeMilliseconds);

    public static TextLogFileSink Create(string path, TextLogFormatter formatter, TextLogFilter filter)
    {
        return new TextLogFileSink(path, formatter, filter);
    }

    public void Emit(TextLogEvent textLogEvent)
    {
        ThrowIfFailed();
        ThrowIfDisposed();
        _channel.Writer.WriteAsync(new TextLogWorkItem(textLogEvent, null, false)).AsTask().GetAwaiter().GetResult();
    }

    public void Flush()
    {
        ThrowIfFailed();
        ThrowIfDisposed();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.WriteAsync(new TextLogWorkItem(null, completion, false)).AsTask().GetAwaiter().GetResult();
        completion.Task.GetAwaiter().GetResult();
        ThrowIfFailed();
    }

    public async ValueTask DisposeAsync()
    {
        await RequestShutdownAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        RequestShutdownAsync().GetAwaiter().GetResult();
    }

    private async Task DrainAsync()
    {
        var batch = new List<string>(BatchRecordCapacity);
        TextLogWorkItem? currentWorkItem = null;
        try
        {
            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var workItem))
                {
                    currentWorkItem = workItem;
                    if (workItem.IsFlush)
                    {
                        await WriteBatchAsync(batch).ConfigureAwait(false);
                        await _writer.FlushAsync().ConfigureAwait(false);
                        workItem.FlushCompletion!.TrySetResult();
                        currentWorkItem = null;
                        continue;
                    }

                    if (workItem.IsComplete)
                    {
                        await WriteBatchAsync(batch).ConfigureAwait(false);
                        await _writer.FlushAsync().ConfigureAwait(false);
                        workItem.FlushCompletion!.TrySetResult();
                        currentWorkItem = null;
                        return;
                    }

                    if (_filter.Allows(workItem.TextLogEvent!))
                    {
                        batch.Add(_formatter.Format(workItem.TextLogEvent!, _filter.View));
                    }

                    currentWorkItem = null;
                    if (batch.Count == BatchRecordCapacity)
                    {
                        await WriteBatchAsync(batch).ConfigureAwait(false);
                    }
                }
            }

            await WriteBatchAsync(batch).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetFailure(exception);
            currentWorkItem?.FlushCompletion?.TrySetException(exception);
            while (_channel.Reader.TryRead(out var pendingWorkItem))
            {
                pendingWorkItem.FlushCompletion?.TrySetException(exception);
            }

            _channel.Writer.TryComplete(exception);
            throw;
        }
    }

    private async Task WriteBatchAsync(List<string> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var line in batch)
        {
            text.AppendLine(line);
        }

        var stopwatch = Stopwatch.StartNew();
        await _writer.WriteAsync(text.ToString()).ConfigureAwait(false);
        stopwatch.Stop();
        Interlocked.Add(ref _writeMilliseconds, stopwatch.ElapsedMilliseconds);
        Interlocked.Add(ref _recordCount, batch.Count);
        Interlocked.Increment(ref _batchCount);
        batch.Clear();
    }

    private static StreamWriter OpenWriter(string path)
    {
        Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(global::System.IO.Path.GetFullPath(path)) ?? ".");
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private Task RequestShutdownAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
        {
            try
            {
                _channel.Writer.WriteAsync(TextLogWorkItem.Complete()).AsTask().GetAwaiter().GetResult();
            }
            catch (ChannelClosedException)
            {
                // The drain has already failed and completed the channel.
            }

            _channel.Writer.TryComplete();
        }

        lock (_shutdownLock)
        {
            return _shutdownTask ??= ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
    {
        Exception? drainException = null;
        try
        {
            await _drainTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            drainException = exception;
        }

        try
        {
            await GetWriterDisposeTask().ConfigureAwait(false);
        }
        catch when (drainException is not null)
        {
            // Preserve the first write failure while still closing the writer.
        }

        if (drainException is not null)
        {
            ExceptionDispatchInfo.Capture(drainException).Throw();
        }
    }

    private Task GetWriterDisposeTask()
    {
        lock (_writerDisposeLock)
        {
            return _writerDisposeTask ??= _writer.DisposeAsync().AsTask();
        }
    }

    private void SetFailure(Exception exception)
    {
        lock (_failureLock)
        {
            _failure ??= exception;
        }
    }

    private void ThrowIfFailed()
    {
        lock (_failureLock)
        {
            if (_failure is not null)
            {
                throw new InvalidOperationException("The text log sink has failed.", _failure);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            throw new ObjectDisposedException(nameof(TextLogFileSink));
        }
    }

    private sealed record TextLogWorkItem(TextLogEvent? TextLogEvent, TaskCompletionSource? FlushCompletion, bool IsComplete)
    {
        public bool IsFlush => !IsComplete && FlushCompletion is not null;

        public static TextLogWorkItem Complete()
        {
            return new TextLogWorkItem(null, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), true);
        }
    }
}
