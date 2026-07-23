using System.Collections.Generic;

namespace RoslynPrototype.Application.Logging;

internal sealed class TextLogFilter
{
    private readonly HashSet<TextLogCategory> _categories;
    private readonly HashSet<TextLogEventType> _eventTypes;

    public TextLogFilter(
      TextLogLevel minimumLevel,
      TextLogView view,
      IReadOnlyCollection<TextLogCategory> categories,
      IReadOnlyCollection<TextLogEventType> eventTypes)
    {
        MinimumLevel = minimumLevel;
        View = view;
        _categories = new HashSet<TextLogCategory>(categories);
        _eventTypes = new HashSet<TextLogEventType>(eventTypes);
    }

    public TextLogLevel MinimumLevel { get; }

    public TextLogView View { get; }

    public bool Allows(TextLogEvent textLogEvent)
    {
        return textLogEvent.Level <= MinimumLevel &&
          _categories.Contains(textLogEvent.Category) &&
          _eventTypes.Contains(textLogEvent.EventType);
    }

    public static TextLogFilter CreateRuntimeFilter(
      IReadOnlyDictionary<string, string> options)
    {
        return CreateFromOptions(
          options,
          defaultLevel: TextLogLevel.Info,
          defaultView: TextLogView.Normal,
          defaultCategories: new[] { TextLogCategory.Run, TextLogCategory.Cpg, TextLogCategory.Mark, TextLogCategory.Diag });
    }

    public static TextLogFilter CreateAnalysisFilter(
      IReadOnlyDictionary<string, string> options)
    {
        return CreateFromOptions(
          options,
          defaultLevel: TextLogLevel.Info,
          defaultView: TextLogView.Normal,
          defaultCategories: new[] { TextLogCategory.File, TextLogCategory.Phase, TextLogCategory.Memory, TextLogCategory.Io });
    }

    private static TextLogFilter CreateFromOptions(
      IReadOnlyDictionary<string, string> options,
      TextLogLevel defaultLevel,
      TextLogView defaultView,
      IReadOnlyCollection<TextLogCategory> defaultCategories)
    {
        var profile = TryParseProfile(options, out var profileSettings) ? profileSettings : null;
        var level = TryParseLevel(options, out var parsedLevel)
          ? parsedLevel
          : profile?.Level ?? defaultLevel;
        var view = TryParseView(options, out var parsedView)
          ? parsedView
          : profile?.View ?? defaultView;
        var categories = TryParseCategories(options, out var parsedCategories)
          ? parsedCategories
          : profile?.Categories ?? defaultCategories;
        var eventTypes = TryParseEventTypes(options, out var parsedEventTypes)
          ? parsedEventTypes
          : profile?.EventTypes ?? GetDefaultEventTypes(view);

        return new TextLogFilter(level, view, categories, eventTypes);
    }

    private static bool TryParseProfile(
      IReadOnlyDictionary<string, string> options,
      out TextLogProfileSettings? profileSettings)
    {
        profileSettings = null;
        if (!options.TryGetValue("log-profile", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        profileSettings = rawValue.Trim().ToLowerInvariant() switch
        {
            "minimal" => new TextLogProfileSettings(
                TextLogLevel.Info,
                TextLogView.Compact,
                new[] { TextLogCategory.Run, TextLogCategory.Diag },
                new[]
                {
                    TextLogEventType.Started,
                    TextLogEventType.Completed,
                    TextLogEventType.Failed,
                    TextLogEventType.Summary,
                    TextLogEventType.Warning,
                    TextLogEventType.Error
                }),
            "normal" => new TextLogProfileSettings(
                TextLogLevel.Info,
                TextLogView.Normal,
                new[] { TextLogCategory.Run, TextLogCategory.File, TextLogCategory.Diag, TextLogCategory.Cpg, TextLogCategory.Mark },
                new[]
                {
                    TextLogEventType.Started,
                    TextLogEventType.Completed,
                    TextLogEventType.Failed,
                    TextLogEventType.Summary,
                    TextLogEventType.Warning,
                    TextLogEventType.Error
                }),
            "diagnostic" => new TextLogProfileSettings(
                TextLogLevel.Debug,
                TextLogView.Diagnostic,
                Enum.GetValues<TextLogCategory>(),
                Enum.GetValues<TextLogEventType>()),
            "benchmark" => new TextLogProfileSettings(
                TextLogLevel.Debug,
                TextLogView.Benchmark,
                new[] { TextLogCategory.Run, TextLogCategory.File, TextLogCategory.Diag, TextLogCategory.Phase, TextLogCategory.Memory, TextLogCategory.Cpg, TextLogCategory.Mark, TextLogCategory.Io, TextLogCategory.Diff },
                new[]
                {
                    TextLogEventType.Started,
                    TextLogEventType.Sampled,
                    TextLogEventType.Completed,
                    TextLogEventType.Failed,
                    TextLogEventType.Summary,
                    TextLogEventType.Snapshot,
                    TextLogEventType.Pending,
                    TextLogEventType.Written
                }),
            _ => null
        };

        return profileSettings is not null;
    }

    private static IReadOnlyCollection<TextLogEventType> GetDefaultEventTypes(TextLogView view)
    {
        return view switch
        {
            TextLogView.Compact => new[]
            {
                TextLogEventType.Started,
                TextLogEventType.Completed,
                TextLogEventType.Failed,
                TextLogEventType.Summary,
                TextLogEventType.Warning,
                TextLogEventType.Error
            },
            TextLogView.Normal => new[]
            {
                TextLogEventType.Started,
                TextLogEventType.Completed,
                TextLogEventType.Failed,
                TextLogEventType.Summary,
                TextLogEventType.Warning,
                TextLogEventType.Error
            },
            TextLogView.Diagnostic => Enum.GetValues<TextLogEventType>(),
            TextLogView.Benchmark => Enum.GetValues<TextLogEventType>(),
            _ => Enum.GetValues<TextLogEventType>()
        };
    }

    private static bool TryParseLevel(
      IReadOnlyDictionary<string, string> options,
      out TextLogLevel level)
    {
        level = TextLogLevel.Info;
        if (!options.TryGetValue("log-level", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (Enum.TryParse(rawValue, ignoreCase: true, out level))
        {
            return true;
        }

        throw new ArgumentException($"Invalid log level '{rawValue}'.", nameof(options));
    }

    private static bool TryParseView(
      IReadOnlyDictionary<string, string> options,
      out TextLogView view)
    {
        view = TextLogView.Normal;
        if (!options.TryGetValue("log-view", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (Enum.TryParse(rawValue, ignoreCase: true, out view))
        {
            return true;
        }

        throw new ArgumentException($"Invalid log view '{rawValue}'.", nameof(options));
    }

    private static bool TryParseCategories(
      IReadOnlyDictionary<string, string> options,
      out IReadOnlyCollection<TextLogCategory> categories)
    {
        categories = Array.Empty<TextLogCategory>();
        if (!options.TryGetValue("log-categories", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var parsedCategories = new List<TextLogCategory>();
        foreach (var candidate in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse(candidate, ignoreCase: true, out TextLogCategory category))
            {
                throw new ArgumentException($"Invalid log category '{candidate}'.", nameof(options));
            }

            if (!parsedCategories.Contains(category))
            {
                parsedCategories.Add(category);
            }
        }

        categories = parsedCategories;
        return parsedCategories.Count > 0;
    }

    private static bool TryParseEventTypes(
      IReadOnlyDictionary<string, string> options,
      out IReadOnlyCollection<TextLogEventType> eventTypes)
    {
        eventTypes = Array.Empty<TextLogEventType>();
        if (!options.TryGetValue("log-events", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var parsedEventTypes = new List<TextLogEventType>();
        foreach (var candidate in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse(candidate, ignoreCase: true, out TextLogEventType eventType))
            {
                throw new ArgumentException($"Invalid log event type '{candidate}'.", nameof(options));
            }

            if (!parsedEventTypes.Contains(eventType))
            {
                parsedEventTypes.Add(eventType);
            }
        }

        eventTypes = parsedEventTypes;
        return parsedEventTypes.Count > 0;
    }

    private sealed record TextLogProfileSettings(
      TextLogLevel Level,
      TextLogView View,
      IReadOnlyCollection<TextLogCategory> Categories,
      IReadOnlyCollection<TextLogEventType> EventTypes);
}
