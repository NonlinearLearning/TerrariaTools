namespace MinimalRoslynCpg.Contracts;

/// <summary>
/// Identifies the resolved boundary relation represented by an interprocedural flow edge.
/// </summary>
public enum RoslynCpgInterproceduralBridgeKind
{
  ArgumentToParameter,
  ReturnToMethodReturn,
  MethodReturnToCallResult,
}
