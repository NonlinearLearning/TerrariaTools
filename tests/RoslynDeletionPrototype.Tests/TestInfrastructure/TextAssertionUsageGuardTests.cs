using System.Text.RegularExpressions;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TextAssertionUsageGuardTests
{
    private static readonly Regex ForbiddenAssertionPattern = new(
      @"Assert\.(Contains|DoesNotContain|Equal)\((?<first>[\s\S]*?),(?<second>[\s\S]*?)\)",
      RegexOptions.Compiled);

    [Fact]
    public void TestSources_WhenAssertingOnRewrittenSourceOrDiffText_MustUseTextDiffAssert()
    {
        var testRoot = Path.Combine(
          AppContext.BaseDirectory,
          "..",
          "..",
          "..");
        var files = Directory.GetFiles(testRoot, "*.cs", SearchOption.AllDirectories)
          .Where(path => !path.EndsWith("TextAssertionUsageGuardTests.cs", StringComparison.Ordinal))
          .Where(path => !path.EndsWith("TextDiffAssert.cs", StringComparison.Ordinal))
          .OrderBy(path => path, StringComparer.Ordinal)
          .ToList();

        var violations = new List<string>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in ForbiddenAssertionPattern.Matches(content))
            {
                var invocation = match.Value;
                if (!invocation.Contains("RewrittenSource", StringComparison.Ordinal) &&
                    !invocation.Contains("DiffText", StringComparison.Ordinal) &&
                    !invocation.Contains("diffText", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add($"{Path.GetFileName(file)} => {CollapseWhitespace(invocation)}");
            }
        }

        Assert.True(
          violations.Count == 0,
          "Source-text assertions must use TextDiffAssert. Violations:" + Environment.NewLine +
          string.Join(Environment.NewLine, violations));
    }

    private static string CollapseWhitespace(string text)
    {
        return string.Join(
          " ",
          text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
