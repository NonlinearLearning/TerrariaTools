using NLISSN.Application;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;

namespace NLISSN.Composition;

/// 从默认阶段列表构建确定性的规则管道。
public static class RuleRegistry
{
    // 汇总整条规则管道声明的 CPG 能力需求，供调用方决定要构哪些图能力。
    public static IReadOnlyList<NLCPG.Contracts.NLCPGCapability> GetRequiredCapabilities(RulePipeline pipeline)
    {
        return pipeline.GetRequiredCapabilities();
    }

    // 基于默认阶段列表创建稳定排序后的规则管道，并按名称排除禁用规则。
    public static RulePipeline CreateDefaultRules(IEnumerable<string>? disabledRuleTypes = null)
    {
        var markers = new RuleDefinitionMark[]
        {
            new SObjectIdentifierNameMarkRule(),
            new SObjectThisExpressionMarkRule(),
            new SObjectBaseExpressionMarkRule(),
            new SObjectVariableDeclaratorMarkRule(),
            new SObjectNumericLiteralMarkRule(),
            new SObjectStringLiteralMarkRule(),
            new SObjectTrueLiteralMarkRule(),
            new SObjectFalseLiteralMarkRule(),
            new SObjectNullLiteralMarkRule(),
            new SObjectMemberAccessMarkRule(),
            new SObjectMemberBindingMarkRule(),
            new SObjectInvocationMarkRule(),
            new SObjectObjectCreationMarkRule(),
            new SObjectImplicitObjectCreationMarkRule(),
            new SObjectElementAccessMarkRule(),
            new SObjectConditionalAccessMarkRule(),
            new ClassDeclarationMarkRule(),
            new ClassExpressionMarkRule(),
            new ClassTypeSyntaxMarkRule(),
            new UnreachableMethodMarkRule(),
            new UnreferencedMethodMarkRule(),
            new ClearUnusedInterfaceImplementationRule(),
            new PrivatizeInternalOnlyPublicMethodRule()
        }.ToList();
        var propagators = new RuleDefinitionPropagate[]
        {
            new SObjectAssignmentLeftValuePropagationRule(),
            new SObjectDefinitionInitializerPropagationRule(),
            new SObjectLogicalConditionPropagationRule(),
            new SObjectLogicalOperandGroupPropagationRule(),
            new SObjectSymbolReferencePropagationRule(),
            new SObjectIfStructureCompletionPropagationRule(),
            new ClassDeclarationHostPropagationRule(),
            new ClassDelegateUsageClassificationPropagationRule(),
            new ClassExtensionMethodMappedCallsitePropagationRule(),
            new ClassIfStructureCompletionPropagationRule(),
            new ClassIndexerParameterUsagePropagationRule(),
            new ClassLocalFunctionParameterUsagePropagationRule(),
            new ClassMethodParameterUsagePropagationRule(),
            new ClassObjectCreationDeclarationPropagationRule(),
            new ClassSymbolReferencePropagationRule()
        }.ToList();
        var lifters = new RuleDefinitionLift[]
        {
            new SObjectExpressionHostLiftingRule(),
            new SObjectIfStructureLiftingRule(),
            new SObjectSwitchStructureLiftingRule(),
            new ClassExpressionHostLiftingRule(),
            new ClassIfStructureLiftingRule(),
            new ClassSwitchStructureLiftingRule()
        }.ToList();
        var proposers = new RuleDefinitionPropose[]
        {
            new LogicalExpressionProposalRule(),
            new IfStructureProposalRule(),
            new ControlStructureRemovalProposalRule(),
            new DefaultRemovalProposalRule(),
            new ClassDefaultRemovalProposalRule(),
            new ClassControlStructureRemovalProposalRule(),
            new ClassTypeSyntaxDeclarationProposalRule(),
            new ClassMethodReturnTypeProposalRule(),
            new ClassPublicMethodReturnTypeProposalRule(),
            new ClassParameterProposalRule(),
            new ClassPrivateMethodParameterShrinkProposalRule(),
            new ClassNamedArgumentMethodParameterShrinkProposalRule(),
            new ClassOptionalParameterDefaultedMethodShrinkProposalRule(),
            new ClassPublicParameterProposalRule(),
            new ClassParamsMethodParameterShrinkProposalRule(),
            new ClassPublicMethodParameterShrinkProposalRule(),
            new ClassNamedArgumentLocalFunctionParameterShrinkProposalRule(),
            new ClassOptionalParameterDefaultedLocalFunctionShrinkProposalRule(),
            new ClassLocalFunctionParameterShrinkProposalRule(),
            new ClassNamedArgumentIndexerParameterShrinkProposalRule(),
            new ClassIndexerParameterShrinkProposalRule(),
            new ClassDelegateParameterShrinkProposalRule(),
            new ClassMethodGroupDelegateParameterShrinkProposalRule(),
            new ClassLambdaDelegateParameterShrinkProposalRule(),
            new ClassDelegateInvocationChainParameterShrinkProposalRule(),
            new ClassExtensionReceiverNonFirstParameterShrinkProposalRule(),
            new ClassInterfaceMethodProposalRule(),
            new ClassInterfacePropertyProposalRule(),
            new ClassInterfaceEventProposalRule(),
            new ClassInterfaceIndexerProposalRule(),
            new ClassDelegateProposalRule(),
            new ClassExtensionReceiverProposalRule(),
            new ClassBaseTypeProposalRule(),
            new ClassGenericTypeArgumentProposalRule(),
            new ClassIfStructureProposalRule(),
            new UnreachableMethodProposalRule(),
            new UnreferencedMethodProposalRule(),
            new ClearUnusedInterfaceImplementationProposalRule(),
            new PrivatizeInternalOnlyPublicMethodProposalRule()
        }.ToList();

        RuleCatalog.ValidateRules(markers.Cast<IRuleDefinition>()
          .Concat(propagators)
          .Concat(lifters)
          .Concat(proposers));
        var disabledTypeNames = (disabledRuleTypes ?? Array.Empty<string>())
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new RulePipeline(
          Markers: CreateRules(markers, disabledTypeNames),
          Propagators: CreateRules(propagators, disabledTypeNames),
          Lifters: CreateRules(lifters, disabledTypeNames),
          Proposers: CreateRules(proposers, disabledTypeNames),
          DisabledMarkers: FindDisabled(markers, disabledTypeNames),
          DisabledPropagators: FindDisabled(propagators, disabledTypeNames),
          DisabledLifters: FindDisabled(lifters, disabledTypeNames),
          DisabledProposers: FindDisabled(proposers, disabledTypeNames));
    }

    private static IReadOnlyList<TRule> CreateRules<TRule>(IEnumerable<TRule> rules, IReadOnlySet<string> disabledTypeNames)
      where TRule : class
    {
        return rules
          .Where(rule => !disabledTypeNames.Contains(rule.GetType().Name))
          .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
          .ToList();
    }

    private static IReadOnlyList<TRule> FindDisabled<TRule>(
      IEnumerable<TRule> rules,
      IReadOnlySet<string> disabledTypeNames)
      where TRule : class
    {
        return rules
          .Where(rule => disabledTypeNames.Contains(rule.GetType().Name))
          .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
          .ToList();
    }
}
