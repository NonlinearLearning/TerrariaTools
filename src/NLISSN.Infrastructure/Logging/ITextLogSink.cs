namespace NLISSN.Logging;

public interface ITextLogSink : IDisposable
{
    void Emit(TextLogEvent textLogEvent);

    void Flush();
}
