# Rule Identity CapabilityId Implementation Plan

> 本方案已被 2026-09-08 的 RuleId-only 决策取代，不得按本文执行。请改用 [RuleId-only 执行计划](2026-09-08-rule-identity-ruleid-execution.md)。

> **状态：历史方案（已废弃）。** 本计划曾建议删除 `RuleId`、保留 `CapabilityId`；最新决策相反：删除 `CapabilityId`、保留重新定义后的 `RuleId`。请改用 [RuleId-only 执行计划](2026-09-08-rule-identity-ruleid-execution.md)，不要执行本文步骤。

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 删除 `IRuleDefinition.RuleId`，把当前规则定义的稳定 `CapabilityId` 作为唯一规范身份，并一次性迁移图、结果、证据、决策、rewrite plan、diff 分类和测试。

**Architecture:** 规则实现只声明 `CapabilityId`；阶段由强类型的 `RuleKind` 提供；执行图使用结构化的 `RuleNodeId(RuleKind, CapabilityId)`，不再通过字符串切割推断来源。所有阶段产出和新工件只记录 `CapabilityId`，rewrite plan schema 升级后直接拒绝旧版本，不设置兼容窗口或迁移字段。

**Tech Stack:** .NET SDK 10.0.200-preview.0.26103.119、C#、Roslyn、xUnit、PowerShell、NLISSN 的 Unit/Contract/Host/Performance 测试层，以及现有串行构建与 harness 脚本。

---

## 执行边界

这是一次不兼容的身份模型收敛，不是逐步兼容迁移。执行前先阅读 [规则身份收敛设计](2026-09-08-rule-identity-capabilityid-design.md)，并确认当前工作区中已有的用户改动没有被覆盖。

本计划不允许引入以下行为：

- 在接口、结果模型或工件中保留第二个规则身份字符串。
- 通过旧字符串到新字符串的映射、别名、双读单写或隐式回退恢复旧身份。
- 为迁移增加实例号、修订号、指纹、版本身份或其他暂存字段。
- 让 `Name`、`RuleSemanticTag`、文件位置、执行顺序或命中序号承担规则身份职责。
- 从 `RuleNodeId.Value` 反解析阶段或能力键。

现有 `CapabilityId` 字面值是迁移基线。具体规则如果已经显式覆盖 `CapabilityId`，保留该值；如果目前只通过 `RuleId` 间接提供能力键，则把当前有效的 `CapabilityId` 值移到新的唯一属性上。不得因为删除属性而依赖类名、注册顺序或生成序号重新计算身份。

本轮文档只描述实施方案。开始实施前不修改 `src/**/*.cs`、`tests/**/*.cs`、`Context/feature_list.json` 或 `Context/progress.md`。

## 完成判定

以下条件全部满足才算完成：

1. `IRuleDefinition` 和四个阶段基类只公开 `CapabilityId`，不存在 `RuleId` 属性或参数。
2. 规则目录仍以 `StringComparer.Ordinal` 验证非空、全局唯一的 `CapabilityId`。
3. 所有 Mark、Propagate、Lift、Propose 规则的声明、内部产出和硬编码规则比较都使用 `CapabilityId`。
4. `RuleNodeId` 由 `RuleKind` 与 `CapabilityId` 结构化组成，内部相等性不依赖 `Value` 文本。
5. mark、propagation、lift、decision、evidence、validation、rewrite plan 和 diff category 都使用 `CapabilityId`，不新增同义字段。
6. rewrite plan 新 schema 写出 `capabilityId`；旧 schema 在读取入口直接失败，不转换、不忽略旧身份字段、不双读单写。
7. 默认规则集、全 feature 规则集、禁用规则图、DOP 1/2/16 的 graph、evidence、decision、rewrite 输出保持语义等价；身份字段按新命名输出。
8. 生产代码和测试代码中不再出现独立的 `RuleId` 标识（`RuleNodeId` 类型名不属于该标识），且没有为迁移新增身份字段。
9. 目标 build、Unit、Contract、Host、Performance、CLI smoke 与 harness 检查均有实际命令证据；未执行的检查不得写成通过。

## 影响面与文件地图

| 区域 | 主要文件 | 目标 |
| --- | --- | --- |
| 声明合同 | `src/NLISSN.Rule/IRuleDefinition.cs`；`src/NLISSN.Core/Marking/RuleDefinitionMark.cs`；`src/NLISSN.Core/Propagation/RuleDefinitionPropagate.cs`；`src/NLISSN.Core/Lifting/RuleDefinitionLift.cs`；`src/NLISSN.Core/Decision/RuleDefinitionPropose.cs` | 删除重复身份入口，保留 `CapabilityId` |
| 图身份 | `src/NLISSN.Rule/RuleGraph.cs`；`src/NLISSN.Application/Analysis/RulePipeline.cs`；`src/NLISSN.Core/Validation/RuleBindingValidator.cs` | 使用结构化 `(RuleKind, CapabilityId)`，取消字符串反解析 |
| 阶段执行 | `src/NLISSN.Core/Marking/MarkRecord.cs`；`src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`；`src/NLISSN.Core/Lifting/LiftedMarkRecord.cs`；`src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`；各阶段 engine/executor | 统一规则产出归因 |
| 决策与证据 | `src/NLISSN.Core/Decision/DecisionModel.cs`；`src/NLISSN.Core/Decision/AnalysisEvidence.cs`；`src/NLISSN.Core/Validation/AnalysisValidationReport.cs`；`src/NLISSN.Core/Validation/DecisionBindingValidator.cs` | 统一决策、证据和问题模型字段 |
| 传播去重 | `src/NLISSN.Core/Propagation/PropagationFactKey.cs`；`src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs` | 用能力键参与事实去重 |
| rewrite 工件 | `src/NLISSN.Core/Rewrite/RewritePlanModel.cs`；`src/NLISSN/Artifacts/RewritePlanArtifactService.cs`；`src/NLISSN/Artifacts/RewritePlanReplayService.cs`；`src/NLISSN/Artifacts/CategoryDiffArtifactService.cs`；`src/NLISSN/Artifacts/RuleDiffCategory.cs` | 新工件只写新字段，旧 schema 直接拒绝 |
| 具体规则 | `src/NLISSN.Rules/Mark/**`、`src/NLISSN.Rules/Propagate/**`、`src/NLISSN.Rules/Lift/**`、`src/NLISSN.Rules/Propose/**` | 将声明值和规则内部引用收敛到 `CapabilityId` |
| 测试合同 | `tests/NLISSN.ContractTests/**`、`tests/NLISSN.HostTests/**`、`tests/NLISSN.UnitTests/**`、`tests/NLISSN.PerformanceTests/**` | 先红后绿地证明身份、图、工件和 DOP 等价 |
| 文档与状态 | `docs/plans/`、`Context/progress.md`、`Context/feature_list.json` | 只在实施时同步状态；本轮不修改状态文件 |

实现时以 `rg -n --glob '*.cs' '\bRuleId\b' src tests` 重新生成完整消费者清单，不要只按本表修改。`RuleNodeId`、`RuleDefinition*` 等包含 `Rule` 的其他类型名不应被机械替换。

## Task 0: 建立基线、feature 状态和身份清单

**Files:**

- Read: `AGENTS.md`、`Context/progress.md`、`Context/feature_list.json`、`Miscellaneous/init.ps1`、`docs/harness-runtime.md`、`src/NLISSN/AGENTS.md`、`tests/AGENTS.md`、`docs/AGENTS.md`。
- Modify at implementation start: `Context/feature_list.json`、`Context/progress.md`。
- Create: `tests/NLISSN.HostTests/Identity/RuleIdentityBaselineTests.cs`。
- Read: `src/NLISSN/Composition/RulePipelineComposer.cs` and the four `RuleDefinition*` stage bases。

### Step 0.1: 保存工作区边界并运行启动检查

Run:

~~~powershell
git status --short
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
pwsh -File .\Miscellaneous\init.ps1
~~~

Expected:

- 已有的 `.learnings/`、`obj/`、`Miscellaneous/schemas/` 和文档改动保持原样。
- `Miscellaneous/init.ps1` 的 NLISSN 健康检查通过；若 restore 再次阻塞，记录“已读源码确认契约，但未完成端到端运行验证”，不能把后续静态检查当作运行验证。

### Step 0.2: 生成当前身份清单

Run:

~~~powershell
rg -n --glob '*.cs' 'CapabilityId|RuleId' .\src\NLISSN.Rule .\src\NLISSN.Core .\src\NLISSN.Application .\src\NLISSN.Rules .\src\NLISSN .\tests
rg -l --glob '*.cs' '\bRuleId\b' .\src .\tests | Sort-Object
~~~

把每个具体规则的阶段、类型全名、当前 `CapabilityId`、当前 `RuleId`（仅作为迁移前观察值）和是否显式覆盖 `CapabilityId` 记录在测试基线中。特别核对当前不可达方法 Mark/Propose 的能力键已经分别是 `mark.unreachable-method` 与 `propose.unreachable-method`，不能把阶段重复的旧字面值搬回新字段。

### Step 0.3: 登记实施 feature

只有开始修改 C# 时才在 `Context/feature_list.json` 增加或切换本次 feature，并在 `Context/progress.md` 把当前 feature、验证边界和下一步压缩成当前事实。设计文档阶段不改变现有 `rule-catalog-source-generator-design` 状态。

### Step 0.4: 写身份红灯测试

在 `RuleIdentityBaselineTests.cs` 增加最小合同测试：

~~~csharp
var propertyNames = typeof(IRuleDefinition)
    .GetProperties()
    .Select(property => property.Name)
    .ToHashSet(StringComparer.Ordinal);

Assert.Contains(nameof(IRuleDefinition.CapabilityId), propertyNames);
Assert.DoesNotContain("RuleId", propertyNames);
~~~

同时为当前四个阶段基类和关键结果模型建立允许字段断言。测试应在当前源码上按预期失败，证明红灯确实针对目标变更，而不是测试误报。

Run:

~~~powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleIdentityBaselineTests"
~~~

Expected: FAIL because `IRuleDefinition.RuleId` still exists. Record the failure text before continuing.

### Step 0.5: Checkpoint

建议提交边界：

~~~text
test: freeze capability identity baseline
~~~

不要在此提交中删除接口字段。

## Task 1: 删除接口重复身份并迁移具体规则声明

**Files:**

- Modify: `src/NLISSN.Rule/IRuleDefinition.cs`。
- Modify: `src/NLISSN.Core/Marking/RuleDefinitionMark.cs`。
- Modify: `src/NLISSN.Core/Propagation/RuleDefinitionPropagate.cs`。
- Modify: `src/NLISSN.Core/Lifting/RuleDefinitionLift.cs`。
- Modify: `src/NLISSN.Core/Decision/RuleDefinitionPropose.cs`。
- Modify: every concrete rule under `src/NLISSN.Rules/Mark/**`, `Propagate/**`, `Lift/**`, and `Propose/**` returned by the Task 0 inventory.
- Modify: matching rule-construction tests under `tests/NLISSN.HostTests/`, `tests/NLISSN.UnitTests/`, and `tests/NLISSN.ContractTests/`.
- Read and follow: `约束/Google-CSharp-Style-Guide-约束.md` and `约束/测试代码编写教程.md` before editing C# or tests.

### Step 1.1: Implement the target declaration contract

Make `IRuleDefinition` contain the existing contract plus exactly one rule identity property:

~~~csharp
public interface IRuleDefinition
{
    string CapabilityId { get; }

    RuleInputCardinality InputCardinality { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }
}
~~~

Each stage base must declare/forward `CapabilityId` and remove its separate identity property. Keep `Name`, `RequiredCapabilities`, allowed-node collections, contracts, and stage execution methods unchanged unless compilation proves a signature update is required.

### Step 1.2: Migrate values without inventing identities

- Concrete rules that already declare `CapabilityId` keep the exact current literal.
- Concrete rules that only declare the old property move the current effective capability value to `CapabilityId`; they do not retain an additional property.
- Replace rule-internal provenance arguments such as `new MarkRecord(...)`, `DeleteDecisionFactory.CreateDeleteDecision(...)`, and `new DecisionUnit(...)` with `CapabilityId`.
- Replace hard-coded comparisons to a rule identity with the corresponding current capability key, preferably through the existing domain constant or an explicit local `const` rather than a new identity abstraction.
- Do not convert `RuleSemanticTag`, `Name`, `Reason`, or syntax/node anchors into identity fields.

### Step 1.3: Compile the rule assembly

Run:

~~~powershell
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -v:minimal -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false
~~~

Expected: no `RuleId` contract errors; no concrete rule is missing `CapabilityId`; `RuleCatalog.ValidateRules` still sees the same current capability-key set.

### Step 1.4: Turn the contract test green

Run the focused identity and rule registry tests:

~~~powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleIdentityBaselineTests|FullyQualifiedName~MarkRuleRegistryCoverageTests"
~~~

Expected: PASS, including non-empty and globally unique capability keys.

### Step 1.5: Checkpoint

~~~text
refactor: make CapabilityId the rule declaration identity
~~~

## Task 2: Replace string rule-node identity with a structured graph key

**Files:**

- Modify: `src/NLISSN.Rule/RuleGraph.cs`。
- Modify: `src/NLISSN.Application/Analysis/RulePipeline.cs`。
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`。
- Modify: `src/NLISSN.Core/Validation/RuleBindingValidator.cs`。
- Modify: `src/NLISSN.Rule/RuleStructureContractGraphCompiler.cs` if its graph declarations require the changed key。
- Modify: tests `tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs`, `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`, `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`, and `tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs`.

### Step 2.1: Define the structured key

Replace the string-only construction path with an immutable key equivalent to:

~~~csharp
public sealed record RuleNodeId(RuleKind Kind, string CapabilityId)
{
    public string Value => $"{Kind}:{CapabilityId}";

    public static RuleNodeId For(RuleKind kind, IRuleDefinition rule) =>
        new(kind, rule.CapabilityId);
}
~~~

Validate `CapabilityId` at construction. `Value` is only a display/snapshot representation; do not expose a parser and do not use `IndexOf(':')`, slicing, or splitting to recover either component.

### Step 2.2: Migrate graph construction

In `RulePipeline`, `RuleGraphAnalysisExecutor`, and contract graph compilation, construct nodes from `declaration.Kind` plus `declaration.Rule.CapabilityId`. Preserve active/disabled declarations, stage order, dependency order, and marker dependencies. Update all `RuleNodeId.For` call sites in one pass.

### Step 2.3: Migrate validator binding

In `RuleBindingValidator`, compare `mark.CapabilityId` directly with `node.NodeId.CapabilityId`; use `node.Kind` for stage checks. Preserve existing validation codes and stable-key ordering unless the changed field name is part of the diagnostic payload. The validator must not derive an expected identity by parsing `node.NodeId.Value`.

### Step 2.4: Test equality and negative cases

Add or update tests for:

- same `(RuleKind, CapabilityId)` produces equal node IDs;
- different stage or capability produces a different node ID;
- a display `Value` change cannot be used as an alternate source of identity;
- a mark attributed to another capability is rejected by `BIND008`;
- graph dependencies and marker nodes remain unchanged apart from the new key representation.

Run:

~~~powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleGraphCompilerTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~BindingValidatorTests"
~~~

Expected: PASS with no string-reversal helper left in the validator or graph compiler.

## Task 3: Migrate stage results, propagation facts, decisions, and evidence

**Files:**

- Modify: `src/NLISSN.Core/Marking/MarkRecord.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`。
- Modify: `src/NLISSN.Core/Lifting/LiftedMarkRecord.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationFactKey.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs` and `src/NLISSN.Core/Propagation/PropagationEngine.cs`。
- Modify: `src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`。
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`。
- Modify: `src/NLISSN.Core/Decision/AnalysisEvidence.cs`。
- Modify: `src/NLISSN.Core/Validation/AnalysisValidationReport.cs` and `src/NLISSN.Core/Validation/DecisionBindingValidator.cs`。
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs` and `src/NLISSN.Application/Analysis/ApplicationService.cs` where these models are assembled.
- Modify: tests named by `rg -l --glob '*.cs' '\bRuleId\b' tests`.

### Step 3.1: Rename the provenance field in data models

Rename the data-bearing property/constructor parameter in these models to `CapabilityId`:

- `MarkRecord`;
- `PropagatedMarkRecord`;
- `LiftedMarkRecord`;
- `DecisionUnit` and `RuleDecision`;
- `ValidationIssue`;
- `PropagationFactKey`;
- evidence node/edge factory inputs that currently accept the rule identity.

Do not add a second property for source compatibility. Preserve nullability and constructor defaults where they are behaviorally meaningful. Update XML comments to say “能力标识” or “规则规范键”, not “旧规则 ID”.

### Step 3.2: Preserve evidence-key determinism

`AnalysisEvidence` may continue to include the capability key in stable evidence keys, but every factory and `CreateKey` call must receive `CapabilityId`. Keep anchor, span, semantic tag, and summary components unchanged. A renamed field must not change evidence ordering or deduplication beyond the intended serialized name.

### Step 3.3: Preserve fixed-point semantics

Use `CapabilityId` in propagation fact keys and fixed-point source/admitted dictionaries. Retain the existing depth, source-mark, payload, and `RuleEvidenceOrigin` behavior. Verify that two rules with different capability keys never collapse into one fact even when their syntax spans and semantic tags match.

### Step 3.4: Run focused tests

Run:

~~~powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~BindingValidatorTests|FullyQualifiedName~PropagationRuleExpansionTests|FullyQualifiedName~FlowSummaryDeletionSafetyTests"
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~DefaultRemovalProposalRuleTests|FullyQualifiedName~DirectoryAnalysisUseCaseTests"
~~~

Expected: PASS; no test should need to inspect an obsolete provenance property.

## Task 4: Migrate the complete analysis and decision pipeline

**Files:**

- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`。
- Modify: `src/NLISSN.Application/Analysis/RulePipeline.cs`。
- Modify: `src/NLISSN.Core/Marking/MarkingEngine.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationEngine.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs`。
- Modify: `src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`。
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`。
- Modify: `src/NLISSN.Core/Validation/RuleBindingValidator.cs` and `src/NLISSN.Core/Validation/DecisionBindingValidator.cs`。
- Modify: every remaining source file returned by `rg -l --glob '*.cs' '\bRuleId\b' src`.

### Step 4.1: Update all rule-to-output boundaries

At each execution boundary, pass the current rule's `CapabilityId` into output factories and compare outputs with the current rule's `CapabilityId`. This includes mark deduplication, proposal recording, decision-unit creation, evidence recording, and graph-node lookup. Do not obtain identity from a type name or from a graph display string.

### Step 4.2: Keep stage ownership explicit

The active rule collection still supplies `RuleKind` through its typed collection. A result carrying `CapabilityId` does not by itself authorize it to run in another stage. Preserve the existing Mark → Propagate → Lift → Propose scheduling and contract checks.

### Step 4.3: Run the full source-side identity audit

Run:

~~~powershell
rg -n --glob '*.cs' '\bRuleId\b' .\src .\tests
rg -n --glob '*.cs' 'IndexOf\(.*:|Split\(.*:|Substring\(' .\src\NLISSN.Rule .\src\NLISSN.Core .\src\NLISSN.Application | Select-String 'NodeId|rule|Rule'
~~~

Expected: the first command returns no standalone `RuleId` member/parameter in production or test C#; the second command returns no rule-node identity parser. Do not treat a silent command as proof by itself; combine it with compilation and tests.

### Step 4.4: Checkpoint

~~~text
refactor: propagate CapabilityId through analysis results
~~~

## Task 5: Migrate rewrite plan, artifact schema, replay, and diff categories

**Files:**

- Modify: `src/NLISSN.Core/Rewrite/RewritePlanModel.cs`。
- Modify: `src/NLISSN.Core/Rewrite/PrototypeRewriter.cs` and `src/NLISSN.Core/Rewrite/PrototypeRewriteResult.cs` where edits are created or consumed.
- Modify: `src/NLISSN/Artifacts/RewritePlanArtifactService.cs`。
- Modify: `src/NLISSN/Artifacts/RewritePlanReplayService.cs`。
- Modify: `src/NLISSN/Artifacts/CategoryDiffArtifactService.cs`。
- Modify: `src/NLISSN/Artifacts/RuleDiffCategory.cs`。
- Modify: `src/NLISSN/Hosting/CommandHost.cs` if artifact or diagnostic output is projected there.
- Modify: `tests/NLISSN.HostTests/Rewrite/RewritePlanPersistenceTests.cs`。
- Modify: `tests/NLISSN.HostTests/Artifacts/CategoryDiffArtifactServiceTests.cs` and `RuleDiffCategoryTests.cs`。

### Step 5.1: Change the edit model and new schema

Rename `RewritePlanEdit.RuleId` to `CapabilityId`. Keep edit span, original text, replacement text, source path, and ordering unchanged. Change `RewritePlanArtifactService.SchemaVersion` from `1` to `2`; new JSON must contain `capabilityId` according to the existing serializer naming policy.

Update every category lookup and replay path to call `RuleDiffCategoryRegistry.Resolve(edit.CapabilityId)` or its equivalent. Replace the category table keys with the current proposal capability keys, preserving the existing category classification. Do not use the old text as a fallback lookup key.

### Step 5.2: Reject prior artifacts at the manifest boundary

The artifact reader must validate the manifest schema before using any plan edit. A schema other than `2` must fail with the existing invalid-artifact error path and a message that names the unsupported schema. It must not:

- deserialize a prior edit identity into the new property;
- inspect an alternate old JSON member;
- synthesize a new capability key;
- silently skip an edit whose identity is unknown.

Add a test that writes a prior-schema manifest and asserts a deterministic failure before replay. Add a second test that writes a schema-2 plan with `capabilityId` and proves replay/category rendering succeeds.

### Step 5.3: Verify category completeness

Build the proposal capability-to-category table from the existing rule inventory and assert that every proposal capable of producing a non-skip rewrite has exactly one category. An unknown capability must fail closed with the existing invalid-category error path.

Run:

~~~powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RewritePlanPersistenceTests|FullyQualifiedName~CategoryDiffArtifactServiceTests|FullyQualifiedName~RuleDiffCategoryTests"
~~~

Expected: PASS; serialized plans expose only `capabilityId` and the schema-2 reader rejects prior schema values.

## Task 6: Rebuild identity and behavior contracts in tests

**Files:**

- Modify: `tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs`。
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`。
- Modify: `tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs`。
- Modify: `tests/NLISSN.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`。
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgExecutionMatrixTests.cs`。
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs` only where identity snapshots are asserted.
- Add or extend: `tests/NLISSN.ContractTests/Identity/RuleIdentityContractTests.cs` if the existing Host contract is not sufficient.

### Step 6.1: Assert the public identity surface

Assert all of the following:

- `IRuleDefinition` has `CapabilityId` and no `RuleId`.
- each stage base exposes no second string identity;
- the concrete rule set has non-empty, ordinal-unique capability keys;
- changing the display name or registration order does not alter a capability key;
- result and artifact models expose `CapabilityId`, not a parallel identity property;
- `RuleSemanticTag` remains an independent semantic-port value.

Use reflection only for test contracts; do not introduce reflection into production registration or execution.

### Step 6.2: Assert graph and output equivalence

For default and all-feature configurations compare exact, ordered snapshots of:

- concrete rule type and stage membership;
- `CapabilityId` values;
- required CPG capabilities;
- enabled and disabled declarations;
- graph node `(RuleKind, CapabilityId)` pairs and dependency edges;
- evidence node/edge stable keys;
- decision capability keys and action/span bindings;
- rewrite edit ordering, spans, original text, replacement text, and capability keys.

Counts alone are insufficient. A changed membership, order, edge, span, or capability key fails the test.

### Step 6.3: Assert DOP determinism

Run the same fixture at DOP 1, 2, and 16 and compare the complete graph/evidence/decision/rewrite snapshots after sorting only by the repository's existing deterministic order. Do not weaken comparisons by discarding capability keys or stable evidence keys.

Run the focused matrix first:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~CpgExecutionMatrixTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~GraphAnalyzerTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~RuleGraphCompilerTests"
~~~

Expected: PASS with the same semantic outputs at every tested DOP.

## Task 7: Update documentation and implementation handoff

**Files:**

- Modify: `docs/plans/2026-09-08-rule-identity-capabilityid-research.md` only if it is still displayed as a current recommendation; mark it as historical research and link this design/execution pair.
- Verify: `docs/plans/2026-09-08-rule-identity-capabilityid-design.md`。
- Modify at implementation start/end: `Context/progress.md` and `Context/feature_list.json`。
- Inspect: `docs/quick-start.md`, `docs/cli-reference.md`, `docs/developer-guide.md`, `docs/contributing.md` for stale field names; change them only if they describe the affected artifact contract。

### Step 7.1: Keep research and design boundaries clear

The research document may retain historical comparisons, but its first visible notice must say that compatibility proposals in that historical document are rejected by the current no-transition design. The design document is normative. The execution document is the only implementation sequence.

### Step 7.2: Record only current status

At implementation completion, `Context/progress.md` must contain only:

- the active feature ID;
- the actual verification evidence;
- any remaining unverified boundary or blocker;
- the next action, if any.

Move detailed command history to the existing historical mechanism only when the repository workflow requires it. Update `Context/feature_list.json` status and definition-of-done evidence together; do not mark the feature complete because a source scan passed.

## Task 8: Run the complete verification sequence

Run in this order, preserving actual output and exit codes:

### Step 8.1: Harness and build

~~~powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
pwsh -File .\Miscellaneous\init.ps1
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -v:minimal -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false
~~~

Expected: both health and full application build pass with no source compile errors.

### Step 8.2: Test tiers

~~~powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -All
~~~

Expected: Unit, Contract, Host, and Performance records all exit `0`. Keep the generated `Build\TestResults\<run-id>\run.json` as evidence; do not infer success from a partial console output.

### Step 8.3: CLI smoke and harness consistency

~~~powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
~~~

Expected: the script prints `[check-harness-consistency] OK` and its configuration smoke exits `0`. If the known pre-existing `AGENTS.md` wording assertion still fails, record that exact failure and do not claim the harness passed.

### Step 8.4: Identity and document audits

~~~powershell
rg -n --glob '*.cs' '\bRuleId\b' .\src .\tests
rg -n --glob '*.cs' 'Legacy|PreviousRule|InstanceId|Revision|Fingerprint|RuleVersion|Alias' .\src .\tests
git diff --check -- .\docs\plans\2026-09-08-rule-identity-capabilityid-design.md .\docs\plans\2026-09-08-rule-identity-capabilityid-execution.md .\docs\plans\2026-09-08-rule-identity-capabilityid-research.md
~~~

Expected:

- the first two scans return no migration identity property or obsolete standalone `RuleId` in source/test C#; comments and unrelated ordinary words must be reviewed rather than blindly suppressed;
- scoped Markdown diff check is clean;
- all relative links in the three plan documents resolve.

### Step 8.5: Final review checkpoint

Before reporting completion, review the final diff for:

- accidental modifications outside the requested source/test implementation or explicitly updated status/docs;
- any compatibility branch that reads an old artifact shape;
- any `RuleNodeId` string parser;
- any capability key derived from class name or registration order;
- changed DOP graph/evidence/decision/rewrite output not covered by a failing test;
- unverified commands described as passed.

Recommended implementation commits, each independently buildable where practical:

~~~text
test: freeze capability identity contract
refactor: remove RuleId from rule declarations
refactor: use structured capability rule node IDs
refactor: propagate CapabilityId through analysis outputs
refactor: migrate rewrite plan identity schema
test: prove capability identity and DOP equivalence
docs: record capability identity migration completion
~~~

Do not combine unrelated generated `obj` changes or existing user modifications into these commits.

## Handoff after this document-only turn

The current turn creates the design and execution documents only. It does not claim that the interface deletion, schema change, source migration, tests, or full verification have been performed. The next implementation session should begin at Task 0, rerun `Miscellaneous/init.ps1`, re-read the dirty-worktree state, and make the identity contract red before changing production code.
