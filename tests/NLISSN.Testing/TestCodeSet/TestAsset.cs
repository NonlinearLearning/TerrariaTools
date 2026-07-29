namespace RoslynPrototype.Testing.TestCodeSet;

public sealed record TestAsset(
  string Id,
  string Domain,
  IReadOnlyDictionary<string, string> Files,
  IReadOnlyList<string> Tags);
