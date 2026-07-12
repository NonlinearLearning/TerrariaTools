using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassDefaultDeleteProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.DefaultDeleteProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Match delete-class default delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.DefaultConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(mark, sourceMark))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }

        foreach (var seedMark in DeleteSObjectProposalHelpers.EnumerateUncoveredSeedMarks(
                     seedMarks,
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(seedMark))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }
    }

    private static bool IsHandledBySpecializedRule(MarkRecord mark)
    {
        var kind = (SyntaxKind)mark.SyntaxNode.RawKind;
        return DeleteSObjectProposalHelpers.IfConflictNodeKinds.Contains(kind) ||
          DeleteSObjectProposalHelpers.ControlConflictNodeKinds.Contains(kind) ||
          mark.SyntaxNode is TypeSyntax ||
          mark.SyntaxNode is ElseClauseSyntax;
    }

    private static bool IsHandledBySpecializedRule(MarkRecord mark, MarkRecord sourceMark)
    {
        return IsHandledBySpecializedRule(mark) ||
          (sourceMark.RuleId == DeleteClassRuleIds.DeclarationHostPropagationRuleId) ||
          (mark.SyntaxNode is LocalDeclarationStatementSyntax &&
           sourceMark.SyntaxNode is TypeSyntax);
    }
}

public sealed class DeleteClassControlStructureDeleteProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.ControlStructureProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Match delete-class control structure delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.ControlConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            var kind = (SyntaxKind)mark.SyntaxNode.RawKind;
            if (!DecisionConflictNodeKinds.Contains(kind))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }
    }
}

public sealed class DeleteClassTypeSyntaxDeclarationProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.TypeSyntaxDeclarationProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete declarations whose type syntax references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.FieldDeclaration,
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.FieldDeclaration))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Declaration type references the delete-class target.",
              payload.HostDeclaration);
        }

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.PropertyDeclaration))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Declaration type references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

public sealed class DeleteClassMethodReturnTypeProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.MethodReturnTypeProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete private methods whose return type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.MethodReturnType))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method ||
                !DeleteClassMethodProposalSafety.IsSafePrivateMethod(method))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Private method return type references the delete-class target.",
              method);
        }
    }
}

public sealed class DeleteClassPublicMethodReturnTypeProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.PublicMethodReturnTypeProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete non-private methods whose return type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.MethodReturnType))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method ||
                !DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(method))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Non-private method return type references the delete-class target.",
              method);
        }
    }
}

public sealed class DeleteClassParameterProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.ParameterProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;
        _ = seedMarks;
        yield break;
    }
}

public sealed class DeleteClassPrivateMethodParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.PrivateMethodParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PrivatePositional))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassNamedArgumentMethodParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.NamedArgumentMethodParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink method parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.NamedArgument))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Method parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Named argument passes the deleted class type value; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassOptionalParameterDefaultedMethodShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.OptionalParameterDefaultedMethodShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink optional method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.Optional))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Optional method parameter type references the delete-class target; shrink the signature and keep omitted callsites unchanged.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Optional parameter is explicitly passed with the deleted class type value; remove the matching argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassPublicParameterProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.PublicParameterProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete non-private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;
        _ = seedMarks;
        yield break;
    }

    private static bool TryResolveNonPrivateMethodFromParameter(TypeSyntax typeSyntax, out MethodDeclarationSyntax method)
    {
        var parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate =>
            candidate.Type?.Span.Contains(typeSyntax.Span) == true);
        method = (parameter?.Parent?.Parent as MethodDeclarationSyntax)!;
        if (method is null)
        {
            return false;
        }

        return DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(method);
    }
}

public sealed class DeleteClassParamsMethodParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.ParamsMethodParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink params method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.ParamsOmitted))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Params method parameter type references the delete-class target; shrink the signature when all callsites omit the params slot.");
        }
    }
}

public sealed class DeleteClassPublicMethodParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.PublicMethodParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink non-private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PublicPositional))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Non-private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassNamedArgumentLocalFunctionParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.NamedArgumentLocalFunctionParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassLocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.NamedArgument))
        {
            if (!DeleteClassLocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in DeleteClassLocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation passes the deleted class type value by name; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassOptionalParameterDefaultedLocalFunctionShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.OptionalParameterDefaultedLocalFunctionShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink optional local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassLocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Optional))
        {
            if (!DeleteClassLocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Optional local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassLocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation explicitly passes the deleted class type optional argument; remove that argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassLocalFunctionParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.LocalFunctionParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassLocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Positional))
        {
            if (!DeleteClassLocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassLocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassNamedArgumentIndexerParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.NamedArgumentIndexerParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target when accesses use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassIndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.NamedArgument))
        {
            if (!DeleteClassIndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature for named-argument accesses.");

            foreach (var decision in DeleteClassIndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Indexer access passes the deleted class type value by name; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassIndexerParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.IndexerParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassIndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.Positional))
        {
            if (!DeleteClassIndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassIndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Indexer access passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassDelegateParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.DelegateParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink delegate parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.PlainSignature))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the signature.");
        }
    }
}

public sealed class DeleteClassMethodGroupDelegateParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.MethodGroupDelegateParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink delegate parameters and method-group targets when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.MethodGroup))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its method-group targets.");

            foreach (var method in payload.MethodTargets)
            {
                var methodParameter = method.ParameterList.Parameters.ElementAtOrDefault(payload.ParameterIndex);
                if (methodParameter is null ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildReplacementMethod(
                      method,
                      methodParameter,
                      out var replacementMethod))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
                  RuleId,
                  method,
                  replacementMethod,
                  "Method group target now removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var localFunction in payload.LocalFunctionTargets)
            {
                var parameter = localFunction.ParameterList.Parameters.ElementAtOrDefault(payload.ParameterIndex);
                if (parameter is null ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildReplacementLocalFunction(
                      localFunction,
                      parameter,
                      out var replacementLocalFunction))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
                  RuleId,
                  localFunction,
                  replacementLocalFunction,
                  "Local-function method group target now removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

public sealed class DeleteClassLambdaDelegateParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.LambdaDelegateParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink delegate parameters and lambda bindings when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.AnonymousMethodExpression,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.Lambda))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its lambda bindings.");

            foreach (var expression in payload.LambdaTargets)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
                if (model.GetOperation(expression, CancellationToken.None) is not IAnonymousFunctionOperation anonymousFunction ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildLambdaRewrite(
                      context,
                      model,
                      expression,
                      anonymousFunction,
                      payload.ParameterIndex,
                      out var lambdaRewrite))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateExpressionReplaceDecision(
                  RuleId,
                  lambdaRewrite.Expression,
                  lambdaRewrite.Replacement,
                  "Lambda binding removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

public sealed class DeleteClassDelegateInvocationChainParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.DelegateInvocationChainParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink delegate parameters and direct delegate invocation chains when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.InvocationChain))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its direct invocation chain.");

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

public sealed class DeleteClassExtensionReceiverNonFirstParameterShrinkProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.ExtensionReceiverNonFirstParameterShrinkProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Shrink non-receiver extension-method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassExtensionMethodUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks))
        {
            if (!DeleteClassExtensionMethodUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Extension method non-receiver parameter type references the delete-class target; shrink the signature and keep the receiver.");

            foreach (var decision in DeleteClassExtensionMethodUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Extension method invocation removes the deleted class type argument while preserving the receiver."))
            {
                yield return decision;
            }
        }
    }
}

public sealed class DeleteClassInterfaceMethodProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.InterfaceMethodProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete interface methods whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceMethod))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Interface method signature references the delete-class target.",
              method);
        }
    }
}

public sealed class DeleteClassInterfacePropertyProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.InterfacePropertyProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete interface properties whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceProperty))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface property signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

public sealed class DeleteClassInterfaceEventProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.InterfaceEventProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete interface events whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceEvent))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface event signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

public sealed class DeleteClassInterfaceIndexerProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.InterfaceIndexerProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete interface indexers whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceIndexer))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface indexer signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

public sealed class DeleteClassDelegateProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.DelegateProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete delegates whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.DelegateReturnType))
        {
            if (payload.HostDeclaration is not DelegateDeclarationSyntax delegateDeclaration)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              delegateDeclaration,
              "Delegate signature references the delete-class target.",
              delegateDeclaration);
        }
    }
}

public sealed class DeleteClassExtensionReceiverProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.ExtensionReceiverProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete extension methods whose receiver type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.ExtensionReceiverMethod))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Extension method receiver type references the delete-class target.",
              method);
        }
    }
}

public sealed class DeleteClassBaseTypeProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.BaseTypeProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Remove base-list entries whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.BaseList,
        SyntaxKind.SimpleBaseType
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.BaseType))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Base type references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

public sealed class DeleteClassGenericTypeArgumentProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.GenericTypeArgumentProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Delete local declarations whose generic type argument references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalDeclarationStatement
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.LocalGenericTypeArgument))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Local declaration type argument references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

public sealed class DeleteClassIfStructureProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteClassRuleIds.IfStructureProposalRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Match delete-class if/elseif/else structure decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.IfConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;
        var consumedKeys = new HashSet<(int Start, int Length, int RawKind)>();

        foreach (var payload in DeleteSObjectProposalHelpers.EnumerateIfStructureCompletionPayloads(
                     propagatedMarks))
        {
            var decisionNode = DeleteSObjectProposalHelpers.GetIfStructureDecisionNode(payload);

            if (consumedKeys.Contains(DeleteSObjectProposalHelpers.BuildNodeKey(decisionNode)))
            {
                continue;
            }

            if (DeleteSObjectProposalHelpers.TryBuildIfStructureDecisionFromMark(
                    RuleId,
                    payload,
                    out var decision,
                    out var consumedNodes) &&
                decision is not null)
            {
                foreach (var node in consumedNodes)
                {
                    consumedKeys.Add(DeleteSObjectProposalHelpers.BuildNodeKey(node));
                }

                yield return decision;
            }
        }
    }
}
