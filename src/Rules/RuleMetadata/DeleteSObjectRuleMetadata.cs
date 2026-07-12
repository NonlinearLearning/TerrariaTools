namespace Rules;

public static class DeleteSObjectRuleMetadata
{
  public const string GroupKey = "DEL-SOBJ";

  public const string IdentifierNameMarkRuleId = "DEL-SOBJ-MARK-ID-001";
  public const string ThisExpressionMarkRuleId = "DEL-SOBJ-MARK-THIS-001";
  public const string BaseExpressionMarkRuleId = "DEL-SOBJ-MARK-BASE-001";
  public const string VariableDeclaratorMarkRuleId = "DEL-SOBJ-MARK-DECL-001";
  public const string NumericLiteralMarkRuleId = "DEL-SOBJ-MARK-LIT-NUM-001";
  public const string StringLiteralMarkRuleId = "DEL-SOBJ-MARK-LIT-STR-001";
  public const string TrueLiteralMarkRuleId = "DEL-SOBJ-MARK-LIT-TRUE-001";
  public const string FalseLiteralMarkRuleId = "DEL-SOBJ-MARK-LIT-FALSE-001";
  public const string NullLiteralMarkRuleId = "DEL-SOBJ-MARK-LIT-NULL-001";
  public const string MemberAccessMarkRuleId = "DEL-SOBJ-MARK-MEMBER-001";
  public const string MemberBindingMarkRuleId = "DEL-SOBJ-MARK-BINDING-001";
  public const string InvocationMarkRuleId = "DEL-SOBJ-MARK-INVOKE-001";
  public const string ObjectCreationMarkRuleId = "DEL-SOBJ-MARK-NEW-001";
  public const string ImplicitObjectCreationMarkRuleId = "DEL-SOBJ-MARK-IMPLICIT-NEW-001";
  public const string ElementAccessMarkRuleId = "DEL-SOBJ-MARK-ELEMENT-001";
  public const string ConditionalAccessMarkRuleId = "DEL-SOBJ-MARK-CONDITIONAL-001";
  public const string AssignmentLeftValuePropagationRuleId = "DEL-SOBJ-PROP-ASSIGN-LHS-001";
  public const string DefinitionInitializerPropagationRuleId = "DEL-SOBJ-PROP-DECL-INIT-001";
  public const string LogicalPropagationRuleId = "DEL-SOBJ-PROP-LOGIC-001";
  public const string LogicalOperandGroupPropagationRuleId = "DEL-SOBJ-PROP-LOGIC-GROUP-001";
  public const string SymbolReferencePropagationRuleId = "DEL-SOBJ-PROP-SYMBOL-001";
  public const string IfStructureCompletionPropagationRuleId = "DEL-SOBJ-PROP-IF-COMPLETE-001";
  public const string HostLiftRuleId = "DEL-SOBJ-LIFT-HOST-001";
  public const string IfStructureLiftRuleId = "DEL-SOBJ-LIFT-IF-001";
  public const string SwitchStructureLiftRuleId = "DEL-SOBJ-LIFT-SWITCH-001";
  public const string LogicalProposalRuleId = "DEL-SOBJ-PROPOSE-LOGIC-001";
  public const string IfStructureProposalRuleId = "DEL-SOBJ-PROPOSE-IF-001";
  public const string ControlStructureProposalRuleId = "DEL-SOBJ-PROPOSE-CTRL-001";
  public const string DefaultDeleteProposalRuleId = "DEL-SOBJ-PROPOSE-DEFAULT-001";
}
