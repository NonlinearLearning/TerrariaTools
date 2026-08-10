using NLISSN.Logging;
using Xunit;

namespace NLISSN.Tests.Logging;

public sealed class TextLogFilterTests
{
    [Fact]
    public void CreateRuntimeFilter_ProfileDefaultsCanBeOverridden()
    {
        var profileFilter = TextLogFilter.CreateRuntimeFilter(
          "minimal",
          string.Empty,
          Array.Empty<string>(),
          Array.Empty<string>(),
          string.Empty);
        var explicitFilter = TextLogFilter.CreateRuntimeFilter(
          "minimal",
          "debug",
          Array.Empty<string>(),
          Array.Empty<string>(),
          "diagnostic");

        Assert.Equal(TextLogLevel.Info, profileFilter.MinimumLevel);
        Assert.Equal(TextLogView.Compact, profileFilter.View);
        Assert.Equal(TextLogLevel.Debug, explicitFilter.MinimumLevel);
        Assert.Equal(TextLogView.Diagnostic, explicitFilter.View);
    }

    [Fact]
    public void Format_EscapesControlCharactersAndKeepsOneLine()
    {
        var text = new TextLogFormatter().Format(
          CreateEvent("first\nsecond\r\ttab\\slash\"quote"),
          TextLogView.Diagnostic);

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain('\r', text);
        Assert.Contains("\\n", text, StringComparison.Ordinal);
        Assert.Contains("\\r", text, StringComparison.Ordinal);
        Assert.Contains("\\t", text, StringComparison.Ordinal);
        Assert.Contains("\\\\", text, StringComparison.Ordinal);
        Assert.Contains("\\\"", text, StringComparison.Ordinal);
    }

    private static TextLogEvent CreateEvent(string message)
    {
        return new TextLogEvent(
          DateTimeOffset.UnixEpoch,
          TextLogLevel.Info,
          TextLogCategory.Run,
          TextLogEventType.Completed,
          message,
          "test-run");
    }
}
