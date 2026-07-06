using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Text;
using RoslynPrototype.Decision;
using Rules;

namespace RoslynPrototype.Rewrite;

public sealed class PrototypeRewriter
{
  /// <summary>
  /// 根据决策列表对语法树执行删除或替换，并产出最终源码与编辑记录。
  /// </summary>
  public PrototypeRewriteResult Rewrite(
    SyntaxNode root,
    SemanticModel semanticModel,
    IEnumerable<RuleDecision> decisions)
  {
    var workspace = new AdhocWorkspace();
    var editor = new SyntaxEditor(root, workspace.Services);
    var edits = new List<RewriteEdit>();

    foreach (var decision in decisions.OrderByDescending(item => item.FinalNode.Span.Length)) {
      if (decision.Action == DecisionActionKind.Skip) {
        continue;
      }

      if (decision.Action == DecisionActionKind.Replace) {
        if (decision.FinalNode is ExpressionSyntax targetExpression &&
            decision.ReplacementNode is ExpressionSyntax replacementExpression) {
          var rewrittenExpression = CloneExpression(replacementExpression)
            .WithTriviaFrom(targetExpression);
          editor.ReplaceNode(targetExpression, rewrittenExpression);
          edits.Add(CreateEdit(targetExpression, rewrittenExpression));
          continue;
        }

        if (decision.FinalNode is StatementSyntax targetStatement &&
            decision.ReplacementNode is StatementSyntax replacementStatement) {
          var rewrittenStatement = CloneStatement(replacementStatement)
            .WithTriviaFrom(targetStatement);
          editor.ReplaceNode(targetStatement, rewrittenStatement);
          var displayEdit = CreateStatementDisplayEdit(targetStatement, rewrittenStatement);
          edits.Add(displayEdit);
          continue;
        }

        if (decision.FinalNode is ElseClauseSyntax targetElseClause &&
            decision.ReplacementNode is ElseClauseSyntax replacementElseClause) {
          var rewrittenElseClause = CloneElseClause(replacementElseClause)
            .WithTriviaFrom(targetElseClause);
          editor.ReplaceNode(targetElseClause, rewrittenElseClause);
          SyntaxNode displayOriginalNode = targetElseClause.Parent is IfStatementSyntax owningOriginalIfStatement
            ? owningOriginalIfStatement
            : targetElseClause;
          SyntaxNode displayReplacementNode = targetElseClause.Parent is IfStatementSyntax owningIfStatement
            ? owningIfStatement.WithElse(rewrittenElseClause)
            : rewrittenElseClause;
          edits.Add(CreateEdit(displayOriginalNode, displayReplacementNode));
          continue;
        }

        if (decision.FinalNode is MethodDeclarationSyntax targetMethod &&
            decision.ReplacementNode is MethodDeclarationSyntax replacementMethod) {
          var rewrittenMethod = CloneMethod(replacementMethod)
            .WithTriviaFrom(targetMethod);
          editor.ReplaceNode(targetMethod, rewrittenMethod);
          edits.Add(CreateEdit(targetMethod, rewrittenMethod));
          continue;
        }

        if (decision.FinalNode is IndexerDeclarationSyntax targetIndexer &&
            decision.ReplacementNode is IndexerDeclarationSyntax replacementIndexer) {
          var rewrittenIndexer = CloneIndexer(replacementIndexer)
            .WithTriviaFrom(targetIndexer);
          editor.ReplaceNode(targetIndexer, rewrittenIndexer);
          edits.Add(CreateEdit(targetIndexer, rewrittenIndexer));
          continue;
        }

        if (decision.FinalNode is DelegateDeclarationSyntax targetDelegate &&
            decision.ReplacementNode is DelegateDeclarationSyntax replacementDelegate) {
          var rewrittenDelegate = CloneDelegate(replacementDelegate)
            .WithTriviaFrom(targetDelegate);
          editor.ReplaceNode(targetDelegate, rewrittenDelegate);
          edits.Add(CreateEdit(targetDelegate, rewrittenDelegate));
          continue;
        }

        throw new InvalidOperationException(
          "Replace decisions must carry expression, statement, else-clause, method, indexer, or delegate nodes.");
      }

      // 表达式删除当前不直接挖空，而是降级成 default(...) 替换，避免生成明显非法的源码。
      if (decision.FinalNode is ExpressionSyntax expression) {
        var replacement = CreateReplacementExpression(expression, semanticModel);
        editor.ReplaceNode(expression, replacement);
        edits.Add(CreateEdit(expression, replacement));
        continue;
      }

      if (decision.FinalNode is ElseClauseSyntax elseClause &&
          elseClause.Parent is IfStatementSyntax parentIfStatement) {
        var rewrittenIfStatement = parentIfStatement.WithElse(null);
        editor.ReplaceNode(parentIfStatement, rewrittenIfStatement);
        edits.Add(CreateEdit(parentIfStatement, rewrittenIfStatement));
        continue;
      }

      editor.RemoveNode(decision.FinalNode, SyntaxRemoveOptions.KeepNoTrivia);
      edits.Add(CreateDeleteEdit(decision.FinalNode));
    }

    var changedRoot = editor.GetChangedRoot();
    var formattedRoot = changedRoot.NormalizeWhitespace(eol: "\r\n");
    var rewrittenSource = NormalizeLineEndings(formattedRoot.ToFullString());
    return new PrototypeRewriteResult(rewrittenSource, edits, BuildDiffText(edits));
  }

  /// <summary>
  /// 为表达式删除场景构造一个类型兼容的占位替换表达式。
  /// </summary>
  private static ExpressionSyntax CreateReplacementExpression(
    ExpressionSyntax expression,
    SemanticModel semanticModel)
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

  private static RewriteEdit CreateStatementDisplayEdit(
    StatementSyntax originalStatement,
    StatementSyntax replacementStatement)
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

  /// <summary>
  /// 把编辑记录格式化成便于查看和落盘的文本差异摘要。
  /// </summary>
  public string BuildDiffText(IReadOnlyList<RewriteEdit> edits)
  {
    if (edits.Count == 0) {
      return string.Empty;
    }

    var builder = new StringBuilder();
    for (var index = 0; index < edits.Count; index++) {
      var edit = edits[index];
      builder.AppendLine($"--- original #{index + 1} {edit.FilePath}:{edit.Span.Start}..{edit.Span.End}");
      builder.AppendLine(edit.OriginalText);
      builder.AppendLine($"+++ rewritten #{index + 1}");
      builder.AppendLine(string.IsNullOrEmpty(edit.ReplacementText)
        ? "<deleted>"
        : edit.ReplacementText);

      if (index + 1 < edits.Count) {
        builder.AppendLine();
      }
    }

    return builder.ToString();
  }
}
