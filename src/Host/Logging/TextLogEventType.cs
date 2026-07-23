namespace RoslynPrototype.Application.Logging;

internal enum TextLogEventType
{
    Started = 0,
    Sampled = 1,
    Completed = 2,
    Failed = 3,
    Summary = 4,
    Snapshot = 5,
    Warning = 6,
    Error = 7,
    WriterFailed = 8,
    Pending = 9,
    Written = 10
}
