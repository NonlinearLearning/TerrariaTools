using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Workspace.Generator;

[Generator]
public sealed class WorkspaceSourceGenerator : IIncrementalGenerator
{
  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var additionalValue = context.AdditionalTextsProvider
      .Where(static text => string.Equals(
        Path.GetFileName(text.Path),
        "generator-input.txt",
        StringComparison.OrdinalIgnoreCase))
      .Select(static (text, cancellationToken) =>
        text.GetText(cancellationToken)?.ToString().Trim() ?? "missing")
      .Collect()
      .Select(static (values, _) => values.Length == 0 ? "missing" : values[0]);
    var prefix = context.AnalyzerConfigOptionsProvider
      .Select(static (provider, _) => provider.GlobalOptions.TryGetValue(
        "build_property.WorkspaceGeneratorPrefix",
        out var value)
        ? value
        : "default");

    context.RegisterSourceOutput(
      additionalValue.Combine(prefix),
      static (productionContext, values) =>
      {
        var source = "namespace Workspace.App;\n" +
          "public static class GeneratedByDriver\n" +
          "{\n" +
          "  public const string Value = \"source-generator\";\n" +
          $"  public const string AdditionalValue = \"{Escape(values.Left)}\";\n" +
          $"  public const string Prefix = \"{Escape(values.Right)}\";\n" +
          "}\n";
        productionContext.AddSource(
          "GeneratedByDriver.g.cs",
          SourceText.From(source, Encoding.UTF8));
      });
  }

  private static string Escape(string value)
  {
    return value.Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace("\"", "\\\"", StringComparison.Ordinal)
      .Replace("\r", "\\r", StringComparison.Ordinal)
      .Replace("\n", "\\n", StringComparison.Ordinal);
  }
}
