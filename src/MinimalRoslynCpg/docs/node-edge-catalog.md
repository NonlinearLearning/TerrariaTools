# Minimal Roslyn CPG Node and Edge Catalog

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

## Scope Boundary

This minimal graph intentionally does not model:

- trivia
- preprocessor directives
- interprocedural data flow
- full dominance/post-dominance
- full external dependency type summaries
- dynamic dispatch candidate sets
- interprocedural call/data flow

It is a Roslyn-native minimal graph, not a full joern schema clone.

## Local View

The CLI now supports a first local CPG view expanded from a single anchor node.

Example:

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj `
  .\src\MinimalRoslynCpg\samples\analysis-sample.cs `
  --view local `
  --anchor-full-name 'Demo.App.StepNormalizer.Normalize:int(int)' `
  --hops 1 `
  --direction both
```

Current local-view behavior:

- exactly one anchor selector is required: `--anchor-id`, `--anchor-full-name`, or `--anchor-name`
- traversal is breadth-first by hop count
- traversal can be limited to `incoming`, `outgoing`, or `both`
- traversal can be filtered by `--edge-kinds`
- the extracted view includes only nodes and edges that remain inside the visited subgraph
- `--json-out` writes the local-view payload as a small JSON artifact for downstream inspection

## Fragment Structure View

`RoslynCpgStructureViewBuilder` is the analysis-time view used by small-scope lift and
propagation code. It accepts one or more Roslyn `SyntaxNode` fragments and copies the
corresponding CPG nodes and edges from the main `RoslynCpgGraph`.

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
