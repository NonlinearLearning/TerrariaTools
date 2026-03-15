namespace TerrariaTools.Testing.TestDoubles;

public sealed class RecordingStage<TInput, TOutput>
{
    private readonly Func<TInput, CancellationToken, Task<TOutput>> _handler;

    public RecordingStage(Func<TInput, CancellationToken, Task<TOutput>> handler)
    {
        _handler = handler;
    }

    public List<TInput> Inputs { get; } = [];

    public Task<TOutput> InvokeAsync(TInput input, CancellationToken cancellationToken)
    {
        Inputs.Add(input);
        return _handler(input, cancellationToken);
    }
}
