# Remove Core Feature Dimension Implementation Plan

**Goal:** 删除规则目录中的 `RuleFeature.Core` 维度和所有 feature 过滤/兼容路径，同时保持当前 68 个已登记规则的四阶段目录、顺序、`RuleId`、规则图声明和默认行为不变。

**Architecture:** `RuleRegistration` 只作为无参数的“进入当前目录”标记，生成 descriptor 只保存 `RuleId`、类型名和 factory，不再保存 feature。Generator 继续按具体基类识别 Mark、Propagate、Lift、Propose，并把四阶段完整性从“每个 feature”改为“当前目录整体”；Composer 直接消费四个扁平 generated 列表，`RuleSelection` 只表达 disabled `RuleId`。四个延期规则族不进入当前目录，历史源码若保留只能使用 `RuleCatalogIgnore`，不得有注册、factory、规则图节点或旧 bool 兼容入口。

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator (`netstandard2.0`), xUnit, `Microsoft.CodeAnalysis.CSharp`, PowerShell harness。

---

## 1. 约束、范围和不变量

### 范围

- 删除 `NLISSN.Core.Pipeline.RuleFeature` 类型及所有 `Feature` 元数据字段。
- 将所有当前目录规则的 `[RuleRegistration(RuleFeature.Core)]` 改为 `[RuleRegistration]`。
- 保持当前非忽略规则共 68 个 factory，阶段计数固定为 `Mark/Propagate/Lift/Propose = 19/12/5/32`。
- 保持四阶段内现有类型排序、`RuleId`、active/disabled 分离、factory 每次创建新实例、规则图节点和边。
- 保持 `RuleSelectionAdapter` 对旧配置中 RuleId/简单类型名/完全限定类型名的 disabled 映射；它不再承担 feature 兼容职责。
- 从测试辅助入口和历史行为测试中删除 `UnreachableMethodDeletion`、`UnreferencedMethodDeletion`、`UnusedInterfaceImplementationCleanup`、`InternalOnlyPublicMethodPrivatization` 的四个 bool 开关及显式 opt-in 路径。
- 历史延期规则源码如继续保留，必须保持 `RuleCatalogIgnore`，并由测试证明它们不在 generated catalog、factory、Composer 管道或规则图中。

### 非范围

- 不删除或重写当前 68 个规则的 Mark、Propagate、Lift、Propose 执行逻辑。
- 不重新实现四个延期 feature 的真实 Propagate/Lift，也不以 no-op 规则补齐它们。
- 不改变 `RuleId`、规则身份快照、规则图语义、CPG capability、decision/evidence/rewrite 行为。
- 不保留 `RuleFeature` 的空壳、别名、旧名称 facade 或反射兼容类型。
- 不运行完整 Unit/Contract/Host 套件、Fast/Host tier、性能测试或 DOP 回归；只运行本计划末尾列出的少量核心过滤测试。
- 本计划执行时不使用子代理。当前工作树已有用户改动，执行者必须保留并在提交前检查交叠文件。

### 现有基线

- 当前 generated catalog 基线为 68 个 factory，四阶段计数为 `19/12/5/32`。
- `RuleFeature` 已删除；删除的是 feature 轴，不是删除当前 68 个规则。
- 四个延期 feature 当前不应出现在 generated descriptor/factory/Composer 兼容路径；不能因删除 feature 轴而误把它们纳入目录。
- `RuleCatalogIgnore` 的历史源码不属于当前框架产物；忽略规则不得触发缺少注册 warning。

## 2. Task 1: 收敛契约和生成器的无 feature 模型

**Files:**
- Modify: `src/NLISSN.Rule/RuleRegistration.cs`
- Modify: `src/NLISSN.Rule/RuleRegistrationDescriptor.cs`
- Modify: `src/NLISSN.Rule.Generator/RuleCatalogDiagnostics.cs`
- Modify: `src/NLISSN.Rule.Generator/RuleCatalogGenerator.cs`
- Test: `tests/NLISSN.ContractTests/RuleCatalog/RuleRegistrationContractTests.cs`
- Test: `tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogGeneratorTests.cs`
- Test: `tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs`
- Test: `tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogMsBuildIntegrationTests.cs`

### Step 1: 先写目标契约测试

把契约测试改成目标形状：

- 通过反射断言 `NLISSN.Core.Pipeline.RuleFeature` 不存在。
- 断言 `RuleRegistrationAttribute` 只有无参数 public constructor，仍然是 class-only、不可重复、不可继承。
- 断言 `RuleRegistration<TStage>` 只有 `Factory`、`FullyQualifiedName`、`RuleId`、`TypeName` 四个公开目录属性。
- 删除所有 `Feature`、`InvalidFeature`、`IncompleteFeature` 的 feature-specific 测试输入。
- GeneratorDriver 的最小合法 fixture 使用 `[RuleRegistration]`，四个阶段各有一个真实规则；生成文本不得出现 `RuleFeature` 或 `.Feature`。
- 新增“当前目录缺少阶段”测试：fixture 只登记 Mark，期望 `NLRCG011`，并断言不产生部分 `GeneratedRuleCatalog`。
- 保留重复 `RuleId`、不可访问构造函数、固定类型冲突、`RuleCatalogIgnore`、未注册 warning 和稳定排序测试。

Run: 

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleCatalog"
```

Expected: FAIL，旧契约仍要求 `RuleFeature`/`Feature`，或生成器仍把缺阶段解释为缺少 feature；这是实现前的红灯。

### Step 2: 实现最小运行时契约

- 在 `RuleRegistration.cs` 删除 `RuleFeature` enum。
- 将 `RuleRegistrationAttribute` 改为无参数 marker attribute，并删除 `Feature` 属性。
- 将 `RuleRegistration<TStage>` 的 constructor 和 record 参数改为 `RuleId`、`TypeName`、`FullyQualifiedName`、`Factory`。
- 不改变 `RuleCatalogIgnoreAttribute` 的目标和行为。

### Step 3: 删除 generator 的 feature 发现和分组

- 删除 `FeatureMetadataName`、`KnownFeatures`、`TryGetFeature`、`GetFeatureName` 以及 `ContractSymbols.Feature`。
- `ResolveContracts` 不再查找或要求 `RuleFeature`。
- 注册属性只验证“存在且不重复”，不读取 feature 参数。
- `RuleEntry` 删除 `Feature` 字段；生成四阶段 descriptor 时只发射四个 runtime 字段。
- 将 `NLRCG011` 从 feature 完整性诊断改为全局目录阶段完整性诊断：只要已生成条目缺少 Mark、Propagate、Lift 或 Propose 任一阶段，就报错并完全不发射 catalog。
- 保留其它 generator error 的原子失败语义；warning-only 情况继续生成剩余合法目录。
- 确保 generator 源码、生成文本和 generator 自身测试 fixture 中不再引用 `RuleFeature`。

### Step 4: 运行生成器核心测试

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleCatalog"
```

Expected: 所选 Contract tests PASS；合法 fixture 生成四个阶段列表，缺任一全局阶段时只有 `NLRCG011` 且没有部分 catalog。

### Step 5: Commit

```powershell
git add src/NLISSN.Rule src/NLISSN.Rule.Generator tests/NLISSN.ContractTests/RuleCatalog
git commit -m "refactor: remove rule catalog feature metadata"
```

## 3. Task 2: 迁移当前规则标注并冻结扁平目录基线

**Files:**
- Modify: every file returned by `rg -l "RuleRegistration\\(" src/NLISSN.Rules -g '*.cs'`
- Preserve: every file returned by `rg -l "RuleCatalogIgnore" src/NLISSN.Rules -g '*.cs'`
- Test: `tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs`
- Test: `tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogMsBuildIntegrationTests.cs`
- Test: `tests/NLISSN.ContractTests/Identity/RuleIdentityContractTests.cs`
- Test: `tests/NLISSN.ContractTests/Identity/RuleIdentitySnapshot.json`

### Step 1: 写当前目录的扁平基线断言

- 删除 baseline 中对 `descriptor.Feature == RuleFeature.Core` 的断言。
- 保留 68 个 descriptor、`19/12/5/32` 阶段计数、全局唯一 `RuleId`、类型集合完全覆盖和 identity snapshot 顺序断言。
- MSBuild integration test 改为验证 descriptor 只有四个目录字段，并确认生成文件仍只落在 `Build` 目录；测试不再枚举 feature。
- Identity contract test 保留默认 Composer 结果与冻结 snapshot 的比较，不增加新的行为基线。

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleCatalogBaselineTests|FullyQualifiedName~RuleCatalogMsBuildIntegrationTests|FullyQualifiedName~RuleIdentityContractTests"
```

Expected: FAIL/compile fail，真实规则仍使用带 `RuleFeature.Core` 参数的注册属性，或测试仍读取已删除的 descriptor 属性。

### Step 2: 迁移注册属性

- 将当前 68 个目录规则的 `[RuleRegistration(RuleFeature.Core)]` 统一改为 `[RuleRegistration]`。
- 不改规则类名、继承关系、`RuleId` 或规则实现。
- 四个延期 feature 的历史规则不添加 `[RuleRegistration]`；继续使用 `RuleCatalogIgnore`，使其不会被 generator 发现。
- 用 `rg` 复核 `src/NLISSN.Rules` 不再出现 `RuleFeature`、`.Feature` 或四个延期 feature 的 registration metadata。

### Step 3: 运行真实目录基线

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleCatalogBaselineTests|FullyQualifiedName~RuleCatalogMsBuildIntegrationTests|FullyQualifiedName~RuleIdentityContractTests"
```

Expected: 68 个 factory 和 `19/12/5/32` 全部保持；生成代码不含 feature 字段；四个延期 feature 不产生 descriptor/factory。

### Step 4: Commit

```powershell
git add src/NLISSN.Rules tests/NLISSN.ContractTests/RuleCatalog tests/NLISSN.ContractTests/Identity
git commit -m "refactor: flatten registered rule catalog"
```

## 4. Task 3: 删除 Composer、Selection 和配置适配中的 Core 维度

**Files:**
- Modify: `src/NLISSN/Composition/RuleSelection.cs`
- Modify: `src/NLISSN/Composition/RulePipelineComposer.cs`
- Modify: `src/NLISSN/Composition/RuleSelectionAdapter.cs`
- Modify as required for compile errors: `src/NLISSN/Hosting/CommandHost.cs`, `src/NLISSN.Application/` callers
- Test: `tests/NLISSN.HostTests/Application/RulePipelineComposerTests.cs`
- Test: `tests/NLISSN.HostTests/Application/RuleSelectionAdapterTests.cs`
- Test: `tests/NLISSN.HostTests/Application/GeneratedRuleCatalogEquivalenceTests.cs`

### Step 1: 写 flat selection/composer 测试

- `RuleSelection` 只用 `disabledRuleIds` 构造；删除所有 `Enum.GetValues<RuleFeature>()`、`RuleFeature.Core` 和 `RequestedFeatures` 断言。
- 默认 `Compose(new RuleSelection())` 必须直接得到 generated 四阶段的所有当前规则。
- disabled RuleId 仍必须从 active 列表移到对应 disabled 列表，并在 `CompileRuleGraph()` 中保留声明节点。
- 未知 disabled RuleId 的大小写不敏感去重和稳定 warning 保持不变。
- 两次 Compose 对同一 descriptor 仍必须创建不同实例。
- Adapter 测试只断言 canonical disabled RuleId 和未知值保留，不再断言自动加入 Core。

Run:

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RulePipelineComposerTests|FullyQualifiedName~RuleSelectionAdapterTests|FullyQualifiedName~GeneratedRuleCatalogEquivalenceTests"
```

Expected: FAIL/compile fail，旧 Composer 仍访问 `RequestedFeatures`、自动加入 Core 或按 descriptor feature 过滤。

### Step 2: 收窄 RuleSelection

- 删除 `using NLISSN.Core.Pipeline` 和 `RequestedFeatures`。
- 将 constructor 收敛为 `IEnumerable<string>? disabledRuleIds = null`，保留空白值过滤和原有不可变数组行为。
- 不改变 `RuleCompositionResult`、`RuleSelectionWarning` 或 `RulePipeline` contract。

### Step 3: 扁平化 RulePipelineComposer

- 删除 `enabledFeatures` 创建和 `enabledFeatures.Add(RuleFeature.Core)`。
- `ComposeStage` 直接按 `TypeName` ordinal、再按 `FullyQualifiedName` ordinal 遍历 descriptor，不做 feature filter。
- `GetAllDescriptors` 只需返回 RuleId 集合或无 feature 的内部 descriptor。
- active/disabled factory 调用顺序、RuleCatalog 校验、warning 排序和异常传播保持不变。
- 生成目录四列表仍分别映射到 `RulePipeline` 的四个阶段；不重新引入 `RuleRegistry`。

### Step 4: 保留 disabled-name 适配但删除 feature 适配

- `RuleSelectionAdapter.FromLegacySettings` 继续把旧配置名称映射为 canonical disabled RuleId。
- 返回 `new RuleSelection(disabledRuleIds)`，不得返回任何 feature 集合。
- `CommandHost` 及配置入口只通过该 adapter 传递 disabled RuleId；不得新增四个 feature bool 或旧 Composer facade。

### Step 5: 运行 Composer 核心测试

Run:

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RulePipelineComposerTests|FullyQualifiedName~RuleSelectionAdapterTests|FullyQualifiedName~GeneratedRuleCatalogEquivalenceTests"
```

Expected: 所选 Host tests PASS；默认阶段集合、RuleId 顺序、disabled graph 节点和 factory 生命周期与迁移前一致。

### Step 6: Commit

```powershell
git add src/NLISSN/Composition src/NLISSN/Hosting src/NLISSN.Application tests/NLISSN.HostTests/Application
git commit -m "refactor: remove core feature selection"
```

## 5. Task 4: 删除四个延期 feature 的测试和兼容入口

**Files:**
- Modify: `tests/NLISSN.Testing/TestInfrastructure/RulePipelineTestFactory.cs`
- Modify: `tests/NLISSN.UnitTests/Application/DirectoryAnalysisUseCaseTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/ApplicationServiceFlowTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Modify: `tests/NLISSN.HostTests/TestCodeSetCoverageTests.cs`
- Modify: `tests/NLISSN.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionEvidenceTests.cs`
- Modify: `tests/NLISSN.ContractTests/Identity/RuleIdentityContractTests.cs`
- Modify or replace: `tests/NLISSN.HostTests/Application/MethodDeletionFeatureScopeTests.cs`
- Preserve: historical deferred rule source files carrying `RuleCatalogIgnore`

### Step 1: 先锁定“不存在兼容入口”

- 删除或改写所有传入 `enableUnreachableMethodDeletion`、`enableUnreferencedMethodDeletion`、`enableUnusedInterfaceImplementationCleanup`、`enableInternalOnlyPublicMethodPrivatization` 的测试。
- `RulePipelineTestFactory.Create` 只保留 disabled name 参数。
- 删除依赖这些 bool 的 `CreateApplication`/coverage helper 参数和分支。
- 将 `MethodDeletionFeatureScopeTests` 改为当前边界测试：四个延期规则族均不在 `GeneratedRuleCatalog` 和默认 Composer 管道中；测试不得构造 `RuleFeature` 或模拟 feature opt-in。
- 对只验证四个延期规则效果的旧行为测试，删除其当前框架路径；不要把规则行为重新接入默认管道。

Run:

```powershell
rg -n "enableUnreachableMethodDeletion|enableUnreferencedMethodDeletion|enableUnusedInterfaceImplementationCleanup|enableInternalOnlyPublicMethodPrivatization|RuleFeature|RequestedFeatures|descriptor\.Feature" src tests -g '*.cs'
```

Expected: 在生产代码和测试辅助代码中无上述四个 bool、`RuleFeature`、`RequestedFeatures` 或 generated descriptor feature 引用；只允许历史文档中的说明和 `RuleCatalogIgnore` 边界测试名称存在。

### Step 2: 实现最小测试入口收敛

- 从 `RulePipelineTestFactory` 和各测试 helper 删除四个 bool 参数。
- 更新调用方为 `RulePipelineTestFactory.Create()` 或只传 `disabledRuleTypes`。
- 删除“启用所有四个可选 feature 后应有四阶段”的旧断言；改为断言这些规则族不会影响当前 68-rule pipeline。
- 保留 `RuleCatalogIgnore` 对历史规则的意图表达，禁止用空数组 descriptor、no-op 阶段或隐藏 factory 替代。

### Step 3: 运行延期 feature 范围核心测试

Run:

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~MethodDeletionFeatureScopeTests|FullyQualifiedName~RulePipelineComposerTests|FullyQualifiedName~GeneratedRuleCatalogEquivalenceTests"
```

Expected: 选中的范围测试 PASS，且四个延期 feature 不能通过任何当前 API 进入目录或规则图。

### Step 4: Commit

```powershell
git add tests/NLISSN.Testing/TestInfrastructure tests/NLISSN.UnitTests tests/NLISSN.HostTests tests/NLISSN.ContractTests/Identity
git commit -m "test: remove deferred feature compatibility paths"
```

## 6. Task 5: 只做少量核心验收和文档收口

**Files:**
- Modify after implementation: `设计docs/目前设计/规则目录-编译期生成.md`
- Modify after implementation: `docs/plans/2026-09-08-rule-catalog-source-generator-execution.md`
- Verify: `tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs`
- Verify: `tests/NLISSN.ContractTests/Identity/RuleIdentityContractTests.cs`
- Verify: `tests/NLISSN.HostTests/Application/GeneratedRuleCatalogEquivalenceTests.cs`
- Verify: `tests/NLISSN.HostTests/Application/RulePipelineComposerTests.cs`
- Verify: `tests/NLISSN.HostTests/Application/MethodDeletionFeatureScopeTests.cs`

### Step 1: 运行唯一允许的核心测试集

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleCatalog|FullyQualifiedName~RuleIdentityContractTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RulePipelineComposerTests|FullyQualifiedName~RuleSelectionAdapterTests|FullyQualifiedName~GeneratedRuleCatalogEquivalenceTests|FullyQualifiedName~MethodDeletionFeatureScopeTests"
```

Expected:

- 两个过滤测试命令均成功，失败数为 0。
- generated catalog 仍为 68 个 factory，阶段计数为 `19/12/5/32`。
- Rule identity snapshot、阶段排序、RuleId 唯一性和默认行为不变。
- disabled rule 仍保留到 pipeline 和 graph 声明中。
- 四个延期 feature 没有 registration、descriptor、factory、Composer 兼容路径或规则图节点。
- 每次 Compose 为同一规则创建新实例。

实际结果（2026-09-09）：Contract 过滤测试 `25/25` 通过；Host 过滤测试 `11/11` 通过，均为 0 失败。

不要运行：

```text
Run-TestTiers.ps1 -Fast
Run-TestTiers.ps1 -Host
Performance tier
无 filter 的 Unit/Contract/Host 全量 dotnet test
```

### Step 2: 文档与静态检查

- 将设计文档中“当前唯一 feature 是 Core”的描述改为“当前目录无 feature 维度”；保留四阶段完整性和 68-rule 基线。
- 将旧执行计划中的 Core 自动加入、feature filter、每 feature 完整性和四个 bool 入口改为本计划的 flat catalog 语义。
- 明确四个延期 feature 仍不是当前框架产物；未来重新纳入必须另行定义真实四阶段输入、输出、规则图节点和行为等价基线。

Run:

```powershell
git diff --check
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
```

Expected: 无 whitespace error；harness consistency 通过。该检查不替代上面的核心测试，也不扩大测试范围。

### Step 3: Commit

```powershell
git add docs/plans/2026-09-09-rule-catalog-remove-core-execution.md "设计docs/目前设计/规则目录-编译期生成.md" docs/plans/2026-09-08-rule-catalog-source-generator-execution.md
git commit -m "docs: record rule catalog feature removal"
```

## 7. 完成条件

- [x] `RuleFeature` 类型、注册属性 feature 参数、descriptor feature 字段和 generated feature 发射全部删除。
- [x] 所有当前目录规则使用无参数 `[RuleRegistration]`，四个阶段都有真实规则，且目录基线仍为 `19/12/5/32`、68 factory。
- [x] Generator 仍在缺少任一全局阶段时报告 `NLRCG011`，并且不发射部分目录。
- [x] `RulePipelineComposer` 不自动加入 Core、不按 feature 过滤，`RuleSelection` 不公开 feature 集合。
- [x] disabled RuleId、排序、factory 生命周期、规则图声明和 identity snapshot 保持不变。
- [x] 四个延期规则族无生成产物、无 Composer 兼容路径、无测试 bool opt-in；历史源码若保留仅由 `RuleCatalogIgnore` 排除。
- [x] 只运行本计划中的少量 Contract/Host 核心过滤测试，所选测试全部通过。
- [x] 设计文档和旧执行计划已同步 flat catalog 语义，并通过 `git diff --check` 与 harness consistency。
