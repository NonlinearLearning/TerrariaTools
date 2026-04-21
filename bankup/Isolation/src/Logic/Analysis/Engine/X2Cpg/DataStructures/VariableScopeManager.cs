using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.X2Cpg.DataStructures;

/// <summary>
/// 管理 CPG 转换阶段的变量作用域和延迟引用。
///
/// 对应 Joern `VariableScopeManager.scala`。它解决两个问题：
/// 1. 标识符使用点可以找到最近的变量声明。
/// 2. 如果引用先出现，声明后出现，可以在最后统一补 `Ref` 边。
/// </summary>
public sealed class VariableScopeManager
{
    private readonly List<ScopeFrame> stack = new();
    private readonly List<PendingReference> pendingReferences = new();

    /// <summary>
    /// 计算当前方法嵌套路径。
    /// </summary>
    public string ComputeScopePath()
    {
        return string.Join(
            ":",
            stack.Where(frame => frame.Type == ScopeType.MethodScope)
                .Reverse<ScopeFrame>()
                .Select(frame => frame.Name));
    }

    /// <summary>
    /// 推入方法作用域。
    /// </summary>
    public void PushNewMethodScope(string methodFullName, string methodName, CpgNode scopeNode, CpgNode? capturingRefNode = null, bool isStatic = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(scopeNode);
        stack.Insert(0, new ScopeFrame(ScopeType.MethodScope, methodName, methodFullName, scopeNode, capturingRefNode, isStatic));
    }

    /// <summary>
    /// 推入块作用域。
    /// </summary>
    public void PushNewBlockScope(CpgNode scopeNode)
    {
        ArgumentNullException.ThrowIfNull(scopeNode);
        stack.Insert(0, new ScopeFrame(ScopeType.BlockScope, "block", string.Empty, scopeNode, capturingRefNode: null, isStatic: false));
    }

    /// <summary>
    /// 推入类型声明作用域。
    /// </summary>
    public void PushNewTypeDeclScope(string name, string fullName, CpgNode scopeNode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(scopeNode);
        stack.Insert(0, new ScopeFrame(ScopeType.TypeDeclScope, name, fullName, scopeNode, capturingRefNode: null, isStatic: false));
    }

    /// <summary>
    /// 弹出当前作用域。
    /// </summary>
    public void PopScope()
    {
        if (stack.Count > 0)
        {
            stack.RemoveAt(0);
        }
    }

    /// <summary>
    /// 添加变量到当前作用域。
    /// </summary>
    public void AddVariable(string variableName, CpgNode variableNode, string typeFullName, string evaluationStrategy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentNullException.ThrowIfNull(variableNode);
        if (stack.Count == 0)
        {
            throw new InvalidOperationException("没有可写入变量的作用域。");
        }

        stack[0].Variables[variableName] = new ScopedVariable(variableNode, typeFullName, evaluationStrategy);
    }

    /// <summary>
    /// 查找变量。
    /// </summary>
    public (CpgNode Node, string TypeFullName)? LookupVariable(string identifier)
    {
        ScopedVariable? variable = VariableFromStack(identifier);
        return variable is null ? null : (variable.Node, variable.TypeFullName);
    }

    /// <summary>
    /// 判断变量是否在当前方法作用域中。
    /// </summary>
    public bool VariableIsInMethodScope(string identifier)
    {
        ScopeFrame? methodScope = stack.FirstOrDefault(frame => frame.Type == ScopeType.MethodScope);
        return methodScope?.Variables.ContainsKey(identifier) == true;
    }

    /// <summary>
    /// 添加一个待解析引用。
    /// </summary>
    public void AddReference(string variableName, CpgNode referenceNode, string typeFullName, string evaluationStrategy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentNullException.ThrowIfNull(referenceNode);
        pendingReferences.Add(new PendingReference(variableName, referenceNode, typeFullName, evaluationStrategy));
    }

    /// <summary>
    /// 解析待处理引用并补 `Ref` 边。
    /// </summary>
    public void ResolvePendingReferences(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        foreach (PendingReference pendingReference in pendingReferences.ToArray())
        {
            ScopedVariable? variable = VariableFromStack(pendingReference.VariableName);
            if (variable is null)
            {
                continue;
            }

            bool exists = graph.GetOutgoingEdges(pendingReference.ReferenceNode.Id, CpgEdgeKind.Ref)
                .Any(edge => edge.TargetId == variable.Node.Id);
            if (!exists)
            {
                graph.AddEdge(pendingReference.ReferenceNode.Id, variable.Node.Id, CpgEdgeKind.Ref);
            }

            AddCaptureEdgesForOuterReferences(graph, pendingReference.ReferenceNode, variable.Node);

            pendingReferences.Remove(pendingReference);
        }
    }

    private void AddCaptureEdgesForOuterReferences(CpgGraph graph, CpgNode referenceNode, CpgNode variableNode)
    {
        int referenceMethodIndex = stack.FindIndex(frame => frame.Type == ScopeType.MethodScope);
        int variableScopeIndex = stack.FindIndex(frame => frame.Variables.Values.Any(variable => variable.Node.Id == variableNode.Id));

        if (referenceMethodIndex < 0 || variableScopeIndex < 0 || variableScopeIndex <= referenceMethodIndex)
        {
            return;
        }

        foreach (ScopeFrame methodFrame in stack.Take(variableScopeIndex).Where(frame => frame.Type == ScopeType.MethodScope))
        {
            if (methodFrame.CapturingRefNode is null)
            {
                continue;
            }

            bool captureExists = graph.GetOutgoingEdges(methodFrame.CapturingRefNode.Id, CpgEdgeKind.Capture)
                .Any(edge => edge.TargetId == variableNode.Id);
            if (!captureExists)
            {
                graph.AddEdge(methodFrame.CapturingRefNode.Id, variableNode.Id, CpgEdgeKind.Capture);
            }
        }
    }

    private ScopedVariable? VariableFromStack(string identifier)
    {
        foreach (ScopeFrame frame in stack)
        {
            if (frame.Variables.TryGetValue(identifier, out ScopedVariable? variable))
            {
                return variable;
            }
        }

        return null;
    }

    private sealed class ScopeFrame
    {
        public ScopeFrame(ScopeType type, string name, string fullName, CpgNode scopeNode, CpgNode? capturingRefNode, bool isStatic)
        {
            Type = type;
            Name = name;
            FullName = fullName;
            ScopeNode = scopeNode;
            CapturingRefNode = capturingRefNode;
            IsStatic = isStatic;
        }

        public ScopeType Type { get; }

        public string Name { get; }

        public string FullName { get; }

        public CpgNode ScopeNode { get; }

        public CpgNode? CapturingRefNode { get; }

        public bool IsStatic { get; }

        public Dictionary<string, ScopedVariable> Variables { get; } = new(StringComparer.Ordinal);
    }

    private sealed record ScopedVariable(CpgNode Node, string TypeFullName, string EvaluationStrategy);

    private sealed record PendingReference(string VariableName, CpgNode ReferenceNode, string TypeFullName, string EvaluationStrategy);
}
