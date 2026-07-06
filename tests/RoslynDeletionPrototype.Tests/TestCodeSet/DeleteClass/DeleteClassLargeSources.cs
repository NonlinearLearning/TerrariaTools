namespace RoslynPrototype.Tests.TestCodeSet.DeleteClass;

public static class DeleteClassLargeSources
{
    public const string IntegratedLargeDeleteClassSource = """
          namespace Demo;

          public sealed class PlayerInput
          {
            public int Frame { get; set; }
          }

          public delegate int InputProjector(PlayerInput input, int frame);

          public interface IInputContract
          {
            PlayerInput Snapshot();
            PlayerInput this[int frame] { get; }
          }

          public sealed class InputBuffer
          {
            public int this[PlayerInput input, int frame] => frame;
          }

          public static class InputExtensions
          {
            public static int Paint(this InputBuffer buffer, PlayerInput input, int frame)
            {
              return frame;
            }
          }

          public sealed class GameFlow
          {
            public int Run(int frame)
            {
              InputProjector projector = KeepFrame;
              var buffer = new InputBuffer();
              var call = Normalize(null, frame);
              var value = projector(null, frame) + buffer[null, frame] + buffer.Paint(null, frame);
              return call + value + Local(null, frame);

              int Local(PlayerInput input, int localFrame)
              {
                return localFrame;
              }
            }

            private int Normalize(PlayerInput input, int frame)
            {
              return frame;
            }

            private int KeepFrame(PlayerInput input, int frame)
            {
              return frame;
            }
          }
          """;

    public static void WriteLargeProject(string projectDirectory)
    {
        Directory.CreateDirectory(projectDirectory);
        WriteFile(projectDirectory, "PlayerInput.cs", PlayerInputSource);
        WriteFile(projectDirectory, Path.Combine("Gameplay", "GameFlow.cs"), GameplaySource);
        WriteFile(projectDirectory, Path.Combine("Ui", "HudBindings.cs"), UiBindingsSource);
        WriteFile(projectDirectory, Path.Combine("Contracts", "InputContracts.cs"), ContractsSource);
    }

    private const string PlayerInputSource = """
          namespace Demo.Shared;

          public sealed class PlayerInput
          {
            public int Frame { get; set; }
          }
          """;

    private const string GameplaySource = """
          using Demo.Shared;

          namespace Demo.Gameplay;

          public sealed class InputBuffer
          {
            public int this[PlayerInput input, int frame] => frame;
          }

          public sealed class GameFlow
          {
            public int Run(int frame)
            {
              var buffer = new InputBuffer();
              var first = Normalize(null, frame);
              var second = Local(null, frame);
              var third = buffer[null, frame];
              return first + second + third;

              int Local(PlayerInput input, int localFrame)
              {
                return localFrame;
              }
            }

            private int Normalize(PlayerInput input, int frame)
            {
              return frame;
            }
          }
          """;

    private const string UiBindingsSource = """
          using Demo.Gameplay;
          using Demo.Shared;

          namespace Demo.Ui;

          public delegate int InputProjector(PlayerInput input, int frame);

          public static class InputExtensions
          {
            public static int Paint(this InputBuffer buffer, PlayerInput input, int frame)
            {
              return frame;
            }
          }

          public sealed class HudBindings
          {
            public int Render(int frame)
            {
              InputProjector projector = KeepFrame;
              var buffer = new InputBuffer();
              return projector(null, frame) + buffer.Paint(null, frame);
            }

            private int KeepFrame(PlayerInput input, int frame)
            {
              return frame;
            }
          }
          """;

    private const string ContractsSource = """
          using Demo.Shared;

          namespace Demo.Contracts;

          public interface IInputContract
          {
            PlayerInput Snapshot();
            PlayerInput this[int frame] { get; }
          }
          """;

    private static void WriteFile(string projectDirectory, string relativePath, string source)
    {
        var filePath = Path.Combine(projectDirectory, relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, source);
    }
}
