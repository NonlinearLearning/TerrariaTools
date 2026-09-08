using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public abstract class DeclarationHostProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleFactKind DeclarationHostFactKind = RuleFactKind.RelationDeclarationHost;

    private static readonly RuleConsumesContract DeclarationHostConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.BaseList,
            SyntaxKind.DelegateDeclaration,
            SyntaxKind.EventDeclaration,
            SyntaxKind.EventFieldDeclaration,
            SyntaxKind.FieldDeclaration,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.LocalDeclarationStatement,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.SimpleBaseType
          },
          DeclarationHostFactKind)
      });

    public override RuleConsumesContract Consumes => DeclarationHostConsumes;
}

public abstract class ParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleFactKind ParameterUsageFactKind = RuleFactKind.RelationParameterUsage;

    private static readonly RuleConsumesContract ParameterUsageConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.InvocationExpression,
            SyntaxKind.ElementAccessExpression
          },
          ParameterUsageFactKind)
      });

    public override RuleConsumesContract Consumes => ParameterUsageConsumes;
}

public abstract class DelegateUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleFactKind DelegateUsageFactKind = RuleFactKind.RelationDelegateUsage;

    private static readonly RuleConsumesContract DelegateUsageConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.DelegateDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression,
            SyntaxKind.InvocationExpression
          },
          DelegateUsageFactKind)
      });

    public override RuleConsumesContract Consumes => DelegateUsageConsumes;
}

public abstract class ExtensionMethodParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleFactKind ExtensionMethodParameterUsageFactKind = RuleFactKind.RelationExtensionUsage;

    private static readonly RuleConsumesContract ExtensionMethodParameterUsageConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration, SyntaxKind.InvocationExpression },
          ExtensionMethodParameterUsageFactKind)
      });

    public override RuleConsumesContract Consumes => ExtensionMethodParameterUsageConsumes;
}

