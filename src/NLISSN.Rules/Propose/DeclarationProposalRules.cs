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
    private static readonly RuleSemanticTag DeclarationHostSemanticTag = RuleFactPorts.RelationDeclarationHost;

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
          DeclarationHostSemanticTag)
      });

    public override RuleConsumesContract Consumes => DeclarationHostConsumes;
}

public abstract class ParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag ParameterUsageSemanticTag = RuleFactPorts.RelationParameterUsage;

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
          ParameterUsageSemanticTag)
      });

    public override RuleConsumesContract Consumes => ParameterUsageConsumes;
}

public abstract class DelegateUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag DelegateUsageSemanticTag = RuleFactPorts.RelationDelegateUsage;

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
          DelegateUsageSemanticTag)
      });

    public override RuleConsumesContract Consumes => DelegateUsageConsumes;
}

public abstract class ExtensionMethodParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag ExtensionMethodParameterUsageSemanticTag = RuleFactPorts.RelationExtensionUsage;

    private static readonly RuleConsumesContract ExtensionMethodParameterUsageConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration, SyntaxKind.InvocationExpression },
          ExtensionMethodParameterUsageSemanticTag)
      });

    public override RuleConsumesContract Consumes => ExtensionMethodParameterUsageConsumes;
}

/// 将 TypeSyntax 标记提升为包含该类型语法的声明改写。
public sealed class TypeSyntaxDeclarationProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.type-syntax-declaration";

    public override string RuleId { get; } = "DEL-CLASS-PROP-TYPE-DECL-001";


    public override string Name { get; } = "Delete declarations whose type syntax references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.FieldDeclaration,
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 把字段和属性上的声明宿主 payload 直接落成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.FieldDeclaration))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Declaration type references the delete-class target.",
              payload.HostDeclaration);
        }

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 删除可安全替换的方法返回类型，并保留声明主体。
public sealed class MethodReturnTypeProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.method-return-type";

    public override string RuleId { get; } = "DEL-CLASS-PROP-RETURN-001";


    public override string Name { get; } = "Delete private methods whose return type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为返回类型引用目标类的私有方法生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.MethodReturnType))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method ||
                !MethodProposalSafety.IsSafePrivateMethod(method))
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

/// 处理公开方法返回类型，要求调用与重载绑定不会因替换而漂移。
public sealed class PublicMethodReturnTypeProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.public-method-return-type";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PUBLIC-RETURN-001";


    public override string Name { get; } = "Delete non-private methods whose return type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为返回类型引用目标类的非私有方法生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.MethodReturnType))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method ||
                !MethodProposalSafety.IsSafeNonPrivateMethod(method))
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

/// 保留旧参数删除入口的规则标识；具体收缩由更严格的专门规则完成。
public sealed class ParameterProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.type.parameter";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PARAM-001";


    public override string Name { get; } = "Delete private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 当前刻意不为“整方法参数删除”直接产出决策，避免与更细粒度的收缩规则冲突。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;
        _ = seedMarks;
        yield break;
    }
}

/// 删除私有方法参数，并同步改写可证明的稳定位置调用。
public sealed class PrivateMethodParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.private-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PRIVATE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩私有方法声明和所有稳定位置调用点的目标参数。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PrivatePositional))
        {
            if (!MethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in MethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 处理具名实参方法调用，按参数符号而非位置删除对应实参。
public sealed class NamedArgumentMethodParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-METHOD-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink method parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名参数调用同步收缩方法声明与命名实参。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.NamedArgument))
        {
            if (!MethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Method parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in MethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 删除可省略的可选参数，同时维持省略调用的绑定语义。
public sealed class OptionalParameterDefaultedMethodShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.optional-parameter-defaulted-method-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-OPTIONAL-METHOD-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink optional method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 收缩带默认值的方法参数，并只在需要时改写显式传参调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.Optional))
        {
            if (!MethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Optional method parameter type references the delete-class target; shrink the signature and keep omitted callsites unchanged.");

            foreach (var decision in MethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 对未被已验证收缩路径覆盖的公开参数保持保守，不生成整方法删除决策。
public sealed class PublicParameterProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.type.public-parameter";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PUBLIC-PARAM-001";


    public override string Name { get; } = "Delete non-private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 当前对非私有参数整方法删除保持空操作，未支持情形交给诊断或更细粒度规则处理。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
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

        return MethodProposalSafety.IsSafeNonPrivateMethod(method);
    }
}

/// 仅收缩末尾 params 参数，并拒绝显式数组或展开形状不确定的调用。
public sealed class ParamsMethodParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.params-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PARAMS-METHOD-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink params method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 仅在所有调用都省略 params 槽位时，收缩 params 方法声明。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.ParamsOmitted))
        {
            if (!MethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Params method parameter type references the delete-class target; shrink the signature when all callsites omit the params slot.");
        }
    }
}

/// 仅在收集到全部调用点时收缩非私有方法参数。
public sealed class PublicMethodParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.public-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PUBLIC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink non-private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 为非私有方法同步收缩声明和可证明完整覆盖的调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PublicPositional))
        {
            if (!MethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Non-private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in MethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 处理局部函数的具名实参，并与局部声明同步替换。
public sealed class NamedArgumentLocalFunctionParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-local-function-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-LOCALFUNC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名参数局部函数调用，同步收缩声明与命名实参。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in LocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.NamedArgument))
        {
            if (!LocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in LocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 删除局部函数的可选参数，同时保留省略实参调用的合法性。
public sealed class OptionalParameterDefaultedLocalFunctionShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.optional-parameter-defaulted-local-function-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-OPTIONAL-LOCALFUNC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink optional local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 收缩带默认值的局部函数参数，并只改写确实需要调整的调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in LocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Optional))
        {
            if (!LocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Optional local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in LocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 删除局部函数参数并改写同一作用域内可绑定的调用点。
public sealed class LocalFunctionParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.local-function-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-LOCALFUNC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 为普通位置参数局部函数同步收缩声明与调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in LocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Positional))
        {
            if (!LocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in LocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 处理索引器具名参数的声明与元素访问同步收缩。
public sealed class NamedArgumentIndexerParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-indexer-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-INDEXER-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target when accesses use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名索引实参同步收缩索引器声明与 element access。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in IndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.NamedArgument))
        {
            if (!IndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature for named-argument accesses.");

            foreach (var decision in IndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
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

/// 仅在所有元素访问都可安全重写时收缩索引器参数。
public sealed class IndexerParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.indexer-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-INDEXER-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对位置索引实参同步收缩索引器声明与 element access。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in IndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.Positional))
        {
            if (!IndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in IndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
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

/// 处理没有方法组或 lambda 绑定链的简单委托参数删除。
public sealed class DelegateParameterShrinkProposalRule : DelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.delegate-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink delegate parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 在委托没有外部复杂绑定时，只收缩委托签名本身。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.PlainSignature))
        {
            if (!DelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the signature.");
        }
    }
}

/// 收缩委托参数并同步改写经方法组绑定的目标方法。
public sealed class MethodGroupDelegateParameterShrinkProposalRule : DelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.method-group-delegate-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-METHODGROUP-DELEGATE-PARAM-SHRINK-001";


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

    // 同步收缩委托签名以及所有 method group 绑定与调用链。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.MethodGroup))
        {
            if (!DelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its method-group targets.");

            foreach (var method in payload.MethodTargets)
            {
                var methodParameter = method.ParameterList.Parameters.ElementAtOrDefault(payload.ParameterIndex);
                if (methodParameter is null ||
                    !ParameterShrinkAnalyzer.TryBuildReplacementMethod(
                      method,
                      methodParameter,
                      out var replacementMethod))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
                  RuleId,
                  method,
                  replacementMethod,
                  "Method group target now removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var localFunction in payload.LocalFunctionTargets)
            {
                var parameter = localFunction.ParameterList.Parameters.ElementAtOrDefault(payload.ParameterIndex);
                if (parameter is null ||
                    !ParameterShrinkAnalyzer.TryBuildReplacementLocalFunction(
                      localFunction,
                      parameter,
                      out var replacementLocalFunction))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
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
                    !ParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

/// 收缩委托参数并同步改写依赖该签名的 lambda 形参。
public sealed class LambdaDelegateParameterShrinkProposalRule : DelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.lambda-delegate-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-LAMBDA-DELEGATE-PARAM-SHRINK-001";


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

    // 同步收缩委托签名以及所有 lambda 绑定与调用链。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.Lambda))
        {
            if (!DelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its lambda bindings.");

            foreach (var expression in payload.LambdaTargets)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
                if (model.GetOperation(expression, CancellationToken.None) is not IAnonymousFunctionOperation anonymousFunction ||
                    !ParameterShrinkAnalyzer.TryBuildLambdaRewrite(
                      context,
                      model,
                      expression,
                      anonymousFunction,
                      payload.ParameterIndex,
                      out var lambdaRewrite))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateExpressionReplaceDecision(
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
                    !ParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

/// 覆盖直接委托调用链，确保声明、绑定和调用实参一起收缩。
public sealed class DelegateInvocationChainParameterShrinkProposalRule : DelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.delegate-invocation-chain-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-INVOKE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink delegate parameters and direct delegate invocation chains when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩委托签名以及直接委托调用链上的实参。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.InvocationChain))
        {
            if (!DelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateDelegateReplaceDecision(
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
                    !ParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

/// 只收缩扩展方法的非接收者参数；接收者参数受调用形式约束而保留。
public sealed class ExtensionReceiverNonFirstParameterShrinkProposalRule : ExtensionMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.extension-receiver-non-first-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-EXT-NONRECV-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink non-receiver extension-method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 保持扩展方法接收者不变，只收缩非首个目标参数及其映射调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in ExtensionMethodUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks))
        {
            if (!ExtensionMethodUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Extension method non-receiver parameter type references the delete-class target; shrink the signature and keep the receiver.");

            foreach (var decision in ExtensionMethodUsageProposalHelpers.CreateInvocationReplaceDecisions(
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

/// 将接口方法签名中的目标类型删除映射为接口成员声明改写。
public sealed class InterfaceMethodProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-method";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-METHOD-001";


    public override string Name { get; } = "Delete interface methods whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接口方法签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 将接口属性签名中的目标类型删除映射为属性声明改写。
public sealed class InterfacePropertyProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-property";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-PROPERTY-001";


    public override string Name { get; } = "Delete interface properties whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接口属性签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 将接口事件签名中的目标类型删除映射为事件声明改写。
public sealed class InterfaceEventProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-event";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-EVENT-001";


    public override string Name { get; } = "Delete interface events whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接口事件签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 将接口索引器签名中的目标类型删除映射为索引器声明改写。
public sealed class InterfaceIndexerProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-indexer";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-INDEXER-001";


    public override string Name { get; } = "Delete interface indexers whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接口索引器签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 处理委托返回类型删除；参数删除由参数收缩专门规则承担。
public sealed class DelegateProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.delegate";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-001";


    public override string Name { get; } = "Delete delegates whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为委托返回类型上的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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
              "Delegate return type references the delete-class target.",
              delegateDeclaration);
        }
    }
}

/// 处理扩展接收者类型删除，并避免把实例调用改写为不等价形式。
public sealed class ExtensionReceiverProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.extension-receiver";

    public override string RuleId { get; } = "DEL-CLASS-PROP-EXT-RECV-001";


    public override string Name { get; } = "Delete extension methods whose receiver type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接收者类型命中目标类的扩展方法直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 删除基类型列表中已标记的目标类型，保留其余继承和接口项。
public sealed class BaseTypeProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.base-type";

    public override string RuleId { get; } = "DEL-CLASS-PROP-BASE-001";


    public override string Name { get; } = "Remove base-list entries whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.BaseList,
        SyntaxKind.SimpleBaseType
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为基类或接口列表中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

/// 处理泛型局部声明中的目标类型实参，且只在宿主语法可完整替换时提出决策。
public sealed class GenericTypeArgumentProposalRule : DeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.generic-type-argument";

    public override string RuleId { get; } = "DEL-CLASS-PROP-GENERIC-001";


    public override string Name { get; } = "Delete local declarations whose generic type argument references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalDeclarationStatement
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为局部泛型声明中引用目标类的类型实参直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
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

