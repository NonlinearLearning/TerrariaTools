using NLISSN.Logging;
using System.Text;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TextLogFileSinkTests
{
    [Fact]
    public void Emit_FilteredEvents_AreNotFormattedOrWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nlissn-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.log");
        try
        {
            var filter = new TextLogFilter(
              TextLogLevel.Info,
              TextLogView.Normal,
              new[] { TextLogCategory.Run },
              new[] { TextLogEventType.Completed });
            int recordsWritten;
            using (var sink = TextLogFileSink.Create(path, new TextLogFormatter(), filter))
            {
                sink.Emit(CreateEvent(TextLogLevel.Debug, TextLogCategory.Run, TextLogEventType.Completed, "level"));
                sink.Emit(CreateEvent(TextLogLevel.Info, TextLogCategory.File, TextLogEventType.Completed, "category"));
                sink.Emit(CreateEvent(TextLogLevel.Info, TextLogCategory.Run, TextLogEventType.Started, "event"));
                sink.Emit(CreateEvent(TextLogLevel.Info, TextLogCategory.Run, TextLogEventType.Completed, "allowed"));
                sink.Flush();
                recordsWritten = sink.RecordsWritten;
            }

            Assert.Equal(1, recordsWritten);
            var lines = File.ReadAllLines(path);
            var line = Assert.Single(lines);
            Assert.Contains("allowed", line, StringComparison.Ordinal);
            Assert.DoesNotContain("level", line, StringComparison.Ordinal);
            Assert.DoesNotContain("category", line, StringComparison.Ordinal);
            Assert.DoesNotContain("event", line, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Flush_WriterFailure_FaultsPendingCompletion()
    {
        var filter = new TextLogFilter(
          TextLogLevel.Trace,
          TextLogView.Diagnostic,
          Enum.GetValues<TextLogCategory>(),
          Enum.GetValues<TextLogEventType>());
        var sink = new TextLogFileSink(
          "<failing-writer>",
          new FailingTextWriter(),
          new TextLogFormatter(),
          filter);

        sink.Emit(CreateEvent(TextLogLevel.Info, TextLogCategory.Run, TextLogEventType.Completed, "failure"));

        Assert.Throws<IOException>(() => sink.Flush());
        Assert.Throws<InvalidOperationException>(() => sink.Emit(
          CreateEvent(TextLogLevel.Info, TextLogCategory.Run, TextLogEventType.Completed, "after-failure")));
        try
        {
            sink.Dispose();
        }
        catch (IOException)
        {
            // Disposal reports the original drain failure after closing the writer.
        }
    }

    [Fact]
    public async Task Dispose_ConcurrentSyncAndAsyncCallsAreIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nlissn-log-dispose-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.log");
        try
        {
            var filter = new TextLogFilter(
              TextLogLevel.Trace,
              TextLogView.Diagnostic,
              Enum.GetValues<TextLogCategory>(),
              Enum.GetValues<TextLogEventType>());
            var sink = TextLogFileSink.Create(path, new TextLogFormatter(), filter);
            sink.Emit(CreateEvent(TextLogLevel.Info, TextLogCategory.Run, TextLogEventType.Completed, "dispose"));

            var disposalTasks = Enumerable.Range(0, 8)
              .Select(index => Task.Run(async () =>
              {
                  if (index % 2 == 0)
                  {
                      sink.Dispose();
                  }
                  else
                  {
                      await sink.DisposeAsync();
                  }
              }))
              .ToArray();

            await Task.WhenAll(disposalTasks);

            Assert.Single(File.ReadAllLines(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static TextLogEvent CreateEvent(
      TextLogLevel level,
      TextLogCategory category,
      TextLogEventType eventType,
      string message)
    {
        return new TextLogEvent(
          DateTimeOffset.UnixEpoch,
          level,
          category,
          eventType,
          message,
          "test-run");
    }

    private sealed class FailingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteAsync(string? value)
        {
            return Task.FromException(new IOException("expected writer failure"));
        }
    }
}
