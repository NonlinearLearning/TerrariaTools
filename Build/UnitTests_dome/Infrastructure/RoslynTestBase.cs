using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using TerrariaTools.Rules.Dome.Mark;
using TerrariaTools.Rules.Dome;
using TerrariaTools.Dome.Tests.Scenarios;

namespace TerrariaTools.Dome.Tests.Infrastructure
{
    /// <summary>
    /// Roslyn 测试基类，提供编译环境模拟
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
            var systemRuntime = Path.Combine(runtimePath, "System.Runtime.dll");
            if (File.Exists(systemRuntime)) yield return MetadataReference.CreateFromFile(systemRuntime);

            var systemConsole = Path.Combine(runtimePath, "System.Console.dll");
            if (File.Exists(systemConsole)) yield return MetadataReference.CreateFromFile(systemConsole);

            var systemCollections = Path.Combine(runtimePath, "System.Collections.dll");
            if (File.Exists(systemCollections)) yield return MetadataReference.CreateFromFile(systemCollections);

            var netstandard = Path.Combine(runtimePath, "netstandard.dll");
            if (File.Exists(netstandard)) yield return MetadataReference.CreateFromFile(netstandard);

            var mscorlib = Path.Combine(runtimePath, "mscorlib.dll");
            if (File.Exists(mscorlib)) yield return MetadataReference.CreateFromFile(mscorlib);
        }

        /// <summary>
        /// 创建一个包含多个文件的解决方案
        /// </summary>
        protected static Task<(AdhocWorkspace workspace, Solution solution, Project project)> CreateSolutionAsync(params (string name, string content)[] files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));

            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "TestProject", "TestAssembly", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithMetadataReferences(GetStandardReferences());

            var project = workspace.AddProject(projectInfo);

            foreach (var file in files)
            {
                var sourceText = SourceText.From(file.content, Encoding.UTF8);
                workspace.AddDocument(projectId, file.name, sourceText);
            }

            return Task.FromResult((workspace, workspace.CurrentSolution, workspace.CurrentSolution.GetProject(projectId)!));
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
            // Use local ScenarioManager
            var source = ScenarioManager.GetScenario(scenarioPath)
                ?? throw new ArgumentException($"Scenario not found: {scenarioPath}");
            return new ScenarioContext(this, source, scenarioPath);
        }

        /// <summary>
        /// 运行管道并返回结果字符串（用于 Scenario 内部调用）。
        /// 这里使用 RuleEngine 模拟 Pipeline 行为。
        /// </summary>
        public async Task<string> ExecutePipelineAsync(string source, Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync();

            // 1. Mark nodes
            var targets = selectNodes(root);
            var markedRoot = root.ReplaceNodes(targets, (original, rewritten) =>
                rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Action=Delete;Reason=Test")));

            // 2. Run RuleEngine
            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            return result.NormalizeWhitespace().ToFullString();
        }

        /// <summary>
        /// 运行 RuleEngine 并返回结果根节点（用于更详细的断言）。
        /// </summary>
        public async Task<SyntaxNode> ExecuteRuleEngineAsync(string source, Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync();

            var targets = selectNodes(root);
            var markedRoot = root.ReplaceNodes(targets, (original, rewritten) =>
                rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Action=Delete;Reason=Test")));

            var engine = new RuleEngine();
            return engine.Apply(markedRoot);
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
        /// 执行 RuleEngine 并验证结果根节点是否满足断言。
        /// </summary>
        public async Task ThenVerify(Action<SyntaxNode> assertion)
        {
            var result = await _testBase.ExecuteRuleEngineAsync(_source, _selector);
            assertion(result);
        }
    }
}
