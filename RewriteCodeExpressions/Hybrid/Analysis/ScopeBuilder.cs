using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// 管理分析阶段的作用域栈，并将节点与其作用域关联。
/// </summary>
public sealed class ScopeBuilder
{
    private readonly RewriteContext _context;
    private readonly Stack<LexicalScope> _scopeStack = new();
    private readonly Dictionary<SyntaxNode, IScope> _scopeMap = new();

    public ScopeBuilder(RewriteContext context)
    {
        _context = context;

        var rootScope = new LexicalScope(parent: null);
        _scopeStack.Push(rootScope);
        _context.CurrentScope = rootScope;
        _context.SetState(AnalysisStateKeys.ScopeRoot, rootScope);
        _context.SetState(AnalysisStateKeys.ScopeMap, _scopeMap);
    }

    public IReadOnlyDictionary<SyntaxNode, IScope> ScopeMap => _scopeMap;

    public void Enter(SyntaxNode node)
    {
        var parent = _scopeStack.Peek();
        var scope = new LexicalScope(parent);
        _scopeStack.Push(scope);
        _context.CurrentScope = scope;
        _scopeMap[node] = scope;
    }

    public void Exit()
    {
        if (_scopeStack.Count <= 1)
        {
            return;
        }

        _scopeStack.Pop();
        _context.CurrentScope = _scopeStack.Peek();
    }

    public void Declare(string symbolName)
    {
        _scopeStack.Peek().Declare(symbolName);
    }
}
