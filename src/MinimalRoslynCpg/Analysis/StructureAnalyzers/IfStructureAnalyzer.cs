using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 区分头部 if 与 else-if 复用出来的 if 节点。
/// </summary>
public enum IfStructureVariant
{
    HeadIf = 0,
    ElseIf = 1
}

/// <summary>
/// 标记 if / else-if / else 链中某一段的结构角色。
/// </summary>
public enum IfSectionKind
{
    If = 0,
    ElseIf = 1,
    Else = 2
}

/// <summary>
/// 表示一个分支片段及其条件和语句体。
/// </summary>
public sealed record IfSection(
    IfSectionKind Kind,
    SyntaxNode Node,
    ExpressionSyntax? Condition,
    StatementSyntax Statement);

/// <summary>
/// 汇总锚点 `if` 及其直接相邻的尾段结构。
/// </summary>
public sealed record IfStructureAnalysis(
    IfStatementSyntax AnchorIf,
    IfStructureVariant AnchorVariant,
    IfSection AnchorSection,
    IfSection? TailSection,
    ElseClauseSyntax? ParentElseClause,
    IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析 if / else-if / else 链的局部结构。
/// </summary>
public sealed class IfStructureAnalyzer
{
    /// <summary>
    /// 返回锚点 `if` 分支及其可直接到达的尾段。
    /// </summary>
    public IfStructureAnalysis Analyze(IfStatementSyntax root, CpgAnalysisContext context)
    {
        _ = context;

        var anchorVariant = root.Parent is ElseClauseSyntax
            ? IfStructureVariant.ElseIf
            : IfStructureVariant.HeadIf;
        var anchorSection = new IfSection(
            anchorVariant == IfStructureVariant.HeadIf
                ? IfSectionKind.If
                : IfSectionKind.ElseIf,
            root,
            root.Condition,
            root.Statement);
        var tailSection = ResolveTailSection(root.Else);
        var nodes = new List<SyntaxNode>
        {
            root,
            root.Condition,
            root.Statement
        };

        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Parent as ElseClauseSyntax);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Else);
        if (tailSection is not null)
        {
            AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, tailSection.Node);
            AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, tailSection.Condition);
            nodes.Add(tailSection.Statement);
        }

        SyntaxNode analysisRoot = root.Parent as ElseClauseSyntax is { } parentElse
            ? parentElse
            : root;
        return new IfStructureAnalysis(
            root,
            anchorVariant,
            anchorSection,
            tailSection,
            root.Parent as ElseClauseSyntax,
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(
                analysisRoot,
                nodes));
    }

    /// <summary>
    /// 找到仍覆盖目标表达式跨度的最窄 if 条件。
    /// </summary>
    public bool TryFindContainingIf(
        ExpressionSyntax expression,
        CpgAnalysisContext context,
        out IfStructureAnalysis? analysis)
    {
        analysis = expression.AncestorsAndSelf()
            .OfType<IfStatementSyntax>()
            .Where(ifStatement => ifStatement.Condition.Span.Contains(expression.Span))
            .OrderBy(ifStatement => ifStatement.Condition.Span.Length)
            .Select(ifStatement => Analyze(ifStatement, context))
            .FirstOrDefault();
        return analysis is not null;
    }

    private static IfSection? ResolveTailSection(ElseClauseSyntax? elseClause)
    {
        if (elseClause is null)
        {
            return null;
        }

        if (elseClause.Statement is IfStatementSyntax elseIfStatement)
        {
            return new IfSection(
                IfSectionKind.ElseIf,
                elseIfStatement,
                elseIfStatement.Condition,
                elseIfStatement.Statement);
        }

        return new IfSection(
            IfSectionKind.Else,
            elseClause,
            null,
            elseClause.Statement);
    }
}
