namespace TerrariaTools.Dome.Application;

/// <summary>
/// Progress reporting contract used by the standard Dome application pipeline.
/// </summary>
public interface IDomeProgressReporter
{
    void Report(string message);
}

/// <summary>
/// No-op progress reporter for standard application flows.
/// </summary>
public sealed class NullDomeProgressReporter : IDomeProgressReporter
{
    public static NullDomeProgressReporter Instance { get; } = new();

    public void Report(string message)
    {
    }
}
