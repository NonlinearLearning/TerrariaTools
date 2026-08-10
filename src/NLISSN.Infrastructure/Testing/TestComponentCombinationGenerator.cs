namespace NLISSN.Infrastructure.Testing;

public sealed class TestComponentCombinationGenerator
{
    private readonly TestComponentComposer _composer = new();

    public IReadOnlyList<TestComponentComposition> Generate(
      IReadOnlyList<TestComponent> availableComponents,
      int componentCount)
    {
        ArgumentNullException.ThrowIfNull(availableComponents);
        if (componentCount < 1 || componentCount > availableComponents.Count)
        {
            throw new ArgumentOutOfRangeException(
              nameof(componentCount),
              "The component count must select at least one available component.");
        }

        var results = new List<TestComponentComposition>();
        var selection = new List<TestComponent>(componentCount);
        GenerateCompositions(availableComponents, componentCount, 0, selection, results);
        return results;
    }

    private void GenerateCompositions(
      IReadOnlyList<TestComponent> availableComponents,
      int componentCount,
      int startIndex,
      List<TestComponent> selection,
      List<TestComponentComposition> results)
    {
        if (selection.Count == componentCount)
        {
            var composition = _composer.Compose(selection);
            if (composition.IsValid)
            {
                results.Add(composition);
            }

            return;
        }

        var remaining = componentCount - selection.Count;
        for (var index = startIndex; index <= availableComponents.Count - remaining; index++)
        {
            selection.Add(availableComponents[index]);
            GenerateCompositions(availableComponents, componentCount, index + 1, selection, results);
            selection.RemoveAt(selection.Count - 1);
        }
    }
}
