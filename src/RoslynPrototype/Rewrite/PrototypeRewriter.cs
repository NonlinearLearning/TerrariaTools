using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using RoslynPrototype.Decision;
using Rules;

namespace RoslynPrototype.Rewrite;

public sealed class PrototypeRewriter
{
  private const int NormalizeWhitespaceDepthLimit = 256;
  private readonly DiffBuilder _diffBuilder = new();
  private readonly TextDiffRenderer _textDiffRenderer = new();
  private readonly record struct RewritePlanEntry(TextRewriteOperation Operation, RewriteEdit Edit);
  private readonly record struct TextRewriteOperation(TextSpan Span, string ReplacementText);

  /// <summary>
  /// 根据决策列表对语法树执行删除或替换，并产出最终源码与编辑记录。
  /// </summary>
  public PrototypeRewriteResult Rewrite(SyntaxNode root, SemanticModel semanticModel, IEnumerable<RuleDecision> decisions)
  {
    var source = root.ToFullString();
    var rewritePlan = new List<RewritePlanEntry>();

    foreach (var decision in decisions.OrderByDescending(item => item.FinalNode.Span.Length)) {
      if (decision.Action == DecisionActionKind.Skip) {
        continue;
      }

      if (decision.Action == DecisionActionKind.Replace) {
        if (decision.FinalNode is ExpressionSyntax targetExpression &&
            decision.ReplacementNode is ExpressionSyntax replacementExpression) {
          var rewrittenExpression = CloneExpression(replacementExpression)
            .WithTriviaFrom(targetExpression);
          rewritePlan.Add(CreateRewritePlanEntry(targetExpression, rewrittenExpression));
          continue;
        }

        if (decision.FinalNode is StatementSyntax targetStatement &&
            decision.ReplacementNode is StatementSyntax replacementStatement) {
          var rewrittenStatement = CloneStatement(replacementStatement)
            .WithTriviaFrom(targetStatement);
          var displayEdit = CreateStatementDisplayEdit(targetStatement, rewrittenStatement);
          rewritePlan.Add(CreateRewritePlanEntry(targetStatement, rewrittenStatement, displayEdit));
          continue;
        }

        if (decision.FinalNode is ElseClauseSyntax targetElseClause &&
            decision.ReplacementNode is ElseClauseSyntax replacementElseClause) {
          var rewrittenElseClause = CloneElseClause(replacementElseClause)
            .WithTriviaFrom(targetElseClause);
          SyntaxNode displayOriginalNode = targetElseClause.Parent is IfStatementSyntax owningOriginalIfStatement
            ? owningOriginalIfStatement
            : targetElseClause;
          SyntaxNode displayReplacementNode = targetElseClause.Parent is IfStatementSyntax owningIfStatement
            ? owningIfStatement.WithElse(rewrittenElseClause)
            : rewrittenElseClause;
          rewritePlan.Add(CreateRewritePlanEntry(
            targetElseClause,
            rewrittenElseClause,
            CreateEdit(displayOriginalNode, displayReplacementNode)));
          continue;
        }

        if (decision.FinalNode is MethodDeclarationSyntax targetMethod &&
            decision.ReplacementNode is MethodDeclarationSyntax replacementMethod) {
          var rewrittenMethod = CloneMethod(replacementMethod)
            .WithTriviaFrom(targetMethod);
          rewritePlan.Add(CreateRewritePlanEntry(targetMethod, rewrittenMethod));
          continue;
        }

        if (decision.FinalNode is IndexerDeclarationSyntax targetIndexer &&
            decision.ReplacementNode is IndexerDeclarationSyntax replacementIndexer) {
          var rewrittenIndexer = CloneIndexer(replacementIndexer)
            .WithTriviaFrom(targetIndexer);
          rewritePlan.Add(CreateRewritePlanEntry(targetIndexer, rewrittenIndexer));
          continue;
        }

        if (decision.FinalNode is DelegateDeclarationSyntax targetDelegate &&
            decision.ReplacementNode is DelegateDeclarationSyntax replacementDelegate) {
          var rewrittenDelegate = CloneDelegate(replacementDelegate)
            .WithTriviaFrom(targetDelegate);
          rewritePlan.Add(CreateRewritePlanEntry(targetDelegate, rewrittenDelegate));
          continue;
        }

        throw new InvalidOperationException(
          "Replace decisions must carry expression, statement, else-clause, method, indexer, or delegate nodes.");
      }

      // 表达式删除当前不直接挖空，而是降级成 default(...) 替换，避免生成明显非法的源码。
      if (decision.FinalNode is ExpressionSyntax expression) {
        var replacement = CreateReplacementExpression(expression, semanticModel);
        rewritePlan.Add(CreateRewritePlanEntry(expression, replacement.WithTriviaFrom(expression)));
        continue;
      }

      if (decision.FinalNode is ElseClauseSyntax elseClause &&
          elseClause.Parent is IfStatementSyntax parentIfStatement) {
        var rewrittenIfStatement = parentIfStatement.WithElse(null);
        rewritePlan.Add(CreateRewritePlanEntry(parentIfStatement, rewrittenIfStatement));
        continue;
      }

      if (decision.FinalNode is SimpleBaseTypeSyntax simpleBaseType &&
          simpleBaseType.Parent is BaseListSyntax baseList) {
        var remainingBaseTypes = baseList.Types.Remove(simpleBaseType);
        if (remainingBaseTypes.Count == 0) {
          rewritePlan.Add(CreateDeleteRewritePlanEntry(baseList));
        } else {
          rewritePlan.Add(CreateRewritePlanEntry(baseList, baseList.WithTypes(remainingBaseTypes)));
        }
        continue;
      }

      rewritePlan.Add(CreateDeleteRewritePlanEntry(decision.FinalNode));
    }

    var effectivePlan = BuildEffectiveRewritePlan(rewritePlan);
    var rewrittenSource = FinalizeRewrittenSource(
      ApplyTextRewriteOperations(
        source,
        effectivePlan.Select(entry => entry.Operation).ToList()),
      root.SyntaxTree.FilePath);
    var edits = effectivePlan.Select(entry => entry.Edit).ToList();
    var diff = _diffBuilder.Build(edits);
    return new PrototypeRewriteResult(
      rewrittenSource,
      edits,
      diff);
  }

  /// <summary>
  /// 为表达式删除场景构造一个类型兼容的占位替换表达式。
  /// </summary>
  private static ExpressionSyntax CreateReplacementExpression(ExpressionSyntax expression, SemanticModel semanticModel)
  {
    var typeInfo = semanticModel.GetTypeInfo(expression);
    var targetType = typeInfo.ConvertedType ?? typeInfo.Type;
    if (targetType is null) {
      return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
    }

    return SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(targetType.ToDisplayString()));
  }

  /// <summary>
  /// 为替换型改写生成一条可追踪的编辑记录。
  /// </summary>
  private static RewriteEdit CreateEdit(SyntaxNode originalNode, SyntaxNode replacementNode)
  {
    return new RewriteEdit(
      originalNode.SyntaxTree.FilePath,
      originalNode.Span,
      GetDisplayText(originalNode),
      GetDisplayText(replacementNode));
  }

  private static RewriteEdit CreateStatementDisplayEdit(StatementSyntax originalStatement, StatementSyntax replacementStatement)
  {
    if (originalStatement is IfStatementSyntax &&
        replacementStatement is BlockSyntax replacementBlock &&
        originalStatement.Parent is BlockSyntax parentBlock) {
      var statementIndex = parentBlock.Statements.IndexOf(originalStatement);
      if (statementIndex >= 0) {
        var rewrittenStatements = parentBlock.Statements
          .RemoveAt(statementIndex)
          .InsertRange(statementIndex, replacementBlock.Statements);
        var flattenedBlock = parentBlock.WithStatements(rewrittenStatements);
        return CreateEdit(parentBlock, flattenedBlock);
      }

      var replacedBlock = parentBlock.ReplaceNode(originalStatement, replacementStatement);
      return CreateEdit(parentBlock, replacedBlock);
    }

    return CreateEdit(originalStatement, replacementStatement);
  }

  /// <summary>
  /// 为删除型改写生成一条可追踪的编辑记录。
  /// </summary>
  private static RewriteEdit CreateDeleteEdit(SyntaxNode originalNode)
  {
    return new RewriteEdit(
      originalNode.SyntaxTree.FilePath,
      originalNode.Span,
      GetDisplayText(originalNode),
      string.Empty);
  }

  private static RewritePlanEntry CreateRewritePlanEntry(
    SyntaxNode originalNode,
    SyntaxNode replacementNode,
    RewriteEdit? displayEdit = null)
  {
    return new RewritePlanEntry(
      CreateTextRewriteOperation(originalNode, replacementNode),
      displayEdit ?? CreateEdit(originalNode, replacementNode));
  }

  private static RewritePlanEntry CreateDeleteRewritePlanEntry(SyntaxNode originalNode)
  {
    return new RewritePlanEntry(
      new TextRewriteOperation(originalNode.Span, string.Empty),
      CreateDeleteEdit(originalNode));
  }

  private static TextRewriteOperation CreateTextRewriteOperation(SyntaxNode originalNode, SyntaxNode replacementNode)
  {
    return new TextRewriteOperation(originalNode.Span, replacementNode.ToFullString());
  }

  private static IReadOnlyList<RewritePlanEntry> BuildEffectiveRewritePlan(IReadOnlyList<RewritePlanEntry> rewritePlan)
  {
    if (rewritePlan.Count <= 1)
    {
      return rewritePlan;
    }

    var orderedPlan = rewritePlan
      .OrderByDescending(entry => entry.Operation.Span.Start)
      .ThenByDescending(entry => entry.Operation.Span.Length)
      .ToList();
    var effectivePlan = new List<RewritePlanEntry>();

    foreach (var entry in orderedPlan)
    {
      var overlappingEntries = effectivePlan
        .Where(existing => existing.Operation.Span.OverlapsWith(entry.Operation.Span))
        .ToList();
      if (overlappingEntries.Count == 0)
      {
        effectivePlan.Add(entry);
        continue;
      }

      if (overlappingEntries.Any(overlappingEntry =>
            overlappingEntry.Operation.Span.Contains(entry.Operation.Span)))
      {
        continue;
      }

      if (overlappingEntries.All(overlappingEntry =>
            entry.Operation.Span.Contains(overlappingEntry.Operation.Span)))
      {
        foreach (var overlappingEntry in overlappingEntries)
        {
          effectivePlan.Remove(overlappingEntry);
        }

        effectivePlan.Add(entry);
        continue;
      }

      var conflictingEntry = overlappingEntries.First(overlappingEntry =>
        !entry.Operation.Span.Contains(overlappingEntry.Operation.Span) &&
        !overlappingEntry.Operation.Span.Contains(entry.Operation.Span));
      throw new InvalidOperationException(
        "Partially overlapping rewrite operations are not supported: " +
        $"{entry.Operation.Span.Start}..{entry.Operation.Span.End} overlaps " +
        $"{conflictingEntry.Operation.Span.Start}..{conflictingEntry.Operation.Span.End}.");
    }

    return effectivePlan
      .OrderByDescending(entry => entry.Operation.Span.Start)
      .ThenByDescending(entry => entry.Operation.Span.Length)
      .ToList();
  }

  private static string ApplyTextRewriteOperations(string source, IReadOnlyList<TextRewriteOperation> operations)
  {
    if (operations.Count == 0)
    {
      return source;
    }

    var builder = new StringBuilder(source);
    var orderedOperations = operations
      .OrderByDescending(operation => operation.Span.Start)
      .ThenByDescending(operation => operation.Span.Length)
      .ToList();
    var lastAppliedStart = source.Length + 1;

    foreach (var operation in orderedOperations)
    {
      if (operation.Span.End > lastAppliedStart)
      {
        throw new InvalidOperationException(
          $"Overlapping rewrite operations detected at span {operation.Span.Start}..{operation.Span.End}.");
      }

      builder.Remove(operation.Span.Start, operation.Span.Length);
      builder.Insert(operation.Span.Start, operation.ReplacementText);
      lastAppliedStart = operation.Span.Start;
    }

    return builder.ToString();
  }

  private static string FinalizeRewrittenSource(string rewrittenSource, string filePath)
  {
    var rewrittenTree = CSharpSyntaxTree.ParseText(rewrittenSource, path: filePath);
    var rewrittenRoot = rewrittenTree.GetRoot();
    if (ComputeMaxSyntaxDepth(rewrittenRoot) > NormalizeWhitespaceDepthLimit)
    {
      return NormalizeLineEndings(rewrittenRoot.ToFullString());
    }

    var formattedRoot = rewrittenRoot.NormalizeWhitespace(eol: "\r\n");
    return NormalizeLineEndings(formattedRoot.ToFullString());
  }

  private static int ComputeMaxSyntaxDepth(SyntaxNode root)
  {
    var maxDepth = 0;
    var pending = new Stack<(SyntaxNode Node, int Depth)>();
    pending.Push((root, 1));

    while (pending.Count > 0)
    {
      var (node, depth) = pending.Pop();
      if (depth > maxDepth)
      {
        maxDepth = depth;
      }

      foreach (var child in node.ChildNodes())
      {
        pending.Push((child, depth + 1));
      }
    }

    return maxDepth;
  }

  private static string GetDisplayText(SyntaxNode node)
  {
    return node.WithoutTrivia().ToFullString();
  }

  private static ExpressionSyntax CloneExpression(ExpressionSyntax expression)
  {
    return SyntaxFactory.ParseExpression(
      expression.NormalizeWhitespace().WithoutTrivia().ToFullString());
  }

  private static StatementSyntax CloneStatement(StatementSyntax statement)
  {
    return SyntaxFactory.ParseStatement(
      statement.NormalizeWhitespace().WithoutTrivia().ToFullString());
  }

  private static ElseClauseSyntax CloneElseClause(ElseClauseSyntax elseClause)
  {
    return SyntaxFactory.ElseClause(CloneStatement(elseClause.Statement));
  }

  private static MethodDeclarationSyntax CloneMethod(MethodDeclarationSyntax method)
  {
    return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
      method.NormalizeWhitespace().WithoutTrivia().ToFullString())!;
  }

  private static IndexerDeclarationSyntax CloneIndexer(IndexerDeclarationSyntax indexer)
  {
    return (IndexerDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
      indexer.NormalizeWhitespace().WithoutTrivia().ToFullString())!;
  }

  private static DelegateDeclarationSyntax CloneDelegate(DelegateDeclarationSyntax delegateDeclaration)
  {
    return (DelegateDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
      delegateDeclaration.NormalizeWhitespace().WithoutTrivia().ToFullString())!;
  }

  private static string NormalizeLineEndings(string source)
  {
    return source
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace("\n", "\r\n", StringComparison.Ordinal);
  }

}
