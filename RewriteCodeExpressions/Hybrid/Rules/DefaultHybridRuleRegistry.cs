using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

/// <summary>
/// 注册首批 Hybrid 默认规则。
/// </summary>
public static class DefaultHybridRuleRegistry
{
    public static void RegisterCoreRules(RuleEngine ruleEngine)
    {
        ruleEngine.RegisterRule<IfStatementSyntax>(rule => rule
            .When((node, _) => node.Condition is not null)
            .Use<MarkedIfStatementMiddleware>()
            .Use<IfConstantConditionMiddleware>()
            .Use<PreserveTriviaMiddleware<IfStatementSyntax>>()
            .Use<FormatNodeMiddleware<IfStatementSyntax>>()
            .Use<LogMetricMiddleware<IfStatementSyntax>>());

        ruleEngine.RegisterRule<WhileStatementSyntax>(rule => rule
            .When((node, _) => node.Condition is not null)
            .Use<MarkedWhileStatementMiddleware>()
            .Use<WhileConstantConditionMiddleware>()
            .Use<PreserveTriviaMiddleware<WhileStatementSyntax>>()
            .Use<FormatNodeMiddleware<WhileStatementSyntax>>()
            .Use<LogMetricMiddleware<WhileStatementSyntax>>());

        ruleEngine.RegisterRule<ConditionalExpressionSyntax>(rule => rule
            .When((node, _) => node.Condition is not null)
            .Use<MarkedConditionalExpressionMiddleware>()
            .Use<ConditionalConstantConditionMiddleware>()
            .Use<PreserveTriviaMiddleware<ConditionalExpressionSyntax>>()
            .Use<FormatNodeMiddleware<ConditionalExpressionSyntax>>()
            .Use<LogMetricMiddleware<ConditionalExpressionSyntax>>());

        // Expression migration: preserve old Merge/Placeholder semantics for marked expressions.
        ruleEngine.RegisterRule<BinaryExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Left, ctx) || IsMarked(node.Right, ctx))
            .When((node, ctx) => IsMarked(node.Left, ctx) && !IsMarked(node.Right, ctx))
            .Use<MergeRightOperationMiddleware<BinaryExpressionSyntax>>());

        ruleEngine.RegisterRule<BinaryExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Left, ctx) || IsMarked(node.Right, ctx))
            .When((node, ctx) => !IsMarked(node.Left, ctx) && IsMarked(node.Right, ctx))
            .Use<MergeLeftOperationMiddleware<BinaryExpressionSyntax>>());

        ruleEngine.RegisterRule<BinaryExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || (IsMarked(node.Left, ctx) && IsMarked(node.Right, ctx)))
            .Use<ReplaceWithPlaceholderOperationMiddleware<BinaryExpressionSyntax>>());

        ruleEngine.RegisterRule<BinaryExpressionSyntax>(rule => rule
            .When((node, ctx) => !IsMarked(node, ctx) && !IsMarked(node.Left, ctx) && !IsMarked(node.Right, ctx))
            .When((node, _) =>
                node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression) ||
                node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression))
            .WithPriority(300)
            .Use<BooleanBinarySimplificationMiddleware>());

        ruleEngine.RegisterRule<ParenthesizedExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MergeLeftOperationMiddleware<ParenthesizedExpressionSyntax>>());

        ruleEngine.RegisterRule<ParenthesizedExpressionSyntax>(rule => rule
            .When((node, ctx) => !IsMarked(node, ctx) && !IsMarked(node.Expression, ctx))
            .WithPriority(300)
            .Use<BooleanParenthesizedSimplificationMiddleware>());

        ruleEngine.RegisterRule<PostfixUnaryExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Operand, ctx))
            .Use<MergeLeftOperationMiddleware<PostfixUnaryExpressionSyntax>>());

        ruleEngine.RegisterRule<PrefixUnaryExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Operand, ctx))
            .Use<MergeRightOperationMiddleware<PrefixUnaryExpressionSyntax>>());

        ruleEngine.RegisterRule<PrefixUnaryExpressionSyntax>(rule => rule
            .When((node, ctx) => !IsMarked(node, ctx) && !IsMarked(node.Operand, ctx))
            .When((node, _) => node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalNotExpression))
            .WithPriority(300)
            .Use<BooleanUnarySimplificationMiddleware>());

        ruleEngine.RegisterRule<AssignmentExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Right, ctx))
            .Use<MergeRightOperationMiddleware<AssignmentExpressionSyntax>>());

        ruleEngine.RegisterRule<CastExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MergeRightOperationMiddleware<CastExpressionSyntax>>());

        ruleEngine.RegisterRule<AwaitExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MergeRightOperationMiddleware<AwaitExpressionSyntax>>());

        ruleEngine.RegisterRule<ConditionalAccessExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MergeRightOperationMiddleware<ConditionalAccessExpressionSyntax>>());

        ruleEngine.RegisterRule<ArgumentSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<ReplaceWithPlaceholderOperationMiddleware<ArgumentSyntax>>());

        ruleEngine.RegisterRule<ArgumentListSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Arguments.Any(argument => IsMarked(argument, ctx)))
            .Use<MarkedArgumentListMiddleware>());

        ruleEngine.RegisterRule<BracketedArgumentListSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Arguments.Any(argument => IsMarked(argument, ctx)))
            .Use<MarkedBracketedArgumentListMiddleware>());

        ruleEngine.RegisterRule<InvocationExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx) || IsMarked(node.ArgumentList, ctx))
            .Use<MarkedInvocationExpressionMiddleware>());

        ruleEngine.RegisterRule<MemberAccessExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedMemberAccessExpressionMiddleware>());

        ruleEngine.RegisterRule<ElementAccessExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx) || IsMarked(node.ArgumentList, ctx))
            .Use<MarkedElementAccessExpressionMiddleware>());

        ruleEngine.RegisterRule<ObjectCreationExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || (node.ArgumentList is not null && IsMarked(node.ArgumentList, ctx))
                || (node.Initializer is not null && IsMarked(node.Initializer, ctx)))
            .Use<MarkedObjectCreationExpressionMiddleware>());

        ruleEngine.RegisterRule<ImplicitObjectCreationExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || (node.ArgumentList is not null && IsMarked(node.ArgumentList, ctx))
                || (node.Initializer is not null && IsMarked(node.Initializer, ctx)))
            .Use<MarkedImplicitObjectCreationExpressionMiddleware>());

        ruleEngine.RegisterRule<AnonymousObjectCreationExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Initializers.Any(initializer => IsMarked(initializer, ctx)))
            .Use<MarkedAnonymousObjectCreationExpressionMiddleware>());

        ruleEngine.RegisterRule<InitializerExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Expressions.Any(expression => IsMarked(expression, ctx)))
            .Use<MarkedInitializerExpressionMiddleware>());

        ruleEngine.RegisterRule<YieldStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || (node.Expression is not null && IsMarked(node.Expression, ctx)))
            .Use<MarkedYieldStatementMiddleware>());

        ruleEngine.RegisterRule<ArrowExpressionClauseSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedArrowExpressionClauseMiddleware>());

        ruleEngine.RegisterRule<SwitchExpressionArmSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || IsMarked(node.Expression, ctx)
                || (node.WhenClause is not null && IsMarked(node.WhenClause.Condition, ctx)))
            .Use<MarkedSwitchExpressionArmMiddleware>());

        ruleEngine.RegisterRule<SwitchExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.GoverningExpression, ctx))
            .Use<MarkedSwitchExpressionMiddleware>());

        ruleEngine.RegisterRule<InterpolationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedInterpolationMiddleware>());

        ruleEngine.RegisterRule<AnonymousObjectMemberDeclaratorSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedAnonymousObjectMemberDeclaratorMiddleware>());

        ruleEngine.RegisterRule<ThrowStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || (node.Expression is not null && IsMarked(node.Expression, ctx)))
            .Use<MarkedThrowStatementMiddleware>());

        ruleEngine.RegisterRule<LockStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedLockStatementMiddleware>());

        ruleEngine.RegisterRule<UsingStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || (node.Expression is not null && IsMarked(node.Expression, ctx))
                || (node.Declaration is not null && IsMarked(node.Declaration, ctx)))
            .Use<MarkedUsingStatementMiddleware>());

        ruleEngine.RegisterRule<FixedStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Declaration, ctx))
            .Use<MarkedFixedStatementMiddleware>());

        ruleEngine.RegisterRule<ForEachStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedForEachStatementMiddleware>());

        ruleEngine.RegisterRule<DoStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Condition, ctx) || IsMarked(node.Statement, ctx))
            .Use<MarkedDoStatementMiddleware>());

        ruleEngine.RegisterRule<ForStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || (node.Condition is not null && IsMarked(node.Condition, ctx))
                || (node.Declaration is not null && IsMarked(node.Declaration, ctx))
                || IsMarked(node.Statement, ctx))
            .Use<MarkedForStatementMiddleware>());

        ruleEngine.RegisterRule<SwitchStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedSwitchStatementMiddleware>());

        ruleEngine.RegisterRule<WithExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedWithExpressionMiddleware>());

        ruleEngine.RegisterRule<IsPatternExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx) || IsMarked(node.Pattern, ctx))
            .Use<MarkedIsPatternExpressionMiddleware>());

        ruleEngine.RegisterRule<DeclarationPatternSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Type, ctx) || IsMarked(node.Designation, ctx))
            .Use<MarkedDeclarationPatternMiddleware>());

        ruleEngine.RegisterRule<VarPatternSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Designation, ctx))
            .Use<MarkedVarPatternMiddleware>());

        ruleEngine.RegisterRule<RecursivePatternSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || (node.Designation is not null && IsMarked(node.Designation, ctx)))
            .Use<MarkedRecursivePatternMiddleware>());

        ruleEngine.RegisterRule<InterpolatedStringExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Contents.Any(content => IsMarked(content, ctx)))
            .Use<MarkedInterpolatedStringExpressionMiddleware>());

        ruleEngine.RegisterRule<CheckedExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedCheckedExpressionMiddleware>());

        ruleEngine.RegisterRule<RefExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedRefExpressionMiddleware>());

        ruleEngine.RegisterRule<FieldDeclarationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Declaration, ctx))
            .Use<MarkedFieldDeclarationMiddleware>());

        ruleEngine.RegisterRule<PropertyDeclarationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || IsMarked(node.Type, ctx)
                || (node.ExpressionBody is not null && IsMarked(node.ExpressionBody, ctx)))
            .Use<MarkedPropertyDeclarationMiddleware>());

        ruleEngine.RegisterRule<IndexerDeclarationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx)
                || IsMarked(node.Type, ctx)
                || (node.ExpressionBody is not null && IsMarked(node.ExpressionBody, ctx)))
            .Use<MarkedIndexerDeclarationMiddleware>());

        ruleEngine.RegisterRule<EventDeclarationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Type, ctx))
            .Use<MarkedEventDeclarationMiddleware>());

        ruleEngine.RegisterRule<ParameterSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || (node.Type is not null && IsMarked(node.Type, ctx)))
            .Use<MarkedParameterMiddleware>());

        ruleEngine.RegisterRule<AttributeSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Name, ctx))
            .Use<MarkedAttributeMiddleware>());

        ruleEngine.RegisterRule<AttributeListSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Attributes.Any(attribute => IsMarked(attribute, ctx)))
            .Use<MarkedAttributeListMiddleware>());

        ruleEngine.RegisterRule<UsingDirectiveSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx))
            .Use<MarkedUsingDirectiveMiddleware>());

        ruleEngine.RegisterRule<SingleVariableDesignationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx))
            .Use<MarkedSingleVariableDesignationMiddleware>());

        ruleEngine.RegisterRule<IdentifierNameSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx))
            .Use<MarkedIdentifierNameMiddleware>());

        ruleEngine.RegisterRule<ExpressionStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Expression, ctx))
            .Use<MarkedExpressionStatementMiddleware>());

        ruleEngine.RegisterRule<BaseExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx))
            .Use<MarkedBaseExpressionMiddleware>());

        ruleEngine.RegisterRule<ThisExpressionSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx))
            .Use<MarkedThisExpressionMiddleware>());

        ruleEngine.RegisterRule<ReturnStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx))
            .Use<MarkedReturnStatementMiddleware>());

        ruleEngine.RegisterRule<LocalDeclarationStatementSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Declaration, ctx))
            .Use<MarkedLocalDeclarationStatementMiddleware>());

        ruleEngine.RegisterRule<VariableDeclaratorSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || (node.Initializer is not null && IsMarked(node.Initializer.Value, ctx)))
            .Use<MarkedVariableDeclaratorMiddleware>());

        ruleEngine.RegisterRule<VariableDeclarationSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || node.Variables.Any(variable => IsMarked(variable, ctx)))
            .Use<MarkedVariableDeclarationMiddleware>());

        ruleEngine.RegisterRule<EqualsValueClauseSyntax>(rule => rule
            .When((node, ctx) => IsMarked(node, ctx) || IsMarked(node.Value, ctx))
            .Use<MarkedEqualsValueClauseMiddleware>());

        ruleEngine.RegisterRule<MethodDeclarationSyntax>(rule => rule
            .When((node, _) => node.Body is not null || node.ExpressionBody is not null)
            .Use<MarkedMethodDeclarationMiddleware>()
            .Use<MethodActionMiddleware>()
            .Use<NameBasedMethodActionMiddleware>());

        ruleEngine.RegisterRule<ClassDeclarationSyntax>(rule => rule
            .When((_, _) => true)
            .Use<ClassActionMiddleware<ClassDeclarationSyntax>>());

        ruleEngine.RegisterRule<RecordDeclarationSyntax>(rule => rule
            .When((_, _) => true)
            .Use<ClassActionMiddleware<RecordDeclarationSyntax>>());

        ruleEngine.RegisterRule<StructDeclarationSyntax>(rule => rule
            .When((_, _) => true)
            .Use<ClassActionMiddleware<StructDeclarationSyntax>>());

        ruleEngine.RegisterRule<InterfaceDeclarationSyntax>(rule => rule
            .When((_, _) => true)
            .Use<ClassActionMiddleware<InterfaceDeclarationSyntax>>());

        ruleEngine.RegisterRule<EnumDeclarationSyntax>(rule => rule
            .When((_, _) => true)
            .Use<ClassActionMiddleware<EnumDeclarationSyntax>>());

        ruleEngine.RegisterRule<DelegateDeclarationSyntax>(rule => rule
            .When((_, _) => true)
            .Use<MarkedDelegateDeclarationMiddleware>()
            .Use<ClassActionMiddleware<DelegateDeclarationSyntax>>());
    }

    private static bool IsMarked(SyntaxNode node, TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts.IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}
