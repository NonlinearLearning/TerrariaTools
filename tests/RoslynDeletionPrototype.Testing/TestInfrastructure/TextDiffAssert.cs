using System.Text;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Testing.TestInfrastructure;

public static class TextDiffAssert
{
  public static void Contains(
    string expectedFragment,
    string? actualText,
    string? diffText = null,
    string? because = null)
  {
    if ((actualText ?? string.Empty).Contains(expectedFragment, StringComparison.Ordinal))
    {
      return;
    }

    throw new InvalidOperationException(BuildContainsFailureMessage(
      "contain",
      expectedFragment,
      actualText ?? "<null>",
      diffText,
      because));
  }

  public static void DoesNotContain(
    string unexpectedFragment,
    string? actualText,
    string? diffText = null,
    string? because = null)
  {
    if (!(actualText ?? string.Empty).Contains(unexpectedFragment, StringComparison.Ordinal))
    {
      return;
    }

    throw new InvalidOperationException(BuildContainsFailureMessage(
      "not contain",
      unexpectedFragment,
      actualText ?? "<null>",
      diffText,
      because));
  }

  public static void Equal(
    string expectedText,
    string? actualText,
    string? diffText = null,
    string? because = null)
  {
    if (string.Equals(expectedText, actualText, StringComparison.Ordinal))
    {
      return;
    }

    throw new InvalidOperationException(BuildEqualityFailureMessage(
      expectedText,
      actualText,
      diffText,
      because));
  }

  public static void Contains(
    string expectedFragment,
    string? actualText,
    DiffDocument diff,
    string? because = null)
  {
    Contains(expectedFragment, actualText, Render(diff), because);
  }

  public static void Contains(
    string expectedFragment,
    DiffDocument actualText,
    DiffDocument diff,
    string? because = null)
  {
    Contains(expectedFragment, Render(actualText), Render(diff), because);
  }

  public static void DoesNotContain(
    string unexpectedFragment,
    string? actualText,
    DiffDocument diff,
    string? because = null)
  {
    DoesNotContain(unexpectedFragment, actualText, Render(diff), because);
  }

  public static void DoesNotContain(
    string unexpectedFragment,
    DiffDocument actualText,
    DiffDocument diff,
    string? because = null)
  {
    DoesNotContain(unexpectedFragment, Render(actualText), Render(diff), because);
  }

  public static void Equal(
    string expectedText,
    string? actualText,
    DiffDocument diff,
    string? because = null)
  {
    Equal(expectedText, actualText, Render(diff), because);
  }

  public static void Equal(
    string expectedText,
    DiffDocument actualText,
    DiffDocument diff,
    string? because = null)
  {
    Equal(expectedText, Render(actualText), Render(diff), because);
  }

  private static string BuildContainsFailureMessage(
    string expectation,
    string fragment,
    string actualText,
    string? diffText,
    string? because)
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

  private static string BuildEqualityFailureMessage(
    string expectedText,
    string? actualText,
    string? diffText,
    string? because)
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

  private static string Render(DiffDocument diff)
  {
    return new TextDiffRenderer().RenderLegacy(diff);
  }
}
