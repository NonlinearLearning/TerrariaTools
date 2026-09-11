using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using NLISSN.Core.Decision;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Rewrite;

public sealed class PrototypeRewriter
{
  private const int NormalizeWhitespaceDepthLimit = 256;
  private readonly DiffBuilder _diffBuilder = new();
  private readonly TextDiffRenderer _textDiffRenderer = new();
  private readonly record struct RewritePlanEntry(RewritePlanEdit Operation, RewriteEdit Edit);

  // 正式执行入口只接受经过 Planner 和 PlanValidator 认证的权限令牌。
  public PrototypeRewriteResult Rewrite(
    SyntaxNode root,
    SemanticModel semanticModel,
    ExecutablePlan executablePlan)
  {
    ValidateExecutablePlan(root, executablePlan);
    var plan = BuildPlan(root, semanticModel, executablePlan);
    return ExecutePlan(root.ToFullString(), root.SyntaxTree.FilePath, plan);
  }

  // 把规则决策直接转换成改写计划并执行，返回源码、编辑和 diff。
  public PrototypeRewriteResult Rewrite(SyntaxNode root, SemanticModel semanticModel, IEnumerable<RuleDecision> decisions)
  {
    var plan = BuildPlan(root, semanticModel, decisions);

    return ExecutePlan(root.ToFullString(), root.SyntaxTree.FilePath, plan);
  }

  // 从已认证计划生成改写操作；正式调用方不会重新接受任意决策集合。
  public PrototypeRewritePlan BuildPlan(
    SyntaxNode root,
    SemanticModel semanticModel,
    ExecutablePlan executablePlan)
  {
    ValidateExecutablePlan(root, executablePlan);
    return BuildPlan(root, semanticModel, executablePlan.Decisions);
  }

  // 根据规则决策生成可移植文本操作和显示用编辑列表，但不立即应用到源码。
  public PrototypeRewritePlan BuildPlan(SyntaxNode root, SemanticModel semanticModel, IEnumerable<RuleDecision> decisions)
  {
    ArgumentNullException.ThrowIfNull(root);
    ArgumentNullException.ThrowIfNull(semanticModel);
    ArgumentNullException.ThrowIfNull(decisions);

    var effectiveDecisions = ComposeResidualReplacements(
      decisions.ToArray(),
      semanticModel);
    var rewritePlan = new List<RewritePlanEntry>();

    foreach (var decision in effectiveDecisions.OrderByDescending(item => item.FinalNode.Span.Length)) {
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

      if (decision.FinalNode is ReturnStatementSyntax returnStatement &&
          TryCreateReturnReplacement(returnStatement, semanticModel, out var rewrittenReturnStatement)) {
        rewritePlan.Add(CreateRewritePlanEntry(returnStatement, rewrittenReturnStatement));
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

  // 按既定改写计划改写源码文本，并生成与之对应的结构化 diff。
  public PrototypeRewriteResult ExecutePlan(string source, string filePath, PrototypeRewritePlan plan)
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

  // 执行从制品回放读取出的改写计划文件，并补齐展示层编辑信息。
  public PrototypeRewriteResult ExecutePlan(string source, string filePath, RewritePlanFile plan)
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

  private static ExpressionSyntax CreateReplacementExpression(ExpressionSyntax expression, SemanticModel semanticModel)
  {
    var typeInfo = semanticModel.GetTypeInfo(expression);
    var targetType = typeInfo.ConvertedType ?? typeInfo.Type;
    if (targetType is null || targetType.TypeKind == TypeKind.Error) {
      return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
    }

    return SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(targetType.ToDisplayString()));
  }

  private static bool TryCreateReturnReplacement(ReturnStatementSyntax returnStatement, SemanticModel semanticModel, out ReturnStatementSyntax replacement)
  {
    replacement = null!;
    if (returnStatement.Expression is null ||
        GetContainingMethodSymbol(returnStatement, semanticModel) is not IMethodSymbol methodSymbol ||
        methodSymbol.ReturnsVoid) {
      return false;
    }

    var accessibilityContext = (ISymbol?)methodSymbol.ContainingType ?? semanticModel.Compilation.Assembly;
    var returnExpression = CreateReturnValueExpression(
      methodSymbol.ReturnType,
      accessibilityContext,
      semanticModel.Compilation);
    replacement = SyntaxFactory.ParseStatement($"return {returnExpression};") as ReturnStatementSyntax
      ?? throw new InvalidOperationException("Unable to create a return replacement statement.");
    return true;
  }

  private static IMethodSymbol? GetContainingMethodSymbol(ReturnStatementSyntax returnStatement, SemanticModel semanticModel)
  {
    var declaration = returnStatement.Ancestors().FirstOrDefault(node => node is
      MethodDeclarationSyntax or
      LocalFunctionStatementSyntax or
      AccessorDeclarationSyntax);
    return declaration is null
      ? null
      : semanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
  }

  private static ExpressionSyntax CreateReturnValueExpression(ITypeSymbol returnType, ISymbol enclosingSymbol, Compilation compilation)
  {
    if (returnType is INamedTypeSymbol namedType &&
        namedType.TypeKind == TypeKind.Class &&
        !namedType.IsAbstract) {
      var parameterlessConstructor = namedType.InstanceConstructors.FirstOrDefault(constructor =>
        constructor.Parameters.Length == 0 &&
        compilation.IsSymbolAccessibleWithin(constructor, enclosingSymbol));
      if (parameterlessConstructor is not null) {
        return SyntaxFactory.ParseExpression(
          $"new {GetConstructibleTypeName(returnType)}()");
      }
    }

    return SyntaxFactory.DefaultExpression(
      SyntaxFactory.ParseTypeName(returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
  }

  private static string GetConstructibleTypeName(ITypeSymbol type)
  {
    return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
      .TrimEnd('?');
  }

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

  private static RewriteEdit CreateDeleteEdit(SyntaxNode originalNode)
  {
    return new RewriteEdit(
      originalNode.SyntaxTree.FilePath,
      originalNode.Span,
      GetDisplayText(originalNode),
      string.Empty);
  }

  private static RewritePlanEntry CreateRewritePlanEntry(SyntaxNode originalNode, SyntaxNode replacementNode, RewriteEdit? displayEdit = null)
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

  private static IReadOnlyList<RuleDecision> ComposeResidualReplacements(
    IReadOnlyList<RuleDecision> decisions,
    SemanticModel semanticModel)
  {
    var result = decisions.ToList();
    var consumed = new HashSet<RuleDecision>();
    var parents = decisions
      .Where(decision =>
        decision.Action == DecisionActionKind.Replace &&
        decision.ReplacementNode is not null &&
        decision.Intent?.Composition == DecisionComposition.Composable &&
        decision.Intent.ResidualMapping is not null)
      .OrderByDescending(decision => decision.FinalNode.Span.Length)
      .ToArray();

    foreach (var parent in parents)
    {
      if (consumed.Contains(parent) || parent.ReplacementNode is null)
      {
        continue;
      }

      var replacementRoot = CloneReplacementNode(parent.ReplacementNode);
      var composed = false;
      foreach (var child in decisions
        .Where(candidate =>
          !ReferenceEquals(candidate, parent) &&
          !consumed.Contains(candidate) &&
          IsDescendantOf(candidate.FinalNode, parent.FinalNode))
        .OrderBy(candidate => candidate.FinalNode.Span.Length)
        .ThenBy(candidate => candidate.FinalNode.SpanStart))
      {
        var childKey = DecisionCpgFactory.BuildNodeKey(child.FinalNode);
        if (!parent.Intent!.ResidualMapping!.TryMap(childKey, out var mapping))
        {
          continue;
        }

        if (mapping.Kind is not (ResidualMappingKind.Retained or ResidualMappingKind.Replaced))
        {
          throw new InvalidOperationException(
            $"Residual mapping for child '{childKey}' is {mapping.Kind} and cannot compose a child edit.");
        }

        var residualTarget = FindResidualNode(replacementRoot, mapping.NewNodeKey);
        if (residualTarget is null)
        {
          throw new InvalidOperationException(
            $"Residual mapping for child '{childKey}' does not resolve in the parent replacement tree.");
        }

        replacementRoot = ApplyNestedDecision(
          replacementRoot,
          residualTarget,
          child,
          semanticModel);
        consumed.Add(child);
        composed = true;
      }

      if (composed)
      {
        var replacementIndex = result.FindIndex(candidate => ReferenceEquals(candidate, parent));
        result[replacementIndex] = parent with { ReplacementNode = replacementRoot };
      }
    }

    return result
      .Where(decision => !consumed.Contains(decision))
      .ToArray();
  }

  private static bool IsDescendantOf(SyntaxNode candidate, SyntaxNode ancestor)
  {
    return candidate.Ancestors().Any(node => ReferenceEquals(node, ancestor));
  }

  private static SyntaxNode? FindResidualNode(SyntaxNode replacementRoot, string? newNodeKey)
  {
    if (string.IsNullOrWhiteSpace(newNodeKey))
    {
      return null;
    }

    var nodes = replacementRoot.DescendantNodesAndSelf().ToArray();
    var exact = nodes.FirstOrDefault(node =>
      string.Equals(DecisionCpgFactory.BuildNodeKey(node), newNodeKey, StringComparison.Ordinal));
    if (exact is not null)
    {
      return exact;
    }

    // Cloning a replacement reparses it and can change only the source path;
    // the span and raw kind remain useful identity components in that case.
    var parts = newNodeKey.Split('|');
    if (parts.Length < 4 ||
        !int.TryParse(parts[^3], out var start) ||
        !int.TryParse(parts[^2], out var end) ||
        !int.TryParse(parts[^1], out var rawKind))
    {
      return null;
    }

    return nodes.FirstOrDefault(node =>
      node.SpanStart == start &&
      node.Span.End == end &&
      node.RawKind == rawKind);
  }

  private static SyntaxNode ApplyNestedDecision(
    SyntaxNode replacementRoot,
    SyntaxNode target,
    RuleDecision decision,
    SemanticModel semanticModel)
  {
    if (decision.Action == DecisionActionKind.Skip)
    {
      return replacementRoot;
    }

    if (decision.Action == DecisionActionKind.Replace)
    {
      if (decision.ReplacementNode is null)
      {
        throw new InvalidOperationException(
          "A composed Replace decision must carry a replacement binding.");
      }

      var replacement = CloneReplacementNode(decision.ReplacementNode)
        .WithTriviaFrom(target);
      return replacementRoot.ReplaceNode(target, replacement);
    }

    if (target is ExpressionSyntax targetExpression)
    {
      var sourceExpression = decision.OriginalNode as ExpressionSyntax ?? targetExpression;
      var replacement = CreateReplacementExpression(sourceExpression, semanticModel)
        .WithTriviaFrom(targetExpression);
      return replacementRoot.ReplaceNode(targetExpression, replacement);
    }

    if (target is ElseClauseSyntax targetElseClause &&
        targetElseClause.Parent is IfStatementSyntax parentIf)
    {
      return replacementRoot.ReplaceNode(parentIf, parentIf.WithElse(null));
    }

    return replacementRoot.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
      ?? throw new InvalidOperationException("A composed child deletion removed the replacement root.");
  }

  private static SyntaxNode CloneReplacementNode(SyntaxNode node)
  {
    return node switch
    {
      ExpressionSyntax expression => CloneExpression(expression),
      StatementSyntax statement => CloneStatement(statement),
      ElseClauseSyntax elseClause => CloneElseClause(elseClause),
      MethodDeclarationSyntax method => CloneMethod(method),
      IndexerDeclarationSyntax indexer => CloneIndexer(indexer),
      DelegateDeclarationSyntax delegateDeclaration => CloneDelegate(delegateDeclaration),
      _ => node.WithoutTrivia()
    };
  }

  private static void ValidateExecutablePlan(SyntaxNode root, ExecutablePlan executablePlan)
  {
    ArgumentNullException.ThrowIfNull(root);
    ArgumentNullException.ThrowIfNull(executablePlan);
    if (!executablePlan.DecisionPlan.IsExecutable)
    {
      throw new InvalidOperationException(
        "Only executable decision plans may cross into Rewrite.");
    }

    foreach (var decision in executablePlan.Decisions)
    {
      if (!IsInOriginalTree(root, decision.OriginalNode) ||
          !IsInOriginalTree(root, decision.FinalNode))
      {
        throw new InvalidOperationException(
          "Executable decision bindings must belong to the original syntax tree.");
      }

      if (decision.Action is DecisionActionKind.Delete or DecisionActionKind.Replace &&
          (decision.Intent is null ||
           decision.Intent.Status != EditIntentStatus.Complete ||
           decision.Intent.Action != decision.Action ||
           decision.Intent.EraseSet.Count == 0 ||
           decision.Intent.ProofReferences.Count == 0))
      {
        throw new InvalidOperationException(
          "Destructive executable decisions must carry a complete authorized intent.");
      }
    }
  }

  private static bool IsInOriginalTree(SyntaxNode root, SyntaxNode node)
  {
    return ReferenceEquals(root.SyntaxTree, node.SyntaxTree) &&
      (ReferenceEquals(root, node) || node.AncestorsAndSelf().Any(candidate => ReferenceEquals(candidate, root)));
  }

  private static IReadOnlyList<RewritePlanEntry> BuildEffectiveRewritePlan(IReadOnlyList<RewritePlanEntry> rewritePlan)
  {
    if (rewritePlan.Count <= 1)
    {
      return rewritePlan;
    }

    // 从右向左应用文本编辑，前一次替换不会改变尚未处理的左侧 Span。
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
        // 外层编辑已覆盖当前编辑，保留外层结果即可。
        continue;
      }

      if (overlappingEntries.All(overlappingEntry =>
            GetTextSpan(entry.Operation).Contains(GetTextSpan(overlappingEntry.Operation))))
      {
        // 更大的编辑替代已收集的子编辑，避免同一源片段被重复处理。
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
