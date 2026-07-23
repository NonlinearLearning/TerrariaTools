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
  private readonly record struct RewritePlanEntry(RewritePlanEdit Operation, RewriteEdit Edit);

  /// <summary>
  /// 根据决策列表对语法树执行删除或替换，并产出最终源码与编辑记录。
  /// </summary>
  public PrototypeRewriteResult Rewrite(SyntaxNode root, SemanticModel semanticModel, IEnumerable<RuleDecision> decisions)
  {
    var plan = BuildPlan(root, semanticModel, decisions);

    return ExecutePlan(root.ToFullString(), root.SyntaxTree.FilePath, plan);
  }

  /// <summary>
  /// Converts Roslyn rule decisions into a portable text-only rewrite plan.
  /// </summary>
  public PrototypeRewritePlan BuildPlan(
    SyntaxNode root,
    SemanticModel semanticModel,
    IEnumerable<RuleDecision> decisions)
  {
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
    return new PrototypeRewritePlan(
      effectivePlan.Select(entry => entry.Operation).ToList(),
      effectivePlan.Select(entry => entry.Edit).ToList());
  }

  /// <summary>
  /// Applies an in-memory plan without accessing Roslyn semantic data.
  /// </summary>
  public PrototypeRewriteResult ExecutePlan(
    string source,
    string filePath,
    PrototypeRewritePlan plan)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(filePath);
    ArgumentNullException.ThrowIfNull(plan);

    var rewrittenSource = FinalizeRewrittenSource(
      ApplyTextRewriteOperations(source, plan.Operations),
      filePath);
    var diff = _diffBuilder.Build(plan.Edits);

    return new PrototypeRewriteResult(
      rewrittenSource,
      plan.Edits,
      diff,
      plan.Operations);
  }

  /// <summary>
  /// Applies a persisted file plan without accepting Roslyn syntax or semantic objects.
  /// </summary>
  public PrototypeRewriteResult ExecutePlan(
    string source,
    string filePath,
    RewritePlanFile plan)
  {
    ArgumentNullException.ThrowIfNull(plan);

    var displayEdits = plan.Edits
      .Select(edit => new RewriteEdit(
        filePath,
        new TextSpan(edit.Start, edit.Length),
        edit.OriginalText,
        edit.ReplacementText))
      .ToList();

    return ExecutePlan(source, filePath, new PrototypeRewritePlan(plan.Edits, displayEdits));
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
      CreateTextRewriteOperation(originalNode, string.Empty),
      CreateDeleteEdit(originalNode));
  }

  private static RewritePlanEdit CreateTextRewriteOperation(SyntaxNode originalNode, SyntaxNode replacementNode)
  {
    return CreateTextRewriteOperation(originalNode, replacementNode.ToFullString().Trim());
  }

  private static RewritePlanEdit CreateTextRewriteOperation(SyntaxNode originalNode, string replacementText)
  {
    var sourceText = originalNode.SyntaxTree.GetText();
    var originalText = sourceText.ToString(originalNode.Span);

    return new RewritePlanEdit(
      originalNode.Span.Start,
      originalNode.Span.Length,
      originalText,
      replacementText);
  }

  private static IReadOnlyList<RewritePlanEntry> BuildEffectiveRewritePlan(IReadOnlyList<RewritePlanEntry> rewritePlan)
  {
    if (rewritePlan.Count <= 1)
    {
      return rewritePlan;
    }

    var orderedPlan = rewritePlan
      .OrderByDescending(entry => entry.Operation.Start)
      .ThenByDescending(entry => entry.Operation.Length)
      .ToList();
    var effectivePlan = new List<RewritePlanEntry>();

    foreach (var entry in orderedPlan)
    {
      var overlappingEntries = effectivePlan
        .Where(existing => GetTextSpan(existing.Operation).OverlapsWith(GetTextSpan(entry.Operation)))
        .ToList();
      if (overlappingEntries.Count == 0)
      {
        effectivePlan.Add(entry);
        continue;
      }

      if (overlappingEntries.Any(overlappingEntry =>
            GetTextSpan(overlappingEntry.Operation).Contains(GetTextSpan(entry.Operation))))
      {
        continue;
      }

      if (overlappingEntries.All(overlappingEntry =>
            GetTextSpan(entry.Operation).Contains(GetTextSpan(overlappingEntry.Operation))))
      {
        foreach (var overlappingEntry in overlappingEntries)
        {
          effectivePlan.Remove(overlappingEntry);
        }

        effectivePlan.Add(entry);
        continue;
      }

      var conflictingEntry = overlappingEntries.First(overlappingEntry =>
        !GetTextSpan(entry.Operation).Contains(GetTextSpan(overlappingEntry.Operation)) &&
        !GetTextSpan(overlappingEntry.Operation).Contains(GetTextSpan(entry.Operation)));
      throw new InvalidOperationException(
        "Partially overlapping rewrite operations are not supported: " +
        $"{entry.Operation.Start}..{entry.Operation.Start + entry.Operation.Length} overlaps " +
        $"{conflictingEntry.Operation.Start}..{conflictingEntry.Operation.Start + conflictingEntry.Operation.Length}.");
    }

    return effectivePlan
      .OrderByDescending(entry => entry.Operation.Start)
      .ThenByDescending(entry => entry.Operation.Length)
      .ToList();
  }

  private static string ApplyTextRewriteOperations(string source, IReadOnlyList<RewritePlanEdit> operations)
  {
    if (operations.Count == 0)
    {
      return source;
    }

    var builder = new StringBuilder(source);
    var orderedOperations = operations
      .OrderByDescending(operation => operation.Start)
      .ThenByDescending(operation => operation.Length)
      .ToList();
    var lastAppliedStart = source.Length + 1;

    foreach (var operation in orderedOperations)
    {
      if (operation.Start < 0 || operation.Length < 0 || operation.Start + operation.Length > source.Length)
      {
        throw new InvalidOperationException(
          $"Rewrite operation span {operation.Start}..{operation.Start + operation.Length} is outside the source text.");
      }

      if (!string.Equals(
            source.Substring(operation.Start, operation.Length),
            operation.OriginalText,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          $"Rewrite operation original text does not match source span {operation.Start}..{operation.Start + operation.Length}.");
      }

      if (operation.Start + operation.Length > lastAppliedStart)
      {
        throw new InvalidOperationException(
          $"Overlapping rewrite operations detected at span {operation.Start}..{operation.Start + operation.Length}.");
      }

      builder.Remove(operation.Start, operation.Length);
      builder.Insert(operation.Start, operation.ReplacementText);
      lastAppliedStart = operation.Start;
    }

    return builder.ToString();
  }

  private static TextSpan GetTextSpan(RewritePlanEdit operation)
  {
    return new TextSpan(operation.Start, operation.Length);
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
