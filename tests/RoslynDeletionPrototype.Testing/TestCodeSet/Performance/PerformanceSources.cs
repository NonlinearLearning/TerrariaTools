namespace RoslynPrototype.Tests.TestCodeSet.Performance;

public static class PerformanceSources
{
    public const string TreeScanSource = """
      namespace Demo;

      public delegate int Handler(PlayerInput input, int frame);

      public sealed class PlayerInput
      {
      }

      public sealed class Runner
      {
        public int Run(Handler handler)
        {
          return handler(null, 1);
        }
      }
      """;

    public const string CleanupPlayerInputSource = """
      namespace Demo.Input;

      public static class PlayerInput
      {
        public static bool Enabled => true;
      }
      """;

    public const string CleanupFirstConsumerSource = """
      using Demo.Input;
      using System;

      namespace Demo;

      public sealed class GameA
      {
        public void Run()
        {
          Console.WriteLine(PlayerInput.Enabled);
        }
      }
      """;

    public const string CleanupSecondConsumerSource = """
      using Demo.Input;
      using System;

      namespace Demo;

      public sealed class GameB
      {
        public void Run()
        {
          Console.WriteLine(PlayerInput.Enabled);
        }
      }
      """;

    public const string FirstEmptyNamespaceSource = """
      namespace Demo.One
      {
        public sealed class PlayerInput
        {
        }
      }
      """;

    public const string SecondEmptyNamespaceSource = """
      namespace Demo.Two
      {
        public sealed class PlayerInput
        {
        }
      }
      """;

    internal static (string FilePath, string Source)[] CreateNamedArgumentMethodPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Game.cs", """
        namespace Demo;

        public sealed class Game
        {
          public int Apply(PlayerInput input, int frame)
          {
            return frame;
          }
        }
        """),
      ("RunnerA.cs", """
        namespace Demo;

        public sealed class RunnerA
        {
          public int Run(Game game)
          {
            return game.Apply(input: null, frame: 1);
          }
        }
        """),
      ("RunnerB.cs", """
        namespace Demo;

        public sealed class RunnerB
        {
          public int Run(Game game, int frame)
          {
            return game.Apply(input: null, frame: frame);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateOptionalParameterMethodPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Game.cs", """
        namespace Demo;

        public sealed class Game
        {
          public int Apply(int frame, PlayerInput input = null, int scale = 1)
          {
            return frame * scale;
          }
        }
        """),
      ("RunnerA.cs", """
        namespace Demo;

        public sealed class RunnerA
        {
          public int Run(Game game, int frame)
          {
            return game.Apply(frame);
          }
        }
        """),
      ("RunnerB.cs", """
        namespace Demo;

        public sealed class RunnerB
        {
          public int Run(Game game, int frame)
          {
            return game.Apply(frame, input: null, scale: 2) + game.Apply(frame, scale: 3);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateImplicitParamsMethodPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Game.cs", """
        namespace Demo;

        public sealed class Game
        {
          public int Apply(int frame, params PlayerInput[] inputs)
          {
            return frame;
          }
        }
        """),
      ("RunnerA.cs", """
        namespace Demo;

        public sealed class RunnerA
        {
          public int Run(Game game)
          {
            return game.Apply(1);
          }
        }
        """),
      ("RunnerB.cs", """
        namespace Demo;

        public sealed class RunnerB
        {
          public int Run(Game game, int frame)
          {
            return game.Apply(frame);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateExplicitParamsMethodPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Game.cs", """
        namespace Demo;

        public sealed class Game
        {
          public int Apply(int frame, params PlayerInput[] inputs)
          {
            return frame;
          }
        }
        """),
      ("Runner.cs", """
        namespace Demo;

        public sealed class Runner
        {
          public int Run(Game game)
          {
            return game.Apply(1, null);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateNamedIndexerPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Buffer.cs", """
        namespace Demo;

        public sealed class Buffer
        {
          public int this[int index, PlayerInput input] => index;
        }
        """),
      ("RunnerA.cs", """
        namespace Demo;

        public sealed class RunnerA
        {
          public int Run(Buffer buffer)
          {
            return buffer[index: 1, input: null];
          }
        }
        """),
      ("RunnerB.cs", """
        namespace Demo;

        public sealed class RunnerB
        {
          public int Run(Buffer buffer, int frame)
          {
            return buffer[index: frame, input: null];
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateDelegateMethodGroupPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Handler.cs", HandlerIntSource),
      ("Targets.cs", """
        namespace Demo;

        public static class Targets
        {
          public static int Apply(PlayerInput input, int frame)
          {
            return frame;
          }
        }
        """),
      ("Runner.cs", """
        namespace Demo;

        public sealed class Runner
        {
          public int Run(int frame)
          {
            Handler handler = Targets.Apply;
            return handler(null, frame) + handler.Invoke(null, frame);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateDelegateLambdaPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Handler.cs", HandlerIntSource),
      ("Runner.cs", """
        namespace Demo;

        public sealed class Runner
        {
          public int Run(int frame)
          {
            Handler handler = (input, currentFrame) => currentFrame;
            return handler(null, frame);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateDelegateInvocationChainPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Handler.cs", HandlerIntSource),
      ("Runner.cs", """
        namespace Demo;

        public sealed class Runner
        {
          public int Run(Handler handler, int frame)
          {
            var alias = handler;
            return handler(null, frame) + alias.Invoke(null, frame);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateExtensionReceiverPlanFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("InputExtensions.cs", """
        namespace Demo;

        public static class InputExtensions
        {
          public static int Score(this int value, PlayerInput input, int frame)
          {
            return value + frame;
          }
        }
        """),
      ("RunnerA.cs", """
        namespace Demo;

        public sealed class RunnerA
        {
          public int Run()
          {
            return 1.Score(null, 2);
          }
        }
        """),
      ("RunnerB.cs", """
        namespace Demo;

        public sealed class RunnerB
        {
          public int Run(int frame)
          {
            return InputExtensions.Score(frame, null, 3);
          }
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateDelegateReferencedTypeFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Handler.cs", HandlerVoidSource),
      ("Holder.cs", """
        namespace Demo;

        public sealed class Holder
        {
          private readonly Handler _handler;
        }
        """)
    ];

    internal static (string FilePath, string Source)[] CreateDelegateOnlyFiles() =>
    [
      ("PlayerInput.cs", PlayerInputSource),
      ("Handler.cs", HandlerVoidSource)
    ];

    private const string PlayerInputSource = """
      namespace Demo;

      public sealed class PlayerInput
      {
      }
      """;

    private const string HandlerIntSource = """
      namespace Demo;

      public delegate int Handler(PlayerInput input, int frame);
      """;

    private const string HandlerVoidSource = """
      namespace Demo;

      public delegate void Handler(PlayerInput input, int frame);
      """;
}
