namespace Sample;

public sealed class Player
{
    public bool Update(int value)
    {
        // dome:delete
        bool allowed = Run(value) && (value > 0);
        return allowed;
    }

    private static bool Run(int value)
    {
        return value > 0;
    }
}
