using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 删除已确认不再需要的接口实现方法体，同时保留接口声明的其他契约。
public sealed class ClearUnusedInterfaceImplementationProposalRule : RuleDefinitionPropose
{
  private static readonly RuleSemanticTag UnusedInterfaceImplementationSemanticTag = new("UnusedInterfaceImplementation");

  private static readonly RuleConsumesContract UnusedInterfaceImplementationConsumes =
    new(new[]
    {
      new RuleConsumedSyntax(
        new[] { SyntaxKind.MethodDeclaration },
        UnusedInterfaceImplementationSemanticTag)
    });

  public override string CapabilityId { get; } = "propose.clear-unused-interface-implementation";

    public override string RuleId { get; } = "CLR-UNUSED-IFACE-IMPL-PROP-001";

  public override RuleConsumesContract Consumes => UnusedInterfaceImplementationConsumes;


  public override string Name { get; } = "Clear unused interface implementation method bodies";

  public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
    Array.Empty<SyntaxKind>();

  // 把已确认无引用的接口实现方法改写成编译安全的空壳实现，而不是直接删除签名。
  public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
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

  private static bool TryBuildReplacementMethod(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, out MethodDeclarationSyntax replacementMethod)
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

  private static DecisionUnit CreateMethodReplaceDecision(string ruleId, MethodDeclarationSyntax anchorNode, MethodDeclarationSyntax replacementNode, string reason)
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
        DecisionCpgFactory.CreateRelation(
          NLCPGDecisionRelationKind.ClearedTo,
          anchorFragment,
          replacementFragment)
      },
      DecisionCpgFactory.CreateSyntaxBindings(
        (anchorFragment, anchorNode),
        (replacementFragment, replacementNode.WithoutTrivia())),
      conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
      mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
      reason: reason);
  }

  private static NLCPGNode CreateFragment(SyntaxNode node, string role, DecisionActionKind action)
  {
    return DecisionCpgFactory.CreateFragment(
      $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
      node,
      role,
      action);
  }
}
