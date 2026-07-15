namespace MinimalRoslynCpg.Contracts;

[Flags]
public enum RoslynCpgCapability
{
    None = 0,
    SyntaxSemantic = 1 << 0,
    MethodModel = 1 << 1,
    CallTargets = 1 << 2,
    Cfg = 1 << 3,
    DataFlow = 1 << 4,
    Dominance = 1 << 5,
    ControlDependence = 1 << 6,
    QueryIndex = 1 << 7,
    Default = SyntaxSemantic |
              MethodModel |
              CallTargets |
              Cfg |
              DataFlow |
              QueryIndex,
    All = Default | Dominance | ControlDependence,
}
