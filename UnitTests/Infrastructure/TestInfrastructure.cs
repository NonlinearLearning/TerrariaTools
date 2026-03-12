using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.IO;
using System.Reflection;
using Bogus;
using Xunit;
using TerrariaTools.RewriteCodeExpressions.Pipeline;
using TerrariaTools.UnitTests.Scenarios;

namespace TerrariaTools.UnitTests.Infrastructure;

/// <summary>
/// Roslyn 测试基类，提供编译环境模拟和代码重写验证功能
/// </summary>
public abstract class RoslynTestBase
{
    protected static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    protected static readonly MetadataReference SystemCoreReference = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
    protected static readonly MetadataReference CSharpSymbolsReference = MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location);
    protected static readonly MetadataReference CodeAnalysisReference = MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);

    /// <summary>
    /// 创建一个包含指定代码的 Ad-hoc 编译环境
    /// </summary>
    protected virtual Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        return CSharpCompilation.Create("TestProject")
            .AddSyntaxTrees(syntaxTree)
            .AddReferences(GetStandardReferences())
            .WithOptions(compilationOptions);
    }

    /// <summary>
    /// 获取标准元数据引用
    /// </summary>
    protected static IEnumerable<MetadataReference> GetStandardReferences()
    {
        var runtimePath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);

        // 添加核心运行库引用
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Console.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Threading.Tasks.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "mscorlib.dll"));
    }

    /// <summary>
    /// 创建一个包含多个文件的解决方案
    /// </summary>
    protected async Task<(AdhocWorkspace workspace, Solution solution, Project project)> CreateSolutionAsync(params (string name, string content)[] files)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "TestProject", "TestAssembly", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithMetadataReferences(GetStandardReferences());

        var project = workspace.AddProject(projectInfo);

        foreach (var file in files)
        {
            var documentId = DocumentId.CreateNewId(projectId);
            var sourceText = SourceText.From(file.content, Encoding.UTF8);
            var filePath = System.IO.Path.GetFullPath(file.name);
            var doc = workspace.AddDocument(projectId, file.name, sourceText);
            workspace.TryApplyChanges(workspace.CurrentSolution.WithDocumentFilePath(doc.Id, filePath));
        }

        return (workspace, workspace.CurrentSolution, workspace.CurrentSolution.GetProject(projectId)!);
    }

    /// <summary>
    /// 获取指定代码中第一个符合条件的节点。
    /// </summary>
    protected T GetNode<T>(string source, Func<T, bool>? predicate = null) where T : SyntaxNode
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var nodes = root.DescendantNodes().OfType<T>();
        return predicate == null ? nodes.First() : nodes.First(predicate);
    }

    /// <summary>
    /// 获取编译后的语法树根节点和语义模型
    /// </summary>
    protected async Task<(SyntaxNode root, SemanticModel model)> GetCompilationAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        return (root, model);
    }

    /// <summary>
    /// 运行重写管道。返回重写后的代码和收集到的诊断信息。
    /// </summary>
    protected async Task<(string result, TerrariaTools.Diagnostics.RewritingTraceContext traceContext)> RunPipelineWithTraceAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var nodesToMark = new HashSet<SyntaxNode>(selectNodes(root));
        var traceContext = new TerrariaTools.Diagnostics.RewritingTraceContext();

        var result = await PipelineExpressionSimplifier.RewriteAsync(root, model, null, _ => false, nodesToMark, null, default, traceContext);
        return (result.ToFullString(), traceContext);
    }

    /// <summary>
    /// 运行重写管道。允许用户提供一个回调来选择需要标记的节点，确保它们来自同一个语法树。
    /// </summary>
    protected async Task<string> RunPipelineWithNodesAsync(string source, Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
    {
        var (result, _) = await RunPipelineWithTraceAsync(source, selectNodes);
        return result;
    }

    // --- Scenario 支持方法 ---

    /// <summary>
    /// 开始一个新的测试场景 (Scenario)。
    /// </summary>
    protected ScenarioContext Given(string source, string scenarioName = "InlineScenario")
    {
        return new ScenarioContext(this, source, scenarioName);
    }

    /// <summary>
    /// 从 SharedScenarios 中加载一个场景。
    /// </summary>
    protected ScenarioContext GivenScenario(string scenarioPath)
    {
        var source = ScenarioManager.GetScenario(scenarioPath)
            ?? throw new ArgumentException($"Scenario not found: {scenarioPath}");
        return new ScenarioContext(this, source, scenarioPath);
    }

    /// <summary>
    /// 运行管道并返回结果字符串（用于 Scenario 内部调用）。
    /// </summary>
    public async Task<string> ExecutePipelineAsync(string source, Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
    {
        return await RunPipelineWithNodesAsync(source, selectNodes);
    }

    /// <summary>
    /// 运行差异化测试并返回结果（用于 Scenario 内部调用）。
    /// </summary>
    public async Task<(bool isMatch, string oldResult, string newResult, TerrariaTools.Diagnostics.RewritingTraceContext traceContext)> ExecuteDifferentialTestAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
    {
        return await RunDifferentialTestAsync(source, selectNodes);
    }

    /// <summary>
    /// 运行差异化测试。返回新旧逻辑是否一致。
    /// </summary>
    protected async Task<(bool isMatch, string oldResult, string newResult, TerrariaTools.Diagnostics.RewritingTraceContext traceContext)> RunDifferentialTestAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var nodesToMark = new HashSet<SyntaxNode>(selectNodes(root));
        var traceContext = new TerrariaTools.Diagnostics.RewritingTraceContext();

        // 1. 运行原版 ExpressionSimplifier
        var oldRewriter = new TerrariaTools.RewriteCodeExpressions.ExpressionSimplifier(n => nodesToMark.Contains(n), model, nodesToMark, traceContext);
        var oldResultRoot = oldRewriter.Visit(root);
        var oldResult = oldResultRoot?.NormalizeWhitespace().ToFullString() ?? "";

        // 2. 运行新版 Pipeline (模拟旧版行为：不含 ExpressionOptimizerLayer 和 PostProcessingLayer)
        var pipeline = new RewritingPipeline()
        {
            ExplicitOnly = true
        }
            .AddLayer(new SemanticIdentifierLayer())
            .AddLayer(new SyntaxTransformerLayer());

        var newResultRoot = await pipeline.ExecuteAsync(root, model, null, n => nodesToMark.Contains(n), nodesToMark, default, default, traceContext);
        var newResult = newResultRoot?.NormalizeWhitespace().ToFullString() ?? "";

        // 调试输出
        if (oldResult != newResult)
        {
            System.Diagnostics.Debug.WriteLine("=== DIFFERENCE DETECTED ===");
            System.Diagnostics.Debug.WriteLine("--- OLD RESULT ---");
            System.Diagnostics.Debug.WriteLine(oldResult);
            System.Diagnostics.Debug.WriteLine("--- NEW RESULT ---");
            System.Diagnostics.Debug.WriteLine(newResult);
        }

        // 3. 使用 DifferentialTester 进行对比
        var diffTester = new TerrariaTools.ConsistentBehaviorGuarantee.DifferentialTester(traceContext);
        bool isMatch = diffTester.Compare(oldResult, newResult, "Pipeline vs Original Simplifier");

        if (!isMatch)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diff_log.txt");
            File.WriteAllText(logPath, $"OLD:\n{oldResult}\n\nNEW:\n{newResult}");
            throw new Exception($"Consistency check failed. Log: {logPath}");
        }

        return (isMatch, oldResult, newResult, traceContext);
    }
}

/// <summary>
/// 表示一个正在运行的测试场景上下文，提供流式断言接口。
/// </summary>
public class ScenarioContext
{
    private readonly RoslynTestBase _testBase;
    private readonly string _source;
    private readonly string _scenarioName;

    public ScenarioContext(RoslynTestBase testBase, string source, string scenarioName = "Unnamed")
    {
        _testBase = testBase;
        _source = source;
        _scenarioName = scenarioName;
    }

    /// <summary>
    /// 开始一个“标记特定节点”的测试步骤。
    /// </summary>
    /// <typeparam name="TNode">要查找和标记的节点类型</typeparam>
    /// <param name="predicate">可选的过滤条件</param>
    /// <returns>验证构建器</returns>
    public ScenarioAssertionBuilder WhenMarking<TNode>(Func<TNode, bool>? predicate = null) where TNode : SyntaxNode
    {
        return new ScenarioAssertionBuilder(_testBase, _source, root =>
        {
            var nodes = root.DescendantNodes().OfType<TNode>();
            return predicate == null ? nodes : nodes.Where(predicate);
        });
    }

    /// <summary>
    /// 开始一个“标记特定节点”的测试步骤，使用自定义的选择逻辑。
    /// </summary>
    public ScenarioAssertionBuilder WhenMarking(Func<SyntaxNode, IEnumerable<SyntaxNode>> selector)
    {
        return new ScenarioAssertionBuilder(_testBase, _source, selector);
    }
}

/// <summary>
/// 场景验证构建器，用于执行 Pipeline 并断言结果。
/// </summary>
public class ScenarioAssertionBuilder
{
    private readonly RoslynTestBase _testBase;
    private readonly string _source;
    private readonly Func<SyntaxNode, IEnumerable<SyntaxNode>> _selector;

    public ScenarioAssertionBuilder(RoslynTestBase testBase, string source, Func<SyntaxNode, IEnumerable<SyntaxNode>> selector)
    {
        _testBase = testBase;
        _source = source;
        _selector = selector;
    }

    /// <summary>
    /// 执行 Pipeline 并验证结果是否满足断言。
    /// </summary>
    public async Task Then(Action<string> assertion)
    {
        var result = await _testBase.ExecutePipelineAsync(_source, _selector);
        assertion(result);
    }

    /// <summary>
    /// 执行差异化测试 (Differential Testing)。
    /// </summary>
    public async Task ThenDifferential(string contextMessage = "")
    {
        var (isMatch, oldResult, newResult, _) = await _testBase.ExecuteDifferentialTestAsync(_source, _selector);
        Assert.True(isMatch, $"差异化测试失败 [{contextMessage}]:\nOld:\n{oldResult}\n\nNew:\n{newResult}");
    }
}

/// <summary>
/// 统一场景管理器，负责发现和加载 SharedScenarios 中的测试场景。
/// </summary>
public static class ScenarioManager
{
    /// <summary>
    /// 获取所有可用的场景分类。
    /// </summary>
    public static IEnumerable<Type> GetCategories()
    {
        return typeof(SharedScenarios).GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
    }

    /// <summary>
    /// 获取指定分类下的所有场景名称和内容。
    /// </summary>
    public static IEnumerable<(string Name, string Content)> GetScenariosFromCategory(Type categoryType)
    {
        return categoryType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral || f.IsInitOnly)
            .Select(f => (f.Name, f.GetValue(null)?.ToString() ?? ""));
    }

    /// <summary>
    /// 根据名称查找场景。支持 "Category.Name" 或 "Name" 格式。
    /// </summary>
    public static string? GetScenario(string scenarioPath)
    {
        if (scenarioPath.Contains('.'))
        {
            var parts = scenarioPath.Split('.');
            var category = typeof(SharedScenarios).GetNestedType(parts[0], BindingFlags.Public | BindingFlags.Static);
            if (category != null)
            {
                return category.GetField(parts[1])?.GetValue(null)?.ToString();
            }
        }

        // 尝试从顶级字段查找
        return typeof(SharedScenarios).GetField(scenarioPath)?.GetValue(null)?.ToString();
    }
}

/// <summary>
/// 使用 Bogus 库生成随机代码测试数据，模拟各种边缘场景
/// </summary>
public static class BogusTestDataGenerator
{
    private static readonly Faker Faker = new();

    /// <summary>
    /// 生成包含指定逻辑的代码片段，随机填充标识符
    /// </summary>
    public static string GenerateExpressionSnippet(string basePattern)
    {
        // 模拟生成随机变量名、类名等
        var variableName = Faker.Random.Word().Replace("-", "_");
        var methodName = Faker.Random.Word().Replace("-", "_");
        var className = Faker.Random.Word().Replace("-", "_");

        return basePattern
            .Replace("{var}", variableName)
            .Replace("{method}", methodName)
            .Replace("{class}", className);
    }

    /// <summary>
    /// 生成一组带有语义上下文的代码
    /// </summary>
    public static string GenerateFullClass(string body)
    {
        var className = Faker.Random.Word().Replace("-", "_");
        return $@"
using System;
using System.Collections.Generic;
using System.Linq;

public class {className}
{{
    public void TestMethod()
    {{
        {body}
    }}
}}";
    }

    /// <summary>
    /// 生成随机深度嵌套的 while 语句（用于压力测试和递归验证）
    /// </summary>
    public static string GenerateNestedWhile(int depth)
    {
        if (depth <= 0) return "Console.WriteLine(\"Base Case\");";

        var condition = Faker.Random.Bool() ? "true" : "i < 10";
        return $@"while ({condition})
{{
    {GenerateNestedWhile(depth - 1)}
}}";
    }
}
