# Diff Model And Renderer Execution Plan

## Goal

Rewrite the current diff output around a structured diff model, then keep text rendering as a separate upper layer.

The priority order is:

1. establish a stable internal diff model
2. route all diff-producing paths through that model
3. keep the current text output through a compatibility renderer
4. add a better human-readable renderer on top
5. keep diff summaries inside a small dedicated diff subsystem

## Why This Change Is Needed

The current implementation has three hard constraints:

1. `PrototypeRewriter.BuildDiffText(...)` directly formats `RewriteEdit` into text blocks, so diff data and rendering are fused
2. `PrototypeAnalysisResult` treats `DiffText` as a primary result contract, which leaks text-shape assumptions into Host, directory aggregation, and tests
3. directory-mode diff aggregation is string concatenation with per-file markdown headers instead of a shared multi-file diff model

Current code anchors:

- `src/RoslynPrototype/Rewrite/PrototypeRewriter.cs`
- `src/RoslynPrototype/Rewrite/PrototypeAnalysisResult.cs`
- `src/Host/DeletionDirectoryAnalysisService.cs`

## Non-Goals

This plan does not do these things in the first version:

1. implement full git-style unified diff or patch hunks
2. merge the diff model with the current text log event model
3. change `--diff-out`, `--no-diff`, or `DiffFilePath` semantics
4. change rewrite decisions or rewrite edit semantics
5. make the current text log system own diff payloads or diff summaries

Hard boundary:

1. diff must not enter the text log system
2. the text log system must not define diff categories, diff events, or diff summary fields
3. if runtime observability needs rewrite facts, it may record only generic execution facts such as `edits=12`, not diff-shaped data

## Existing Constraints

### Output stability

Current tests depend on stable diff text fragments. The first rollout must preserve that behavior through a compatibility renderer.

### Directory determinism

Directory diff output must stay stable under parallel analysis. File order and edit order must remain deterministic.

### Subsystem boundary

Diff should remain a small self-contained subsystem. It may expose its own summary objects or sidecar files, but it should not depend on the current text log design for its primary architecture.

## External Design Direction

The selected design direction borrows the useful separation pattern from high-star projects:

1. `difftastic`: structured diff data first, multiple output surfaces later
2. `DiffPlex`: builder/model separated from side-by-side or inline rendering
3. `delta`: rendering layer owns layout, wrapping, and readability concerns rather than the diff data model

This repo should follow the same split:

1. diff model
2. diff builder
3. one or more renderers

## Proposed Internal Architecture

### Layer 1: diff model

Add a new model under `src/RoslynPrototype/Rewrite/`.

Suggested file:

- `src/RoslynPrototype/Rewrite/DiffModel.cs`

Suggested first-version types:

- `DiffDocument`
- `DiffFile`
- `DiffSection`
- `DiffBlock`
- `DiffLine`
- `DiffLineKind`

Suggested `DiffLineKind` values:

- `Meta`
- `Context`
- `Delete`
- `Insert`
- `ReplaceOld`
- `ReplaceNew`

The first version should remain edit-driven, not line-LCS-driven. A `DiffSection` should correspond to one `RewriteEdit` or one stable grouped edit region.

Each section should carry:

- `filePath`
- `spanStart`
- `spanEnd`
- `editIndex`
- `editKind`
- original text payload
- replacement text payload

### Layer 2: diff builder

Add a dedicated builder that converts rewrite edits into the structured model.

Suggested file:

- `src/RoslynPrototype/Rewrite/DiffBuilder.cs`

Responsibilities:

1. convert one `RewriteEdit` into one `DiffSection`
2. group sections into `DiffFile`
3. preserve deterministic file ordering
4. preserve deterministic edit ordering
5. expose summary counts such as files, sections, blocks, inserted lines, deleted lines, and replaced blocks

The builder must not format text.

### Layer 3: renderers

Add separate renderers that consume `DiffDocument`.

Suggested files:

- `src/RoslynPrototype/Rewrite/TextDiffRenderer.cs`
- optional later: `src/RoslynPrototype/Rewrite/DiffSummaryRenderer.cs`

The first rollout should provide two views:

1. `legacy`
2. `readable`

#### `legacy` renderer

Purpose:

- keep current output shape stable
- allow existing string-based tests to keep passing during migration

Expected shape:

- `--- original #n path:start..end`
- original text
- `+++ rewritten #n`
- replacement text or `<deleted>`

#### `readable` renderer

Purpose:

- improve human readability without changing rewrite logic

Expected improvements:

- explicit file headers
- explicit edit kind
- stable span metadata
- clearer delete / replace / insert markers
- optional short context lines later

The renderer owns only presentation.

## Contract Changes

### `PrototypeAnalysisResult`

Current contract carries:

- `DiffText`
- `DiffFilePath`

Target contract should carry:

- `DiffDocument Diff`
- `string DiffText` only as a compatibility field during migration
- `string? DiffFilePath`

Recommended migration shape:

1. add `DiffDocument` first
2. keep `DiffText` populated by the default renderer
3. move consumers gradually from text-first to model-first
4. remove or de-emphasize direct text coupling only after tests and callers are migrated

### `PrototypeRewriteResult`

Current rewrite result returns:

- rewritten source
- edits
- diff text

Target result should return:

- rewritten source
- edits
- `DiffDocument`
- compatibility `DiffText`

## Directory Aggregation Changes

Current directory-mode aggregation appends strings such as:

- `### {filePath}`
- `{result.DiffText}`

This should be replaced by model aggregation.

Recommended change:

1. `DirectoryResultAggregator` stores `DiffFile` entries instead of raw string sections
2. single-file `.rewrite.diff` output is rendered from the corresponding `DiffFile`
3. multi-file aggregate diff output is rendered from the full `DiffDocument`
4. write-back and diff file path semantics remain unchanged

Affected anchor:

- `src/Host/DeletionDirectoryAnalysisService.cs`

## Dedicated Diff Subsystem

Diff should own its own narrow summary and metadata path instead of integrating with the current text log system.

Recommended additions:

- `DiffSummary`
- optional `DiffStats`
- optional `DiffManifest` or sidecar summary file if future tooling needs stable machine input

Suggested fields:

- `files`
- `edits`
- `sections`
- `blocks`
- `insertLines`
- `deleteLines`
- `replaceBlocks`
- `diffPath`
- `rewriteMs`

Allowed output shapes for the small subsystem:

1. in-memory summary objects returned with analysis results
2. optional plain-text summary block rendered beside the main diff
3. optional separate sidecar file written next to diff output later if needed

Forbidden in the first version:

1. full diff body inside summaries
2. multi-line original or replacement text inside summaries
3. coupling summary shape to the current text log categories, events, or views

This keeps the design small and local:

1. diff formatting remains a renderer concern
2. diff summary remains a diff concern
3. later logging still must not consume diff summary; any cross-subsystem handoff stays outside `.log`

## CLI Surface Direction

First migration window:

1. keep `--diff-out`
2. keep `--no-diff`
3. preserve current default text shape through `legacy`

After the model is stable, add:

- `--diff-view legacy|readable`

Recommended first default:

- `legacy`

Do not change the default to `readable` until compatibility and acceptance are proven.

## Implementation Batches

### Batch 1: introduce the diff model with no user-visible behavior change

Files:

- add `src/RoslynPrototype/Rewrite/DiffModel.cs`
- add `src/RoslynPrototype/Rewrite/DiffBuilder.cs`
- add `src/RoslynPrototype/Rewrite/TextDiffRenderer.cs`
- modify `src/RoslynPrototype/Rewrite/PrototypeRewriter.cs`
- modify `src/RoslynPrototype/Rewrite/PrototypeRewriteResult.cs`

Tasks:

1. create the structured diff model
2. build `DiffDocument` from `RewriteEdit`
3. generate current `DiffText` through the `legacy` renderer
4. keep all current external text behavior stable

Stop condition:

- any existing diff-shape test changes unexpectedly

### Batch 2: move result contracts and consumers to model-first plumbing

Files:

- modify `src/RoslynPrototype/Rewrite/PrototypeAnalysisResult.cs`
- modify `src/Application/DeletionApplicationService.cs`
- modify `src/Host/DeletionCommandHost.cs`
- modify `src/Host/DeletionDirectoryAnalysisService.cs`

Tasks:

1. add `DiffDocument` to analysis result contracts
2. route single-file and directory paths through the new model
3. aggregate directory diffs as model objects, not markdown strings
4. render diff text only at output boundaries

Stop condition:

- file order, edit order, or emitted diff file semantics drift

### Batch 3: add a better readable renderer

Files:

- modify `src/RoslynPrototype/Rewrite/TextDiffRenderer.cs`
- modify `src/Host/DeletionApplicationOptions.cs`
- modify `docs/quick-start.md`
- modify `docs/developer-guide.md` if needed

Tasks:

1. add `readable` rendering mode
2. add CLI selection for diff view
3. keep `legacy` as default
4. add stable tests for `readable`

Stop condition:

- renderer changes force rewrite-core changes

### Batch 4: add dedicated diff summary only

Files:

- modify diff-side result and output paths only
- add tests for `DiffSummary`

Tasks:

1. add a dedicated diff summary object
2. optionally render or persist the summary beside diff output
3. verify summary never carries full diff body

Stop condition:

- any attempt to merge diff rendering and the current text log system into one abstraction

## Test Plan

### New tests required

#### Diff model tests

Add focused tests for:

1. single delete edit
2. single replace edit
3. single insert edit if supported
4. multiple edits in one file
5. multiple files in directory aggregation
6. deterministic ordering by file path and edit index

#### Legacy renderer compatibility tests

Keep and extend tests that assert:

1. current `--- original` and `+++ rewritten` markers remain stable
2. `<deleted>` still appears on delete edits
3. file path and span metadata remain stable

#### Readable renderer tests

Add tests that assert:

1. file header format is stable
2. edit kind labels are stable
3. delete / replace / insert rendering remains deterministic

#### Diff summary tests

Add tests that assert:

1. `DiffSummary` is populated when diff output exists
2. only summary fields are present
3. full diff body is absent

### Existing tests that will likely need touchpoints

Look first at:

- `tests/RoslynDeletionPrototype.Tests/GraphAnalyzerTests.cs`
- `tests/RoslynDeletionPrototype.Tests/Mark/MarkRuleEffectTests.cs`
- `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- `tests/RoslynDeletionPrototype.Tests/TestInfrastructure/TextDiffAssert.cs`

## Verification Commands

Minimum verification after each batch:

```powershell
pwsh -File .\init.ps1
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GraphAnalyzerTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~PipelineComponentTests"
```

If renderer or directory aggregation changes touch docs or CLI guidance, also run:

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
```

## Acceptance Criteria

This plan is complete only when:

1. single-file and directory diff paths both generate a structured diff model first
2. text output is generated only by a renderer layer
3. current default diff text remains compatible during migration
4. a second readable text renderer can be added without touching rewrite-core logic
5. directory aggregation no longer constructs diff output via ad-hoc string concatenation
6. diff summary is owned by the diff subsystem and does not depend on the current text log system
7. `--diff-out`, `--no-diff`, and `DiffFilePath` semantics remain stable

## Risks

### Risk 1: accidental text-shape breakage

Current tests depend on specific diff fragments. Mitigation:

1. keep `legacy` renderer first
2. migrate tests incrementally

### Risk 2: over-designing toward git patch output

This rewrite pipeline is edit-driven and syntax-aware. Forcing full unified diff semantics too early can hide useful structure. Mitigation:

1. keep the first model section-based
2. add hunk-style rendering only if a concrete need appears later

### Risk 3: subsystem boundary drift

It will be tempting to wire diff directly into the current logging design. Mitigation:

1. keep diff summary types local to the rewrite/diff subsystem
2. treat any later logging hookup as an adapter, not as the primary contract

## Recommendation

Implement Batch 1 and Batch 2 first. They produce the real architectural change.

Do not start with renderer cosmetics. The core win is converting diff from a string artifact into a stable internal model that can support:

1. current legacy text
2. future readable text
3. directory-level aggregation
4. future local diff summary and optional adapters
