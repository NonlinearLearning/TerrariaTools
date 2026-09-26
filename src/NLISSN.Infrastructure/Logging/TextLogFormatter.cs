using System.Globalization;
using System.Text;

namespace NLISSN.Logging;

public sealed class TextLogFormatter
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
            if (ShouldRenderField(view, textLogEvent, field.Name))
            {
                AppendOptionalField(builder, field.Name, FormatValue(field.Value));
            }
        }

        return builder.ToString();
    }

    private static bool ShouldRenderField(TextLogView view, TextLogEvent textLogEvent, string fieldName)
    {
        return view switch
        {
            TextLogView.Compact => IsCompactField(textLogEvent, fieldName),
            TextLogView.Normal => IsNormalField(textLogEvent, fieldName),
            _ => true
        };
    }

    private static bool IsCompactField(TextLogEvent textLogEvent, string fieldName)
    {
        return (textLogEvent.Category == TextLogCategory.Run && fieldName is "files" or "elapsedMs" or "edits" or "diags" or "status") ||
          (textLogEvent.Category == TextLogCategory.Diag && fieldName is "diags" or "warnings" or "errors");
    }

    private static bool IsNormalField(TextLogEvent textLogEvent, string fieldName)
    {
        return IsCompactField(textLogEvent, fieldName) ||
          fieldName is "op" or "inputKind" or "file" or "phase" or "dop" or "directoryDop" or "cpgDop" or "groupDop" or "helperDop" or "replayDop" or "maxConcurrentOperations" or "nodes" or "edges" or "rules" or "slowestRule" or "slowestMs" or "cacheHits" or "cacheMisses" or "heapBytes" or "privateBytes" or "committedBytes" or "fragmentedBytes" or "allocBytes" or "tpThreads" or "tpPending" or "tpCompleted" or "availableWorkers" or "maxWorkers" or "syntaxMs" or "dataFlowMs" or "freezeMs" ||
          // 风险 R4/R4b：默认 normal 视图也必须能看到内核**实际生效**的额度，
          // 否则请求值与生效值不一致时运维无法从日志发现。
          fieldName is "workerCount" or "ruleGroupEffective" or "helperEffective" or "directoryEffective" or "cpgEffective" or "replayEffective" or "defaultEffective" or "groupParallelism" or "directoryParallelism" or "helperParallelism" or "directoryWindowSemantics" or
          // 逐 worker 的使用率行（op=worker）：不登记就会在默认 normal 视图下整行消失。
          "workerIndex" or "items" or "busyMs" or "idleMs" or "lifetimeMs" or "utilization";
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
        if (!string.IsNullOrWhiteSpace(value))
        {
            AppendField(builder, name, value);
        }
    }

    private static void AppendValue(StringBuilder builder, string value)
    {
        if (!NeedsQuoting(value))
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                case '"':
                    builder.Append('\\');
                    builder.Append(character);
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static bool NeedsQuoting(string value)
    {
        return value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '"' or '\\' or '=');
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
