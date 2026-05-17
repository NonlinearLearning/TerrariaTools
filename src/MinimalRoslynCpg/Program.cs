using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;

var inputPath = args.FirstOrDefault();
var source = inputPath is not null && File.Exists(inputPath)
  ? File.ReadAllText(inputPath)
  : """
    namespace Demo;

    public sealed class Sample {
      public int Add(int left, int right) {
        var sum = left + right;
        return sum;
      }
    }
    """;
var filePath = inputPath ?? "demo.cs";

var graph = new RoslynCpgBuilder().BuildFromSource(source, filePath);

Console.WriteLine($"Nodes: {graph.Nodes.Count}");
Console.WriteLine($"Edges: {graph.Edges.Count}");

foreach (var kind in Enum.GetValues<RoslynCpgNodeKind>()) {
  var count = graph.Nodes.Count(node => node.Kind == kind);
  if (count > 0) {
    Console.WriteLine($"{kind}: {count}");
  }
}
