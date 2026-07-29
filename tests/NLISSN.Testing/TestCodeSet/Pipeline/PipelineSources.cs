namespace RoslynPrototype.Tests.TestCodeSet.Pipeline;

public static class PipelineSources
{
  public const string RuntimeConfiguredDopSource =
    """
    namespace Demo;

    public sealed class Sample
    {
      public int Run(int value)
      {
        return value + 1;
      }
    }
    """;

  public const string ConcurrentMarkingSource =
    """
    namespace Demo;

    public sealed class First
    {
    }

    public sealed class Second
    {
    }
    """;

  public const string SnapshotCacheSource =
    """
    namespace Demo;

    public sealed class Box
    {
      public Box? Next { get; set; }

      public int Value { get; set; }

      public int Read() => Value;
    }

    public sealed class Consumer
    {
      public int Execute(Box s)
      {
        var created = new Box();
        return s?.Next?.Read() ?? s.Value;
      }
    }
    """;

  public const string ChainedPropagationSource =
    """
    namespace Demo;

    public sealed class Sample
    {
      public int Compute(Box s)
      {
        var value = s.Seed;
        return value + 1;
      }
    }

    public sealed class Box
    {
      public int Seed { get; set; }
    }
    """;

  public const string RuntimeAwareSource =
    """
    namespace Demo;

    public sealed class RuntimeAware
    {
    }
    """;

  public const string ParallelMarkingSource =
    """
    namespace Demo;

    public sealed class Alpha
    {
    }

    public sealed class Beta
    {
    }
    """;

  public const string ParallelPropagationSource =
    """
    namespace Demo;

    public sealed class Alpha
    {
      public void Step()
      {
      }
    }

    public sealed class Beta
    {
      public void Step()
      {
      }
    }
    """;

  public const string DeleteClassMethodParameterUsageSource =
    """
    namespace Demo;

    public class PlayerInput
    {
    }

    public sealed class Game
    {
      private int ApplyPrivate(PlayerInput input, int frame)
      {
        return frame;
      }

      public int Run(int frame)
      {
        return ApplyPrivate(null, frame);
      }
    }
    """;

  public const string DeleteClassLocalFunctionParameterUsageSource =
    """
    namespace Demo;

    public class PlayerInput
    {
    }

    public sealed class Game
    {
      public int Run(int frame)
      {
        return ApplyLocal(null, frame);

        int ApplyLocal(PlayerInput input, int value)
        {
          return value;
        }
      }
    }
    """;

  public const string DeleteClassIndexerParameterUsageSource =
    """
    namespace Demo;

    public class PlayerInput
    {
    }

    public sealed class Board
    {
      public int this[PlayerInput input, int slot]
      {
        get
        {
          return slot;
        }
      }
    }

    public sealed class Game
    {
      public int Run(Board board, int slot)
      {
        return board[null, slot];
      }
    }
    """;

  public const string DeleteClassDelegateUsageSource =
    """
    namespace Demo;

    public class PlayerInput
    {
    }

    public delegate int Handler(PlayerInput input, int frame);

    public sealed class Game
    {
      private int Apply(PlayerInput input, int frame)
      {
        return frame;
      }

      public int Run(int frame)
      {
        Handler handler = Apply;
        return handler(null, frame) + handler.Invoke(null, frame);
      }
    }
    """;

  public const string DeleteClassExtensionMethodSource =
    """
    namespace Demo;

    public class PlayerInput
    {
    }

    public static class Extensions
    {
      public static int Use(this string text, PlayerInput input, int frame)
      {
        return frame;
      }
    }

    public sealed class Game
    {
      public int Run(string text, int frame)
      {
        return text.Use(null, frame);
      }
    }
    """;

  public const string DeleteClassDeclarationHostSource =
    """
    namespace Demo;

    public class PlayerInput
    {
    }

    public interface IContract
    {
      PlayerInput Current { get; }
      int Apply(PlayerInput input);
      int this[PlayerInput input] { get; }
      event System.Action<PlayerInput> Changed;
    }

    public delegate PlayerInput Build();

    public sealed class BaseGame : PlayerInput
    {
    }

    public static class InputExtensions
    {
      public static int Score(this PlayerInput input)
      {
        return 1;
      }
    }

    public sealed class Game
    {
      private PlayerInput _field;
      public PlayerInput Property { get; set; }

      public PlayerInput Create()
      {
        return null;
      }

      public void Run()
      {
        System.Collections.Generic.List<PlayerInput> values = new();
      }
    }
    """;

  public const string LogicalOperandGroupSource =
    """
    namespace Demo;

    public sealed class Box
    {
      public bool IsReady { get; set; }
    }

    public sealed class Game
    {
      public bool Run(Box s, bool ready, bool fallback)
      {
        return s.IsReady && ready && fallback;
      }
    }
    """;

  public const string SObjectIfStructureCompletionSource =
    """
    namespace Demo;

    public sealed class Box
    {
      public bool IsReady { get; set; }
    }

    public sealed class Game
    {
      public int Run(Box s, bool fallback)
      {
        if (s.IsReady)
        {
          return 1;
        }
        else if (fallback)
        {
          return 2;
        }
        else
        {
          return 3;
        }

        if (fallback)
        {
          return 4;
        }
        else if (s.IsReady)
        {
          return 5;
        }

        return 6;
      }
    }
    """;

  public const string DeleteClassIfStructureCompletionSource =
    """
    namespace Demo;

    public sealed class PlayerInput
    {
      public bool IsReady { get; set; }
    }

    public sealed class Game
    {
      public int Run(PlayerInput input, bool fallback)
      {
        if (input.IsReady)
        {
          return 1;
        }
        else if (fallback)
        {
          return 2;
        }
        else
        {
          return 3;
        }
      }
    }
    """;

  public const string OverlappingDeleteSource =
    """
    namespace Demo;

    internal static class Sample
    {
      public static int Run(bool ready)
      {
        if (ready)
        {
          return 1;
        }

        return 2;
      }
    }
    """;
}
