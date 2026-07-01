using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 调用、对象创建、成员访问和索引访问结构分析结果。
/// </summary>
public sealed record CallAndAccessStructureAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析最常见的调用和访问结构，保留调用目标、接收者、参数列表和访问名。
/// </summary>
public sealed class CallAndAccessStructureAnalyzer
{
    /// <summary>
    /// 根据表达式实际 Roslyn 节点类型，返回该调用或访问结构的关键语法节点。
    /// </summary>
    public CallAndAccessStructureAnalysis Analyze(ExpressionSyntax root, CpgAnalysisContext context)
    {
        var affectedNodes = root switch
        {
            InvocationExpressionSyntax invocation => AnalyzeInvocation(invocation),
            ObjectCreationExpressionSyntax objectCreation => AnalyzeObjectCreation(objectCreation),
            ImplicitObjectCreationExpressionSyntax objectCreation => AnalyzeImplicitObjectCreation(objectCreation),
            MemberAccessExpressionSyntax memberAccess => AnalyzeMemberAccess(memberAccess),
            MemberBindingExpressionSyntax memberBinding => AnalyzeMemberBinding(memberBinding),
            ElementAccessExpressionSyntax elementAccess => AnalyzeElementAccess(elementAccess),
            ConditionalAccessExpressionSyntax conditionalAccess => AnalyzeConditionalAccess(conditionalAccess),
            _ => throw new ArgumentException("Unsupported call or access syntax node.", nameof(root))
        };

        return new CallAndAccessStructureAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(root, affectedNodes));
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeInvocation(InvocationExpressionSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Expression,
            root.ArgumentList
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeObjectCreation(ObjectCreationExpressionSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.Type };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ArgumentList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeImplicitObjectCreation(
        ImplicitObjectCreationExpressionSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.ArgumentList };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeMemberAccess(MemberAccessExpressionSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Expression,
            root.Name
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeMemberBinding(MemberBindingExpressionSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Name
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeElementAccess(ElementAccessExpressionSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Expression,
            root.ArgumentList
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeConditionalAccess(ConditionalAccessExpressionSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Expression,
            root.WhenNotNull
        };
    }
}
