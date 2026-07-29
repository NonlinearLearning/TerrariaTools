using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLISSN.Core.Analysis;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Core.Decision;

/// 汇集 Proposal 阶段共用的冲突节点、标记筛选与决策构造约定。
public static class DeleteSObjectProposalHelpers
{
    public static readonly IReadOnlyList<SyntaxKind> LogicalConflictNodeKinds =
      new[]
      {
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression
      };

    public static readonly IReadOnlyList<SyntaxKind> IfConflictNodeKinds =
      new[]
      {
        SyntaxKind.IfStatement
      };

    public static readonly IReadOnlyList<SyntaxKind> ControlConflictNodeKinds =
      new[]
      {
        SyntaxKind.ForStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.ReturnStatement
      };

    public static readonly IReadOnlyList<SyntaxKind> DefaultConflictNodeKinds =
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

    public static readonly IReadOnlyList<SyntaxKind> MergeableNodeKinds =
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

    public static IReadOnlyList<(MarkRecord Mark, MarkRecord SourceMark)> EnumerateDerivedMarks(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        return propagatedMarks
          .Select(mark => (
            Mark: mark.Mark,
            SourceMark: mark.SourceMark,
            IsSymbolReference: IsSymbolReferencePropagation(mark),
            IsStructuredPayload: IsStructuredPropagationPayload(mark.Payload)))
          .Concat(liftedMarks.Select(mark => (
            Mark: mark.Mark,
            SourceMark: mark.SourceMark,
            IsSymbolReference: false,
            IsStructuredPayload: false)))
          .Where(mark => !mark.IsSymbolReference && !mark.IsStructuredPayload)
          .Select(mark => (mark.Mark, mark.SourceMark))
          .DistinctBy(mark => BuildNodeKey(mark.Mark.SyntaxNode))
          .OrderBy(mark => mark.Mark.SyntaxNode.SpanStart)
          .ThenByDescending(mark => mark.Mark.SyntaxNode.Span.Length)
          .ToList();
    }

    public static IReadOnlyList<(MarkRecord Mark, MarkRecord SourceMark)> EnumerateActiveDerivedMarks(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        var derivedMarks = EnumerateDerivedMarks(propagatedMarks, liftedMarks);
        var coveredDerivedKeys = BuildCoveredSeedKeys(
          derivedMarks.Select(item => item.Mark).ToList(),
          derivedMarks.Select(item => item.Mark).ToList());
        return derivedMarks
          .Where(item => !coveredDerivedKeys.Contains(BuildNodeKey(item.Mark.SyntaxNode)))
          .ToList();
    }

    // 返回尚未被传播或提升宿主覆盖的 seed mark，供默认删除规则兜底消费。
    public static IEnumerable<MarkRecord> EnumerateUncoveredSeedMarks(IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
      var derivedMarks = propagatedMarks
        .Select(mark => mark.Mark)
        .Concat(liftedMarks.Select(mark => mark.Mark))
        .Concat(GetSpecializedPayloadHosts(propagatedMarks))
        .DistinctBy(mark => BuildNodeKey(mark.SyntaxNode))
        .ToList();
        var coveredSeedKeys = BuildCoveredSeedKeys(seedMarks, derivedMarks);

        foreach (var seedMark in seedMarks)
        {
            if (coveredSeedKeys.Contains(BuildNodeKey(seedMark.SyntaxNode)))
            {
                continue;
            }

            yield return seedMark;
      }
    }

    // 专门 Proposal 消费的结构化 payload 也覆盖其子 seed，不能再落入默认删除。
    private static IEnumerable<MarkRecord> GetSpecializedPayloadHosts(
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
      foreach (var propagatedMark in propagatedMarks)
      {
        switch (propagatedMark.Payload)
        {
          case LogicalHostPayload logicalHost:
            yield return propagatedMark.Mark with { SyntaxNode = logicalHost.Host };
            break;
          case IfStructureCompletionPayload ifStructure:
            yield return propagatedMark.Mark with { SyntaxNode = ifStructure.AnchorIf };
            break;
        }
      }
    }

    // 识别来自局部定义符号引用传播的 mark，避免把它们当成原始结构事实再次处理。
    public static bool IsSymbolReferencePropagation(PropagatedMarkRecord propagatedMark)
    {
        return propagatedMark.Mark.Reason.StartsWith(
          "Symbol reference ",
          StringComparison.Ordinal);
    }

    // 提取唯一的逻辑宿主 payload，并按源码顺序交给逻辑替换提案规则。
    public static IEnumerable<LogicalHostPayload> EnumerateLogicalHostPayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return propagatedMarks
          .Where(mark => mark.Payload is LogicalHostPayload)
          .Select(mark => (LogicalHostPayload)mark.Payload!)
          .DistinctBy(payload => BuildNodeKey(payload.Host))
          .OrderBy(payload => payload.Host.SpanStart)
          .ThenByDescending(payload => payload.Host.Span.Length);
    }

    // 提取唯一的 if 结构完成态 payload，并按决策节点顺序交给 if 提案规则。
    public static IEnumerable<IfStructureCompletionPayload> EnumerateIfStructureCompletionPayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return propagatedMarks
          .Where(mark => mark.Payload is IfStructureCompletionPayload)
          .Select(mark => (IfStructureCompletionPayload)mark.Payload!)
          .DistinctBy(payload => BuildNodeKey(GetIfStructureDecisionNode(payload)))
          .OrderBy(payload => GetIfStructureDecisionNode(payload).SpanStart)
          .ThenByDescending(payload => GetIfStructureDecisionNode(payload).Span.Length);
    }

    // 计算已经被更大 propagated mark 覆盖的 seed 节点键，避免默认删除重复落在子节点上。
    public static HashSet<(int Start, int Length, int RawKind)> BuildCoveredSeedKeys(IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<MarkRecord> propagatedMarks)
    {
        var coveredSeedKeys = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            if (propagatedMarks.Any(propagatedMark =>
                  !ReferenceEquals(propagatedMark.SyntaxNode, seedMark.SyntaxNode) &&
                  propagatedMark.SyntaxNode.Span.Contains(seedMark.SyntaxNode.Span)))
            {
                coveredSeedKeys.Add(BuildNodeKey(seedMark.SyntaxNode));
            }
        }

        return coveredSeedKeys;
    }

    // 按宿主的逻辑运算符把保留操作数重新拼回替换表达式。
    public static ExpressionSyntax? BuildLogicalReplacement(LogicalHostPayload payload)
    {
        if (payload.RemovableOperands.Count == 0 || payload.SurvivorOperands.Count == 0)
        {
            return null;
        }

        var survivors = payload.SurvivorOperands
          .Select(operand => operand.WithoutTrivia())
          .ToList();
        if (survivors.Count == 0)
        {
            return null;
        }

        var replacement = survivors[0];
        for (var index = 1; index < survivors.Count; index++)
        {
            replacement = SyntaxFactory.BinaryExpression(
              payload.Host.Kind(),
              replacement,
              survivors[index]);
        }

        return replacement;
    }

    // 为逻辑表达式规约生成带锚点、替换片段和关系边的 Replace 决策。
    public static DecisionUnit CreateLogicalReplaceDecision(string ruleId, BinaryExpressionSyntax anchorNode, ExpressionSyntax replacementNode)
    {
        var anchorFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(anchorNode),
          anchorNode,
          "anchor",
          DecisionActionKind.Replace);
        var replacementFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(replacementNode),
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: $"Reduced {anchorNode.Kind()} to the surviving operand.",
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation(
              NLCPGDecisionRelationKind.ReducedTo,
              anchorFragment,
              replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: $"Reduced {anchorNode.Kind()} to the surviving operand.");
    }

    // 按 if 完成态 payload 生成对应的删除或替换决策，并返回本次消费掉的结构节点。
    public static bool TryBuildIfStructureDecisionFromMark(string ruleId, IfStructureCompletionPayload payload, out DecisionUnit? decision, out IReadOnlyList<SyntaxNode> consumedNodes)
    {
        decision = null;
        consumedNodes = BuildIfStructureConsumedNodes(payload);

        switch (payload.Kind)
        {
            case IfStructureCompletionKind.ReplaceIfWithElseIfTail:
                if (payload.TailNode is not IfStatementSyntax elseIfTail)
                {
                    return false;
                }

                decision = CreateStatementReplaceDecision(
                  ruleId,
                  payload.AnchorIf,
                  elseIfTail,
                  "If section is fully marked; replace it with the remaining elseif branch.");
                return true;
            case IfStructureCompletionKind.DeleteWholeIf:
                decision = DeleteDecisionFactory.CreateDeleteDecision(
                  ruleId,
                  payload.AnchorIf,
                  "If/else structure is fully marked; delete the whole if statement.");
                return true;
            case IfStructureCompletionKind.ReplaceOwningElseWithElseTail:
                if (payload.ParentElseClause is null ||
                    payload.TailNode is not ElseClauseSyntax elseTail)
                {
                    return false;
                }

                decision = CreateElseClauseReplaceDecision(
                  ruleId,
                  payload.ParentElseClause,
                  SyntaxFactory.ElseClause(elseTail.Statement.WithoutTrivia()),
                  "Else-if section is fully marked; collapse its owning else to the remaining else branch.");
                return true;
            case IfStructureCompletionKind.ReplaceIfWithElseTail:
                if (payload.TailNode is not ElseClauseSyntax tailElse)
                {
                    return false;
                }

                decision = CreateStatementReplaceDecision(
                  ruleId,
                  payload.AnchorIf,
                  tailElse.Statement,
                  "If section is fully marked; replace it with the remaining else branch.");
                return true;
            case IfStructureCompletionKind.DeleteOwningElseClause:
                if (payload.ParentElseClause is null)
                {
                    return false;
                }

                decision = DeleteDecisionFactory.CreateDeleteDecision(
                  ruleId,
                  payload.ParentElseClause,
                  "Else-if section is fully marked and has no remaining tail; remove owning else clause.",
                  payload.AnchorIf,
                  conflictKey: DecisionCpgFactory.BuildNodeKey(payload.AnchorIf));
                return true;
            default:
                return false;
        }
    }

    // 为普通语句宿主生成 Replace 决策，供 if 结构和局部函数等规则复用。
    public static DecisionUnit CreateStatementReplaceDecision(string ruleId, StatementSyntax anchorNode, StatementSyntax replacementNode, string reason)
    {
        var anchorFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(anchorNode),
          anchorNode,
          "anchor",
          DecisionActionKind.Replace);
        var replacementFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(replacementNode),
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: reason,
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation(
              NLCPGDecisionRelationKind.ReducedTo,
              anchorFragment,
              replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    // 为 else 子句生成 Replace 决策，保留父结构上的冲突键与语法绑定。
    public static DecisionUnit CreateElseClauseReplaceDecision(string ruleId, ElseClauseSyntax anchorNode, ElseClauseSyntax replacementNode, string reason)
    {
        var anchorFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(anchorNode),
          anchorNode,
          "anchor",
          DecisionActionKind.Replace);
        var replacementFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(replacementNode),
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: reason,
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation(
              NLCPGDecisionRelationKind.ReducedTo,
              anchorFragment,
              replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    // 用 span 与 raw kind 生成稳定节点键，供传播、提升和提案跨阶段去重。
    public static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }

    private static string BuildFragmentId(SyntaxNode node)
    {
        return $"frag:{DecisionCpgFactory.BuildNodeKey(node)}";
    }

    // 把任意命中节点解析回所属 if 结构分析结果，供完成态判断与改写决策复用。
    public static bool TryResolveMarkedIfStructure(RuleContext context, SyntaxNode markedNode, IfStructureAnalyzer ifStructureAnalyzer, out IfStructureAnalysis? ifAnalysis)
    {
        ifAnalysis = null;
        if (markedNode is IfStatementSyntax ifStatement)
        {
            ifAnalysis = context.AnalyzeIfStructure(ifStatement);
            return true;
        }

        if (markedNode is ElseClauseSyntax elseClause &&
            elseClause.Statement is IfStatementSyntax elseIfStatement)
        {
            ifAnalysis = context.AnalyzeIfStructure(elseIfStatement);
            return true;
        }

        if (markedNode is not ExpressionSyntax expression ||
            !context.TryFindContainingIf(expression, out ifAnalysis) ||
            ifAnalysis is null)
        {
            return false;
        }

        return true;
    }

    // 仅在当前命中足以判定整段 if 结构的删除或折叠结果时，构造结构化完成态 payload。
    public static IfStructureCompletionPayload? TryBuildIfStructureCompletionPayload(RuleContext context, SyntaxNode markedNode)
    {
        if (!TryResolveMarkedIfStructure(
              context,
              markedNode,
              new IfStructureAnalyzer(),
              out var ifAnalysis))
        {
            return null;
        }

        if (ifAnalysis!.TailSection is not null)
        {
            if (ifAnalysis.TailSection.Kind == IfSectionKind.ElseIf)
            {
                return new IfStructureCompletionPayload(
                  ifAnalysis.AnchorIf,
                  ifAnalysis.ParentElseClause,
                  ifAnalysis.TailSection.Node,
                  IfStructureCompletionKind.ReplaceIfWithElseIfTail);
            }

            if (ifAnalysis.AnchorVariant == IfStructureVariant.HeadIf)
            {
                return new IfStructureCompletionPayload(
                  ifAnalysis.AnchorIf,
                  ifAnalysis.ParentElseClause,
                  ifAnalysis.TailSection.Node,
                  IfStructureCompletionKind.DeleteWholeIf);
            }

            if (ifAnalysis.ParentElseClause is not null)
            {
                return new IfStructureCompletionPayload(
                  ifAnalysis.AnchorIf,
                  ifAnalysis.ParentElseClause,
                  ifAnalysis.TailSection.Node,
                  IfStructureCompletionKind.ReplaceOwningElseWithElseTail);
            }

            return new IfStructureCompletionPayload(
              ifAnalysis.AnchorIf,
              ifAnalysis.ParentElseClause,
              ifAnalysis.TailSection.Node,
              IfStructureCompletionKind.ReplaceIfWithElseTail);
        }

        if (ifAnalysis.AnchorVariant == IfStructureVariant.ElseIf &&
            ifAnalysis.ParentElseClause is not null)
        {
            return new IfStructureCompletionPayload(
              ifAnalysis.AnchorIf,
              ifAnalysis.ParentElseClause,
              null,
              IfStructureCompletionKind.DeleteOwningElseClause);
        }

        if (ifAnalysis.AnchorVariant == IfStructureVariant.HeadIf &&
            markedNode is ExpressionSyntax expression &&
            IsConditionEquivalent(expression, ifAnalysis.AnchorIf.Condition))
        {
            return new IfStructureCompletionPayload(
              ifAnalysis.AnchorIf,
              ifAnalysis.ParentElseClause,
              null,
              IfStructureCompletionKind.DeleteWholeIf);
        }

        return null;
    }

    // 返回当前 if 完成态真正参与冲突检测和决策去重的结构节点。
    public static SyntaxNode GetIfStructureDecisionNode(IfStructureCompletionPayload payload)
    {
        return payload.Kind switch
        {
            IfStructureCompletionKind.ReplaceOwningElseWithElseTail or
            IfStructureCompletionKind.DeleteOwningElseClause => payload.ParentElseClause is not null
              ? payload.ParentElseClause
              : payload.AnchorIf,
            _ => payload.AnchorIf
        };
    }

    private static IReadOnlyList<SyntaxNode> BuildIfStructureConsumedNodes(IfStructureCompletionPayload payload)
    {
        var nodes = new List<SyntaxNode> { payload.AnchorIf };
        if (payload.ParentElseClause is not null)
        {
            nodes.Add(payload.ParentElseClause);
        }

        if (payload.TailNode is not null)
        {
            nodes.Add(payload.TailNode);
            if (payload.TailNode is ElseClauseSyntax elseClause)
            {
                nodes.Add(elseClause.Statement);
            }
        }

        return nodes
          .DistinctBy(BuildNodeKey)
          .ToList();
    }

    private static bool IsConditionEquivalent(ExpressionSyntax expression, ExpressionSyntax condition)
    {
        var currentExpression = UnwrapParenthesizedExpression(expression);
        var currentCondition = UnwrapParenthesizedExpression(condition);
        return currentExpression.Span.Equals(currentCondition.Span);
    }

    private static ExpressionSyntax UnwrapParenthesizedExpression(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            current = parenthesizedExpression.Expression;
        }

        return current;
    }

    private static bool IsStructuredPropagationPayload(object? payload)
    {
        return payload is MethodParameterUsagePayload or
          LogicalHostPayload or
          IfStructureCompletionPayload;
    }
}
