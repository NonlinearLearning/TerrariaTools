namespace MinimalRoslynCpg.Contracts;

public enum RoslynCpgDispatchCategory
{
  Method,
  DecisionAction,
}

[Flags]
public enum RoslynCpgDispatchFlags
{
  None = 0,
  Internal = 1 << 0,
  External = 1 << 1,
  Static = 1 << 2,
  Instance = 1 << 3,
  Extension = 1 << 4,
  Interface = 1 << 5,
  InterfaceImplementation = 1 << 6,
  Override = 1 << 7,
  Virtual = 1 << 8,
  Abstract = 1 << 9,
  Definition = 1 << 10,
  Dispatch = 1 << 11,
  PropertyGet = 1 << 12,
  PropertySet = 1 << 13,
  PropertyAccessor = 1 << 14,
  Indexer = 1 << 15,
  Exact = 1 << 16,
  ReceiverExact = 1 << 17,
  Hierarchy = 1 << 18,
  Fallback = 1 << 19,
  ExternalFallback = 1 << 20,
}

public enum RoslynCpgDecisionActionKind
{
  Skip = 0,
  Delete = 1,
  Replace = 2,
}

public readonly record struct RoslynCpgDispatchKind(
  RoslynCpgDispatchCategory Category,
  RoslynCpgDispatchFlags Flags = RoslynCpgDispatchFlags.None,
  RoslynCpgDecisionActionKind? Action = null)
{
  public static RoslynCpgDispatchKind ForDecisionAction(RoslynCpgDecisionActionKind action)
  {
    return new RoslynCpgDispatchKind(
      RoslynCpgDispatchCategory.DecisionAction,
      RoslynCpgDispatchFlags.None,
      action);
  }

  public override string ToString()
  {
    return Category switch
    {
      RoslynCpgDispatchCategory.Method => FormatMethodDispatch(),
      RoslynCpgDispatchCategory.DecisionAction => Action?.ToString() ?? string.Empty,
      _ => string.Empty,
    };
  }

  private string FormatMethodDispatch()
  {
    var segments = new List<string>();
    if ((Flags & RoslynCpgDispatchFlags.Internal) != 0)
    {
      segments.Add("internal");
    }
    else if ((Flags & RoslynCpgDispatchFlags.External) != 0)
    {
      segments.Add("external");
    }

    if ((Flags & RoslynCpgDispatchFlags.Extension) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Instance) != 0
        ? "extension-instance"
        : "extension-static");
    }
    else if ((Flags & RoslynCpgDispatchFlags.InterfaceImplementation) != 0)
    {
      segments.Add("interface-implementation");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Interface) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Definition) != 0
        ? "interface-definition"
        : "interface-dispatch");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Override) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Definition) != 0
        ? "override-definition"
        : "override-dispatch");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Abstract) != 0)
    {
      segments.Add("abstract-definition");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Virtual) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Definition) != 0
        ? "virtual-definition"
        : "virtual-dispatch");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Definition) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Static) != 0
        ? "static-definition"
        : "instance-definition");
    }
    else
    {
      segments.Add("static");
    }

    if ((Flags & RoslynCpgDispatchFlags.PropertyGet) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Indexer) != 0
        ? "indexer-get"
        : "property-get");
    }
    else if ((Flags & RoslynCpgDispatchFlags.PropertySet) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Indexer) != 0
        ? "indexer-set"
        : "property-set");
    }
    else if ((Flags & RoslynCpgDispatchFlags.PropertyAccessor) != 0)
    {
      segments.Add((Flags & RoslynCpgDispatchFlags.Indexer) != 0
        ? "indexer-accessor"
        : "property-accessor");
    }

    if ((Flags & RoslynCpgDispatchFlags.ExternalFallback) != 0)
    {
      segments.Add("external-fallback");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Exact) != 0)
    {
      segments.Add("exact");
    }
    else if ((Flags & RoslynCpgDispatchFlags.ReceiverExact) != 0)
    {
      segments.Add("receiver-exact");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Hierarchy) != 0)
    {
      segments.Add("hierarchy");
    }
    else if ((Flags & RoslynCpgDispatchFlags.Fallback) != 0)
    {
      segments.Add("fallback");
    }

    return string.Join("-", segments);
  }
}
