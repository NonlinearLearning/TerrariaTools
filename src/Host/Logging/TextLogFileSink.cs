using System.Threading.Channels;
using System.Text;
using System.Diagnostics;

namespace RoslynPrototype.Application.Logging;

internal sealed class TextLogFileSink : ITextLogSink, IAsyncDisposable
{
    private const int BatchRecordCapacity = 64;
    private readonly Channel<TextLogWorkItem> _channel;
    private readonly StreamWriter _writer;
    private readonly TextLogFormatter _formatter;
    private readonly TextLogFilter _filter;
    private readonly Task _drainTask;
    private readonly object _failureLock = new();
    private Exception? _failure;
    private bool _disposed;
    private int _batchCount;
    private int _recordCount;
    private long _writeMilliseconds;

    public TextLogFileSink(string path, TextLogFormatter formatter, TextLogFilter filter)
    {
        Path = path;
        _formatter = formatter;
        _filter = filter;
        Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(global::System.IO.Path.GetFullPath(path)) ?? ".");
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
        _channel.Writer.WriteAsync(new TextLogWorkItem(textLogEvent, null, false))
          .AsTask()
          .GetAwaiter()
          .GetResult();
    }

    public void Flush()
    {
        ThrowIfFailed();
        ThrowIfDisposed();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.WriteAsync(new TextLogWorkItem(null, completion, false))
          .AsTask()
          .GetAwaiter()
          .GetResult();
        completion.Task.GetAwaiter().GetResult();
        ThrowIfFailed();
    }

    public async ValueTask DisposeAsync()
    {
        DisposeCore();
        await _drainTask.ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeCore();
        _drainTask.GetAwaiter().GetResult();
        _writer.Dispose();
    }

    private async Task DrainAsync()
    {
        var batch = new List<string>(BatchRecordCapacity);
        try
        {
            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var workItem))
                {
                    if (workItem.IsFlush)
                    {
                        await WriteBatchAsync(batch).ConfigureAwait(false);
                        await _writer.FlushAsync().ConfigureAwait(false);
                        workItem.FlushCompletion!.SetResult();
                        continue;
                    }

                    if (workItem.IsComplete)
                    {
                        await WriteBatchAsync(batch).ConfigureAwait(false);
                        await _writer.FlushAsync().ConfigureAwait(false);
                        workItem.FlushCompletion!.SetResult();
                        return;
                    }

                    batch.Add(_formatter.Format(workItem.TextLogEvent!, _filter.View));
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
            lock (_failureLock)
            {
                _failure ??= exception;
            }

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

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.WriteAsync(TextLogWorkItem.Complete())
          .AsTask()
          .GetAwaiter()
          .GetResult();
        _channel.Writer.TryComplete();
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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TextLogFileSink));
        }
    }

    private sealed record TextLogWorkItem(
      TextLogEvent? TextLogEvent,
      TaskCompletionSource? FlushCompletion,
      bool IsComplete)
    {
        public bool IsFlush => !IsComplete && FlushCompletion is not null;

        public static TextLogWorkItem Complete()
        {
            return new TextLogWorkItem(null, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), true);
        }
    }
}
