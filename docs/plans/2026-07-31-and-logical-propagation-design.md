# And Logical Propagation Design

## Goal

When one operand of `&&` is a deletion target, propagate a target-expression
fact to its sibling operand. Do not cross an `||` boundary.

## Data Flow

`LogicalExpressionPropagationRule` keeps its existing host fact and, for a
logical-and host, emits one sibling fact on `TargetExpression`. Fixed-point
execution lets the sibling fact participate in the normal Lift coverage proof.
The logical-or branch emits no sibling fact.

## Acceptance

- `CanExecuteCommand() && PlayerInput.Triggers.JustPressed.Grapple` receives a
  propagated fact for `CanExecuteCommand()` when the right operand is targeted.
- The complete `&&` condition can produce the normal enclosing `if` deletion.
- An equivalent `||` condition does not propagate a fact to its sibling and
  retains its existing logical reduction behavior.
- Focused Host propagation and decision tests pass.
