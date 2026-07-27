namespace NLCPG.Model;

public readonly record struct NodeId(uint Value) : IComparable<NodeId>
{
  public static NodeId Empty => default;

  public bool IsEmpty => Value == 0;

  // 按底层无符号值比较两个 NodeId。
  public int CompareTo(NodeId other)
  {
    return Value.CompareTo(other.Value);
  }

  // 返回 NodeId 的十进制字符串表示。
  public override string ToString()
  {
    return Value.ToString();
  }
}
