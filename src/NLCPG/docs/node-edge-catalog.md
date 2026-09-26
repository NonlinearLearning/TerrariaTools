# NLCPG Node and Edge Catalog

## In-memory carrier contract

`NLCPGNode` and `NLCPGEdge` are public `readonly record struct` values. They are
immutable graph carriers: copying either value copies its scalar fields, but does not
clone the graph-owned string table, labels, or collections. `NLCPGNode` stores
`NameId`, `FullNameId`, `SignatureId`, `TypeFullNameId`, and `FilePathId`; ID `0` means
no value, and an ID is meaningful only inside its owning `NLCPGGraph`. The graph owns
identity materialization and deduplication, so callers must use the value returned by
`NLCPGGraph.AddNode` and must treat a copied node as a snapshot value.

`NLCPGNode` intentionally has no `Text`, `DisplayKind`, or string-valued name/path
property. `NLCPGNodeDraft` carries source strings only until `NLCPGGraph.AddNode`
interns them. Display text is derived by `NLCPGGraph.GetDisplayText`: source span text
first, then graph-resolved `FullName`, `Name`, and a `Kind`/syntax-derived display kind.
Decision reasons and analysis evidence remain in their decision/evidence models instead
of being duplicated on every node. `NLCPGEdge` stores `NodeId` endpoints and optional
structured context/labels; it does not retain node object references.

After `FreezeQueryIndex`, the graph owns one canonical node array ordered by `NodeId`.
Kind and file-path indexes contain ordinals and are exposed through an `OrdinalNodeList`
view, so index buckets do not store repeated node values. Persistent shard formats still
resolve IDs back to their existing text fields at the export boundary; changing that
external schema requires a separate versioned design.

## Node Kinds

| Kind | Meaning | Minimal Source |
| --- | --- | --- |
| `SyntaxTree` | One source file parse root | `SyntaxTree` |
| `SyntaxNode` | Roslyn syntax node | `SyntaxNode` |
| `SyntaxToken` | Roslyn syntax token | `SyntaxToken` |
| `Method` | Declared method abstraction node | `IMethodSymbol` declaration |
| `MethodParameter` | Stable method parameter abstraction node | `IMethodSymbol.Parameters` |
| `MethodReturn` | Stable method return abstraction node | `IMethodSymbol.ReturnType` |
| `MethodEntry` | Synthetic method CFG entry node | method abstraction |
| `MethodExit` | Synthetic method CFG exit node | method abstraction |
| `TypeDecl` | Declared type abstraction node | `INamedTypeSymbol` declaration |
| `TypeRef` | Explicit type usage abstraction node | `TypeSyntax` / object creation / base type |
| `Reference` | Stable symbol reference abstraction node | syntax reference resolved by `SemanticModel` |
| `CallSite` | Invocation abstraction node | `IInvocationOperation` |
| `SymbolNamespace` | Declared or referenced namespace | `INamespaceSymbol` |
| `SymbolType` | Declared or referenced type | `ITypeSymbol` |
| `SymbolMethod` | Declared or referenced method | `IMethodSymbol` |
| `SymbolProperty` | Declared or referenced property | `IPropertySymbol` |
| `SymbolField` | Declared or referenced field | `IFieldSymbol` |
| `SymbolLocal` | Local variable symbol | `ILocalSymbol` |
| `SymbolParameter` | Parameter symbol | `IParameterSymbol` |
| `SymbolUnknown` | Any other symbol kind | `ISymbol` fallback |
| `Operation` | Generic Roslyn operation | `IOperation` fallback |
| `OpBlock` | Operation block | `IBlockOperation` |
| `OpInvocation` | Invocation operation | `IInvocationOperation` |
| `OpArgument` | Call argument operation | `IArgumentOperation` |
| `OpBinary` | Binary operation | `IBinaryOperation` |
| `OpAssignment` | Assignment operation | `IAssignmentOperation` |
| `OpLocalReference` | Local reference | `ILocalReferenceOperation` |
| `OpParameterReference` | Parameter reference | `IParameterReferenceOperation` |
| `OpFieldReference` | Field reference | `IFieldReferenceOperation` |
| `OpPropertyReference` | Property reference | `IPropertyReferenceOperation` |
| `OpLiteral` | Literal operation | `ILiteralOperation` |
| `OpReturn` | Return operation | `IReturnOperation` |
| `OpConditional` | Conditional operation | `IConditionalOperation` |
| `OpLoop` | Loop operation | `ILoopOperation` |

## Edge Kinds

| Kind | Meaning | Source Layer |
| --- | --- | --- |
| `SyntaxChild` | Syntax tree parent-child relation | syntax |
| `TokenChild` | Syntax node to token relation | syntax |
| `ParameterLink` | Argument value or method node links to method parameter abstraction | callgraph / abstraction |
| `DeclaresSymbol` | Syntax declaration binds symbol | syntax -> semantic |
| `ReferencesSymbol` | Syntax reference resolves symbol | syntax -> semantic |
| `Ref` | Stable reference abstraction points to symbol | reference -> semantic |
| `HasType` | Node carries a resolved type | semantic / operation |
| `EvalType` | Node carries an evaluated result type | operation / reference / call |
| `ReturnsType` | Method symbol returns type symbol | semantic |
| `ContainsSymbol` | Namespace/type/method contains child symbol | semantic |
| `BaseType` | Type symbol inherits or implements type | semantic |
| `InheritsFrom` | Type declaration inherits from type symbol | type abstraction |
| `RefersToType` | Type declaration or reference points to type symbol | type abstraction |
| `SyntaxHasOperation` | Syntax owns an operation root | syntax -> operation |
| `OpHasSyntax` | Operation maps back to syntax | operation -> syntax |
| `OpResolvesToSymbol` | Operation resolves to symbol | operation -> semantic |
| `CallTargets` | Call-site abstraction targets method symbol | callgraph |
| `AccessesMember` | Member-access abstraction points at the accessed member wrapper | semantic / abstraction |
| `DecisionRelation` | Semantic relation between decision nodes; its label identifies the relation | decision |
| `OpChild` | Generic operation child edge | operation |
| `OpArgument` | Invocation to argument edge | operation |
| `OpInstance` | Invocation, field access, or property/indexer base receiver edge | operation |
| `OpTarget` | Operation target value edge | operation |
| `OpCondition` | Conditional or loop condition edge | operation |
| `OpBody` | Loop body edge | operation |
| `OpWhenTrue` | Conditional true branch edge | operation |
| `OpWhenFalse` | Conditional false branch edge | operation |
| `CfgNext` | Minimal intra-method control-flow edge | analysis |
| `CfgTrue` | Branch or loop true-flow edge | analysis |
| `CfgFalse` | Branch false-flow edge | analysis |
| `DataFlow` | Minimal intraprocedural reaching-definition edge for locals and parameters | analysis |
| `InterproceduralDataFlow` | Opt-in boundary bridge between a uniquely resolved call target and its parameter/return endpoint | opt-in analysis overlay |
| `Dominates` | Method-local Roslyn CFG dominance relation | opt-in analysis overlay |
| `PostDominates` | Method-local Roslyn CFG post-dominance relation | opt-in analysis overlay |
| `ControlDependence` | Condition to branch-controlled node relation derived from post-dominance | opt-in analysis overlay |

## Structured Edge Labels

`NLCPGEdgeKind` gives an edge its broad relationship category. `NLCPGEdgeLabel` supplies
the optional semantic detail needed when the same edge kind has several distinct meanings.
For example, every cross-method data-flow bridge has kind `InterproceduralDataFlow`, while
its label identifies whether it maps an argument to a parameter, a returned expression to a
method return, or a method return to a call result.

| Label family | Applies to edge kind | Values | Purpose |
| --- | --- | --- | --- |
| Interprocedural bridge | `InterproceduralDataFlow` | `ArgumentToParameter`, `ReturnToMethodReturn`, `MethodReturnToCallResult` | Identifies the resolved call-boundary mapping. |
| Decision relation | `DecisionRelation` | `AccessibilityToPrivate`, `ClearedTo`, `DerivedFrom`, `Inherits`, `ReducedTo`, `ReplacedWith` | Identifies the semantic relation between decision nodes. |

An edge has at most one structured label. The label is optional because most edge kinds need
no additional distinction. Its `StableKey`, such as
`interprocedural-bridge:ArgumentToParameter`, participates in deterministic edge ordering,
graph hashing, and shard persistence; the shard reader parses that key back into the structured
label rather than treating it as an untyped display string.

## Scope Boundary

This minimal graph intentionally does not model:

- trivia
- preprocessor directives
- full external dependency type summaries
- dynamic dispatch candidate sets
- complete interprocedural call/data flow (the opt-in unique-target overlay below is deliberately partial)

It is a Roslyn-native minimal graph, not a full joern schema clone.

## Control overlays

`Dominance`, `PostDominance`, and `ControlDependence` are opt-in capabilities. They are calculated independently for each method from Roslyn `ControlFlowGraph` basic blocks, then projected only to graph nodes with a stable operation or synthetic entry/exit mapping. Synthetic CFG blocks without a graph-node mapping remain internal to the calculation.

`Dominates(source)` and `PostDominates(source)` return direct overlay edges from the frozen query index. `Controls(source)` and `ControlledBy(target)` expose control-dependence edges without rescanning the graph.

## Interprocedural data-flow overlay

`InterproceduralDataFlow` is an opt-in capability and is excluded from the default capability set.
It depends on the method model, resolved `CallTargets`, intraprocedural `DataFlow`, and the
frozen query index. It materializes only deterministic bridges for an internal callsite with one
resolved target: actual argument to formal parameter, returned expression to `MethodReturn`, and
`MethodReturn` to the call result. Each bridge label is one of `ArgumentToParameter`,
`ReturnToMethodReturn`, or `MethodReturnToCallResult`.

Dynamic, delegate, external, recursive, aliased, heap, and multi-target calls are not inferred by
this overlay. They remain explicit cuts; queries must report those cuts rather than inventing a
flow. Cross-process traversal consumes call-depth budget only when it crosses an
`InterproceduralDataFlow` edge. The overlay is deterministically ordered by caller method, source
span, callsite, target, and parameter ordinal.

## Local View

The NLCPG entrypoint now reads the unified `nlissn.yml` configuration and supports a
local CPG view expanded from a single anchor node. It accepts no business command-line
arguments.

Example:

```powershell
Push-Location .\cpg-run
dotnet run --project ..\src\NLCPG\NLCPG.csproj
Pop-Location
```

The `cpg-run\nlissn.yml` file contains:

```yaml
schemaVersion: 3
tool: nlcpg
input:
  path: ./Sample.cs
nlcpg:
  view:
    mode: local
    anchor:
      fullName: Demo.Sample.Add:int(int, int)
    hops: 1
    direction: both
    edgeKinds: [Call]
  output:
    json: ./Build/local-view.json
```

Current local-view behavior:

- exactly one anchor selector is required: `nlcpg.view.anchor.nodeId`, `fullName`, or `name`
- traversal is breadth-first by hop count
- traversal can be limited to `incoming`, `outgoing`, or `both`
- traversal can be filtered by the `nlcpg.view.edgeKinds` YAML array
- the extracted view includes only nodes and edges that remain inside the visited subgraph
- `nlcpg.output.json` writes the local-view payload as a small JSON artifact for downstream inspection

## Fragment Structure View

`NLCPGStructureViewBuilder` is the analysis-time view used by small-scope lift and
propagation code. It accepts one or more Roslyn `SyntaxNode` fragments and copies the
corresponding CPG nodes and edges from the main `NLCPGGraph`.

Current fragment-view behavior:

- each input fragment contributes graph nodes whose file path and span are inside that fragment
- all existing main-graph edges between selected nodes are copied into the view
- when multiple fragment groups are provided, the builder connects each pair with the shortest
  undirected path in the main graph
- all CPG edge kinds participate in shortest-path search
- no synthetic operation or data-flow edges are generated by the view builder
- the single-fragment overload delegates to the multi-fragment builder

## Current Analysis Guarantees

- CFG is now method-local and operation-oriented for blocks, conditionals, loops, returns, sequential statements, synthetic method entry/exit, switch-case fallthrough including empty-case forwarding, and a first terminal propagation for try/catch paths with or without finally blocks, including empty-try to finally forwarding.
- Data flow now seeds parameter abstractions, propagates reaching definitions over explicit CFG edges with a minimal predecessor/worklist solver, distinguishes local/parameter/call/member-family definition facts with simple access-path-style keys, applies first container/part/alias-style matching on receiver-sensitive member facts, lets plain local/parameter rebinding kill dependent member-family facts that share the same receiver root, adds first getter-like and setter-like property summary paths through internal accessor parameters and MethodReturn / assignment targets, also maps property/indexer arguments into accessor parameter flow, exposes property accesses as first-class accessor-like callsites with CallTargets edges, routes accessor summary flow through those callsites, and aligns accessor callsites with the same candidate/ranking/dispatch/fallback pipeline as ordinary calls while distinguishing property and indexer accessor dispatch kinds, finer resolved dispatch explanations, and property-aware candidate ranking.
- Field/property/global/interprocedural summaries are still partial rather than complete.
- MemberAccess owner selection is now receiver-type-aware for current field/property/indexer cases, but it is still not a full joern-style member pass.
- MemberAccess `FullName` now prefers the access-site receiver type where compatible, while `Ref` still points to the resolved field/property symbol.
