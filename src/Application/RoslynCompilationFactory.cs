using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynPrototype.Application;

public static class RoslynCompilationFactory
{
  public static CSharpCompilation CreateCompilation(SyntaxTree tree)
  {
    return CreateCompilation(new[] { tree });
  }

  public static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
  {
    var references = new[]
    {
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
    };

    return CSharpCompilation.Create(
      assemblyName: "RoslynPrototype",
      syntaxTrees: trees,
      references: references);
  }
}
