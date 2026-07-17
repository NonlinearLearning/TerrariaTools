namespace RoslynPrototype.Application.Logging;

internal interface ITextLogSink : IDisposable
{
    void Emit(TextLogEvent textLogEvent);

    void Flush();
}
