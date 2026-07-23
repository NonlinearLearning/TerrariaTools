namespace RoslynPrototype.Tests.TestCodeSet.DeleteClassDirectory;

internal static class DirectoryDeleteClassSources
{
    public const string PlayerInputEnabledSource = """
      namespace Demo;

      public static class PlayerInput
      {
        public static bool Enabled => true;
      }
      """;

    public const string GameUsingPlayerInputSource = """
      namespace Demo;

      public sealed class Game
      {
        public bool Run()
        {
          return PlayerInput.Enabled;
        }
      }
      """;

    public const string RendererUsingPlayerInputSource = """
      namespace Demo;

      public sealed class Renderer
      {
        public int Render()
        {
          return PlayerInput.Enabled ? 1 : 0;
        }
      }
      """;

    public const string RendererWithBlockBodyUsingPlayerInputSource = """
      namespace Demo;

      public sealed class Renderer
      {
        public int Render()
        {
          if (PlayerInput.Enabled)
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string EmptyPlayerInputClassSource = """
      namespace Demo;

      public sealed class PlayerInput
      {
      }
      """;
}
