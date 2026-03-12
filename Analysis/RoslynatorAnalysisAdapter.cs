using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// Roslynator 分析适配器
    /// 封装 Roslynator 的分析功能，提供代码复杂度分析和重构建议。
    /// </summary>
    public sealed class RoslynatorAnalysisAdapter
    {
        public ComplexityAnalysisResult AnalyzeComplexity(SyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var visitor = new ComplexityWalker();
            visitor.Visit(node);

            return new ComplexityAnalysisResult
            {
                CyclomaticComplexity = Math.Max(1, visitor.CyclomaticComplexity),
                CognitiveComplexity = visitor.CognitiveComplexity,
                DecisionPointCount = visitor.DecisionPoints,
                Provider = "RoslynatorAdapter"
            };
        }

        public IReadOnlyList<RefactoringSuggestion> SuggestRefactorings(SyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var suggestions = new List<RefactoringSuggestion>();

            if (node is MethodDeclarationSyntax method)
            {
                var complexity = AnalyzeComplexity(method);
                if (complexity.CyclomaticComplexity >= 10)
                {
                    suggestions.Add(new RefactoringSuggestion("ExtractMethod", "Method complexity is high."));
                }

                var localDeclarations = method.Body?.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Count() ?? 0;
                if (localDeclarations >= 5)
                {
                    suggestions.Add(new RefactoringSuggestion("IntroduceVariable", "Many inline expressions can be named."));
                }
            }

            if (node.DescendantNodes().OfType<IfStatementSyntax>().Any(i => i.Else is IfStatementSyntax))
            {
                suggestions.Add(new RefactoringSuggestion("UseSwitchExpression", "If-else chain can be converted to switch."));
            }

            if (node.DescendantNodes().OfType<InvocationExpressionSyntax>().Any())
            {
                suggestions.Add(new RefactoringSuggestion("InlineMethod", "Candidate invocations may be inlined or simplified."));
            }

            return suggestions;
        }

        private sealed class ComplexityWalker : CSharpSyntaxWalker
        {
            private int _nesting;

            public int CyclomaticComplexity { get; private set; } = 1;
            public int CognitiveComplexity { get; private set; }
            public int DecisionPoints { get; private set; }

            public override void VisitIfStatement(IfStatementSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitIfStatement(node);
                ExitNesting();
            }

            public override void VisitForStatement(ForStatementSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitForStatement(node);
                ExitNesting();
            }

            public override void VisitForEachStatement(ForEachStatementSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitForEachStatement(node);
                ExitNesting();
            }

            public override void VisitWhileStatement(WhileStatementSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitWhileStatement(node);
                ExitNesting();
            }

            public override void VisitDoStatement(DoStatementSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitDoStatement(node);
                ExitNesting();
            }

            public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitConditionalExpression(node);
                ExitNesting();
            }

            public override void VisitSwitchStatement(SwitchStatementSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitSwitchStatement(node);
                ExitNesting();
            }

            public override void VisitCatchClause(CatchClauseSyntax node)
            {
                AddDecision();
                EnterNesting();
                base.VisitCatchClause(node);
                ExitNesting();
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddDecision();
                }

                base.VisitBinaryExpression(node);
            }

            private void AddDecision()
            {
                CyclomaticComplexity++;
                DecisionPoints++;
                CognitiveComplexity += 1 + _nesting;
            }

            private void EnterNesting() => _nesting++;
            private void ExitNesting() => _nesting = Math.Max(0, _nesting - 1);
        }
    }

    public sealed class ComplexityAnalysisResult
    {
        public int CyclomaticComplexity { get; set; }
        public int CognitiveComplexity { get; set; }
        public int DecisionPointCount { get; set; }
        public string Provider { get; set; } = string.Empty;
    }

    public sealed record RefactoringSuggestion(string Id, string Reason);
}
