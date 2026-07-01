using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 将基础结构 analyzer 的结果投射成复用现有 CPG node/edge kind 的结构视图。
/// </summary>
public sealed class RoslynCpgStructureViewBuilder
{
    /// <summary>
    /// 为一个 Roslyn 语法节点构建局部结构视图。
    /// </summary>
    public RoslynCpgStructureView Build(SyntaxNode root, CpgAnalysisContext context)
    {
        var writer = new RoslynCpgStructureViewWriter(context);
        var rootNode = writer.GetOrCreateNode(root, ResolveRootNodeKind(root), "root");

        switch (root)
        {
            case BinaryExpressionSyntax binaryExpression:
                EmitBinaryExpression(binaryExpression, context, writer, rootNode);
                break;
            case AssignmentExpressionSyntax assignmentExpression:
                EmitAssignmentExpression(assignmentExpression, context, writer, rootNode);
                break;
            case VariableDeclaratorSyntax variableDeclarator when variableDeclarator.Initializer is not null:
                EmitAssignmentDefinition(variableDeclarator, context, writer, rootNode);
                break;
            case StatementSyntax statement when IsLoopStatement(statement):
                EmitLoopStructure(statement, context, writer, rootNode);
                break;
            case SwitchStatementSyntax or SwitchExpressionSyntax:
                EmitSwitchStructure(root, context, writer, rootNode);
                break;
            case ConditionalExpressionSyntax conditionalExpression:
                EmitConditionalExpression(conditionalExpression, context, writer, rootNode);
                break;
            case ExpressionSyntax expression when IsUnaryExpression(expression):
                EmitUnaryExpression(expression, context, writer, rootNode);
                break;
            case ExpressionSyntax expression when IsCallOrAccessExpression(expression):
                EmitCallOrAccessStructure(expression, context, writer, rootNode);
                break;
            default:
                EmitDefinitionStructure(root, context, writer, rootNode);
                break;
        }

        return writer.Build(rootNode);
    }

    private static void EmitBinaryExpression(
        BinaryExpressionSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        var analysis = new BinaryExpressionAnalyzer().Analyze(root, root.Left, context);
        foreach (var node in analysis.AffectedSyntaxTree.Where(node => !ReferenceEquals(node, root)))
        {
            var childNode = writer.GetOrCreateNode(node, ResolveNodeKind(node), ResolveRole(root, node));
            writer.AddEdge(rootNode, childNode, RoslynCpgEdgeKind.OpChild, ResolveRole(root, node));
        }
    }

    private static void EmitAssignmentExpression(
        AssignmentExpressionSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        _ = new AssignmentExpressionAnalyzer().Analyze(root, context);
        writer.AddEdge(rootNode, writer.GetOrCreateNode(root.Left, ResolveNodeKind(root.Left), "target"), RoslynCpgEdgeKind.OpTarget, "target");
        writer.AddEdge(rootNode, writer.GetOrCreateNode(root.Right, ResolveNodeKind(root.Right), "value"), RoslynCpgEdgeKind.OpChild, "value");
        writer.AddEdge(writer.GetOrCreateNode(root.Right, ResolveNodeKind(root.Right), "value"), rootNode, RoslynCpgEdgeKind.DataFlow, "value-to-assignment");
    }

    private static void EmitAssignmentDefinition(
        VariableDeclaratorSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        var analysis = new AssignmentDefinitionAnalyzer().Analyze(root, context);
        foreach (var node in analysis.AffectedSyntaxTree.Where(node => !ReferenceEquals(node, root)))
        {
            writer.AddEdge(rootNode, writer.GetOrCreateNode(node, ResolveNodeKind(node), ResolveRole(root, node)), RoslynCpgEdgeKind.OpChild, ResolveRole(root, node));
        }

        if (root.Initializer is not null)
        {
            var valueNode = writer.GetOrCreateNode(root.Initializer.Value, ResolveNodeKind(root.Initializer.Value), "value");
            writer.AddEdge(valueNode, rootNode, RoslynCpgEdgeKind.DataFlow, "initializer-to-definition");
        }
    }

    private static void EmitLoopStructure(
        StatementSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        var analysis = new LoopStructureAnalyzer().Analyze(root, context);
        foreach (var node in analysis.AffectedSyntaxTree.Where(node => !ReferenceEquals(node, root)))
        {
            writer.AddEdge(rootNode, writer.GetOrCreateNode(node, ResolveNodeKind(node), ResolveRole(root, node)), ResolveLoopEdgeKind(root, node), ResolveRole(root, node));
        }
    }

    private static void EmitSwitchStructure(
        SyntaxNode root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        var analysis = new SwitchStructureAnalyzer().Analyze(root, context);
        foreach (var node in analysis.AffectedSyntaxTree.Where(node => !ReferenceEquals(node, root)))
        {
            writer.AddEdge(rootNode, writer.GetOrCreateNode(node, ResolveNodeKind(node), ResolveRole(root, node)), ResolveSwitchEdgeKind(root, node), ResolveRole(root, node));
        }
    }

    private static void EmitConditionalExpression(
        ConditionalExpressionSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        _ = new ConditionalExpressionAnalyzer().Analyze(root, context);
        writer.AddEdge(rootNode, writer.GetOrCreateNode(root.Condition, ResolveNodeKind(root.Condition), "condition"), RoslynCpgEdgeKind.OpCondition, "condition");
        writer.AddEdge(rootNode, writer.GetOrCreateNode(root.WhenTrue, ResolveNodeKind(root.WhenTrue), "when-true"), RoslynCpgEdgeKind.OpWhenTrue, "when-true");
        writer.AddEdge(rootNode, writer.GetOrCreateNode(root.WhenFalse, ResolveNodeKind(root.WhenFalse), "when-false"), RoslynCpgEdgeKind.OpWhenFalse, "when-false");
    }

    private static void EmitUnaryExpression(
        ExpressionSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        var analysis = new UnaryExpressionAnalyzer().Analyze(root, context);
        foreach (var node in analysis.AffectedSyntaxTree.Where(node => !ReferenceEquals(node, root)))
        {
            writer.AddEdge(rootNode, writer.GetOrCreateNode(node, ResolveNodeKind(node), "operand"), RoslynCpgEdgeKind.OpChild, "operand");
        }
    }

    private static void EmitCallOrAccessStructure(
        ExpressionSyntax root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        _ = new CallAndAccessStructureAnalyzer().Analyze(root, context);
        switch (root)
        {
            case InvocationExpressionSyntax invocation:
                writer.AddEdge(rootNode, writer.GetOrCreateNode(invocation.Expression, ResolveNodeKind(invocation.Expression), "callee"), RoslynCpgEdgeKind.OpTarget, "callee");
                writer.AddEdge(rootNode, writer.GetOrCreateNode(invocation.ArgumentList, ResolveNodeKind(invocation.ArgumentList), "arguments"), RoslynCpgEdgeKind.OpArgument, "arguments");
                break;
            case MemberAccessExpressionSyntax memberAccess:
                writer.AddEdge(rootNode, writer.GetOrCreateNode(memberAccess.Expression, ResolveNodeKind(memberAccess.Expression), "receiver"), RoslynCpgEdgeKind.OpInstance, "receiver");
                writer.AddEdge(rootNode, writer.GetOrCreateNode(memberAccess.Name, ResolveNodeKind(memberAccess.Name), "member"), RoslynCpgEdgeKind.AccessesMember, "member");
                break;
            case ElementAccessExpressionSyntax elementAccess:
                writer.AddEdge(rootNode, writer.GetOrCreateNode(elementAccess.Expression, ResolveNodeKind(elementAccess.Expression), "receiver"), RoslynCpgEdgeKind.OpInstance, "receiver");
                writer.AddEdge(rootNode, writer.GetOrCreateNode(elementAccess.ArgumentList, ResolveNodeKind(elementAccess.ArgumentList), "arguments"), RoslynCpgEdgeKind.OpArgument, "arguments");
                break;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                writer.AddEdge(rootNode, writer.GetOrCreateNode(conditionalAccess.Expression, ResolveNodeKind(conditionalAccess.Expression), "receiver"), RoslynCpgEdgeKind.OpInstance, "receiver");
                writer.AddEdge(rootNode, writer.GetOrCreateNode(conditionalAccess.WhenNotNull, ResolveNodeKind(conditionalAccess.WhenNotNull), "when-not-null"), RoslynCpgEdgeKind.OpWhenTrue, "when-not-null");
                break;
        }
    }

    private static void EmitDefinitionStructure(
        SyntaxNode root,
        CpgAnalysisContext context,
        RoslynCpgStructureViewWriter writer,
        RoslynCpgNode rootNode)
    {
        var analysis = new DefinitionStructureAnalyzer().Analyze(root, context);
        foreach (var node in analysis.AffectedSyntaxTree.Where(node => !ReferenceEquals(node, root)))
        {
            writer.AddEdge(rootNode, writer.GetOrCreateNode(node, ResolveNodeKind(node), ResolveRole(root, node)), RoslynCpgEdgeKind.OpChild, ResolveRole(root, node));
        }
    }

    private static RoslynCpgNodeKind ResolveRootNodeKind(SyntaxNode node)
    {
        return node switch
        {
            BinaryExpressionSyntax => RoslynCpgNodeKind.OpBinary,
            AssignmentExpressionSyntax or VariableDeclaratorSyntax { Initializer: not null } => RoslynCpgNodeKind.OpAssignment,
            InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => RoslynCpgNodeKind.OpInvocation,
            MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax => RoslynCpgNodeKind.MemberAccess,
            ConditionalExpressionSyntax => RoslynCpgNodeKind.OpConditional,
            SwitchStatementSyntax or SwitchExpressionSyntax => RoslynCpgNodeKind.OpSwitch,
            ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax => RoslynCpgNodeKind.OpLoop,
            _ => RoslynCpgNodeKind.SyntaxNode
        };
    }

    private static RoslynCpgNodeKind ResolveNodeKind(SyntaxNode node)
    {
        return node switch
        {
            ArgumentSyntax => RoslynCpgNodeKind.OpArgument,
            BinaryExpressionSyntax => RoslynCpgNodeKind.OpBinary,
            AssignmentExpressionSyntax => RoslynCpgNodeKind.OpAssignment,
            InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => RoslynCpgNodeKind.OpInvocation,
            MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax => RoslynCpgNodeKind.MemberAccess,
            LiteralExpressionSyntax => RoslynCpgNodeKind.OpLiteral,
            ReturnStatementSyntax => RoslynCpgNodeKind.OpReturn,
            _ => RoslynCpgNodeKind.SyntaxNode
        };
    }

    private static string ResolveRole(SyntaxNode root, SyntaxNode node)
    {
        return (root, node) switch
        {
            (AssignmentExpressionSyntax assignment, _) when ReferenceEquals(node, assignment.Left) => "target",
            (AssignmentExpressionSyntax assignment, _) when ReferenceEquals(node, assignment.Right) => "value",
            (ConditionalExpressionSyntax conditional, _) when ReferenceEquals(node, conditional.Condition) => "condition",
            (ConditionalExpressionSyntax conditional, _) when ReferenceEquals(node, conditional.WhenTrue) => "when-true",
            (ConditionalExpressionSyntax conditional, _) when ReferenceEquals(node, conditional.WhenFalse) => "when-false",
            (ForStatementSyntax forStatement, _) when ReferenceEquals(node, forStatement.Condition) => "condition",
            (WhileStatementSyntax whileStatement, _) when ReferenceEquals(node, whileStatement.Condition) => "condition",
            (DoStatementSyntax doStatement, _) when ReferenceEquals(node, doStatement.Condition) => "condition",
            (SwitchStatementSyntax switchStatement, _) when ReferenceEquals(node, switchStatement.Expression) => "condition",
            (SwitchExpressionSyntax switchExpression, _) when ReferenceEquals(node, switchExpression.GoverningExpression) => "condition",
            _ when node is StatementSyntax => "body",
            _ when node is TypeSyntax => "type",
            _ when node is ArgumentListSyntax or BracketedArgumentListSyntax => "arguments",
            _ => "child"
        };
    }

    private static RoslynCpgEdgeKind ResolveLoopEdgeKind(SyntaxNode root, SyntaxNode node)
    {
        var role = ResolveRole(root, node);
        return role switch
        {
            "condition" => RoslynCpgEdgeKind.OpCondition,
            "body" => RoslynCpgEdgeKind.OpBody,
            _ => RoslynCpgEdgeKind.OpChild
        };
    }

    private static RoslynCpgEdgeKind ResolveSwitchEdgeKind(SyntaxNode root, SyntaxNode node)
    {
        var role = ResolveRole(root, node);
        return role == "condition" ? RoslynCpgEdgeKind.OpCondition : RoslynCpgEdgeKind.OpChild;
    }

    private static bool IsLoopStatement(SyntaxNode node)
    {
        return node is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or
            WhileStatementSyntax or DoStatementSyntax;
    }

    private static bool IsUnaryExpression(SyntaxNode node)
    {
        return node is PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or AwaitExpressionSyntax or
            CastExpressionSyntax;
    }

    private static bool IsCallOrAccessExpression(SyntaxNode node)
    {
        return node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax or
            ImplicitObjectCreationExpressionSyntax or MemberAccessExpressionSyntax or
            MemberBindingExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax;
    }
}
