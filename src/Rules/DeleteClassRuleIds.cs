namespace Rules;

public static class DeleteClassRuleIds
{
    public const string GroupKey = "DEL-CLASS";

    public const string DeclarationMarkRuleId = "DEL-CLASS-MARK-DECL-001";
    public const string ExpressionMarkRuleId = "DEL-CLASS-MARK-EXPR-001";
    public const string TypeSyntaxMarkRuleId = "DEL-CLASS-MARK-TYPE-001";

    public const string HostLiftRuleId = "DEL-CLASS-LIFT-HOST-001";
    public const string IfStructureLiftRuleId = "DEL-CLASS-LIFT-IF-001";
    public const string SwitchStructureLiftRuleId = "DEL-CLASS-LIFT-SWITCH-001";

    public const string ObjectCreationDeclarationPropagationRuleId = "DEL-CLASS-PROP-NEW-DECL-001";
    public const string LocalReferencePropagationRuleId = "DEL-CLASS-PROP-LOCAL-REF-001";
    public const string MethodParameterUsagePropagationRuleId = "DEL-CLASS-PROP-METHOD-PARAM-USAGE-001";
    public const string LocalFunctionParameterUsagePropagationRuleId = "DEL-CLASS-PROP-LOCALFUNC-PARAM-USAGE-001";
    public const string IndexerParameterUsagePropagationRuleId = "DEL-CLASS-PROP-INDEXER-PARAM-USAGE-001";
    public const string DelegateUsageClassificationPropagationRuleId = "DEL-CLASS-PROP-DELEGATE-USAGE-001";
    public const string ExtensionMethodMappedCallsitePropagationRuleId = "DEL-CLASS-PROP-EXT-MAPPED-001";
    public const string DeclarationHostPropagationRuleId = "DEL-CLASS-PROP-DECL-HOST-001";
    public const string IfStructureCompletionPropagationRuleId = "DEL-CLASS-PROP-IF-COMPLETE-001";

    public const string DefaultDeleteProposalRuleId = "DEL-CLASS-PROP-DEFAULT-001";
    public const string ControlStructureProposalRuleId = "DEL-CLASS-PROP-CTRL-001";
    public const string IfStructureProposalRuleId = "DEL-CLASS-PROP-IF-001";
    public const string TypeSyntaxDeclarationProposalRuleId = "DEL-CLASS-PROP-TYPE-DECL-001";
    public const string MethodReturnTypeProposalRuleId = "DEL-CLASS-PROP-RETURN-001";
    public const string PublicMethodReturnTypeProposalRuleId = "DEL-CLASS-PROP-PUBLIC-RETURN-001";
    public const string ParameterProposalRuleId = "DEL-CLASS-PROP-PARAM-001";
    public const string PrivateMethodParameterShrinkProposalRuleId = "DEL-CLASS-PROP-PRIVATE-PARAM-SHRINK-001";
    public const string NamedArgumentMethodParameterShrinkProposalRuleId = "DEL-CLASS-PROP-NAMED-METHOD-PARAM-SHRINK-001";
    public const string OptionalParameterDefaultedMethodShrinkProposalRuleId = "DEL-CLASS-PROP-OPTIONAL-METHOD-PARAM-SHRINK-001";
    public const string ParamsMethodParameterShrinkProposalRuleId = "DEL-CLASS-PROP-PARAMS-METHOD-PARAM-SHRINK-001";
    public const string PublicMethodParameterShrinkProposalRuleId = "DEL-CLASS-PROP-PUBLIC-PARAM-SHRINK-001";
    public const string LocalFunctionParameterShrinkProposalRuleId = "DEL-CLASS-PROP-LOCALFUNC-PARAM-SHRINK-001";
    public const string NamedArgumentLocalFunctionParameterShrinkProposalRuleId = "DEL-CLASS-PROP-NAMED-LOCALFUNC-PARAM-SHRINK-001";
    public const string OptionalParameterDefaultedLocalFunctionShrinkProposalRuleId = "DEL-CLASS-PROP-OPTIONAL-LOCALFUNC-PARAM-SHRINK-001";
    public const string DelegateParameterShrinkProposalRuleId = "DEL-CLASS-PROP-DELEGATE-PARAM-SHRINK-001";
    public const string IndexerParameterShrinkProposalRuleId = "DEL-CLASS-PROP-INDEXER-PARAM-SHRINK-001";
    public const string NamedArgumentIndexerParameterShrinkProposalRuleId = "DEL-CLASS-PROP-NAMED-INDEXER-PARAM-SHRINK-001";
    public const string MethodGroupDelegateParameterShrinkProposalRuleId = "DEL-CLASS-PROP-METHODGROUP-DELEGATE-PARAM-SHRINK-001";
    public const string LambdaDelegateParameterShrinkProposalRuleId = "DEL-CLASS-PROP-LAMBDA-DELEGATE-PARAM-SHRINK-001";
    public const string DelegateInvocationChainParameterShrinkProposalRuleId = "DEL-CLASS-PROP-DELEGATE-INVOKE-PARAM-SHRINK-001";
    public const string ExtensionReceiverNonFirstParameterShrinkProposalRuleId = "DEL-CLASS-PROP-EXT-NONRECV-PARAM-SHRINK-001";
    public const string PublicParameterProposalRuleId = "DEL-CLASS-PROP-PUBLIC-PARAM-001";
    public const string InterfaceMethodProposalRuleId = "DEL-CLASS-PROP-IFACE-METHOD-001";
    public const string InterfacePropertyProposalRuleId = "DEL-CLASS-PROP-IFACE-PROPERTY-001";
    public const string InterfaceEventProposalRuleId = "DEL-CLASS-PROP-IFACE-EVENT-001";
    public const string InterfaceIndexerProposalRuleId = "DEL-CLASS-PROP-IFACE-INDEXER-001";
    public const string DelegateProposalRuleId = "DEL-CLASS-PROP-DELEGATE-001";
    public const string ExtensionReceiverProposalRuleId = "DEL-CLASS-PROP-EXT-RECV-001";
    public const string BaseTypeProposalRuleId = "DEL-CLASS-PROP-BASE-001";
    public const string GenericTypeArgumentProposalRuleId = "DEL-CLASS-PROP-GENERIC-001";
}
