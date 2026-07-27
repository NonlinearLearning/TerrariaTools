using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
internal sealed class ControlFlowPass : INLCPGPass
{
  internal static ControlFlowPass Instance { get; } = new();

  private ControlFlowPass()
  {
  }

  public string Name => nameof(ControlFlowPass);

  // 触发控制流 pass，为方法边界和结构化分支补 CFG 边。
  public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
  {
    builder.RunControlFlowPass(context);
  }
}
}

namespace NLCPG.Builder
{
public sealed partial class NLCPGBuilder
{
    internal void RunControlFlowPass(NLCPGBuildContext context)
    {
        AddMethodLevelControlFlow(context);
    }

    private void AddMethodLevelControlFlow(NLCPGBuildContext context)
    {
        var graph = context.Graph;
        foreach (var rootPlan in GetOperationRootPlans(context.Root, context.SemanticModel))
        {
            if (context.SemanticModel.GetOperation(rootPlan.BodySyntax) is not IBlockOperation methodBlock ||
                !IsMethodRootBlock(methodBlock))
            {
                continue;
            }

            if (rootPlan.OwningMethod is IMethodSymbol methodSymbol)
            {
                var entryNode = GetOrCreateMethodEntryNode(methodSymbol, graph);
                var parameterNodes = methodSymbol.Parameters
                  .Select(parameter => GetOrCreateMethodParameterNode(methodSymbol, parameter, graph))
                  .ToList();
                var returnNode = GetOrCreateMethodReturnNode(methodSymbol, graph);
                var exitNode = GetOrCreateMethodExitNode(methodSymbol, graph);
                var firstOperation = FirstExecutableOperation(methodBlock);

                // 先串起 entry -> parameters -> 首个可执行节点；空方法则直接落到 return。
                if (parameterNodes.Count > 0)
                {
                    AddControlFlowEdge(entryNode, parameterNodes[0], NLCPGEdgeKind.CfgNext, graph);
                    for (var index = 0; index < parameterNodes.Count - 1; index += 1)
                    {
                        AddControlFlowEdge(parameterNodes[index], parameterNodes[index + 1], NLCPGEdgeKind.CfgNext, graph);
                    }

                    if (firstOperation is not null)
                    {
                        AddControlFlowEdge(
                          parameterNodes[^1],
                          GetOrCreateOperationNode(firstOperation, graph),
                          NLCPGEdgeKind.CfgNext,
                          graph);
                    }
                    else
                    {
                        AddControlFlowEdge(parameterNodes[^1], returnNode, NLCPGEdgeKind.CfgNext, graph);
                    }
                }
                else if (firstOperation is not null)
                {
                    AddControlFlowEdge(entryNode, GetOrCreateOperationNode(firstOperation, graph), NLCPGEdgeKind.CfgNext, graph);
                }
                else
                {
                    AddControlFlowEdge(entryNode, returnNode, NLCPGEdgeKind.CfgNext, graph);
                }

                // 显式 return 始终先汇入 MethodReturn，再由 MethodReturn 统一连向 MethodExit。
                foreach (var returnOperation in methodBlock.DescendantsAndSelf().OfType<IReturnOperation>())
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(returnOperation, graph), returnNode, NLCPGEdgeKind.CfgNext, graph);
                }

                // 没有显式 return 且末尾仍可顺序流出时，补一条隐式 return 边。
                var terminalOperation = methodBlock.Operations.LastOrDefault();
                if (terminalOperation is not null && !ContainsExplicitReturn(methodBlock) && !StopsSequentialFlow(terminalOperation))
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(terminalOperation, graph), returnNode, NLCPGEdgeKind.CfgNext, graph);
                }

                AddControlFlowEdge(returnNode, exitNode, NLCPGEdgeKind.CfgNext, graph);
            }

            // 先铺普通顺序边，再补结构化控制流的额外语义。
            AddSequentialEdges(methodBlock.Operations, graph);
            foreach (var operation in methodBlock.Descendants())
            {
                switch (operation)
                {
                    case IConditionalOperation conditional:
                        AddConditionalEdges(conditional, graph);
                        break;
                    case IWhileLoopOperation whileLoop:
                        AddWhileLoopEdges(whileLoop, graph);
                        break;
                    case IForLoopOperation forLoop:
                        AddForLoopEdges(forLoop, graph);
                        break;
                    case ISwitchOperation switchOperation:
                        AddSwitchEdges(switchOperation, graph);
                        break;
                    case ITryOperation tryOperation:
                        AddTryEdges(tryOperation, graph);
                        break;
                    case IReturnOperation:
                        break;
                }
            }

            AddLoopJumpEdges(methodBlock, graph);
        }
    }

    private void AddSequentialEdges(IEnumerable<IOperation> operations, NLCPGGraph graph)
    {
        var ordered = operations.ToList();
        for (var index = 0; index < ordered.Count - 1; index += 1)
        {
            if (StopsSequentialFlow(ordered[index]))
            {
                continue;
            }

            AddControlFlowEdge(
              GetOrCreateOperationNode(ordered[index], graph),
              GetOrCreateOperationNode(ordered[index + 1], graph),
              NLCPGEdgeKind.CfgNext,
              graph);
        }

        foreach (var nestedBlock in ordered.OfType<IBlockOperation>())
        {
            AddSequentialEdges(nestedBlock.Operations, graph);
        }
    }

    private void AddConditionalEdges(IConditionalOperation conditional, NLCPGGraph graph)
    {
        var conditionNode = GetOrCreateOperationNode(conditional.Condition, graph);
        var trueNode = conditional.WhenTrue is null ? null : GetOrCreateOperationNode(conditional.WhenTrue, graph);
        var falseNode = conditional.WhenFalse is null ? null : GetOrCreateOperationNode(conditional.WhenFalse, graph);
        if (trueNode is not null)
        {
            AddControlFlowEdge(conditionNode, trueNode, NLCPGEdgeKind.CfgTrue, graph);
        }

        if (falseNode is not null)
        {
            AddControlFlowEdge(conditionNode, falseNode, NLCPGEdgeKind.CfgFalse, graph);
        }
    }

    private void AddWhileLoopEdges(IWhileLoopOperation whileLoop, NLCPGGraph graph)
    {
        if (whileLoop.Condition is null)
        {
            return;
        }

        var conditionNode = GetOrCreateOperationNode(whileLoop.Condition, graph);
        var bodyNode = whileLoop.Body is null ? null : GetOrCreateOperationNode(whileLoop.Body, graph);
        if (bodyNode is null)
        {
            return;
        }

        AddControlFlowEdge(conditionNode, bodyNode, NLCPGEdgeKind.CfgTrue, graph);
        AddControlFlowEdge(bodyNode, conditionNode, NLCPGEdgeKind.CfgNext, graph);

        var exitTarget = NextSiblingOperation(whileLoop);
        if (exitTarget is not null)
        {
            AddControlFlowEdge(conditionNode, GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgFalse, graph);
        }
    }

    private void AddForLoopEdges(IForLoopOperation forLoop, NLCPGGraph graph)
    {
        var conditionOperation = forLoop.Condition ?? (forLoop.Before.Length > 0 ? forLoop.Before.LastOrDefault() : null);
        var bodyNode = forLoop.Body is null ? null : GetOrCreateOperationNode(forLoop.Body, graph);
        if (conditionOperation is not null && bodyNode is not null)
        {
            var conditionNode = GetOrCreateOperationNode(conditionOperation, graph);
            AddControlFlowEdge(conditionNode, bodyNode, NLCPGEdgeKind.CfgTrue, graph);
            AddControlFlowEdge(bodyNode, conditionNode, NLCPGEdgeKind.CfgNext, graph);

            var exitTarget = NextSiblingOperation(forLoop);
            if (exitTarget is not null)
            {
                AddControlFlowEdge(conditionNode, GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgFalse, graph);
            }
        }
    }

    private void AddSwitchEdges(ISwitchOperation switchOperation, NLCPGGraph graph)
    {
        var switchValueNode = GetOrCreateOperationNode(switchOperation.Value, graph);
        var exitTarget = NextSiblingOperation(switchOperation);
        var hasDefaultCase = false;
        var caseEntries = switchOperation.Cases
          .Select(@case => new
          {
              Case = @case,
              Entry = FirstExecutableOperation(@case.Body),
              Terminal = LastExecutableOperation(@case.Body),
          })
          .ToList();

        // case 入口可能需要越过空 body，继续寻找后续可执行 case。
        foreach (var item in caseEntries)
        {
            var @case = item.Case;
            var caseBodyEntry = ResolveSwitchCaseEntry(caseEntries, @case, exitTarget);
            if (caseBodyEntry is null)
            {
                continue;
            }

            AddControlFlowEdge(switchValueNode, GetOrCreateOperationNode(caseBodyEntry, graph), NLCPGEdgeKind.CfgTrue, graph);

            if (@case.Clauses.Any(clause => clause.CaseKind == CaseKind.Default))
            {
                hasDefaultCase = true;
            }
        }

        if (!hasDefaultCase && exitTarget is not null)
        {
            AddControlFlowEdge(switchValueNode, GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgFalse, graph);
        }

        // case 尾部未终止时，沿 fallthrough 接到下一个可执行 case；break 则接到 switch 之后。
        for (var index = 0; index < caseEntries.Count; index += 1)
        {
            var @case = caseEntries[index].Case;
            var caseTerminal = caseEntries[index].Terminal;
            var nextCaseEntry = index + 1 < caseEntries.Count
              ? ResolveSwitchCaseEntry(caseEntries, caseEntries[index + 1].Case, exitTarget)
              : exitTarget;

            if (caseTerminal is not null &&
                caseTerminal is not IBranchOperation { BranchKind: BranchKind.Break } &&
                !StopsSequentialFlow(caseTerminal))
            {
                if (nextCaseEntry is not null)
                {
                    AddControlFlowEdge(
                      GetOrCreateOperationNode(caseTerminal, graph),
                      GetOrCreateOperationNode(nextCaseEntry, graph),
                      NLCPGEdgeKind.CfgNext,
                      graph);
                }
                else if (exitTarget is not null)
                {
                    AddControlFlowEdge(
                      GetOrCreateOperationNode(caseTerminal, graph),
                      GetOrCreateOperationNode(exitTarget, graph),
                      NLCPGEdgeKind.CfgNext,
                      graph);
                }
            }

            foreach (var operation in DescendantsAndSelf(@case.Body))
            {
                if (operation is IBranchOperation { BranchKind: BranchKind.Break })
                {
                    if (exitTarget is not null)
                    {
                        AddControlFlowEdge(
                          GetOrCreateOperationNode(operation, graph),
                          GetOrCreateOperationNode(exitTarget, graph),
                          NLCPGEdgeKind.CfgNext,
                          graph);
                    }
                }
            }
        }
    }

    private static IOperation? ResolveSwitchCaseEntry(IEnumerable<dynamic> caseEntries, ISwitchCaseOperation @case, IOperation? exitTarget)
    {
        var entries = caseEntries.ToList();
        var startIndex = entries.FindIndex(item => ReferenceEquals(item.Case, @case));
        if (startIndex < 0)
        {
            return exitTarget;
        }

        for (var index = startIndex; index < entries.Count; index += 1)
        {
            if (entries[index].Entry is IOperation entry)
            {
                return entry;
            }
        }

        return exitTarget;
    }

    private void AddTryEdges(ITryOperation tryOperation, NLCPGGraph graph)
    {
        var tryBodyEntry = FirstExecutableOperation(tryOperation.Body);
        var finallyEntry = tryOperation.Finally is null ? null : FirstExecutableOperation(tryOperation.Finally);
        var exitTarget = NextSiblingOperation(tryOperation);
        var tryTerminal = LastExecutableOperation(tryOperation.Body);

        // 优先进入 try body；空 try body 才直接进入 finally。
        if (tryBodyEntry is not null)
        {
            AddControlFlowEdge(GetOrCreateOperationNode(tryOperation, graph), GetOrCreateOperationNode(tryBodyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
        }
        else if (finallyEntry is not null)
        {
            AddControlFlowEdge(GetOrCreateOperationNode(tryOperation, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
        }

        // catch 入口由 try body 的异常分支触发；catch 结束后继续流向 finally 或 try 之后。
        foreach (var catchClause in tryOperation.Catches)
        {
            var catchEntry = FirstExecutableOperation(catchClause.Handler);
            var catchTerminal = LastExecutableOperation(catchClause.Handler);
            if (tryBodyEntry is not null && catchEntry is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(tryBodyEntry, graph), GetOrCreateOperationNode(catchEntry, graph), NLCPGEdgeKind.CfgFalse, graph);
            }

            if (catchEntry is not null && finallyEntry is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(catchEntry, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
            }

            if (catchTerminal is not null &&
                !ReferenceEquals(catchTerminal, catchEntry) &&
                !StopsSequentialFlow(catchTerminal))
            {
                if (finallyEntry is not null)
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(catchTerminal, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
                }
                else if (exitTarget is not null)
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(catchTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
                }
            }
        }

        // try 正常结束后也要继续流向 finally 或后继节点。
        if (tryTerminal is not null && !StopsSequentialFlow(tryTerminal))
        {
            if (finallyEntry is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(tryTerminal, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
            }
            else if (exitTarget is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(tryTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
            }
        }

        var finallyTerminal = tryOperation.Finally is null ? null : LastExecutableOperation(tryOperation.Finally);
        if (finallyTerminal is not null && exitTarget is not null && !StopsSequentialFlow(finallyTerminal))
        {
            AddControlFlowEdge(GetOrCreateOperationNode(finallyTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
        }
        else if (finallyEntry is not null && exitTarget is not null)
        {
            AddControlFlowEdge(GetOrCreateOperationNode(finallyEntry, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
        }

        // finally 存在时，return 先跳入 finally，由 finally 再决定后续出边。
        if (finallyEntry is not null)
        {
            foreach (var returnOperation in tryOperation.Descendants().OfType<IReturnOperation>())
            {
                if (tryOperation.Finally is not null && IsWithinOperation(returnOperation, tryOperation.Finally))
                {
                    continue;
                }

                AddControlFlowEdge(GetOrCreateOperationNode(returnOperation, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
            }
        }
    }

    private void AddLoopJumpEdges(IBlockOperation methodBlock, NLCPGGraph graph)
    {
        foreach (var loop in methodBlock.Descendants().OfType<ILoopOperation>())
        {
            var targets = LoopTargets(loop);
            if (targets.ContinueTarget is null && targets.BreakTarget is null)
            {
                continue;
            }

            foreach (var operation in loop.Body?.DescendantsAndSelf() ?? Enumerable.Empty<IOperation>())
            {
                if (operation.Kind == OperationKind.Branch)
                {
                    var branch = (IBranchOperation)operation;
                    if (branch.BranchKind == BranchKind.Continue && targets.ContinueTarget is not null)
                    {
                        AddControlFlowEdge(
                          GetOrCreateOperationNode(operation, graph),
                          GetOrCreateOperationNode(targets.ContinueTarget, graph),
                          NLCPGEdgeKind.CfgNext,
                          graph);
                    }

                    if (branch.BranchKind == BranchKind.Break && targets.BreakTarget is not null)
                    {
                        AddControlFlowEdge(
                          GetOrCreateOperationNode(operation, graph),
                          GetOrCreateOperationNode(targets.BreakTarget, graph),
                          NLCPGEdgeKind.CfgNext,
                          graph);
                    }
                }
            }
        }
    }

    private static LoopControlTargets LoopTargets(ILoopOperation loop)
    {
        return loop switch
        {
            IWhileLoopOperation whileLoop => new LoopControlTargets(whileLoop.Condition, NextSiblingOperation(whileLoop)),
            IForLoopOperation forLoop => new LoopControlTargets(
              forLoop.Condition ?? (forLoop.Before.Length > 0 ? forLoop.Before.LastOrDefault() : null),
              NextSiblingOperation(forLoop)),
            _ => new LoopControlTargets(null, NextSiblingOperation(loop)),
        };
    }

    private static IOperation? NextSiblingOperation(IOperation operation)
    {
        if (operation.Parent is not IBlockOperation parentBlock)
        {
            return null;
        }

        var siblings = parentBlock.Operations;
        for (var index = 0; index < siblings.Length - 1; index += 1)
        {
            if (ReferenceEquals(siblings[index], operation))
            {
                return siblings[index + 1];
            }
        }

        return null;
    }

    private static IOperation? FirstExecutableOperation(IOperation operation)
    {
        if (operation is IBlockOperation blockOperation)
        {
            return blockOperation.Operations.FirstOrDefault();
        }

        return operation;
    }

    private static IOperation? FirstExecutableOperation(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations)
        {
            var executable = FirstExecutableOperation(operation);
            if (executable is not null)
            {
                return executable;
            }
        }

        return null;
    }

    private static IOperation? LastExecutableOperation(IOperation operation)
    {
        return operation switch
        {
            IBlockOperation blockOperation => LastExecutableOperation(blockOperation.Operations),
            _ => operation,
        };
    }

    private static IOperation? LastExecutableOperation(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations.Reverse())
        {
            var executable = LastExecutableOperation(operation);
            if (executable is not null)
            {
                return executable;
            }
        }

        return null;
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations)
        {
            foreach (var descendant in operation.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    private bool IsMethodRootBlock(IBlockOperation blockOperation)
    {
        return blockOperation.Parent is IMethodBodyOperation or IConstructorBodyOperation;
    }

    private static bool StopsSequentialFlow(IOperation operation)
    {
        return operation is IReturnOperation ||
               operation is IBranchOperation { BranchKind: BranchKind.Break or BranchKind.Continue };
    }

    private static bool ContainsExplicitReturn(IBlockOperation blockOperation)
    {
        return blockOperation.Descendants().OfType<IReturnOperation>().Any();
    }

    private static bool IsWithinOperation(IOperation candidate, IOperation container)
    {
        for (var current = candidate.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
    }
}
}
