using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class ClearUnusedInterfaceImplementationProposalRule : RuleDefinitionPropose
{
  public override string RuleId { get; } = ClearUnusedInterfaceImplementationRuleIds.ProposalRuleId;

  public override string GroupKey { get; } = ClearUnusedInterfaceImplementationRuleIds.GroupKey;

  public override string Name { get; } = "Clear unused interface implementation method bodies";

  public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
    Array.Empty<SyntaxKind>();

  public override IEnumerable<DecisionUnit> Propose(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
    IReadOnlyList<LiftedMarkRecord> liftedMarks)
  {
    _ = propagatedMarks;
    _ = liftedMarks;

    foreach (var seedMark in seedMarks)
    {
      if (seedMark.SyntaxNode is not MethodDeclarationSyntax method ||
          context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None)
            is not IMethodSymbol methodSymbol ||
          !TryBuildReplacementMethod(method, methodSymbol, out var replacementMethod))
      {
        continue;
      }

      yield return CreateMethodReplaceDecision(
        RuleId,
        method,
        replacementMethod,
        "Clear unused interface implementation body and keep a compile-safe stub.");
    }
  }

  private static bool TryBuildReplacementMethod(
    MethodDeclarationSyntax method,
    IMethodSymbol methodSymbol,
    out MethodDeclarationSyntax replacementMethod)
  {
    replacementMethod = method;
    if (method.Body is null && method.ExpressionBody is null)
    {
      return false;
    }

    var statements = new List<StatementSyntax>();
    foreach (var parameter in methodSymbol.Parameters.Where(parameter => parameter.RefKind == RefKind.Out))
    {
      statements.Add(CreateOutAssignment(parameter));
    }

    if (!methodSymbol.ReturnsVoid)
    {
      statements.Add(SyntaxFactory.ParseStatement(
        $"return {CreateValueExpressionText(methodSymbol.ReturnType)};"));
    }

    replacementMethod = method
      .WithExpressionBody(null)
      .WithSemicolonToken(default)
      .WithBody(SyntaxFactory.Block(statements));
    return true;
  }

  private static StatementSyntax CreateOutAssignment(IParameterSymbol parameter)
  {
    return SyntaxFactory.ParseStatement($"{parameter.Name} = {CreateValueExpressionText(parameter.Type)};");
  }

  private static ExpressionSyntax CreateValueExpression(ITypeSymbol type)
  {
    return SyntaxFactory.ParseExpression(CreateValueExpressionText(type));
  }

  private static string CreateValueExpressionText(ITypeSymbol type)
  {
    var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (CanUseObjectCreation(type))
    {
      return $"new {typeName}()";
    }

    return $"default({typeName})";
  }

  private static bool CanUseObjectCreation(ITypeSymbol type)
  {
    if (type.IsValueType)
    {
      return true;
    }

    if (type.TypeKind == TypeKind.TypeParameter)
    {
      return true;
    }

    if (type.TypeKind != TypeKind.Class || type.IsAbstract)
    {
      return false;
    }

    return type.GetMembers()
      .OfType<IMethodSymbol>()
      .Any(member =>
        member.MethodKind == MethodKind.Constructor &&
        !member.IsStatic &&
        member.Parameters.Length == 0 &&
        member.DeclaredAccessibility == Accessibility.Public);
  }

  private static DecisionUnit CreateMethodReplaceDecision(
    string ruleId,
    MethodDeclarationSyntax anchorNode,
    MethodDeclarationSyntax replacementNode,
    string reason)
  {
    var anchorFragment = CreateFragment(anchorNode, "anchor", DecisionActionKind.Replace);
    var replacementFragment = CreateFragment(
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
        DecisionCpgFactory.CreateRelation("cleared-to", anchorFragment, replacementFragment)
      },
      DecisionCpgFactory.CreateSyntaxBindings(
        (anchorFragment, anchorNode),
        (replacementFragment, replacementNode.WithoutTrivia())),
      conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
      mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
      reason: reason);
  }

  private static RoslynCpgNode CreateFragment(
    SyntaxNode node,
    string role,
    DecisionActionKind action)
  {
    return DecisionCpgFactory.CreateFragment(
      $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
      node,
      role,
      action);
  }
}
