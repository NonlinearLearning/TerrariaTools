namespace Sample;

public sealed class Player
{
    public void Update(int value)
    {
        int copy = value;

        // dome:delete
        Run(copy);
    }

    private void Run(int value)
    {
        Consume(value);
    }

    private static void Consume(int value)
    {
    }
}
