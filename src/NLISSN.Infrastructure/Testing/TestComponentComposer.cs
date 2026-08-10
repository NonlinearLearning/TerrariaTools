using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NLISSN.Infrastructure.Testing;

public sealed class TestComponentComposer
{
    public TestComponentComposition Compose(IEnumerable<TestComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var componentList = components.ToArray();
        ValidateComponentIds(componentList);
        var knownIds = componentList
          .Select(component => component.Id)
          .ToHashSet(StringComparer.Ordinal);
        var compositionDiagnostics = componentList
          .SelectMany(component => component.Prerequisites
            .Where(prerequisite => !knownIds.Contains(prerequisite))
            .Select(prerequisite => new TestComponentCompositionDiagnostic(
              "TEST-COMPONENT-PREREQUISITE-001",
              component.Id,
              $"Required component '{prerequisite}' is not present.")))
          .ToList();
        AddPrerequisiteCycleDiagnostics(componentList, knownIds, compositionDiagnostics);

        var grouped = componentList
          .SelectMany(component => component.Sources.Select(source => (component, source)))
          .GroupBy(
            item => item.source.RelativePath,
            StringComparer.OrdinalIgnoreCase)
          .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var sources = new List<TestComponentSource>();
        var provenance = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<TestComponentSyntaxDiagnostic>();

        foreach (var group in grouped)
        {
            var fragments = group.ToArray();
            var text = string.Join(
              Environment.NewLine,
              fragments.Select(fragment => fragment.source.Text.TrimEnd('\r', '\n')));
            var path = group.Key;
            sources.Add(new TestComponentSource(path, text));
            provenance[path] = fragments
              .Select(fragment => fragment.component.Id)
              .Distinct(StringComparer.Ordinal)
              .ToArray();

            var tree = CSharpSyntaxTree.ParseText(text, path: path);
            foreach (var diagnostic in tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(new TestComponentSyntaxDiagnostic(
                  FindOwningComponent(fragments, diagnostic.Location.SourceSpan.Start),
                  path,
                  diagnostic.GetMessage(),
                  diagnostic.Location.SourceSpan.Start,
                  diagnostic.Location.SourceSpan.Length));
            }
        }

        return new TestComponentComposition(
          componentList,
          sources,
          provenance,
          diagnostics,
          compositionDiagnostics);
    }

    private static void AddPrerequisiteCycleDiagnostics(
      IReadOnlyList<TestComponent> components,
      IReadOnlySet<string> knownIds,
      ICollection<TestComponentCompositionDiagnostic> diagnostics)
    {
        var byId = components.ToDictionary(component => component.Id, StringComparer.Ordinal);
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var component in components.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            Visit(component.Id);
        }

        void Visit(string id)
        {
            if (states.TryGetValue(id, out var state))
            {
                if (state == VisitState.Completed)
                {
                    return;
                }

                var cycleStart = stack.IndexOf(id);
                var cycle = stack.Skip(cycleStart).Append(id);
                diagnostics.Add(new TestComponentCompositionDiagnostic(
                  "TEST-COMPONENT-PREREQUISITE-002",
                  id,
                  $"Prerequisite cycle detected: {string.Join(" -> ", cycle)}."));
                return;
            }

            states[id] = VisitState.Visiting;
            stack.Add(id);
            foreach (var prerequisite in byId[id].Prerequisites.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (knownIds.Contains(prerequisite))
                {
                    Visit(prerequisite);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            states[id] = VisitState.Completed;
        }
    }

    private static void ValidateComponentIds(IReadOnlyList<TestComponent> components)
    {
        var duplicate = components
          .GroupBy(component => component.Id, StringComparer.Ordinal)
          .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate test component id '{duplicate.Key}'.", nameof(components));
        }
    }

    private static string FindOwningComponent(
      IReadOnlyList<(TestComponent component, TestComponentSource source)> fragments,
      int position)
    {
        var offset = 0;
        foreach (var fragment in fragments)
        {
            var end = offset + fragment.source.Text.Length;
            if (position <= end)
            {
                return fragment.component.Id;
            }

            offset = end + Environment.NewLine.Length;
        }

        return fragments[^1].component.Id;
    }

    private enum VisitState
    {
        Visiting,
        Completed
    }
}
