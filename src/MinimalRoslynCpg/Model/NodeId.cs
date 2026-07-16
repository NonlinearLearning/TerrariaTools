namespace MinimalRoslynCpg.Model;

public readonly record struct NodeId(uint Value) : IComparable<NodeId>
{
  public static NodeId Empty => default;

  public bool IsEmpty => Value == 0;

  public int CompareTo(NodeId other)
  {
    return Value.CompareTo(other.Value);
  }

  public override string ToString()
  {
    return Value.ToString();
  }
}
