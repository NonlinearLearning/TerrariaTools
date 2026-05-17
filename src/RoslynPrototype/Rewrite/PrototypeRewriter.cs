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
        if (decision.FinalNode is not ExpressionSyntax targetExpression ||
            decision.ReplacementNode is not ExpressionSyntax replacementExpression) {
          throw new InvalidOperationException("Replace decisions must carry expression nodes.");
        }

        var rewrittenExpression = replacementExpression.WithTriviaFrom(targetExpression);
        editor.ReplaceNode(targetExpression, rewrittenExpression);
        edits.Add(CreateEdit(targetExpression, rewrittenExpression));
        continue;
      }

      if (decision.FinalNode is ExpressionSyntax expression) {
        var replacement = CreateReplacementExpression(expression, semanticModel);
        editor.ReplaceNode(expression, replacement);
        edits.Add(CreateEdit(expression, replacement));
        continue;
      }

      editor.RemoveNode(decision.FinalNode, SyntaxRemoveOptions.KeepNoTrivia);
      edits.Add(CreateDeleteEdit(decision.FinalNode));
    }

    var changedRoot = editor.GetChangedRoot();
    var rewrittenSource = changedRoot.NormalizeWhitespace().ToFullString();
    return new PrototypeRewriteResult(rewrittenSource, edits, BuildDiffText(edits));
  }

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

  private static RewriteEdit CreateEdit(SyntaxNode originalNode, SyntaxNode replacementNode)
  {
    return new RewriteEdit(
      originalNode.SyntaxTree.FilePath,
      originalNode.Span,
      originalNode.NormalizeWhitespace().ToFullString(),
      replacementNode.NormalizeWhitespace().ToFullString());
  }

  private static RewriteEdit CreateDeleteEdit(SyntaxNode originalNode)
  {
    return new RewriteEdit(
      originalNode.SyntaxTree.FilePath,
      originalNode.Span,
      originalNode.NormalizeWhitespace().ToFullString(),
      string.Empty);
  }

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
