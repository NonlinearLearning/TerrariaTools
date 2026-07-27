using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NLISSN.Application;

/// 为删除规则分析创建具有最小基础框架引用集的 Roslyn 编译。
public static class RoslynCompilationFactory
{
    // 为单个语法树补齐最小框架引用，复用统一的多树编译配置。
    public static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CreateCompilation(new[] { tree });
    }

    // 为一组语法树创建可做语义分析和诊断的临时 Roslyn 编译。
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
