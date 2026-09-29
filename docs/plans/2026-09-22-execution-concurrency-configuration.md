# Execution Concurrency Configuration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove `execution.maxDegreeOfParallelism`, expose independent configuration limits for every NLISSN concurrency boundary, and eliminate the implicit CPG half-budget cap.

**Architecture:** Keep local CPG WorkBatch and project-pool DOP APIs intact, but replace the global NLISSN execution option with six named limits: directory, CPG, rule group, helper, replay, and admission operations. Wire each consumer directly to its matching limit, remove `FairCapped`, and make Schema 2 require positive values for all six limits. The authoritative design is [运行时并发配置](../CodeDesign/目前设计/运行时并发配置.md).

**Tech Stack:** .NET 10, C# preview, YamlDotNet, JSON Schema 2020-12, existing `AnalysisRuntime`, `BoundedConcurrencyPool`, CPG admission budget, xUnit Host/Contract/Performance tests, and repository serial build/test harness.

---

## Working rules

- Work in `D:\ProjectItem\SourceCode\Net\NL` and preserve unrelated existing user changes.
- Read `Context/progress.md`, the relevant local `AGENTS.md` files, and `docs/harness-runtime.md` before compile-capable commands.
- Before each build or test, inspect active `dotnet.exe` and `csc.exe` processes. Do not terminate an unclear owner.
- Use `Build\Tools\Invoke-SerialDotnet.ps1` for compile-capable commands with `-m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false`.
- Do not use `git reset`, `git checkout --`, or broad cleanup. Do not overwrite unrelated dirty files.
- This plan intentionally changes the YAML contract. There is no compatibility migration from `execution.maxDegreeOfParallelism`.

## Target configuration contract

```yaml
execution:
  directoryMaxDegreeOfParallelism: 4
  cpgMaxDegreeOfParallelism: 12
  groupMaxDegreeOfParallelism: 4
  helperMaxDegreeOfParallelism: 4
  replayMaxDegreeOfParallelism: 4
  maxConcurrentOperations: 8
```

All six values are required positive integers. The existing boolean fields
`directoryParallelism`, `groupParallelism`, and `helperParallelism` remain switches; they do not provide a numeric fallback.

## Current execution state

The implementation changes described by this plan are already present in the working
tree. The focused evidence currently recorded is: `YamlConfigurationLoaderTests` `26/26`,
CPG/runtime Host selections `7/7`, Schema Contract `1/1`, Workspace selections `3/3`,
and `NLISSN.PerformanceTests` serial build with `0 warning / 0 error`. A separate CLI
smoke using the checked-in `Miscellaneous/nlissn.yml` exited with code `0` and wrote a
resolved configuration containing all six named limits.

The short checks are complete: the harness CLI smoke YAML was updated, harness
consistency returned `OK`, the forbidden-reference search found no removed NLISSN global
configuration or CPG admission-policy symbol in production paths, and `git diff --check`
returned exit code 0. This round intentionally does not run the complete Fast, Host, or
Performance tiers, Terraria, Version4, or other long-running real-project tests.

### Task 1: Lock the new configuration contract with failing tests

**Files:**
- Modify: `tests/NLISSN.HostTests/Configuration/YamlConfigurationLoaderTests.cs`
- Modify: `tests/NLISSN.HostTests/TestSupport/CommandHostTestConfigurationExtensions.cs`
- Modify: `tests/NLISSN.PerformanceTests/TestSupport/CommandHostTestConfigurationExtensions.cs`
- Modify: `tests/NLISSN.Testing/AnalysisLegacyOptionsTestExtensions.cs`

**Step 1: Write the failing tests**

Add configuration cases that assert:

- all six new fields map to `ExecutionSettings` independently;
- a document containing only the new fields loads successfully without the old field;
- omission of each field reports that field's required/positive-value diagnostic;
- zero and negative values are rejected independently;
- `execution.maxDegreeOfParallelism` is rejected as an unknown property and is never used as a fallback;
- resolved configuration output contains the six new values and no old field.

Update test-only command argument builders to accept named options for the six limits. Do not make the production YAML loader accept the old global option.

**Step 2: Run the focused tests**

Run:

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter FullyQualifiedName~YamlConfigurationLoaderTests '-m:1' '-nr:false' '-p:UseSharedCompilation=false' '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL because the new YAML fields and validation do not exist yet.

**Step 3: Commit**

Do not commit until the production configuration model is updated; keep the failing tests in the same working change.

### Task 2: Replace the YAML model, schema, examples, and resolved projection

**Files:**
- Modify: `src/NLISSN.Infrastructure/Configuration/ExecutionSettings.cs`
- Modify: `src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs`
- Modify: `Miscellaneous/schemas/nlissn.schema.2.json`
- Modify: `Miscellaneous/schemas/README.md`
- Modify: `Miscellaneous/nlissn.yml`
- Modify: `docs/quick-start.md`
- Modify: `docs/cli-reference.md`

**Step 1: Implement the configuration shape**

Replace the old global and nullable CPG fields with six non-nullable named values in `ExecutionSettings` and `YamlExecution`. Remove `maxDegreeOfParallelism` from the JSON Schema properties and required list. Add the six names to the required list and positive-integer validation.

Use distinct diagnostics for directory, CPG, group, helper, replay, and admission values. Keep existing boolean and rewrite validation unchanged. Keep the resolved artifact projection based on `ExecutionSettings`, so the new fields are fingerprinted automatically.

Update every checked-in YAML example to contain all six values. State in the schema guide and CLI reference that the old field is removed, not inherited.

**Step 2: Run the focused tests**

Run the same `YamlConfigurationLoaderTests` command. Expected: PASS, including the old-field rejection and resolved configuration assertions.

### Task 3: Introduce independent runtime options and wire the host

**Files:**
- Modify: `src/NLISSN.Application/ExecutionRuntime.cs`
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs`
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`

**Step 1: Write the failing runtime tests**

Assert that `RoslynPrototypeExecutionOptions` exposes independent effective values and that a runtime created with six different values preserves all six values through `InvalidateCaches()` and `NextEpoch()`. Add a host-level test proving configuration values are passed without positional global-DOP inheritance.

**Step 2: Implement the runtime options**

Remove `MaxDegreeOfParallelism` and `EffectiveMaxDegreeOfParallelism` from `RoslynPrototypeExecutionOptions`. Remove nullable CPG inheritance. Add named values and effective properties for all six limits plus `MaxConcurrentOperations`.

Update `CreateDefault()` to initialize every limit explicitly from a positive processor-count-based default. Update `CommandHost` to use named arguments from `ExecutionSettings`; do not pass one value as a fallback for another.

**Step 3: Run focused tests**

Run the Host selections for `PipelineComponentTests` and `RuleGraphCompilerTests`. Expected: PASS after all direct constructor call sites use the named limits.

### Task 4: Remove the CPG half-cap policy

**Files:**
- Modify: `src/NLCPG/Builder/CpgBuildAdmissionBudget.cs`
- Modify: `src/NLISSN.Application/ExecutionRuntime.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `tests/NLISSN.HostTests/Application/CpgBuildAdmissionBudgetTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Write the failing admission tests**

Replace the `FairCapped` contract with a test that creates a total CPG budget of 12, requests 12 for a directory file, and asserts `GrantedDegree == 12`. Preserve tests for FIFO waiting, cancellation, release, and high-water accounting.

**Step 2: Implement the minimal budget change**

Delete `CpgBuildAdmissionPolicy`, the policy constructor/Acquire overload, `FairCapped`, and policy metadata from pending requests and leases. A request is capped only by its requested degree and the currently available total budget. Construct the runtime budget from `CpgMaxDegreeOfParallelism` without a policy.

Update directory and single-file call sites to request the independent CPG limit. The directory pool uses the directory limit; it no longer computes or expects half of the CPG value.

**Step 3: Run focused tests**

Run:

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter "FullyQualifiedName~CpgBuildAdmissionBudgetTests|FullyQualifiedName~PipelineComponentTests" '-m:1' '-nr:false' '-p:UseSharedCompilation=false' '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: PASS with no `FairCapped` symbol and no half-DOP assertion.

### Task 5: Route group, helper, replay, and admission consumers

**Files:**
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- Modify: `src/NLISSN.Core/Marking/MarkingEngine.cs`
- Modify: `src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Modify: `src/NLISSN/Artifacts/RewritePlanReplayService.cs`
- Modify: `src/NLISSN.Application/ExecutionRuntime.cs`
- Modify: related Host and Performance tests under `tests/NLISSN.HostTests` and `tests/NLISSN.PerformanceTests`

**Step 1: Add boundary-isolation tests**

Use a recording concurrency pool or existing runtime-aware rules to assert:

- group execution receives only `groupMaxDegreeOfParallelism` when enabled and 1 when disabled;
- helper scans receive only `helperMaxDegreeOfParallelism` when enabled;
- replay uses only `replayMaxDegreeOfParallelism`;
- admission uses only `maxConcurrentOperations` even when all other limits differ.

**Step 2: Implement direct wiring**

Replace each read of `EffectiveMaxDegreeOfParallelism` with the matching effective property. Preserve the existing `ConcurrencyExecutionPolicy` enable/disable behavior. Change `CreateConcurrencyAdmissionOptions` to use `EffectiveMaxConcurrentOperations`, including its derived item reservation, rather than any other limit.

**Step 3: Run focused tests**

Run the existing rule graph, helper, replay, and concurrency pool selections. Expected: all independent values are visible in recorded calls and no production consumer references the removed global property.

### Task 6: Update telemetry, performance identity, and all fixtures

**Files:**
- Modify: `src/NLISSN/Telemetry/RuntimeMeasurementLog.cs`
- Modify: `src/NLISSN/Performance/PerformanceRunIdentityFactory.cs`
- Modify: performance identity/summary tests under `tests/NLISSN.HostTests/Performance` and `tests/NLISSN.PerformanceTests/Performance`
- Modify: all remaining YAML and `ExecutionSettings` fixtures found by `rg`
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgExecutionMatrixTests.cs` and related test helpers

**Step 1: Write the failing observability tests**

Assert that runtime logs and performance identities distinguish directory, CPG, group, helper, replay, and admission values. Two runs that differ only in one named limit must not share a configuration/performance identity.

**Step 2: Implement the projections**

Replace the single runtime log DOP field with named fields or the existing structured execution projection. Populate performance identity values from their matching configuration/runtime options. Keep local WorkBatch telemetry fields unchanged where they describe a local executor.

**Step 3: Migrate fixtures**

Update direct `ExecutionSettings` constructors, YAML snippets, test argument builders, and performance matrix helpers. Remove tests whose only contract is inheritance from the deleted global field and replace them with independent-value tests.

**Step 4: Run focused tests**

Run Host performance artifact tests and the Performance summary harness. Expected: named settings appear in the artifact and all matrix cases remain deterministic.

### Task 7: Search for forbidden global configuration references and verify

**Step 1: Static search**

Run:

```powershell
rg -n --hidden --glob '!**/bin/**' --glob '!**/obj/**' "execution\.maxDegreeOfParallelism|Execution\.MaxDegreeOfParallelism|EffectiveMaxDegreeOfParallelism|CpgBuildAdmissionPolicy|FairCapped" src tests docs Miscellaneous README.md
```

Expected: no NLISSN global configuration/runtime or `FairCapped` references. Remaining `NLCPGBuilderOptions.MaxDegreeOfParallelism`, local concurrency API parameters, and project-export worker options must be reviewed as intentional local contracts rather than blindly renamed.

**Step 2: Build affected projects**

Run serial builds for `src/NLISSN.Infrastructure/NLISSN.Infrastructure.csproj`, `src/NLISSN.Application/NLISSN.Application.csproj`, `src/NLISSN/NLISSN.csproj`, and the owning Host, Contract, and Performance test projects. Expected: zero errors; record any pre-existing warnings separately.

**Step 3: Run affected tests**

Run only the focused configuration/admission/rule/replay selections that cover roughly 10%
of the affected tests, then run the short repository consistency checks:

```powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1 -RepoRoot (Get-Location).Path
git diff --check
```

Do not run the full `Fast`, `Host`, or `Performance` tiers in this round, and do not start
Terraria, Version4, or other long-running real-project tests. Do not record any tier as
passed unless it actually ran to completion. The acceptance condition for this round is the
new config contract, independent boundary limits, no hidden CPG half-cap, and unchanged
semantic results in the focused DOP cases.

## Completion checklist for this round

- [x] `execution.maxDegreeOfParallelism` is absent from the YAML model, schema, examples, and runtime mapping; design/docs may mention its removal as a migration rule.
- [x] Six new positive integer limits are validated and mapped independently.
- [x] Directory and CPG limits are independent; a CPG request of 12 is not reduced to 6 by policy.
- [x] `FairCapped` and its policy metadata are removed.
- [x] Group, helper, replay, and admission consumers use their matching values.
- [x] Runtime telemetry, performance identity, resolved configuration, and tests reflect the new fields.
- [x] Focused tests, short affected builds, harness consistency, and diff checks have current evidence; complete tiers are explicitly deferred.
