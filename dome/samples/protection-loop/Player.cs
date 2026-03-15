namespace Sample;

public sealed class Item
{
    public int Value { get; set; }
}

public sealed class Player
{
    public int Update(int seed)
    {
        // dome:delete
        int count = seed;
        var item = new Item { Value = count };
        int next = count;
        return next;
    }
}
