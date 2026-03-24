using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Dome.Adapters.Rewrite.Roslyn;

public sealed partial class RoslynRewriteExecutor
{
    private static StatementSyntax CommentOut(StatementSyntax statement, ModelPlanning.PlannedChange change)
    {
        var ruleId = change.Reason.RuleId;
        return SyntaxFactory.EmptyStatement()
            .WithLeadingTrivia(
                SyntaxFactory.Comment($"// {ruleId}: {statement.ToString().Trim()}"),
                SyntaxFactory.CarriageReturnLineFeed);
    }

    private static StatementSyntax ReplaceWithDefault(StatementSyntax statement, ModelPlanning.PlannedChange change)
    {
        if (statement is not ExpressionStatementSyntax expressionStatement ||
            expressionStatement.Expression is not AssignmentExpressionSyntax assignment)
        {
            throw new InvalidOperationException($"Action '{ModelPrimitives.PlanActionKind.ReplaceWithDefault}' is unsupported for target '{BuildTargetKey(change.Target, change.Locator)}' because the statement is not an assignment.");
        }

        var payload = change.Action.Payload ?? "default";
        var expression = SyntaxFactory.ParseExpression(payload);
        return statement.ReplaceNode(assignment.Right, expression);
    }

    private static StatementSyntax AddReturn(StatementSyntax statement, ModelPlanning.PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return SyntaxFactory.ParseStatement(returnStatement)
            .WithTriviaFrom(statement);
    }

    private static MethodDeclarationSyntax AddReturn(MethodDeclarationSyntax method, ModelPlanning.PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        var statement = SyntaxFactory.ParseStatement(returnStatement);
        var body = method.Body ?? SyntaxFactory.Block();
        return method.WithBody(body.AddStatements(statement)).WithExpressionBody(null).WithSemicolonToken(default);
    }

    private static ApplyChangeResult ApplyChange(SyntaxNode root, SyntaxNode node, ModelPlanning.PlannedChange change)
    {
        try
        {
            var updatedRoot = change.Target.TargetKind switch
            {
                ModelPrimitives.TargetKind.Class => ApplyClassChange(root, node, change),
                ModelPrimitives.TargetKind.Method => ApplyMethodChange(root, node, change),
                ModelPrimitives.TargetKind.Field => ApplyFieldChange(root, node, change),
                ModelPrimitives.TargetKind.Property => ApplyPropertyChange(root, node, change),
                _ => ApplyStatementChange(root, node, change)
            };

            return ApplyChangeResult.Success(updatedRoot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ApplyChangeResult.Failure(ex.Message);
        }
    }

    private static SyntaxNode ApplyClassChange(SyntaxNode root, SyntaxNode node, ModelPlanning.PlannedChange change)
    {
        if (node is not ClassDeclarationSyntax classNode)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{BuildTargetKey(change.Target, change.Locator)}' because the target is not a class.");
        }

        return change.Action.Kind switch
        {
            ModelPrimitives.PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                     ?? throw new InvalidOperationException($"Action '{ModelPrimitives.PlanActionKind.Delete}' invalidated target '{BuildTargetKey(change.Target, change.Locator)}'."),
            ModelPrimitives.PlanActionKind.ReorderPublicMethods => root.ReplaceNode(node, ReorderPublicMethods(classNode)),
            _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for class target '{BuildTargetKey(change.Target, change.Locator)}'.")
        };
    }

    private static SyntaxNode ApplyMethodChange(SyntaxNode root, SyntaxNode node, ModelPlanning.PlannedChange change)
    {
        return change.Action.Kind switch
        {
            ModelPrimitives.PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                     ?? throw new InvalidOperationException($"Action '{ModelPrimitives.PlanActionKind.Delete}' invalidated target '{BuildTargetKey(change.Target, change.Locator)}'."),
            ModelPrimitives.PlanActionKind.AddReturn when node is MethodDeclarationSyntax methodNode => root.ReplaceNode(node, AddReturn(methodNode, change)),
            ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate when node is MethodDeclarationSyntax methodNode => root.ReplaceNode(node, ChangeVisibilityToPrivate(methodNode)),
            _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for method target '{BuildTargetKey(change.Target, change.Locator)}'.")
        };
    }

    private static SyntaxNode ApplyFieldChange(SyntaxNode root, SyntaxNode node, ModelPlanning.PlannedChange change)
    {
        if (change.Action.Kind != ModelPrimitives.PlanActionKind.Delete || node is not VariableDeclaratorSyntax variable)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for field target '{BuildTargetKey(change.Target, change.Locator)}'.");
        }

        if (variable.Parent is not VariableDeclarationSyntax declaration || declaration.Parent is not FieldDeclarationSyntax fieldDeclaration)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for field target '{BuildTargetKey(change.Target, change.Locator)}' because the target is not a field declarator.");
        }

        if (declaration.Variables.Count == 1)
        {
            return root.RemoveNode(fieldDeclaration, SyntaxRemoveOptions.KeepNoTrivia)
                   ?? throw new InvalidOperationException($"Action '{ModelPrimitives.PlanActionKind.Delete}' invalidated target '{BuildTargetKey(change.Target, change.Locator)}'.");
        }

        return root.ReplaceNode(declaration, declaration.WithVariables(declaration.Variables.Remove(variable)));
    }

    private static SyntaxNode ApplyPropertyChange(SyntaxNode root, SyntaxNode node, ModelPlanning.PlannedChange change)
    {
        if (change.Action.Kind != ModelPrimitives.PlanActionKind.Delete || node is not PropertyDeclarationSyntax)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for property target '{BuildTargetKey(change.Target, change.Locator)}'.");
        }

        return root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
               ?? throw new InvalidOperationException($"Action '{ModelPrimitives.PlanActionKind.Delete}' invalidated target '{BuildTargetKey(change.Target, change.Locator)}'.");
    }

    private static SyntaxNode ApplyStatementChange(SyntaxNode root, SyntaxNode node, ModelPlanning.PlannedChange change)
    {
        if (node is not StatementSyntax statementNode)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{BuildTargetKey(change.Target, change.Locator)}' because the target is not a statement.");
        }

        return change.Action.Kind switch
        {
            ModelPrimitives.PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                     ?? throw new InvalidOperationException($"Action '{ModelPrimitives.PlanActionKind.Delete}' invalidated target '{BuildTargetKey(change.Target, change.Locator)}'."),
            ModelPrimitives.PlanActionKind.CommentOut => root.ReplaceNode(node, CommentOut(statementNode, change)),
            ModelPrimitives.PlanActionKind.ReplaceWithDefault => root.ReplaceNode(node, ReplaceWithDefault(statementNode, change)),
            ModelPrimitives.PlanActionKind.AddReturn => root.ReplaceNode(node, AddReturn(statementNode, change)),
            _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{BuildTargetKey(change.Target, change.Locator)}'.")
        };
    }

    private static MethodDeclarationSyntax ChangeVisibilityToPrivate(MethodDeclarationSyntax method)
    {
        var rewrittenModifiers = new List<SyntaxToken>(method.Modifiers.Count);
        var replacedAccessModifier = false;

        foreach (var modifier in method.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.PublicKeyword) ||
                modifier.IsKind(SyntaxKind.PrivateKeyword) ||
                modifier.IsKind(SyntaxKind.ProtectedKeyword) ||
                modifier.IsKind(SyntaxKind.InternalKeyword))
            {
                if (replacedAccessModifier)
                {
                    continue;
                }

                rewrittenModifiers.Add(
                    SyntaxFactory.Token(
                        modifier.LeadingTrivia,
                        SyntaxKind.PrivateKeyword,
                        SyntaxFactory.TriviaList(SyntaxFactory.Space)));
                replacedAccessModifier = true;
                continue;
            }

            rewrittenModifiers.Add(modifier);
        }

        if (!replacedAccessModifier)
        {
            rewrittenModifiers.Insert(
                0,
                SyntaxFactory.Token(
                    method.GetLeadingTrivia(),
                    SyntaxKind.PrivateKeyword,
                    SyntaxFactory.TriviaList(SyntaxFactory.Space)));
        }

        return method.WithModifiers(SyntaxFactory.TokenList(rewrittenModifiers));
    }

    private static ClassDeclarationSyntax ReorderPublicMethods(ClassDeclarationSyntax classNode)
    {
        var ordinaryMethods = classNode.Members.OfType<MethodDeclarationSyntax>().Where(static method => !method.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword))).ToArray();
        if (ordinaryMethods.Length < 2)
        {
            return classNode;
        }

        var orderedMethods = ordinaryMethods
            .Where(IsPublicMethod)
            .OrderBy(static method => method.Identifier.ValueText, StringComparer.Ordinal)
            .ThenBy(static method => method.ParameterList.Parameters.Count)
            .Concat(ordinaryMethods.Where(static method => !IsPublicMethod(method)))
            .ToArray();

        var originalMethods = new HashSet<MethodDeclarationSyntax>(ordinaryMethods, ReferenceEqualityComparer.Instance);
        var queue = new Queue<MethodDeclarationSyntax>(orderedMethods);
        var rewrittenMembers = classNode.Members.Select(member => member is MethodDeclarationSyntax method && originalMethods.Contains(method) ? (MemberDeclarationSyntax)queue.Dequeue() : member);
        return classNode.WithMembers(SyntaxFactory.List(rewrittenMembers));
    }

    private static bool IsPublicMethod(MethodDeclarationSyntax method)
        => method.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword));

    private static int GetActionPriority(ModelPrimitives.PlanActionKind kind)
        => kind == ModelPrimitives.PlanActionKind.ReorderPublicMethods ? 1 : 0;
}



