using System.Text;
using Xunit.Sdk;

namespace RoslynPrototype.Tests;

internal static class TextDiffAssert
{
    public static void Contains(string expectedFragment, string actualText, string? diffText = null, string? because = null)
    {
        if (actualText.Contains(expectedFragment, StringComparison.Ordinal))
        {
            return;
        }

        throw new XunitException(BuildContainsFailureMessage(
          "contain",
          expectedFragment,
          actualText,
          diffText,
          because));
    }

    public static void DoesNotContain(string unexpectedFragment, string actualText, string? diffText = null, string? because = null)
    {
        if (!actualText.Contains(unexpectedFragment, StringComparison.Ordinal))
        {
            return;
        }

        throw new XunitException(BuildContainsFailureMessage(
          "not contain",
          unexpectedFragment,
          actualText,
          diffText,
          because));
    }

    public static void Equal(string expectedText, string? actualText, string? diffText = null, string? because = null)
    {
        if (string.Equals(expectedText, actualText, StringComparison.Ordinal))
        {
            return;
        }

        throw new XunitException(BuildEqualityFailureMessage(
          expectedText,
          actualText,
          diffText,
          because));
    }

    private static string BuildContainsFailureMessage(string expectation, string fragment, string actualText, string? diffText, string? because)
    {
        var builder = new StringBuilder();
        builder.Append("Expected text to ");
        builder.Append(expectation);
        builder.Append(" fragment:");
        if (!string.IsNullOrWhiteSpace(because))
        {
            builder.Append(' ');
            builder.Append(because);
        }

        builder.AppendLine();
        builder.AppendLine("--- expected fragment");
        builder.AppendLine(fragment);
        builder.AppendLine("+++ actual text");
        builder.AppendLine(actualText);
        AppendDiffBlock(builder, diffText);
        return builder.ToString();
    }

    private static string BuildEqualityFailureMessage(string expectedText, string? actualText, string? diffText, string? because)
    {
        var builder = new StringBuilder();
        builder.Append("Expected text to match exactly.");
        if (!string.IsNullOrWhiteSpace(because))
        {
            builder.Append(' ');
            builder.Append(because);
        }

        builder.AppendLine();
        builder.AppendLine("--- expected");
        builder.AppendLine(expectedText);
        builder.AppendLine("+++ actual");
        builder.AppendLine(actualText ?? "<null>");
        AppendDiffBlock(builder, diffText);
        return builder.ToString();
    }

    private static void AppendDiffBlock(StringBuilder builder, string? diffText)
    {
        builder.AppendLine("### diff");
        if (string.IsNullOrWhiteSpace(diffText))
        {
            builder.AppendLine("<no diff text provided>");
            return;
        }

        builder.AppendLine(diffText.TrimEnd());
    }
}
