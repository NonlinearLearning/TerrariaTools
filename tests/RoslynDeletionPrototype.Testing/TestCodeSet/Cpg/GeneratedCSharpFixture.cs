namespace RoslynPrototype.Testing.TestCodeSet.Cpg;

public sealed record GeneratedCSharpFixture(
  string Id,
  IReadOnlyDictionary<string, string> Files)
{
  public string PrimaryFileName => Files.Keys.OrderBy(value => value, StringComparer.Ordinal).First();

  public string PrimarySource => Files[PrimaryFileName];

  public static GeneratedCSharpFixture Create(int seed)
  {
    var value = Math.Abs(seed % 17) + 1;
    var template = (seed % 9) switch
    {
      0 => """
        class Generated { int Run(int value) { return value > __VALUE__ && value < __VALUE3__ ? value : __VALUE__; } }
        """,
      1 => """
        class Generated { int Add(int left, int right = __VALUE__) => left + right; int Run(int value) => Add(left: value); }
        """,
      2 => """
        class Generated { delegate int Mapper(int value); int Run(int value) { Mapper mapper = item => item + __VALUE__; return mapper(value); } }
        """,
      3 => """
        class Generated { int Run(int value) { var values = new[] { value, __VALUE__ }; return values[0] + values[1]; } }
        """,
      4 => """
        class Generated { int Run(int value) { int Local(int item) => item + __VALUE__; return Local(value); } }
        """,
      5 => """
        class Generated { int _value = __VALUE__; int Run(int value) => this._value + value; }
        """,
      6 => """
        class Generated { int Run(int value) { return value == 0 || value > __VALUE__ ? value + __VALUE__ : value - __VALUE__; } }
        """,
      7 => """
        class Generated { int Run(int value) { var text = value.ToString(); return text.Length + __VALUE__; } }
        """,
      _ => """
        public class Generated { public int Run(int value) => Support.Add(value, __VALUE__); }
        """,
    };
    var source = template
      .Replace("__VALUE3__", (value + 3).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
      .Replace("__VALUE__", value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    var files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["Generated.cs"] = source,
    };
    if (seed % 9 == 8)
    {
      files["Support.cs"] = """
        public static class Support { public static int Add(int left, int right) => left + right; }
        """;
    }

    return new GeneratedCSharpFixture($"generated-{seed:D4}", files);
  }
}
