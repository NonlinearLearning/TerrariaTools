namespace Demo;

public sealed class Sample
{
  public int Compute(Box s, int offset)
  {
    var value = s.Seed + offset;
    if (s.IsReady)
    {
      return value;
    }

    return offset;
  }
}

public sealed class Box
{
  public int Seed { get; set; }

  public bool IsReady { get; set; }
}
