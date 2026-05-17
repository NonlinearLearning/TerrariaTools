namespace Demo;

public static class Sample
{
  public static void MainEntry()
  {
    Live();
  }

  public static void Live()
  {
  }

  public static void DeadA()
  {
    DeadB();
  }

  public static void DeadB()
  {
  }
}
