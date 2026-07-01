using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Analysis;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace Rules;

/// <summary>
/// 命中以目标对象名为根的表达式，并向逻辑宿主或语句宿主做最小传播。
/// </summary>
public sealed class DeleteSObjectExpressionRule : RuleDefinition
{
    /// <summary>
    /// 规则稳定标识。
    /// </summary>
    public string RuleId { get; } = "DEL-SOBJ-001";

    /// <summary>
    /// 规则的人类可读名称。
    /// </summary>
    public string Name { get; } = "Match s-rooted expressions";

    /// <summary>
    /// 标记阶段允许产出的语法节点种类。
    /// </summary>
    public IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[]
      {
      SyntaxKind.IdentifierName,
      SyntaxKind.SimpleMemberAccessExpression,
      SyntaxKind.InvocationExpression,
      SyntaxKind.ElementAccessExpression,
      SyntaxKind.ConditionalAccessExpression,
      SyntaxKind.AddExpression,
      };

    /// <summary>
    /// 传播阶段允许产出的语法节点种类。
    /// </summary>
    public IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
      SyntaxKind.LogicalAndExpression,
      SyntaxKind.LogicalOrExpression,
      SyntaxKind.LocalDeclarationStatement,
      SyntaxKind.ExpressionStatement,
      SyntaxKind.IfStatement,
      SyntaxKind.ForStatement,
      SyntaxKind.WhileStatement,
      SyntaxKind.DoStatement,
      SyntaxKind.ReturnStatement
      };

    /// <summary>
    /// 命中后会引发决策冲突的核心依赖语法节点。
    /// </summary>
    public IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
      SyntaxKind.IfStatement,
      SyntaxKind.ForStatement,
      SyntaxKind.WhileStatement,
      SyntaxKind.DoStatement,
      SyntaxKind.SwitchStatement,
      SyntaxKind.TryStatement,
      SyntaxKind.ReturnStatement,
      SyntaxKind.LogicalAndExpression,
      SyntaxKind.LogicalOrExpression,
      SyntaxKind.ConditionalExpression
      };

    /// <summary>
    /// 可视为同一决策域、允许合并处理的语法节点。
    /// </summary>
    public IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
      new[]
      {
      SyntaxKind.IdentifierName,
      SyntaxKind.SimpleMemberAccessExpression,
      SyntaxKind.InvocationExpression,
      SyntaxKind.ElementAccessExpression,
      SyntaxKind.NumericLiteralExpression,
      SyntaxKind.StringLiteralExpression,
      SyntaxKind.TrueLiteralExpression,
      SyntaxKind.FalseLiteralExpression,
      SyntaxKind.NullLiteralExpression,
      SyntaxKind.ParenthesizedExpression,
      SyntaxKind.CastExpression,
      SyntaxKind.AddExpression,
      SyntaxKind.SubtractExpression,
      SyntaxKind.MultiplyExpression,
      SyntaxKind.DivideExpression
      };

    /// <summary>
    /// 查找所有以目标对象名为根的最小表达式命中。
    /// </summary>
    public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        if (!context.TryGetOption("target-name", out var targetName) ||
            string.IsNullOrWhiteSpace(targetName))
        {
            yield break;
        }

        foreach (var expression in RuleAnalysisHelpers.EnumerateAllowedExpressions(
                   root,
                   AllowedMarkNodeKinds,
                   context.AnalysisContext))
        {
            if (!IsRootedAtTarget(context, expression, targetName))
            {
                continue;
            }

            if (HasRootedAncestor(context, expression, targetName))
            {
                continue;
            }

            yield return RuleAnalysisHelpers.CreateMark(
              RuleId,
              expression,
              $"Expression is rooted at target '{targetName}'.");
        }
    }

    /// <summary>
    /// 先提升到逻辑表达式宿主，再提升到语句或控制结构宿主。
    /// </summary>
    public IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            var currentNode = expression;
            var currentDepth = 0;

            var logicalHost = RuleAnalysisHelpers.FindLogicalHost(
              currentNode,
              context.AnalysisContext);
            if (logicalHost is not null && !ReferenceEquals(logicalHost, currentNode))
            {
                currentDepth++;
                currentNode = logicalHost;
                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(RuleId, currentNode, seedMark.Reason),
                  seedMark,
                  currentDepth);
            }

            var structuralHost = RuleAnalysisHelpers.FindStructuralHost(
              currentNode,
              context.AnalysisContext);
            if (structuralHost is null || ReferenceEquals(structuralHost, currentNode))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(RuleId, structuralHost, seedMark.Reason),
              seedMark,
              currentDepth + 1);
        }
    }

    /// <summary>
    /// 直接把标记和传播结果转成最终决策。
    /// </summary>
    public IEnumerable<DecisionUnit> Propose(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        foreach (var seedMark in seedMarks)
        {
            yield return RuleAnalysisHelpers.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }

        foreach (var propagatedMark in propagatedMarks)
        {
            var decision = BuildDecisionFromPropagatedMark(context, propagatedMark);
            if (decision is not null)
            {
                yield return decision;
            }
        }
    }

    /// <summary>
    /// 判断表达式的语义树中是否以目标对象名为根。
    /// </summary>
    private static bool IsRootedAtTarget(RuleContext context, ExpressionSyntax expression, string targetName)
    {
        var operation = context.SemanticModel.GetOperation(expression);
        if (operation is null)
        {
            return false;
        }

        return ReferencesTarget(operation, targetName);
    }

    /// <summary>
    /// 过滤掉已经被更大根表达式覆盖的子表达式命中。
    /// </summary>
    private static bool HasRootedAncestor(RuleContext context, ExpressionSyntax expression, string targetName)
    {
        foreach (var ancestor in expression.Ancestors().OfType<ExpressionSyntax>())
        {
            if (ancestor.IsKind(SyntaxKind.LogicalAndExpression) || ancestor.IsKind(SyntaxKind.LogicalOrExpression))
            {
                continue;
            }

            if (IsRootedAtTarget(context, ancestor, targetName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 递归检查语义操作树中是否引用了目标局部变量或参数。
    /// </summary>
    private static bool ReferencesTarget(IOperation operation, string targetName)
    {
        if (operation is ILocalReferenceOperation localReference && localReference.Local.Name == targetName)
        {
            return true;
        }

        if (operation is IParameterReferenceOperation parameterReference && parameterReference.Parameter.Name == targetName)
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ReferencesTarget(child, targetName))
            {
                return true;
            }
        }

        return false;
    }

    private DecisionUnit? BuildDecisionFromPropagatedMark(
      RuleContext context,
      PropagatedMarkRecord propagatedMark)
    {
        if (propagatedMark.Mark.SyntaxNode is not ExpressionSyntax expression)
        {
            return RuleAnalysisHelpers.CreateDeleteDecision(
              RuleId,
              propagatedMark.Mark.SyntaxNode,
              propagatedMark.Mark.Reason,
              propagatedMark.SourceMark.SyntaxNode);
        }

        if (expression is BinaryExpressionSyntax binaryExpression &&
            (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
             binaryExpression.IsKind(SyntaxKind.LogicalOrExpression)))
        {
            var sourceNode = propagatedMark.SourceMark.SyntaxNode;
            var replacementNode = BuildLogicalReplacement(context, binaryExpression, sourceNode);
            if (replacementNode is not null)
            {
                var anchorFragment = DecisionCpgFactory.CreateFragment(
                  BuildFragmentId(binaryExpression),
                  binaryExpression,
                  "anchor",
                  DecisionActionKind.Replace);
                var replacementFragment = DecisionCpgFactory.CreateFragment(
                  BuildFragmentId(replacementNode),
                  replacementNode.WithoutTrivia(),
                  "replacement",
                  DecisionActionKind.Replace);
                var unitNode = DecisionCpgFactory.CreateUnit(
                  RuleId,
                  DecisionActionKind.Replace,
                  anchorFragment,
                  reason: $"Reduced {binaryExpression.Kind()} to the surviving operand.",
                  conflictKey: DecisionCpgFactory.BuildNodeKey(binaryExpression),
                  mergeKey: DecisionCpgFactory.BuildNodeKey(binaryExpression));

                return new DecisionUnit(
                  RuleId,
                  DecisionActionKind.Replace,
                  unitNode,
                  new[] { anchorFragment, replacementFragment },
                  new[]
                  {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("reduced-to", anchorFragment, replacementFragment)
                  },
                  DecisionCpgFactory.CreateSyntaxBindings(
                    (anchorFragment, binaryExpression),
                    (replacementFragment, replacementNode.WithoutTrivia())),
                  mergeKey: DecisionCpgFactory.BuildNodeKey(binaryExpression),
                  conflictKey: DecisionCpgFactory.BuildNodeKey(binaryExpression),
                  reason: $"Reduced {binaryExpression.Kind()} to the surviving operand.");
            }
        }

        return RuleAnalysisHelpers.CreateDeleteDecision(
          RuleId,
          propagatedMark.Mark.SyntaxNode,
          propagatedMark.Mark.Reason,
          propagatedMark.SourceMark.SyntaxNode);
    }

    private static string BuildFragmentId(SyntaxNode node)
    {
        return $"frag:{DecisionCpgFactory.BuildNodeKey(node)}";
    }

    private static ExpressionSyntax? BuildLogicalReplacement(
      RuleContext context,
      BinaryExpressionSyntax binaryExpression,
      SyntaxNode sourceNode)
    {
        if (sourceNode is not ExpressionSyntax sourceExpression)
        {
            return null;
        }

        var operands = new BinaryExpressionAnalyzer()
          .Analyze(binaryExpression, sourceExpression, context.AnalysisContext)
          .AffectedSyntaxTree
          .OfType<ExpressionSyntax>()
          .Where(node => node is not BinaryExpressionSyntax nested ||
            !nested.IsKind(binaryExpression.Kind()))
          .ToList();
        var removed = false;
        var survivors = new List<ExpressionSyntax>();

        foreach (var operand in operands)
        {
            if (!removed && operand.Span.Contains(sourceNode.Span))
            {
                removed = true;
                continue;
            }

            survivors.Add(operand.WithoutTrivia());
        }

        if (!removed || survivors.Count == operands.Count || survivors.Count == 0)
        {
            return null;
        }

        var replacement = survivors[0];
        for (var index = 1; index < survivors.Count; index++)
        {
            replacement = SyntaxFactory.BinaryExpression(
              binaryExpression.Kind(),
              replacement,
              survivors[index]);
        }

        return replacement;
    }
}
