using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;

namespace TerrariaTools.UnitTests.AnalysisTests
{
    public class CodeDependencyAnalyzerOperatorTests
    {
        private async Task<(CodeDependencyAnalyzer analyzer, ISymbol symbol)> SetupAnalyzerAsync(string source, string typeName)
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId, debugName: "TestFile.cs");

            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
            };

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "TestProject",
                "TestAssembly",
                LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var workspace = new AdhocWorkspace();
            var solution = workspace.CurrentSolution
                .AddProject(projectInfo)
                .AddMetadataReferences(projectId, references)
                .AddDocument(documentId, "TestFile.cs", SourceText.From(source));

            var project = solution.GetProject(projectId);
            var compilation = await project.GetCompilationAsync();
            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new System.Exception("Compilation failed: " + string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }
            var syntaxTree = compilation.SyntaxTrees.First();
            var model = compilation.GetSemanticModel(syntaxTree);

            var symbol = model.GetDeclaredSymbol(syntaxTree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().First(t => t.Identifier.Text == typeName.Split('.').Last()));

            var analyzer = new CodeDependencyAnalyzer(solution);

            return (analyzer, symbol);
        }

        [Fact]
        public async Task Analyze_BinaryOperator_ShouldDetectOperatorDependency()
        {
            var source = SharedScenarios.OperatorDependencyScenarios.BinaryOperator;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var methodSymbol = (symbol as INamedTypeSymbol).GetMembers("Method").First();
            var methodNode = graph.GetOrAddNode(methodSymbol);

            Assert.Contains(methodNode.Dependencies.Keys, n => n.Symbol.Name == "op_Addition" && n.Symbol.ContainingType.Name == "Vector2");
        }

        [Fact]
        public async Task Analyze_Deconstruction_ShouldDetectDeconstructDependency()
        {
            var source = SharedScenarios.OperatorDependencyScenarios.Deconstruction;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var methodSymbol = (symbol as INamedTypeSymbol).GetMembers("Method").First();
            var methodNode = graph.GetOrAddNode(methodSymbol);

            Assert.Contains(methodNode.Dependencies.Keys, n => n.Symbol.Name == "Deconstruct" && n.Symbol.ContainingType.Name == "Point");
        }

        [Fact]
        public async Task Analyze_AssignmentDeconstruction_ShouldDetectDeconstructDependency()
        {
            var source = SharedScenarios.OperatorDependencyScenarios.AssignmentDeconstruction;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var methodSymbol = (symbol as INamedTypeSymbol).GetMembers("Method").First();
            var methodNode = graph.GetOrAddNode(methodSymbol);

            Assert.Contains(methodNode.Dependencies.Keys, n => n.Symbol.Name == "Deconstruct" && n.Symbol.ContainingType.Name == "Point");
        }

        [Fact]
        public async Task Analyze_Deconstruction_Foreach_ShouldDetectDeconstructDependency()
        {
            var source = SharedScenarios.OperatorDependencyScenarios.ForeachDeconstruction;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var methodSymbol = (symbol as INamedTypeSymbol).GetMembers("Method").First();
            var methodNode = graph.GetOrAddNode(methodSymbol);

            Assert.Contains(methodNode.Dependencies.Keys, n => n.Symbol.Name == "Deconstruct" && n.Symbol.ContainingType.Name == "Point");
        }
    }
}
