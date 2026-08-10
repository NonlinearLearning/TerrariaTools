namespace NLISSN.Logging;

public sealed class TextLogFilter
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
        return Allows(textLogEvent.Level, textLogEvent.Category, textLogEvent.EventType);
    }

    public bool Allows(TextLogLevel level, TextLogCategory category, TextLogEventType eventType)
    {
        return level <= MinimumLevel && _categories.Contains(category) && _eventTypes.Contains(eventType);
    }

    public static TextLogFilter CreateRuntimeFilter(
      string profile,
      string level,
      IReadOnlyCollection<string> categories,
      IReadOnlyCollection<string> eventTypes,
      string view)
    {
        var profileSettings = CreateProfile(profile);
        var parsedView = string.IsNullOrWhiteSpace(view)
          ? profileSettings.View
          : ParseEnum<TextLogView>(view, nameof(view));
        var parsedLevel = string.IsNullOrWhiteSpace(level)
          ? profileSettings.Level
          : ParseEnum<TextLogLevel>(level, nameof(level));
        IReadOnlyCollection<TextLogCategory> parsedCategories = categories.Count > 0
          ? ParseEnums<TextLogCategory>(categories, nameof(categories))
          : profileSettings.Categories;
        IReadOnlyCollection<TextLogEventType> parsedEventTypes = eventTypes.Count > 0
          ? ParseEnums<TextLogEventType>(eventTypes, nameof(eventTypes))
          : profileSettings.EventTypes;

        return new TextLogFilter(
          parsedLevel,
          parsedView,
          parsedCategories,
          parsedEventTypes);
    }

    private static TextLogProfileSettings CreateProfile(string profile)
    {
        return profile.Trim().ToLowerInvariant() switch
        {
            "minimal" => new TextLogProfileSettings(
              TextLogLevel.Info,
              TextLogView.Compact,
              new[] { TextLogCategory.Run, TextLogCategory.Diag },
              new[]
              {
          TextLogEventType.Started, TextLogEventType.Completed, TextLogEventType.Failed,
          TextLogEventType.Summary, TextLogEventType.Warning, TextLogEventType.Error
              }),
            "normal" => new TextLogProfileSettings(
              TextLogLevel.Info,
              TextLogView.Normal,
              new[]
              {
          TextLogCategory.Run, TextLogCategory.File, TextLogCategory.Diag,
          TextLogCategory.Cpg, TextLogCategory.Mark
              },
              new[]
              {
          TextLogEventType.Started, TextLogEventType.Completed, TextLogEventType.Failed,
          TextLogEventType.Summary, TextLogEventType.Warning, TextLogEventType.Error
              }),
            "diagnostic" => new TextLogProfileSettings(
              TextLogLevel.Debug,
              TextLogView.Diagnostic,
              Enum.GetValues<TextLogCategory>(),
              Enum.GetValues<TextLogEventType>()),
            "benchmark" => new TextLogProfileSettings(
              TextLogLevel.Debug,
              TextLogView.Benchmark,
              Enum.GetValues<TextLogCategory>(),
              Enum.GetValues<TextLogEventType>()),
            _ => throw new ArgumentException($"Invalid log profile '{profile}'.", nameof(profile))
        };
    }

    private static TEnum ParseEnum<TEnum>(string value, string parameterName)
      where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Invalid {typeof(TEnum).Name} '{value}'.", parameterName);
    }

    private static IReadOnlyCollection<TEnum> ParseEnums<TEnum>(
      IReadOnlyCollection<string> values,
      string parameterName)
      where TEnum : struct, Enum
    {
        return values
          .Select(value => ParseEnum<TEnum>(value, parameterName))
          .Distinct()
          .ToArray();
    }

    private sealed record TextLogProfileSettings(
      TextLogLevel Level,
      TextLogView View,
      IReadOnlyCollection<TextLogCategory> Categories,
      IReadOnlyCollection<TextLogEventType> EventTypes);
}
