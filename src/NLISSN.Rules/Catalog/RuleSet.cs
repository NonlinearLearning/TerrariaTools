namespace NLISSN.Rules;

public interface IRuleSet
{
  string Id { get; }

  IReadOnlyList<RuleDefinitionMark> Markers { get; }

  IReadOnlyList<RuleDefinitionPropagate> Propagators { get; }

  IReadOnlyList<RuleDefinitionLift> Lifters { get; }

  IReadOnlyList<RuleDefinitionPropose> Proposers { get; }
}

public abstract class RuleSetDefinition : IRuleSet
{
  public abstract string Id { get; }

  public virtual IReadOnlyList<RuleDefinitionMark> Markers => Array.Empty<RuleDefinitionMark>();

  public virtual IReadOnlyList<RuleDefinitionPropagate> Propagators => Array.Empty<RuleDefinitionPropagate>();

  public virtual IReadOnlyList<RuleDefinitionLift> Lifters => Array.Empty<RuleDefinitionLift>();

  public virtual IReadOnlyList<RuleDefinitionPropose> Proposers => Array.Empty<RuleDefinitionPropose>();
}

public sealed class SObjectRuleSet : RuleSetDefinition
{
  public override string Id => "s-object";

  public override IReadOnlyList<RuleDefinitionMark> Markers { get; } =
    new RuleDefinitionMark[]
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
      new SObjectConditionalAccessMarkRule()
    };

  public override IReadOnlyList<RuleDefinitionPropagate> Propagators { get; } =
    new RuleDefinitionPropagate[]
    {
      new SObjectAssignmentLeftValuePropagationRule(),
      new SObjectDefinitionInitializerPropagationRule(),
      new SObjectLogicalConditionPropagationRule(),
      new SObjectLogicalOperandGroupPropagationRule(),
      new SObjectSymbolReferencePropagationRule(),
      new SObjectIfStructureCompletionPropagationRule()
    };

  public override IReadOnlyList<RuleDefinitionLift> Lifters { get; } =
    new RuleDefinitionLift[]
    {
      new SObjectExpressionHostLiftingRule(),
      new SObjectIfStructureLiftingRule(),
      new SObjectSwitchStructureLiftingRule()
    };

  public override IReadOnlyList<RuleDefinitionPropose> Proposers { get; } =
    new RuleDefinitionPropose[]
    {
      new LogicalExpressionProposalRule(),
      new IfStructureProposalRule(),
      new ControlStructureRemovalProposalRule(),
      new DefaultRemovalProposalRule()
    };
}

public sealed class ClassRuleSet : RuleSetDefinition
{
  public override string Id => "class";

  public override IReadOnlyList<RuleDefinitionMark> Markers { get; } =
    new RuleDefinitionMark[]
    {
      new ClassDeclarationMarkRule(),
      new ClassExpressionMarkRule(),
      new ClassTypeSyntaxMarkRule()
    };

  public override IReadOnlyList<RuleDefinitionPropagate> Propagators { get; } =
    new RuleDefinitionPropagate[]
    {
      new ClassDeclarationHostPropagationRule(),
      new ClassDelegateUsageClassificationPropagationRule(),
      new ClassExtensionMethodMappedCallsitePropagationRule(),
      new ClassIfStructureCompletionPropagationRule(),
      new ClassIndexerParameterUsagePropagationRule(),
      new ClassLocalFunctionParameterUsagePropagationRule(),
      new ClassMethodParameterUsagePropagationRule(),
      new ClassObjectCreationDeclarationPropagationRule(),
      new ClassSymbolReferencePropagationRule()
    };

  public override IReadOnlyList<RuleDefinitionLift> Lifters { get; } =
    new RuleDefinitionLift[]
    {
      new ClassExpressionHostLiftingRule(),
      new ClassIfStructureLiftingRule(),
      new ClassSwitchStructureLiftingRule()
    };

  public override IReadOnlyList<RuleDefinitionPropose> Proposers { get; } =
    new RuleDefinitionPropose[]
    {
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
      new ClassIfStructureProposalRule()
    };
}

public sealed class UnreachableMethodRuleSet : RuleSetDefinition
{
  public override string Id => "unreachable-method";

  public override IReadOnlyList<RuleDefinitionMark> Markers { get; } =
    new RuleDefinitionMark[] { new UnreachableMethodMarkRule() };

  public override IReadOnlyList<RuleDefinitionPropose> Proposers { get; } =
    new RuleDefinitionPropose[] { new UnreachableMethodProposalRule() };
}

public sealed class UnreferencedMethodRuleSet : RuleSetDefinition
{
  public override string Id => "unreferenced-method";

  public override IReadOnlyList<RuleDefinitionMark> Markers { get; } =
    new RuleDefinitionMark[] { new UnreferencedMethodMarkRule() };

  public override IReadOnlyList<RuleDefinitionPropose> Proposers { get; } =
    new RuleDefinitionPropose[] { new UnreferencedMethodProposalRule() };
}

public sealed class ClearUnusedInterfaceImplementationRuleSet : RuleSetDefinition
{
  public override string Id => "clear-unused-interface-implementation";

  public override IReadOnlyList<RuleDefinitionMark> Markers { get; } =
    new RuleDefinitionMark[] { new ClearUnusedInterfaceImplementationRule() };

  public override IReadOnlyList<RuleDefinitionPropose> Proposers { get; } =
    new RuleDefinitionPropose[] { new ClearUnusedInterfaceImplementationProposalRule() };
}

public sealed class PrivatizeInternalOnlyPublicMethodRuleSet : RuleSetDefinition
{
  public override string Id => "privatize-internal-only-public-method";

  public override IReadOnlyList<RuleDefinitionMark> Markers { get; } =
    new RuleDefinitionMark[] { new PrivatizeInternalOnlyPublicMethodRule() };

  public override IReadOnlyList<RuleDefinitionPropose> Proposers { get; } =
    new RuleDefinitionPropose[] { new PrivatizeInternalOnlyPublicMethodProposalRule() };
}

public static class DefaultRuleSets
{
  public static IReadOnlyList<IRuleSet> Create()
  {
    return new IRuleSet[]
    {
      new SObjectRuleSet(),
      new ClassRuleSet(),
      new UnreachableMethodRuleSet(),
      new UnreferencedMethodRuleSet(),
      new ClearUnusedInterfaceImplementationRuleSet(),
      new PrivatizeInternalOnlyPublicMethodRuleSet()
    };
  }
}
