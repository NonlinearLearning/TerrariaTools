using System.Globalization;
using System.Text;

namespace RoslynPrototype.Application.Logging;

internal sealed class TextLogFormatter
{
    public string Format(TextLogEvent textLogEvent, TextLogView view)
    {
        var builder = new StringBuilder(256);
        AppendField(builder, "ts", textLogEvent.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendField(builder, "lvl", textLogEvent.Level.ToString().ToUpperInvariant());
        AppendField(builder, "cat", textLogEvent.Category.ToString().ToLowerInvariant());
        AppendField(builder, "evt", textLogEvent.EventType.ToString().ToLowerInvariant());
        AppendField(builder, "msg", textLogEvent.Message);
        AppendField(builder, "run", textLogEvent.RunId);
        AppendOptionalField(builder, "op", textLogEvent.Operation);
        AppendOptionalField(builder, "inputKind", textLogEvent.InputKind);
        AppendOptionalField(builder, "inputPath", textLogEvent.InputPath);
        AppendOptionalField(builder, "src", textLogEvent.Source);
        AppendOptionalField(builder, "file", textLogEvent.FilePath);
        AppendOptionalField(builder, "phase", textLogEvent.Phase);
        AppendOptionalField(builder, "dop", textLogEvent.Dop?.ToString(CultureInfo.InvariantCulture));

        if (textLogEvent.Fields is null)
        {
            return builder.ToString();
        }

        foreach (var field in textLogEvent.Fields)
        {
            if (!ShouldRenderField(view, textLogEvent, field.Name))
            {
                continue;
            }

            AppendOptionalField(builder, field.Name, FormatValue(field.Value));
        }

        return builder.ToString();
    }

    private static bool ShouldRenderField(
      TextLogView view,
      TextLogEvent textLogEvent,
      string fieldName)
    {
        return view switch
        {
            TextLogView.Compact => IsCompactField(textLogEvent, fieldName),
            TextLogView.Normal => IsNormalField(textLogEvent, fieldName),
            TextLogView.Diagnostic => true,
            TextLogView.Benchmark => true,
            _ => true
        };
    }

    private static bool IsCompactField(TextLogEvent textLogEvent, string fieldName)
    {
        return (textLogEvent.Category == TextLogCategory.Run &&
          fieldName is "files" or "elapsedMs" or "edits" or "diags" or "status") ||
          (textLogEvent.Category == TextLogCategory.Diag &&
            fieldName is "diags" or "warnings" or "errors");
    }

    private static bool IsNormalField(TextLogEvent textLogEvent, string fieldName)
    {
        return IsCompactField(textLogEvent, fieldName) ||
          fieldName is "op" or "inputKind" or "file" or "phase" or "dop" or "nodes" or "edges" or "rules" or "slowestRule" or "slowestMs" or "cacheHits" or "cacheMisses" or "heapBytes" or "privateBytes" or "committedBytes" or "fragmentedBytes" or "allocBytes" or "tpThreads" or "tpPending" or "tpCompleted" or "availableWorkers" or "maxWorkers" or "syntaxMs" or "dataFlowMs" or "freezeMs";
    }

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(name);
        builder.Append('=');
        AppendValue(builder, value);
    }

    private static void AppendOptionalField(StringBuilder builder, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AppendField(builder, name, value);
    }

    private static void AppendValue(StringBuilder builder, string value)
    {
        if (NeedsQuoting(value))
        {
            builder.Append('"');
            foreach (var ch in value)
            {
                if (ch == '\\' || ch == '"')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            builder.Append('"');
            return;
        }

        builder.Append(value);
    }

    private static bool NeedsQuoting(string value)
    {
        return value.Any(character => char.IsWhiteSpace(character) ||
          character is '"' or '\\' or '=');
    }

    private static string? FormatValue(object? value)
    {
        return value switch
        {
            null => null,
            string stringValue => stringValue,
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
