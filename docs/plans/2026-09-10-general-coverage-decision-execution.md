# Coverage 与 Decision 通用约束模型执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 Mark、Coverage、Proof、Intent、DecisionPlanner、Rewrite 和重绑定收敛为单向 authority funnel，防止拓扑事实或语法 span 静默授权结构删除，并保持现有 AND/Delete、OR/Replace 和父控制结构删除行为。

**Architecture:** 采用 `Fact -> CoverageContract/Proof -> EditIntent -> DecisionPlan -> ExecutablePlan -> Rewrite -> Rebind/Postcondition` 的单向链路。Mark/Propagate 只发布带 provenance 和 certainty 的事实，Lift 生成绑定目标的证明，Proposal 生成带 footprint 的候选，唯一 DecisionPlanner 负责组合、支配、冲突和 atomic group，Rewrite 只执行已验证的计划。

**Tech Stack:** C#、.NET 10、Roslyn syntax/semantic model、NLISSN Mark/Propagate/Lift/Propose 管道、xUnit、PowerShell、现有 Unit/Contract/Host/Performance 测试和 harness 脚本。

---

## 1. 文档关系与执行边界

本计划实施以下设计，不重新解释设计中的领域结论：

- [设计文档](2026-09-10-general-coverage-decision-model.md)：定义 Fact、Proof、Intent、DecisionPlan、Coverage 需求、父子支配、residual mapping、Unknown 和 authority funnel。
- [Coverage/Decision 组合研究](../research/2026-09-10-coverage-decision-composition-research.md)：记录当前代码证据、GitHub 一手实现和组合模型对照。
- [性能与事实传播对照](../research/2026-09-10-performance-fact-propagation-llvm-comparison.md)：记录 provenance、去重和传播开销的约束。

本计划是当前实施入口。早期的 [Coverage Decision Constraints 执行计划](2026-09-09-coverage-decision-constraints-execution.md) 只覆盖较窄的 typed coverage/footprint 迁移；执行本计划时不得把两份计划并行作为两个 resolver 真相源。需要复用的测试契约应迁移到本计划对应阶段，旧计划保留为历史参考。

本轮只创建和维护设计/执行文档，不修改生产代码。真正执行时必须遵循以下边界：

- 不把 `Delete`、`Replace`、父子 winner、冲突选择、atomic group 提交或 Rewrite 执行放入 Mark。
- 不用 `Reason` 文本、生成顺序、`FinalNode.Span` 大小或 replacement span 推断结构授权。
- 任何无法证明的破坏性动作都返回 `Rejected`、`Conflict`、`Unknown` 或整组 `Skip`，不能静默丢弃候选。
- 每个阶段先写失败测试，再做最小实现，再运行阶段测试；未通过阶段不得删除旧路径。
- 保持用户已有工作树修改，不使用 `git reset --hard`、`git checkout --`、全目录清理或覆盖式脚本。

## 2. 执行前置条件

### 2.1 工作树和分支

执行者首先保存工作树快照：

```powershell
git status --short
git branch --show-current
```

建议从当前已审查基线创建隔离分支 `codex/coverage-decision-model`。若当前工作树有未提交的用户修改，不能通过 reset/stash 隐藏它们；应先由仓库维护者决定是提交、复制到独立 worktree，还是在当前工作树继续。创建 worktree 前先确认目标目录不是现有工作树：

```powershell
git worktree list
git worktree add ..\NL-coverage-decision-model -b codex/coverage-decision-model master
```

如果不能安全创建 worktree，则在当前目录执行，并在每个提交前只 stage 本阶段列出的文件。

### 2.2 环境健康检查

从仓库根目录执行：

```powershell
pwsh -File .\Miscellaneous\init.ps1
```

预期：输出 `build check passed`。执行 .NET 命令前保留仓库要求的本地 CLI home：

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
```

### 2.3 不变行为基线

在改动前运行并保存实际输出。当前代码必须继续满足：

| 场景 | 预期行为 | 主要测试 |
| --- | --- | --- |
| `ready && s.IsReady`，目标为 `s` | 外层 `if` 可结构删除 | `DecisionStructureValidationTests`、`GraphAnalyzerTests` |
| `ready || s.IsReady || fallback`，目标为 `s` | 只做逻辑表达式 `Replace`，不删除外层 `if` | `DecisionComplexTests` |
| 父控制结构删除与 body 删除 | 父结构可以在已有合法证明时覆盖 body | `DecisionComplexTests`、`PipelineComponentTests` |
| 只有一个逻辑 sibling 被标记 | 不是完整 coverage | `RuleStructureContractTests` |

定向命令：

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~GraphAnalyzerTests.Analyze_LogicalAndCondition_RightTargetDeletesIf|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DefaultRemovalProposalRuleTests" --logger "console;verbosity=minimal"
```

预期：已有测试全部通过。任何基线失败先修复环境或记录现状，不把它归因于本计划。

## 3. 目标管道和公共术语

实施完成后的执行顺序固定为：

```text
Mark/Propagate
  -> ValidatedFact
  -> CoverageContract
  -> GoalSpecificProof
  -> IntentDraft
  -> AuthorizedIntent
  -> ExecutablePlan
  -> Rewrite
  -> Rebind/PostRewriteValidator
```

阶段权限如下：

| 阶段 | 可以产出 | 不可以产出 |
| --- | --- | --- |
| Mark | `AtomicTarget`、`TopologyHost`、引用/调用/查询状态、provenance | `Delete`、`Replace`、`StructureComplete`、winner |
| Propagate | 关系事实、payload、来源链、certainty | 最终动作、支配、atomic group 完成 |
| Lift | `LogicalReduction`、`ExpressionReplacement`、`StructureComplete` 等 Proof | 跨候选选赢家、执行 Rewrite |
| Propose | `IntentDraft`、footprint、behavior budget、atomic group 归属 | 解决其他候选冲突 |
| DecisionPlanner | `Selected`、`Composed`、`Dominated`、`Rejected`、`Conflict`、`Unknown` | 重新猜测 Mark 语义 |
| Rewrite | 执行 `ExecutablePlan`，拒绝非法重叠 | 根据 span 选择业务 winner |
| PostRewrite | 重新绑定、编译和保留义务验证 | 用失败结果补发部分编辑 |

关键类型的最小形状：

```text
CoverageProof:
  Goal, Status, AcceptedEvidence, RejectedEvidence,
  MissingRequirements, ConsumedNodeKeys, PreservedObligations

EditIntent:
  Anchor, Operation, ReadSet, EraseSet, WriteSet,
  PreserveSet, ProofSet, AllowedBehaviorChanges,
  Composition, ResidualMapping, AtomicGroup

DecisionPlan:
  Selected, Composed, Dominated, Rejected, Conflicts,
  Unknowns, DominanceRelations, AtomicGroups
```

`StructureComplete` 不能作为 Mark capability；它只能由 Lift 根据完整输入生成。`Unknown`、`Unavailable`、`Unresolved`、`Truncated`、`Unsupported` 和 `Failed` 不能转换为 `NoMatch`。

## 4. 分阶段执行

### Task 1: 冻结行为并建立可观察的候选结果

**Files:**
- Modify: `tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`
- Create: `tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs`
- Modify: `src/NLISSN.Core/Decision/AnalysisEvidence.cs` only if现有证据模型无法记录候选结果

**Step 1: Write the failing tests**

覆盖以下行为：

- topology host 与完整结构事实落在同一个 `if` 条件时，只有完整 proof 才能产生外层 Delete。
- synthetic outer Delete + inner logical Replace 没有 dominance proof 时，inner candidate 不能静默消失；结果必须是冲突、拒绝父候选或冲突域跳过。
- 同一候选对在有 `StructureComplete`、`EraseSet` 和 `DominatesChildren` 时只保留外层 Delete。
- 两个部分重叠 Replace 不依赖输入顺序，结果为 `Conflict` 或整域 `Skip`。
- 每个 `Rejected`、`Dominated`、`Conflict` 和 `Unknown` 都有稳定候选 ID、关系和原因。

测试先通过现有 `ApplicationService` fixture 覆盖端到端，再通过直接 resolver fixture 覆盖组合语义。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests" --logger "console;verbosity=minimal"
```

预期：新增的 proof/footprint 断言失败，或当前实现无法构造测试所需的显式状态。已有 AND/OR 测试仍应通过。

**Step 3: Write minimal implementation**

只增加测试 fixture、诊断投影和稳定测试 helper，不在本任务移动规则或改变 resolver。若 `AnalysisEvidence` 已能承载诊断，则不修改生产文件。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~CoverageDecisionConflictTests" --logger "console;verbosity=minimal"
```

预期：基线契约通过；新增场景先允许以明确的 `NotYetSupported`/diagnostic 失败，不能以静默删除通过。

**Step 5: Commit**

```powershell
git add tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs
git commit -m "test: freeze coverage and nested decision contracts"
```

### Task 2: 引入 Fact identity、provenance 和 certainty

**Files:**
- Create: `src/NLISSN.Core/Lifting/CoverageEvidence.cs`
- Create: `src/NLISSN.Core/Lifting/FactProvenance.cs`
- Modify: `src/NLISSN.Core/Marking/MarkRecord.cs`
- Modify: `src/NLISSN.Core/Marking/MarkRecordFactory.cs`
- Modify: `src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`
- Modify: `src/NLISSN.Core/Propagation/PropagationFactKey.cs`
- Modify: `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs`
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- Test: `tests/NLISSN.UnitTests/Decision/CoverageEvidenceTests.cs`
- Test: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`
- Test: `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`

**Step 1: Write the failing test**

定义并测试这些事实属性：

```text
FactCapability = AtomicTarget | TopologyHost | ChildComposable | GlobalTarget
FactCertainty = Available | Truncated | Unavailable | Unsupported | Failed
FactProvenance = ruleId + source stage + source facts + propagation path + depth
FactIdentity = source tree version + anchor + fact kind + payload identity + provenance identity
```

断言：

- `FlowLogicalExpression` 只能分类为 `TopologyHost`。
- 原子目标只能分类为 `AtomicTarget`。
- 全局方法目标只能分类为 `GlobalTarget`。
- `StructureComplete` 不存在于 `FactCapability`。
- `PropagatedMarkRecord.Payload`、source mark、depth 和 provenance 在构造 coverage evidence 后仍存在。
- 同一节点上的不同 FactKind、payload 或 provenance 不会被 `PropagationFactKey` 合并成一条事实。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageEvidence" --logger "console;verbosity=minimal"
```

预期：新类型或字段尚不存在，或者现有 key 会把不同事实去重。

**Step 3: Write minimal implementation**

在 Core 中增加不可变事实元数据。`MarkRecord` 可以暂时继续是 public record，但所有跨阶段消费者必须把其输入视为 untrusted value；新增字段只能描述事实能力，不能描述动作或执行授权。`PropagatedMarkRecord` 保持完整 payload，不再在 Lift 调用点只投影 `item.Mark`。

事实去重键至少包含：

```text
SourceTreeVersion, AnchorNodeKey, FactKind,
PayloadIdentity, ProvenanceIdentity
```

对旧测试规则缺失 capability 的记录赋予 `Unknown`，而不是猜测为结构完整。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageEvidence" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~PropagationRuleExpansionTests" --logger "console;verbosity=minimal"
```

预期：新事实合同通过，现有传播和规则结构测试没有事实数量或 provenance 回归。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Lifting src/NLISSN.Core/Marking src/NLISSN.Core/Propagation src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs tests/NLISSN.UnitTests/Decision/CoverageEvidenceTests.cs tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs
git commit -m "feat: preserve typed fact provenance and certainty"
```

### Task 3: 将布尔 coverage 改为 typed coverage contract 和 Proof

**Files:**
- Modify: `src/NLISSN.Core/Lifting/CoverageEvidence.cs`
- Create: `src/NLISSN.Core/Lifting/CoverageProof.cs`
- Modify: `src/NLISSN.Rules/Lift/Support/MarkCoverage.cs`
- Modify: `src/NLISSN.Rules/Lift/Support/ExpressionHostResolver.cs`
- Modify: `src/NLISSN.Rules/Lift/LogicalExpressionLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs`
- Modify: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`
- Create: `tests/NLISSN.UnitTests/Decision/CoverageProofTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs`

**Step 1: Write the failing test**

测试 evaluator 的目标相关接口，而不是继续测试无上下文的 `IsCovered(node) -> bool`：

```text
Evaluate(AtomicTarget, evidence) -> Complete only for an atomic target
Evaluate(LogicalReduction, evidence) -> requires host, removable operands and survivors
Evaluate(ExpressionReplacement, evidence) -> requires typed expression replacement evidence
Evaluate(StructureComplete, evidence) -> rejects a topology host alone
```

覆盖 `Complete`、`Partial`、`Unknown`、`Rejected` 四种状态，并检查 `MissingRequirements`、`AcceptedEvidence`、`RejectedEvidence` 和 `ConsumedNodeKeys`。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageProof" --logger "console;verbosity=minimal"
```

预期：当前 `MarkCoverage` 只能返回布尔值，无法区分 topology host、原子目标和结构证明。

**Step 3: Write minimal implementation**

实现 requirement-aware evaluator：

- 精确比较原始树的稳定 node key。
- 节点直接命中时只接受 requirement 允许的 capability。
- 未直接证明时递归到原始树子节点，但不把 descendant 命中自动提升成结构 proof。
- 保留 evidence、payload、source rule、provenance 和 certainty。
- `Unknown` 不转成 `false` 后再被调用方当作 NoMatch。
- 保留旧 `IsCovered` wrapper 仅供已确认的旧逻辑语义使用；wrapper 永远不能满足 `StructureComplete`。

逻辑表达式使用 `LogicalReduction`；控制结构使用 `StructureComplete`。禁止在 Mark 或 Propagate 中直接创建 `CoverageProof` 的结构完整结论。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageProof" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~DecisionComplexTests" --logger "console;verbosity=minimal"
```

预期：topology-only 结构覆盖被拒绝；已有 AND/OR 结果仍保持原契约。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Lifting src/NLISSN.Rules/Lift tests/NLISSN.UnitTests/Decision/CoverageProofTests.cs tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs
git commit -m "feat: evaluate coverage by typed proof requirements"
```

### Task 4: 为 if、loop、switch 生成 Lift-only structure proof

**Files:**
- Modify: `src/NLISSN.Core/Lifting/StructuralLiftPayloads.cs`
- Modify: `src/NLISSN.Core/Lifting/LiftedMarkRecord.cs`
- Modify: `src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs`
- Modify: `src/NLISSN.Rules/Lift/ControlStructures/ControlStructureLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/Structures/SwitchStructureLifter.cs`
- Modify: `src/NLISSN.Rules/Lift/SwitchStructureLiftingRule.cs`
- Modify: `src/NLISSN.Core/Analysis/StructureAnalyzers/LoopStructureAnalyzer.cs`
- Modify: `src/NLISSN.Core/Analysis/StructureAnalyzers/SwitchStructureAnalyzer.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: `src/NLISSN.Rules/Propose/IfStructureProposalRule.cs`
- Modify: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs`

**Step 1: Write the failing test**

证明形状必须明确区分：

```text
LogicalExpressionReductionProof:
  Host, RemovableOperands, SurvivorOperands,
  ConsumedNodeKeys, AllowedAction=ExpressionReplace,
  DominatesChildren=false

IfStructureCompletionProof:
  AnchorIf, RequiredConditionKeys, ConsumedNodeKeys,
  Tail/Else obligations, AllowedAction, DominatesChildren=true
```

测试以下矩阵：

- `ready && s.IsReady` 生成完整 if proof，并包含条件宿主和消费节点。
- `ready || s.IsReady || fallback` 只生成 logical reduction proof，不生成 structure-complete proof。
- 只有 `FlowLogicalExpression`、自定义 operator、dynamic、error type 或未知 effect 时不生成 structure-complete proof。
- loop 的 condition/body、`break`/`continue` 和 switch 的 section/default/flow 义务缺失时返回 `Unknown` 或 `Rejected`。
- proposal 没有完整 proof 时不能调用结构 Delete factory。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~RuleStructureContractTests" --logger "console;verbosity=minimal"
```

预期：当前 payload 的 `Kind` 和节点 coverage 不能表达 `ConsumedNodeKeys`、保留义务和 proof 状态。

**Step 3: Write minimal implementation**

让 If/Loop/Switch Lift 从完整 `MarkRecord`、`PropagatedMarkRecord` 和 typed coverage result 构造 proof；不能从 `item.Mark` 或 `Reason` 字符串恢复 payload。`LogicalExpressionReductionPayload` 明确为表达式级证明，并把 `DominatesChildren` 固定为 false。

结构 proposal 在生成 `Delete`/结构 `Replace` 前验证 proof：

```text
proof.Goal == StructureComplete
proof.Status == Complete
proof.ConsumedNodeKeys contains required original nodes
proof.PreservedObligations are satisfied
```

不满足时返回 diagnostic/skip，不生成结构性 `DecisionUnit`。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~GraphAnalyzerTests.Analyze_LogicalAndCondition_RightTargetDeletesIf" --logger "console;verbosity=minimal"
```

预期：AND 仍删除 if，OR 仍保留逻辑 Replace；拓扑 host 不再直接升级为结构 proof。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Lifting src/NLISSN.Core/Analysis/StructureAnalyzers src/NLISSN.Rules/Lift src/NLISSN.Rules/Propose/Support/ProposalFacts.cs src/NLISSN.Rules/Propose/IfStructureProposalRule.cs tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs tests/NLISSN.HostTests/Decision tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs
git commit -m "feat: require lift proofs for structural edits"
```

### Task 5: 为每个候选增加 EditIntent、footprint 和 behavior budget

**Files:**
- Create: `src/NLISSN.Core/Decision/DecisionFootprint.cs`
- Create: `src/NLISSN.Core/Decision/EditIntent.cs`
- Create: `src/NLISSN.Core/Decision/BehaviorBudget.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `src/NLISSN.Core/Decision/DeleteDecisionFactory.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ReplaceDecisionFactory.cs`
- Modify: `src/NLISSN.Rules/Propose/IfStructureProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/LogicalExpressionProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Test: `tests/NLISSN.UnitTests/Decision/DecisionFootprintTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs`

**Step 1: Write the failing test**

对所有内建候选断言：

- 有原始树 anchor 和非空 `ConsumedNodeKeys`。
- logical Replace 只消费原始 logical host、removable operands 和必要的 replacement obligations。
- structural if Delete 只有在 structure proof 完成时才声明 `DominatesChildren`。
- replacement node 不会进入原始 `ConsumedNodeKeys`。
- 参数声明、调用点、method group、lambda 和 delegate binding 候选具有一致的 `ReadSet/WriteSet/PreserveSet`。
- 旧 proposal factory 缺 footprint 时进入 compatibility/Unknown，不会产生可执行候选。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionFootprint" --logger "console;verbosity=minimal"
```

预期：现有 `DecisionUnit` 主要依赖 fragment/span，无法表达 erase/preserve/composition。

**Step 3: Write minimal implementation**

新增不可变候选模型：

```text
DecisionFootprint:
  AnchorNodeKey
  ConsumedNodeKeys
  Action
  Composition = Independent | Composable | OpaqueDominates
                | Exclusive | AtomicTransaction | Unknown
  ProofKind

EditIntent:
  ReadSet, EraseSet, WriteSet, PreserveSet,
  ProofSet, AllowedBehaviorChanges, ResidualMapping,
  AtomicGroup, SourceVersion
```

`Anchor` 只表示动作起点；`EraseSet` 表示有证明会被移除的原始节点；`ReplacementNode` 只属于输出树。Delete/Replace factory 必须要求最小 footprint，结构动作必须带 `StructureComplete` proof reference。

效果义务至少可表示 getter/call、await、赋值、异常、short-circuit、binding、type/overload 和 scope；不要把这些信息放在单一 display string 中。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionFootprint" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~DecisionEvidenceTests|FullyQualifiedName~BindingValidatorTests" --logger "console;verbosity=minimal"
```

预期：所有内建候选可解释地携带 footprint；旧候选只进入明确兼容诊断状态。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Decision src/NLISSN.Rules/Propose tests/NLISSN.UnitTests/Decision/DecisionFootprintTests.cs tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs
git commit -m "feat: attach edit intents and behavior budgets"
```

### Task 6: 建立唯一 DecisionPlanner 和显式关系图

**Files:**
- Create: `src/NLISSN.Core/Decision/DecisionConflict.cs`
- Create: `src/NLISSN.Core/Decision/DecisionPlan.cs`
- Create: `src/NLISSN.Core/Decision/DecisionPlanner.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `src/NLISSN.Core/Decision/AnalysisEvidence.cs`
- Modify: `src/NLISSN.Core/Validation/DecisionBindingValidator.cs`
- Create: `tests/NLISSN.UnitTests/Decision/DecisionPlannerTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionEvidenceTests.cs`

**Step 1: Write the failing test**

为关系图固定以下结果：

| 关系 | 结果 |
| --- | --- |
| 独立 footprint | 两者都 `Selected`/`Composed` |
| 父 Delete + 子 Replace，有完整 dominance proof | 父 `Selected`，子 `Dominated` |
| 父 Delete + 子 Replace，无 proof | `Conflict`/父 `Rejected`，子不能静默消失 |
| 父 Replace + 子节点 retained/replaced | 只有有 residual mapping 时组合 |
| 同 anchor 不同 Replace | `Conflict` |
| 部分重叠且无 list composition proof | `Conflict` 或整域 `Skip` |
| AtomicGroup 一个成员 Unknown | 整组 `Blocked`/`Skip` |

每条关系都必须附 candidate IDs、proof references 和 diagnostic reason。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionPlanner" --logger "console;verbosity=minimal"
```

预期：当前 `FilterCompetingAncestors`、`MergeBySyntaxCoverage` 和 `FilterCoveredDecisions` 会按 span/动作直接过滤，无法返回完整关系结果。

**Step 3: Write minimal implementation**

`DecisionPlanner` 在原始树上构造关系边：

```text
requires(parent, proof)
dominates(parent, child)
composes(parent, child, residual-map)
conflicts(candidate-a, candidate-b, reason)
member-of(candidate, atomic-group)
```

父 Delete 只能通过以下检查：

```text
parent.Proof.Status == Complete
parent.Action == Delete
parent.Composition == OpaqueDominates
child.AnchorNodeKey in parent.EraseSet
child.PreserveSet compatible with parent.PreserveSet
parent.Proof contains StructureComplete
```

Planner 输出所有候选的状态：`Selected`、`Composed`、`Dominated`、`Rejected`、`Conflict`、`Unknown` 或 `Blocked`。不允许使用 `Where` 作为无诊断的 resolver 行为。

迁移期间保留 `RuleDecisionEngine` 作为兼容 facade，但其语义实现必须委托 `DecisionPlanner`；不能同时保留两套 parent/child winner 逻辑。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionPlanner" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~DecisionEvidenceTests|FullyQualifiedName~DecisionComplexTests" --logger "console;verbosity=minimal"
```

预期：父子决策的结果可观察且与 AND/OR 既有行为一致。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Decision src/NLISSN.Core/Validation tests/NLISSN.UnitTests/Decision/DecisionPlannerTests.cs tests/NLISSN.HostTests/Decision
git commit -m "feat: resolve candidates through one decision planner"
```

### Task 7: 收紧 authority funnel，封闭 legacy API 旁路

**Files:**
- Create: `src/NLISSN.Core/Decision/CandidateAuthorization.cs`
- Create: `src/NLISSN.Core/Decision/ExecutablePlan.cs`
- Create: `src/NLISSN.Core/Validation/CandidateValidator.cs`
- Create: `src/NLISSN.Core/Validation/PlanValidator.cs`
- Modify: `src/NLISSN.Core/Decision/RuleDefinitionPropose.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `src/NLISSN.Core/Rewrite/PrototypeRewriter.cs`
- Modify: `src/NLISSN.Core/Validation/DecisionBindingValidator.cs`
- Modify: `tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs`
- Create: `tests/NLISSN.HostTests/Decision/AuthorityFunnelTests.cs`
- Modify: `tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs`

**Step 1: Write the failing test**

增加 API 级测试，验证权限而不是只验证源码 diff：

- untyped `MarkRecord` 不能授权 structural Delete。
- `TopologyHost` 不能构造 `StructureComplete` proof。
- forged `LiftedMarkRecord` 没有 producer/proof contract 时被拒绝。
- 没有 typed `ProofReference` 的 `RuleDecision` 不能进入 `ExecutablePlan`。
- `ValidateBindings=false` 只能关闭详细诊断，不能关闭安全门。
- `PrototypeRewriter` 不能从任意 `IEnumerable<RuleDecision>` 绕过 Planner 执行破坏性候选。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~AuthorityFunnelTests|FullyQualifiedName~BindingValidatorTests" --logger "console;verbosity=minimal"
```

预期：当前 `RuleDecision` 有 public constructor，`PrototypeRewriter.Rewrite` 接受任意决策，规则 proposal 可以直接构造 Delete。

**Step 3: Write minimal implementation**

将阶段类型区分为：

```text
UntrustedRuleOutput -> ValidatedFact -> GoalSpecificProof
-> IntentDraft -> AuthorizedIntent -> ExecutablePlan -> Rewrite
```

正式 Rewrite 入口改为接收 `ExecutablePlan`。若保留旧 overload，只能通过兼容适配器：

- 缺 proof/footprint/source version 的 Delete/Replace 进入拒绝或 Unknown。
- 兼容路径只能用于诊断，不能直接执行未经 Planner 的结构动作。
- 构造器可以在迁移期保持 public，但每层 factory/validator 必须重新验证，不能把 public record 当成不可伪造 token。
- `DecisionBindingValidator` 的配置开关不能绕过 candidate/plan safety gate。

所有旧调用点必须迁移到 `DecisionPlanner -> PlanValidator -> ExecutablePlan`。兼容测试只保证已有安全行为，不保证旧 API 继续接受不带证明的破坏性输入。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~AuthorityFunnelTests|FullyQualifiedName~BindingValidatorTests|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DiffModelTests" --logger "console;verbosity=minimal"
```

预期：正式执行只能接受可验证计划；已有合法 rewrite 结果不变。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Decision src/NLISSN.Core/Rewrite/PrototypeRewriter.cs src/NLISSN.Core/Validation tests/NLISSN.HostTests/Validation tests/NLISSN.HostTests/Decision/AuthorityFunnelTests.cs tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs
git commit -m "fix: close unvalidated rewrite authority paths"
```

### Task 8: 实现 residual mapping 和 tree-level composition

**Files:**
- Create: `src/NLISSN.Core/Decision/ResidualMapping.cs`
- Modify: `src/NLISSN.Core/Decision/EditIntent.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionPlanner.cs`
- Modify: `src/NLISSN.Core/Rewrite/PrototypeRewriter.cs`
- Modify: `src/NLISSN.Core/Rewrite/RewritePlanModel.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Create: `tests/NLISSN.HostTests/Rewrite/ResidualMappingTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Modify: `tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs`

**Step 1: Write the failing test**

覆盖父 Replace 与子候选的四种映射：

```text
Retained(newPath)
Replaced(newPath, obligations)
Removed
Unknown(reason)
```

场景：

- 父 Replace 保留 child，child 在新树中仍可被 tracked/annotation 定位，两个 intent 可以组合。
- 父 Replace 移除 child，child 不需要保留，父可以显式支配 child。
- 父 Replace 移除 child，但 child PreserveSet 非空，产生冲突。
- 无法建立旧树到新树映射时为 Unknown，破坏性动作 fail closed。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ResidualMappingTests|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"
```

预期：当前 `PrototypeRewriter` 通过旧 span 和文本包含关系丢弃子编辑，无法表达 residual mapping。

**Step 3: Write minimal implementation**

使用 Roslyn tracked node/annotation 或等价的 tree-level composition：先在原始树上绑定 parent/child，再应用 parent replacement，最后在 replacement tree 中解析 residual child。禁止把旧树 child span 直接应用到新文本。

组合条件：

```text
Composable + Retained/Replaced + obligations compatible -> remap and compose
OpaqueDominates + child in EraseSet -> parent dominates
Removed + child PreserveSet not empty -> Conflict
Unknown -> Unknown; no destructive execution
```

`PrototypeRewriter` 只拒绝非法计划和非法重叠，不根据 span 猜测业务 winner。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ResidualMappingTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~DecisionComplexTests" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DiffModelTests" --logger "console;verbosity=minimal"
```

预期：父 Replace 的保留子节点和冲突场景均可解释；已有父控制结构 rewrite 保持通过。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Decision src/NLISSN.Core/Rewrite src/NLISSN.Application/Analysis/ApplicationService.cs tests/NLISSN.HostTests/Rewrite/ResidualMappingTests.cs tests/NLISSN.HostTests/Application/PipelineComponentTests.cs tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs
git commit -m "feat: compose nested edits through residual mappings"
```

### Task 9: 建立 atomic group、Unknown 状态和 source/binding version

**Files:**
- Create: `src/NLISSN.Core/Decision/AtomicEditGroup.cs`
- Create: `src/NLISSN.Core/Decision/AnalysisCompleteness.cs`
- Create: `src/NLISSN.Core/Decision/SourceVersion.cs`
- Modify: `src/NLISSN.Core/Decision/EditIntent.cs`
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/MethodParameterUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/DelegateAndExtensionUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/LocalFunctionAndIndexerUsage.cs`
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Create: `tests/NLISSN.HostTests/Decision/AtomicEditGroupTests.cs`

**Step 1: Write the failing test**

把声明同步测试成一个事务：

- method declaration、普通调用、named/optional/params 调用、method group、lambda、delegate conversion 和跨文件调用属于同一 atomic group。
- dynamic/unresolved invocation、跨 compilation 查询不可用、截断、预算耗尽、unsupported binding 和任一成员冲突都令组为 `Unknown` 或 `Rejected`。
- `NoMatch` 只表示已证明没有成员，不表示查询失败。
- 组内任一成员失败时默认 `RejectWholeGroup`，不能只修改声明。
- 同一 source/binding version 的独立 callsite edit 可以 `ListComposable`；版本不一致时整组 Unknown。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~AtomicEditGroupTests|FullyQualifiedName~FlowSummaryDeletionSafetyTests" --logger "console;verbosity=minimal"
```

预期：当前参数收缩和 flow summary 逻辑可能以已发现的调用点集合直接生成局部决策，无法区分空集合和查询不可用。

**Step 3: Write minimal implementation**

增加组级 completeness proof、failure policy 和 source/binding version。把参数 shrink 的 declaration/callsite/member-group/lambda 结果先放入 group，再由 Planner 投影为 intents。传播和查询层必须保留：

```text
NoMatch, Unavailable, Unresolved, Truncated,
Unsupported, Failed
```

这些状态不能被 `.Where`、空列表或默认值吞掉。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~AtomicEditGroupTests|FullyQualifiedName~FlowSummaryDeletionSafetyTests|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"
```

预期：不完整声明同步整组不执行；已证明的多调用点组仍能稳定生成同一结果。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Decision src/NLISSN.Rules/Propose/ParameterShrink src/NLISSN.Rules/Propose/Parameters src/NLISSN.Application/Analysis tests/NLISSN.HostTests/Decision/AtomicEditGroupTests.cs tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs tests/NLISSN.HostTests/Application/PipelineComponentTests.cs
git commit -m "feat: make cross-site edits atomic and uncertainty explicit"
```

### Task 10: 增加 post-rewrite rebind、编译和保留义务校验

**Files:**
- Create: `src/NLISSN.Core/Validation/PostRewriteValidator.cs`
- Create: `src/NLISSN.Core/Validation/PostRewriteResult.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Application/Analysis/PostRewriteCleanupService.cs`
- Modify: `src/NLISSN.Core/Rewrite/PrototypeRewriteResult.cs`
- Modify: `src/NLISSN.Core/Rewrite/RewritePlanModel.cs`
- Modify: `tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs`
- Create: `tests/NLISSN.HostTests/Validation/PostRewriteValidatorTests.cs`
- Modify: `tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs`

**Step 1: Write the failing test**

验证：

- 计划执行后重新 parse/rebind，结构 proof 声明的 binding/type/scope/effect/preserve 义务仍满足。
- 某个 atomic group 的 postcondition 失败时，组内没有部分 Rewrite 结果。
- 多文件计划按 group 回滚或不提交失败组，成功组不被无关失败污染。
- `ValidateBindings=false` 不会关闭 post-rewrite safety gate。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PostRewriteValidatorTests|FullyQualifiedName~BindingValidatorTests" --logger "console;verbosity=minimal"
```

预期：当前 rewrite 返回文本/diff，但没有统一的 postcondition validator 或 group rollback 状态。

**Step 3: Write minimal implementation**

执行顺序必须是：

```text
validate ExecutablePlan
  -> execute affected edits in an isolated result
  -> parse and rebuild semantic model/compilation
  -> validate proof preserve obligations and bindings
  -> publish group result only when all postconditions pass
```

验证失败时返回结构化 `PostRewriteFailure`，不把失败转换为成功的空 diff，也不从失败结果中补发新决策。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PostRewriteValidatorTests|FullyQualifiedName~BindingValidatorTests|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DiffModelTests" --logger "console;verbosity=minimal"
```

预期：成功计划仍输出原有 rewrite artifact；失败组可观察且没有部分提交。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Validation src/NLISSN.Core/Rewrite src/NLISSN.Application/Analysis tests/NLISSN.HostTests/Validation tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs
git commit -m "feat: validate bindings and post-rewrite conditions"
```

### Task 11: 删除重复 semantic span filter，保留 Rewrite 防御检查

**Files:**
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Core/Rewrite/PrototypeRewriter.cs`
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs` only if它仍实现候选语义去重
- Modify: `tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionEvidenceTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs`

**Step 1: Write the failing test**

先把旧过滤器的每个责任固定为 Planner 结果：

- `FilterCompetingAncestors` 不再依据 ancestor/span 或任意 Replace 存在性直接选择父候选。
- `FilterCoveredDecisions` 不再因为 `FinalNode.Span.Contains` 静默丢 child。
- `ApplicationService.FilterNestedDeleteDecisions` 不再拥有第二套 parent Delete winner。
- `PrototypeRewriter` 只拒绝计划中仍存在的非法 overlap，不选择业务 winner。
- 被支配候选在 evidence 中状态为 `Dominated`，无 proof 候选为 `Rejected`/`Conflict`，而不是消失。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~DecisionEvidenceTests|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"
```

预期：在删除旧逻辑前，新增的 evidence/diagnostic 断言会指出候选仍由多个位置过滤。

**Step 3: Write minimal implementation**

将所有 semantic winner 逻辑移到 `DecisionPlanner`。Application 只负责编排、配置的 unsafe local declaration 防护和调用 validator；Rewrite 只做计划有效性、源版本和非法重叠的 defensive check。

删除或旁路旧过滤器前，先让其输出与 Planner 结果逐项比对。比对稳定 candidate ID，不比较生成顺序或 display text。确认无双重裁决后再删除 dead helper。

**Step 4: Run test to verify it passes**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

预期：Unit、Host、Contract 全部通过；AND/Delete、OR/Replace、父控制结构删除以及 rewrite overlap 回归保持通过。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Decision/DecisionModel.cs src/NLISSN.Application/Analysis/ApplicationService.cs src/NLISSN.Core/Rewrite/PrototypeRewriter.cs src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs tests/NLISSN.HostTests/Decision tests/NLISSN.HostTests/Application/PipelineComponentTests.cs
git commit -m "fix: remove duplicate semantic span filtering"
```

## 5. 全量验收与性能边界

### 5.1 Definition of Done

以下条件全部满足才算完成：

- `TopologyHost` 不能单独满足 `StructureComplete`。
- Fact identity 包含 source version、fact kind、payload 和 provenance；传播不会静默丢 payload。
- `StructureComplete` 只能由 Lift proof 产生，并带原始 `ConsumedNodeKeys`、保留义务和 certainty。
- 每个 Delete/Replace 都有原始树 footprint、proof reference、source version 和行为预算。
- 父 Delete 只有 explicit dominance、完整 `EraseSet` 和 preserve compatibility 才能支配 child Replace。
- 父 Replace 保留 child 时有 residual mapping；无法映射时进入 conflict/Unknown，不直接套旧 span。
- Partial overlap、Unknown、unresolved callsite 和 atomic group failure 都可观察且 fail closed。
- `DecisionPlanner` 是唯一的候选组合和 parent/child resolver。
- `RuleDecision` 不能绕过 Planner 进入 Rewrite；正式 Rewrite 只接受 `ExecutablePlan`。
- `ValidateBindings=false` 不能关闭 candidate、plan 和 post-rewrite safety gate。
- ApplicationService 不再实现第二套语义 winner；PrototypeRewriter 不再依据 span 选择业务 winner。
- 现有 AND/Delete、OR/Replace、parent-control-delete 和合法 rewrite overlap 测试继续通过。

### 5.2 完整验证命令

先串行执行 owning projects：

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
```

再运行仓库 tier 和 harness：

```powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Host
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Performance
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

预期：每个命令的实际通过数量记录在交付说明中；未运行的 tier 不得写成通过。性能验证重点不是单纯总耗时，而是确认 provenance/footprint 保存没有导致事实数量、内存或传播 fixed-point 失控。必要时补跑现有 Performance 项目：

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false
```

### 5.3 文档和链接检查

回读设计和执行文档的标题、相对链接、命令和文件路径：

```powershell
Get-Content .\docs\plans\2026-09-10-general-coverage-decision-model.md -Raw
Get-Content .\docs\plans\2026-09-10-general-coverage-decision-execution.md -Raw
git diff -- docs/plans/2026-09-10-general-coverage-decision-model.md docs/plans/2026-09-10-general-coverage-decision-execution.md
```

至少确认以下相对链接存在：

```powershell
Test-Path .\docs\plans\2026-09-10-general-coverage-decision-model.md
Test-Path .\docs\research\2026-09-10-coverage-decision-composition-research.md
Test-Path .\docs\research\2026-09-10-performance-fact-propagation-llvm-comparison.md
```

最后检查 `git status --short`，确保没有把用户无关修改或生成的 `obj`/测试输出加入提交。

## 6. 推荐提交边界

每个提交只完成一个可回滚阶段：

1. `test: freeze coverage and nested decision contracts`
2. `feat: preserve typed fact provenance and certainty`
3. `feat: evaluate coverage by typed proof requirements`
4. `feat: require lift proofs for structural edits`
5. `feat: attach edit intents and behavior budgets`
6. `feat: resolve candidates through one decision planner`
7. `fix: close unvalidated rewrite authority paths`
8. `feat: compose nested edits through residual mappings`
9. `feat: make cross-site edits atomic and uncertainty explicit`
10. `feat: validate bindings and post-rewrite conditions`
11. `fix: remove duplicate semantic span filtering`

文档提交单独完成，且只包含：

```powershell
git add docs/plans/2026-09-10-general-coverage-decision-model.md docs/plans/2026-09-10-general-coverage-decision-execution.md
git commit -m "docs: specify coverage and decision authority funnel"
```

不要把生产代码、测试、生成目录、测试结果或用户已有文件混入文档提交。

## 7. 失败处理和回滚策略

- 若 Task 2 的事实 key 造成传播数量变化，先回滚该 task 的提交或禁用新 key 的消费路径；不能通过删除事实恢复旧计数。
- 若 Task 3/4 使合法 AND/Delete 消失，检查 proof 输入和结构 contract，不能把 topology host 重新当作 structure proof。
- 若 Task 6 发现旧过滤器和新 Planner 输出不同，保留旧过滤器作为断言/遥测，暂停删除重复逻辑，先解释每个 candidate ID 的差异。
- 若 Task 7 的 public API 迁移导致外部测试无法编译，提供只读兼容适配器；不能重新开放未经验证的 Rewrite 入口。
- 若 Task 8 无法建立 residual mapping，结果必须为 conflict/Unknown；不要降级为按文本 span 应用 child edit。
- 若 Task 9 查询被截断或 binding unresolved，整组拒绝；不要把空集合当作 NoMatch。
- 若 Task 10 post-rewrite 校验失败，拒绝受影响 group 并保留诊断；不能发布部分 diff。
- 任何阶段出现未覆盖的自定义 rule/plugin 输入，都按 untrusted value 处理并进入 CandidateValidator，而不是特判为可信。

## 8. 最终审查问题

完成前逐项回答：

1. 哪些内容仍然是 Mark fact，哪些内容明确只由 Lift/Planner 产生？
2. 是否存在任何 public API 可以从 raw mark、propagated mark 或普通 `RuleDecision` 直接进入 Rewrite？
3. 父 Delete 支配 child 的证据是否同时包含 `StructureComplete`、`EraseSet`、composition 和 preserve compatibility？
4. parent Replace 保留 child 时是否有 residual mapping，且映射发生在新树而不是旧文本 span？
5. 每个候选被选中、组合、支配、拒绝、冲突或未知的原因是否可观察？
6. `NoMatch` 与 `Unavailable`/`Unresolved`/`Truncated`/`Unsupported`/`Failed` 是否区分？
7. declaration/callsite/method-group/lambda/delegate 同步是否以 atomic group 处理？
8. ApplicationService、RuleDecisionEngine 和 PrototypeRewriter 是否还存在重复的 semantic winner 选择？
9. `ValidateBindings=false` 是否只影响诊断详细程度，而没有关闭安全门？
10. 所有原有 AND/Delete、OR/Replace、parent-control-delete 测试以及全量 tier 是否用实际输出证明通过？

