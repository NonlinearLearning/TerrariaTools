# Application Flow Assembly Design

## Goal

Redesign the `src/Application` layer so application flows are assembled in code from stable, fixed slot contracts instead of being hard-coded as stage arrays in each composition root. The new design must preserve the existing layered split:

- `apps/` hosts
- `src/Application/` orchestration
- `src/Adapters/` infrastructure
- `src/Core/` domain models and services

The target is not a generic workflow engine. The target is a predictable application-flow assembly model that supports:

- standard flows
- per-slot specialization
- limited per-slot decoration
- separate context types per application family
- explicit, typed stage inputs and outputs

## Scope

This document covers:

- standard Dome flow assembly
- runtime host flow assembly
- shadow extraction flow assembly
- fixed slot contracts
- `Replace<Slot>` and `Decorate<Slot>` rules
- typed stage inputs and outputs
- execution context boundaries
- migration strategy from the current pipeline composition

This document does not cover:

- external workflow engines
- JSON or configuration-driven flow definition
- visual designers
- persisted long-running workflow state
- arbitrary DAG or graph execution

## Current State

The project already has a usable pipeline execution kernel:

- [`PipelineAbstractions.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Application\Pipeline\PipelineAbstractions.cs)

It already supports:

- ordered stage execution
- terminal short-circuit
- stage trace collection
- observer callbacks

The missing piece is assembly. Today the real flow topology is hard-coded in composition roots:

- [`DomeApplicationComposition.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\apps\Dome.Application\Composition\DomeApplicationComposition.cs)
- [`TerrariaRuntimeComposition.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\apps\Dome.Application.Runtime\Composition\TerrariaRuntimeComposition.cs)
- [`TerrariaRuntimeShadowExtractionComposition.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\apps\Dome.Application.ShadowExtraction\Composition\TerrariaRuntimeShadowExtractionComposition.cs)

That shape has three problems:

1. Flow topology is embedded in host code.
2. Slot specialization leaks into orchestration code.
3. Shared execution mechanics exist, but there is no shared assembly model.

## Design Decisions

### 1. Code Assembly Only

Flows are assembled in code. The system will not support runtime JSON, YAML, CLI, or database-defined workflows.

Why:

- the project already uses composition roots and typed contracts
- the user requirement is code assembly only
- configuration-driven assembly would add flexibility that is not currently needed
- a code-first model preserves compile-time safety and keeps migration simpler

### 2. Shared Assembly Model, Separate Context Types

All application families share the same assembly mechanics, but not the same context type.

Keep separate context types:

- `DomePipelineContext`
- `TerrariaRuntimePipelineContext`
- `ShadowExtractionPipelineContext`

Do not collapse them into a single universal context object.

Why:

- the three flows carry materially different state
- forcing them into one context would create a nullable "god object"
- the project needs a shared orchestration model, not shared business state storage

### 3. Fixed Slot Priority

Each application family defines a fixed slot vocabulary. Flows are assembled by filling or replacing those slots, not by arbitrarily appending stages.

For the standard Dome family, the slot model is:

- `Load`
- `Analyze`
- `Rule`
- `Decision`
- `Result`

For runtime and shadow extraction, the slot names may differ. They share the assembly mechanism, not the exact business vocabulary.

### 4. Limited Extension Model

The system supports only two official extension mechanisms:

- `Replace<Slot>`
- `Decorate<Slot>`

The system does not support arbitrary `AddStage`, `InsertBefore`, or `InsertAfter` on the public assembly surface.

Why:

- arbitrary insertion would turn fixed slots back into a free-form pipeline
- public branching and insertion would erode flow predictability
- `Replace` and `Decorate` are enough for the stated specialization needs

### 5. Typed Business Inputs and Outputs

The application layer should not continue relying on a mutable business context as the primary state carrier between slots.

Instead, each slot exposes explicit typed input and output contracts.

Example for the Dome family:

```csharp
LoadInput -> LoadOutput
AnalyzeInput -> AnalyzeOutput
RuleInput -> RuleOutput
DecisionInput -> DecisionOutput
ResultInput -> RunResult
```

This makes data dependencies explicit in type signatures instead of implicit in mutable context state.

### 6. Thin Execution Context

Removing the fat business context does not mean removing execution context entirely.

Keep a thin execution context that carries only cross-cutting execution concerns:

- correlation id
- stage traces
- diagnostics bag
- terminal state
- execution-scoped metadata

Do not use it as a business object transport.

## Architecture

### Flow Kernel

The flow kernel is the shared orchestration runtime. It owns:

- execution ordering
- terminal behavior
- stage tracing
- observer integration
- failure propagation

The existing pipeline runner is the correct starting point and should be retained conceptually, even if types or wrappers are renamed during migration.

Core kernel responsibilities:

- execute the assembled slot sequence
- stop when terminal state is reached
- trace stage start and completion
- surface failures without embedding business logic

The kernel must remain ignorant of:

- Dome-specific rule semantics
- runtime host semantics
- shadow extraction semantics
- specialized analysis engines
- specialized result producers

### Flow Recipe

A recipe is the typed declaration of a flow's slot selections.

It answers:

- which slot implementation is active
- which slot decorators apply
- which slots are omitted for a given flow mode

It does not answer:

- how slot logic works internally
- how adapters talk to infrastructure
- how domain services compute results

For Dome, the initial recipe set should be:

- `AnalyzeOnly`
- `PlanOnly`
- `Standard`

Recommended mapping:

- `AnalyzeOnly`: `Load -> Analyze -> Result`
- `PlanOnly`: `Load -> Analyze -> Rule -> Decision -> Result`
- `Standard`: `Load -> Analyze -> Rule -> Decision -> Result`

In this model, `Result` is the terminal slot that decides how the accumulated outputs become a final `RunResult`.

### Flow Builder

The builder transforms a recipe into an executable flow.

It is responsible for:

- validating slot completeness
- validating replacement and decoration rules
- assembling the executable sequence
- binding default implementations when no explicit replacement is provided

It is not responsible for:

- runtime inference of unknown contracts
- discovering arbitrary stage graphs
- supporting contract drift across implementations

The builder should only assemble against the standard slot contracts of the family it serves.

### Slot Contracts

Each slot exposes a stable public behavior contract.

For Dome, the intended public contracts are:

```csharp
public interface ILoadSlot
{
    Task<LoadOutput> ExecuteAsync(
        LoadInput input,
        FlowExecutionContext executionContext,
        CancellationToken cancellationToken);
}

public interface IAnalyzeSlot
{
    Task<AnalyzeOutput> ExecuteAsync(
        AnalyzeInput input,
        FlowExecutionContext executionContext,
        CancellationToken cancellationToken);
}

public interface IRuleSlot
{
    Task<RuleOutput> ExecuteAsync(
        RuleInput input,
        FlowExecutionContext executionContext,
        CancellationToken cancellationToken);
}

public interface IDecisionSlot
{
    Task<DecisionOutput> ExecuteAsync(
        DecisionInput input,
        FlowExecutionContext executionContext,
        CancellationToken cancellationToken);
}

public interface IResultSlot
{
    Task<RunResult> ExecuteAsync(
        ResultInput input,
        FlowExecutionContext executionContext,
        CancellationToken cancellationToken);
}
```

These contracts must remain stable for the `Application` assembly layer.

## Input and Output Modeling

### Standard Dome Contracts

Recommended canonical transport types:

```csharp
public sealed record LoadInput(string InputPath, string OutputPath, RunMode Mode);
public sealed record LoadOutput(WorkspaceLoadResult LoadResult);

public sealed record AnalyzeInput(LoadOutput Load);
public sealed record AnalyzeOutput(AnalysisOutput Analysis);

public sealed record RuleInput(AnalyzeOutput Analysis);
public sealed record RuleOutput(IReadOnlyList<MarkDecision> Decisions);

public sealed record DecisionInput(AnalyzeOutput Analysis, RuleOutput Rule);
public sealed record DecisionOutput(PlanningOutput Planning);

public sealed record ResultInput(
    LoadOutput Load,
    AnalyzeOutput Analysis,
    RuleOutput? Rule,
    DecisionOutput? Decision);
```

These records should be immutable.

Why:

- they make slot dependencies explicit
- they keep `Replace<Slot>` stable
- they make `Decorate<Slot>` behavior predictable
- they eliminate "field may have been set earlier" ambiguity

### No Business Builder Objects

Do not model public slot inputs as builder-style mutable objects.

Why:

- mutable builders weaken slot boundaries
- decoration becomes order-sensitive in unintended ways
- replacement implementations may read partially-populated state
- debugging becomes harder because construction order matters

Immutable records are the preferred standard.

## Replace and Decorate Rules

### Replace

`Replace<Slot>` selects a different active implementation for a single slot.

Valid use cases:

- special analysis behavior
- special result behavior
- environment-specific load behavior

Constraints:

- only one active core implementation per slot
- replacement implementation must honor the standard slot contract
- replacement must not change the slot's public input and output types

### Decorate

`Decorate<Slot>` wraps a slot implementation without changing the slot contract.

Valid use cases:

- metrics
- logging
- diagnostics
- lightweight validation
- non-semantic enrichment

Constraints:

- decorators may be multiple
- decorators wrap one slot only
- decorators must not alter flow topology
- decorators must not introduce hidden cross-slot data coupling

### Public Assembly Surface

Recommended public assembly operations:

- `UseLoad<TSlot>()`
- `UseAnalyze<TSlot>()`
- `UseRule<TSlot>()`
- `UseDecision<TSlot>()`
- `UseResult<TSlot>()`
- `DecorateLoad<TDecorator>()`
- `DecorateAnalyze<TDecorator>()`
- `DecorateRule<TDecorator>()`
- `DecorateDecision<TDecorator>()`
- `DecorateResult<TDecorator>()`

Do not expose:

- `AddStage(...)`
- `InsertBefore(...)`
- `InsertAfter(...)`
- `MapWhen(...)`
- arbitrary branching

Those are intentionally excluded from the public design.

## Special Implementations and Adapters

### Rule

Special implementations may internally require their own request and response models.

That is acceptable.

What is not acceptable is letting the `Application` assembly layer compose directly against those private contracts.

The rule is:

- private implementation contracts are allowed
- public slot contracts are fixed
- special implementations must adapt back to the public slot contract

Example:

```csharp
public interface ISpecialAnalyzeEngine
{
    Task<SpecialAnalyzeResponse> ExecuteAsync(
        SpecialAnalyzeRequest request,
        CancellationToken cancellationToken);
}

public sealed class SpecialAnalyzeSlotAdapter : IAnalyzeSlot
{
    ...
}
```

### Why Adapters Matter

Without adapters, the assembly layer would need to understand implementation-specific inputs and outputs. That would:

- leak specialization into orchestration
- break `Replace<Slot>`
- destabilize builder logic
- reintroduce per-flow contract drift

The adapter boundary keeps specialization local.

## Rule and Decision Separation

Keep `Rule` and `Decision` as separate slots.

Reasoning:

- the current code already reflects a meaningful split between marking and plan compilation
- keeping the split aligns with the current staged behavior in [`DomeApplicationStages.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Application\Pipeline\DomeApplicationStages.cs)
- a combined `Plan` slot would hide a real architectural boundary during migration
- separate slots make targeted replacement and testing easier

Recommended current mapping:

- `Rule`: build initial and predicted decisions from analysis outputs
- `Decision`: compile those decisions into a plan and related planning output

This can be revisited later if the boundary proves artificial. It should not be collapsed in the first migration.

## Runtime and Shadow Extraction Families

The assembly model is shared. The slot vocabulary is not.

### Runtime

Recommended runtime-family slot vocabulary:

- `Prepare`
- `ExecuteDome`
- `LoadReport`
- `BuildWorkspace`
- `Persist`

### Shadow Extraction

Recommended shadow-family slot vocabulary:

- `ResolveInput`
- `Analyze`
- `BuildClosure`
- `WriteWorkspace`
- `Build`
- `Persist`

Do not try to force these families into Dome's `Load/Analyze/Rule/Decision/Result` names.

That would create abstraction pressure without delivering real value.

## Migration Plan

### Phase 1: Introduce Flow Vocabulary

Add new recipe and slot abstractions alongside the current pipeline code.

Do not delete the current pipeline runner or stage classes yet.

Targets:

- define `FlowExecutionContext`
- define slot contracts
- define recipes for the Dome family
- define builder validation rules

### Phase 2: Wrap Existing Stages

Adapt the current Dome stage logic into the new slot model.

The first version may use thin wrappers around existing services or stage internals. That is acceptable if it reduces risk.

Targets:

- map `WorkspaceLoadStage` behavior to `ILoadSlot`
- map `AnalysisStage` behavior to `IAnalyzeSlot`
- map `MarkDecisionsStage` behavior to `IRuleSlot`
- map `CompilePlanStage` behavior to `IDecisionSlot`
- map current finalize and rewrite behavior to `IResultSlot`

### Phase 3: Convert Composition Roots

Change composition roots from manual stage arrays to recipe-based assembly.

Targets:

- standard Dome application composition
- runtime host composition
- shadow extraction composition

Expected outcome:

- composition roots choose recipe and implementation
- builders produce executable flow
- stage arrays are no longer hand-authored in hosts

### Phase 4: Remove Redundant Orchestration Shapes

Once recipe-based assembly is stable:

- remove duplicated manual flow assembly code
- narrow old pipeline types to internal compatibility roles if still needed
- simplify host composition to recipe selection

## Validation Strategy

### Required Test Coverage

Add or update tests for:

- default recipe assembly
- `Replace<Slot>` behavior
- `Decorate<Slot>` order and isolation
- adapter-based special implementation integration
- recipe validation failures
- cross-family assembly consistency

### Must-Prove Properties

The redesign is not complete unless it proves:

1. standard Dome flow still runs
2. analyze-only and plan-only recipes still run
3. runtime host still assembles and executes
4. shadow extraction host still assembles and executes
5. special analysis can be swapped without changing public flow contracts
6. special result production can be swapped without changing public flow contracts
7. decorators do not mutate flow topology

## Risks

### Risk 1: Slot Boundary Drift

If replacements start changing public contracts, the builder becomes unstable.

Mitigation:

- keep public slot contracts fixed
- require adapters for private contract drift

### Risk 2: Execution Context Re-Growth

The thin execution context may slowly become a new fat business context.

Mitigation:

- prohibit business payload storage in execution context
- review new fields against a strict cross-cutting-only rule

### Risk 3: Decorator Abuse

Decorators may become hidden pipeline insertion points.

Mitigation:

- keep decorator responsibilities narrow
- ban topology changes inside decorators
- keep decorators bound to one slot only

### Risk 4: Premature Cross-Family Unification

Trying to unify Dome, runtime, and shadow extraction into one business vocabulary will create a false abstraction.

Mitigation:

- unify only kernel and assembly model
- allow each family to keep its own slot language

## Rejected Alternatives

### 1. Universal Mutable Context

Rejected because it:

- hides dependencies
- weakens slot boundaries
- scales poorly across families
- encourages nullable "god object" growth

### 2. Fully Generic Free-Chain Assembly

Rejected because it:

- weakens fixed slot guarantees
- makes `Replace<Slot>` and `Decorate<Slot>` more fragile
- pushes contract inference into the builder
- increases coupling between adjacent implementations

### 3. Configuration-Driven Workflow

Rejected because it:

- exceeds current user needs
- adds unnecessary runtime complexity
- weakens compile-time safety
- would push the design toward a workflow engine rather than an application assembly model

## References

The design direction aligns with official pipeline and dependency injection guidance:

- ASP.NET Core middleware fundamentals:
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-9.0
- ASP.NET Core dependency injection fundamentals:
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-9.0

These references informed the pipeline assembly approach, especially:

- ordered component assembly
- short-circuit-capable execution
- replacement through composition
- keeping public contracts stable while allowing implementation variation

## Final Recommendation

Implement a code-first application flow assembly model with:

- shared flow kernel
- separate family contexts
- fixed slot vocabularies per family
- typed immutable slot inputs and outputs
- thin execution context
- stable public slot contracts
- `Replace<Slot>` and `Decorate<Slot>` as the only supported extension mechanisms
- adapters for special implementation-specific contracts

This design preserves the current layered architecture while making flow assembly explicit, reusable, and safe.
