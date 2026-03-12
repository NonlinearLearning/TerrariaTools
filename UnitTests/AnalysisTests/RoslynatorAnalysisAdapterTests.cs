using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis;

namespace TerrariaTools.UnitTests.AnalysisTests
{
    public class RoslynatorAnalysisAdapterTests
    {
        [Fact]
        public void AnalyzeComplexity_ShouldReportDecisionMetrics()
        {
            var source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.RoslynatorAnalysisAdapterTests_Source_1;

            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var adapter = new RoslynatorAnalysisAdapter();

            var result = adapter.AnalyzeComplexity(method);

            Assert.True(result.CyclomaticComplexity >= 5);
            Assert.True(result.DecisionPointCount >= 4);
            Assert.Equal("RoslynatorAdapter", result.Provider);
        }

        [Fact]
        public void SuggestRefactorings_ShouldReturnActionableSuggestions()
        {
            var source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.RoslynatorAnalysisAdapterTests_Source_1;

            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "M");
            var adapter = new RoslynatorAnalysisAdapter();

            var suggestions = adapter.SuggestRefactorings(method);

            Assert.Contains(suggestions, s => s.Id == "UseSwitchExpression");
            Assert.Contains(suggestions, s => s.Id == "InlineMethod");
        }
    }
}

