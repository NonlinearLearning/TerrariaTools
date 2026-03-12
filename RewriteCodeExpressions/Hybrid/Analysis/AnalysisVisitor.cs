using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// Pass 1 分析 Visitor，只读遍历语法树并构建作用域信息。
/// </summary>
public sealed class AnalysisVisitor : CSharpSyntaxWalker
{
    private readonly RewriteContext _context;
    private readonly ScopeBuilder _scopeBuilder;
    private readonly DefUseAnalyzer _defUseAnalyzer;

    public AnalysisVisitor(RewriteContext context)
        : base()
    {
        _context = context;
        _scopeBuilder = new ScopeBuilder(context);
        _defUseAnalyzer = new DefUseAnalyzer(context);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        _scopeBuilder.Enter(node);
        foreach (var parameter in node.ParameterList.Parameters)
        {
            _scopeBuilder.Declare(parameter.Identifier.ValueText);
        }

        base.VisitMethodDeclaration(node);
        _scopeBuilder.Exit();
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        _scopeBuilder.Enter(node);
        foreach (var parameter in node.ParameterList.Parameters)
        {
            _scopeBuilder.Declare(parameter.Identifier.ValueText);
        }

        base.VisitLocalFunctionStatement(node);
        _scopeBuilder.Exit();
    }

    public override void VisitBlock(BlockSyntax node)
    {
        _scopeBuilder.Enter(node);
        base.VisitBlock(node);
        _scopeBuilder.Exit();
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        _scopeBuilder.Enter(node);
        _scopeBuilder.Declare(node.Parameter.Identifier.ValueText);
        base.VisitSimpleLambdaExpression(node);
        _scopeBuilder.Exit();
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        _scopeBuilder.Enter(node);
        foreach (var parameter in node.ParameterList.Parameters)
        {
            _scopeBuilder.Declare(parameter.Identifier.ValueText);
        }

        base.VisitParenthesizedLambdaExpression(node);
        _scopeBuilder.Exit();
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        _scopeBuilder.Declare(node.Identifier.ValueText);
        _defUseAnalyzer.OnVariableDeclarator(node);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitCatchDeclaration(CatchDeclarationSyntax node)
    {
        if (node.Identifier != default)
        {
            _scopeBuilder.Declare(node.Identifier.ValueText);
        }

        base.VisitCatchDeclaration(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        _defUseAnalyzer.OnParameter(node);
        base.VisitParameter(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        _defUseAnalyzer.OnIdentifierName(node);
        base.VisitIdentifierName(node);
    }

    public void Complete()
    {
        _defUseAnalyzer.PublishToContext();
    }

    public void CollectMruPlan(SyntaxNode root, RuleEngine ruleEngine)
    {
        var collector = new MruPlanningVisitor(ruleEngine, _context);
        collector.VisitNode(root);
        _context.SetState(AnalysisStateKeys.MruPlanItems, collector.Items);
    }

    private sealed class MruPlanningVisitor
    {
        private readonly RuleEngine _ruleEngine;
        private readonly RewriteContext _context;

        public MruPlanningVisitor(RuleEngine ruleEngine, RewriteContext context)
        {
            _ruleEngine = ruleEngine;
            _context = context;
        }

        public List<RewritePlanItem> Items { get; } = new();

        public void VisitNode(SyntaxNode? node)
        {
            if (node is null)
            {
                return;
            }

            if (IsMruCandidate(node))
            {
                var matched = _ruleEngine.FindMatchingRule(node, _context);
                if (matched is not null)
                {
                    Items.Add(new RewritePlanItem(node, matched));
                    // MRU hit: stop default rule matching in this subtree.
                    return;
                }
            }

            foreach (var child in node.ChildNodes())
            {
                VisitNode(child);
            }
        }

        private static bool IsMruCandidate(SyntaxNode node)
        {
            return node is StatementSyntax
                || node is ExpressionSyntax
                || node is MemberDeclarationSyntax
                || node is SwitchExpressionArmSyntax
                || node is VariableDeclaratorSyntax
                || node is VariableDeclarationSyntax
                || node is EqualsValueClauseSyntax
                || node is ArgumentListSyntax
                || node is BracketedArgumentListSyntax
                || node is AttributeSyntax
                || node is AttributeListSyntax
                || node is UsingDirectiveSyntax
                || node is SingleVariableDesignationSyntax;
        }
    }
}
