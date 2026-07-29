namespace RoslynPrototype.Testing.TestCodeSet;

public sealed class TestAssetCatalog
{
  public TestAssetCatalog(IEnumerable<TestAsset> assets)
  {
    ArgumentNullException.ThrowIfNull(assets);

    var assetsById = new Dictionary<string, TestAsset>(StringComparer.Ordinal);
    foreach (var asset in assets)
    {
      ArgumentNullException.ThrowIfNull(asset);
      if (!assetsById.TryAdd(asset.Id, asset))
      {
        throw new ArgumentException($"Duplicate test asset id: {asset.Id}", nameof(assets));
      }
    }

    Assets = assetsById.Values
      .OrderBy(asset => asset.Id, StringComparer.Ordinal)
      .ToArray();
  }

  public IReadOnlyList<TestAsset> Assets { get; }
}
