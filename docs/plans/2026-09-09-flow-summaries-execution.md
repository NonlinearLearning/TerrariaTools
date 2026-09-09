# FlowSummaries 生产启用与外部调用保护实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在真实的单文件、目录和 Workspace 分析入口中启用 `FlowSummaries`，为无法或不应展开的外部方法提供可验证的数据流摘要，同时对未知、歧义、阻断和预算截断保持保守，不因摘要缺失生成不安全删除决定。

**Architecture:** 保留当前 Roslyn 绑定优先的调用目标解析；仅当调用目标没有当前 CPG 内部方法边界时查询外部摘要。默认框架摘要由当前 `Compilation` 中的符号构造完整方法键，避免硬编码运行时程序集版本；摘要边只在请求 `InterproceduralDataFlow` capability 时生成。规则层消费带状态和端点 provenance 的 `ExternalSummaryFlowPayload`，把摘要当作数据流事实而不是删除授权，未知或不完整事实进入 evidence 并保护相关删除候选。

**Tech Stack:** C# / .NET 10, Roslyn `Compilation`、`IOperation` 和 `IInvocationOperation`，现有 NLCPG `CallFlowResolver` / `InterproceduralDataFlow`，NLISSN Mark/Propagate/Propose pipeline，xUnit，PowerShell harness。

---

## 1. 当前事实、真实调用点和边界

本计划基于以下已验证事实：

- `FlowSummaryMethodKey` 使用程序集身份、包含类型、元数据方法名、泛型参数数、参数数量、参数名和参数类型形状；不能只按方法名登记摘要。
- `NLCPGBuilder` 只在无目标、无法取得目标符号或目标没有当前图内方法边界时调用外部摘要；多目标调用直接记录 `AmbiguousTarget`，不尝试摘要。
- `InterproceduralDataFlow` 不在默认 capability 集合中。当前生产入口构造 `ApplicationService` 时没有传入 `ICallFlowResolver`；`NLCPGDefaultFlowSummaries.All` 为空。因此已有摘要解析和持久化代码是测试可用、生产未接通的组件。
- Workspace 虽然加载完整 project `Compilation`，但 `DirectoryAnalysisUseCase` 仍按源码文件逐个构图和运行规则。跨文件的项目内部方法不能因为当前文件没有边界节点就被伪装成框架摘要；这属于项目级 CPG 建模问题。

真实源码中的调用点按风险分层如下：

| 调用形态 | 真实位置/代表 | 处理决定 |
| --- | --- | --- |
| 接收者到返回值 | `tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs` 已用 `object.ToString()` 验证；真实分析代码也有 `IMethodSymbol.ToString()` | 首批只登记经过完整符号键验证的稳定无副作用方法；`IMethodSymbol.ToString()` 不得误当作 `System.Object.ToString()` |
| 多参数值到返回值 | `Path.Combine` 出现在 `src/NLCPG/Builder/CpgShardBuildCoordinator.cs` 和 `CpgShardBuildSession.cs` | 作为候选，先验证每个 overload 的参数键和所有输入映射；未验证前不进默认目录 |
| 泛型参数到返回值 | `Task.FromResult` 出现在 `src/NLCPG/Analysis/CpgRelationQueryService.cs`、`src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs` 等 | 作为候选，必须先覆盖泛型键和构造后的参数类型；不能用方法名匹配 |
| 集合元素流 | LINQ 的 `.Select`、`.Where` 链式调用在分析器和规则代码中大量出现 | 首批排除；当前端点模型没有元素级流语义，参数到返回的粗粒度摘要会过度近似 |
| 条件性 `out` 流 | `Dictionary.TryGetValue` | 首批排除；`out` 只有在成功分支成立，不能建立无条件流边 |
| 副作用 API | `File.WriteAllText`、文件/目录删除和移动 | 首批排除；摘要只描述数据流，不能证明副作用安全或授权删除实参 |
| 项目内部跨文件 helper | Workspace 的真实多文件项目调用 | 不用 framework/user summary 掩盖；优先补齐项目级 CPG 边界或明确记录 external/project boundary cut |

首批默认框架目录至少覆盖一个真实可验证的 `System.Object.ToString()` 形态；`Path.Combine`、`Task.FromResult` 和 Roslyn `ISymbol.ToString()` 只有在对应 overload、符号键和删除安全测试完成后才能加入。用户/项目摘要可以继续通过显式 registry 注入，但不能隐式把项目内部方法当作框架方法。

### 真实生产调用清单

下面的调用点来自当前 `src` 扫描；它们是候选证据，不代表已经应该全部加入默认目录：

| 具体调用点 | 可表达的摘要映射 | 结论 |
| --- | --- | --- |
| `src/NLCPG/Builder/CpgShardBuildCoordinator.cs:164`、`src/NLCPG/Builder/CpgShardBuildSession.cs:164`、`:166` 的 `Path.Combine` | 对具体 overload 的每个 path 参数建立 `Parameter(i) -> Return` | 已验证为默认目录外的候选；后续若晋级，必须按 overload 完整键登记，不能按 `Path.Combine` 方法名合并 |
| `src/NLCPG/Builder/NLCPGBuildContext.cs:89`、`:93`、`:114`、`:115`、`:138` 的 `Path.GetFullPath`、`GetFileName`、`GetFileNameWithoutExtension` | `Parameter(0) -> Return` | 当前仍未登记；需要具体 overload、nullable return 和删除保护测试后再评估 |
| `src/NLCPG/Analysis/CpgRelationQueryService.cs:68` 的 `Task.FromResult`；`src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs:119`、`:146`、`:161`、`:190`、`:217` 的 `Task.FromResult` | `Parameter(0) -> Return` | 已验证为默认目录外的候选；需要泛型 arity、构造后的参数类型和 `Task` 返回包装测试后再评估 |
| `src/NLCPG/Cli/NLCPGCli.cs:338`、`src/NLISSN/Artifacts/RewritePlanArtifactService.cs:38`、`:53`、`src/NLISSN/Artifacts/AnalysisEvidenceArtifactService.cs:57` 的 `JsonSerializer.Serialize` | 序列化输入对象到字符串返回值，理论上是参数到返回 | 不进入首批默认目录；generic/overload 和对象图语义复杂，且这些调用属于 artifact 边界，先用 unknown 保守处理 |
| `src/NLISSN.Rules/Mark/MethodGlobal/UnreferencedMethodMarkRule.cs:95`、`src/NLISSN.Rules/Mark/MethodGlobal/UnreachableMethodMarkRule.cs:82` 的 `IMethodSymbol.ToString()` | receiver 到 return | 已验证与 `System.Object.ToString()` 共享 Roslyn method key 但受 receiver type constraint 拒绝；不进入默认目录 |
| `src/NLISSN/Hosting/CommandHost.cs:131`、`src/NLISSN/Hosting/DirectoryAnalysisService.cs:76`、`src/NLCPG/Builder/CpgShardBuildSession.cs:369` 等 `File.WriteAllText` | 无返回值；输入影响外部文件副作用 | 明确排除；FlowSummary 不能替代副作用/纯度分析 |
| `src/NLCPG/Builder/NLCPGBuilder.cs` 多处 `TryGetValue`、`src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs:267` | cache/index lookup，部分带条件性 `out` | 明确排除；这些是内部索引控制流，不应作为外部 API 数据流摘要 |

因此，当前实际启用的首批只有“符号精确的 `System.Object.ToString()` 无参数 receiver-to-return 摘要”，而不是对所有看起来返回数据的框架方法做 broad mapping。真实调用点测试确认 `Path.Combine`、`Task.FromResult`、Roslyn `IMethodSymbol.ToString()`、LINQ、`TryGetValue` 和 `File.WriteAllText` 均未进入默认目录；它们继续返回 unknown 或由显式摘要决定。跨文件项目 helper 也经过 Workspace project-compilation 回归测试，未命中 framework catalog。

## 2. 不变量和非范围

必须保持：

- 没有匹配摘要、签名不兼容、`Block`、多目标、dynamic、delegate 或 budget 截断时，不建立摘要数据流边。
- 摘要来源按 `Project > User > Framework > Unknown` 保持确定性；同一来源同一完整键重复登记仍然失败。
- resolved summary 只产生带 method key、来源、源端点、目标端点的事实和证据，不直接产生删除决定。
- 未知/阻断/截断调用不能被默认删除规则当作“没有流”；相关 seed 或输入端点必须保守保留，或者由规则明确返回 skip/unknown。
- 生产目录、Workspace、单文件 `ApplicationService` 和直接 `NLCPGBuilder` 入口的 resolver/capability 行为一致；自定义测试 resolver 仍可覆盖默认 resolver。
- 摘要边和 evidence 在 DOP=1 与并行构图下稳定，预算计数和截断原因可观察。

本计划不实现：

- 通用方法体摘要推导、全程序精确 interprocedural analysis、dynamic dispatch 完整候选集或 heap/alias 分析；
- LINQ `.Select/.Where` 元素级 taint/flow、`TryGetValue` 成功分支建模或副作用纯度证明；
- 用摘要替代跨文件项目级 CPG；
- 运行时反射扫描所有框架方法；
- 在本计划中扩大 `FlowSummaryEndpoint` 到集合元素、异常路径或异步状态机内部节点。

## 3. Task 1：冻结框架摘要键和默认目录

**Files:**

- Modify: `src/NLCPG/Analysis/FlowSummaries/NLCPGDefaultFlowSummaries.cs`
- Modify if required by the factory: `src/NLCPG/Analysis/FlowSummaries/FlowSummaryModels.cs`
- Modify: `src/NLCPG/Analysis/FlowSummaries/NLCPGFlowSummary.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**Step 1: Write the failing catalog tests**

添加测试，使用真实 `CSharpCompilation` 的 metadata references 查找 `System.Object.ToString()`，断言：

- 默认 framework catalog 能用当前 Roslyn symbol 生成完整 `FlowSummaryMethodKey`；
- `Receiver -> Return` 能解析为 `Framework`；
- 同一 compilation 换程序集版本或 reference identity 时，catalog 不依赖硬编码的 `System.Private.CoreLib` 版本字符串；
- 未登记方法返回 `Unknown`；
- 同一完整键的重复 framework/project/user 登记仍按 registry 契约失败。

**Step 2: Run the focused tests to verify the baseline fails**

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryResolverTests|FullyQualifiedName~NLCPGPartitionedBuilderTests"
```

Expected: 新增默认目录断言失败，因为当前默认列表为空且生产没有从当前 `Compilation` 建立 framework summaries。

**Step 3: Implement symbol-backed framework catalog construction**

保留旧 `NLCPGFlowSummary`/`All`/`TryGet` 的兼容行为，新增面向 `FlowSummary` 的默认目录构造入口。目录项只保存声明式的元数据类型、方法名、泛型 arity、参数形状约束和 endpoint mappings；真正创建 `FlowSummaryMethodKey` 时从当前 `Compilation` 的 `IMethodSymbol` 读取 assembly identity、参数名和 fully qualified type shape。

初始目录只加入已经有真实 CPG 测试和明确语义的 `System.Object.ToString()`。对 overload 必须按参数数量、参数类型和必要的参数名确认唯一符号；找不到或匹配多个时返回未启用状态而不是猜测。

**Step 4: Run the focused tests to verify the catalog passes**

Run the same focused command. Expected: PASS; legacy empty-list compatibility assertions and new symbol-backed framework assertions both pass.

**Step 5: Commit the catalog contract**

```powershell
git add src/NLCPG/Analysis/FlowSummaries tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs
git commit -m "feat: add symbol-backed default flow summaries"
```

## 4. Task 2：把默认 resolver 接入所有生产分析入口

**Files:**

- Create: `src/NLISSN.Application/Analysis/FlowSummaryResolverFactory.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Modify: `src/NLISSN/Hosting/WorkspaceAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/DirectoryAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Test: `tests/NLISSN.HostTests/Application/CommandHostCompositionTests.cs`
- Test: `tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs`

**Step 1: Write failing production wiring tests**

添加以下行为测试：

- `ApplicationService` 使用默认 pipeline 分析含 `value.ToString()` 的真实源码时，builder options 收到 `InterproceduralDataFlow` capability，且 graph/evidence 中出现 framework summary；
- `CommandHost` 的单文件入口和目录入口不再是 resolver-null；
- Workspace 使用 project compilation 构造默认 resolver；跨文件项目内部 helper 不匹配 framework summary；
- 传入自定义 `ICallFlowResolver` 时，自定义结果优先于默认 catalog。

**Step 2: Run the host tests to verify the baseline fails**

Run:

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~CommandHostCompositionTests|FullyQualifiedName~WorkspaceAnalysisHostTests"
```

Expected: 新增 wiring 断言失败；当前 `new ApplicationService(pipeline)` 没有默认 resolver，pipeline 也没有 summary rule 提供 capability。

**Step 3: Implement one resolver factory and preserve explicit overrides**

让 factory 接收当前 `Compilation`，调用 Task 1 的 symbol-backed catalog 创建 `CallFlowResolver`。`ApplicationService` 保留现有显式 resolver 构造路径；没有显式 resolver 时，在已经拿到 `SemanticModel.Compilation` 后创建默认 resolver，并把同一个有效 resolver 同时放入 `NLCPGBuilderOptions.CallFlowResolver` 和 `CpgAnalysisContext.CallFlowResolver`。

目录和 Workspace 不各自复制 registry 组装逻辑。每个 project compilation 使用一个稳定 resolver，跨文件调用仍由当前 CPG 的 internal-boundary 判断决定；只有显式 project/user summary 才允许覆盖该边界策略。

**Step 4: Run the host tests to verify production wiring**

Run the same host command. Expected: PASS; single-file, directory, compiled Workspace and custom override cases all exercise the same resolver contract.

**Step 5: Commit the production wiring**

```powershell
git add src/NLISSN.Application/Analysis src/NLISSN/Hosting tests/NLISSN.HostTests/Application/CommandHostCompositionTests.cs tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs
git commit -m "feat: wire flow summaries into production analysis"
```

## 5. Task 3：启用 interprocedural capability 和生产摘要边验证

**Files:**

- Create: `src/NLISSN.Rules/Propagate/ExternalSummaryFlowPropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: `src/NLISSN.Core/Propagation/ExternalSummaryFlowPayload.cs`
- Modify if necessary: `src/NLISSN.Core/Propagation/RuleDefinitionPropagate.cs` or the rule's capability override only
- Test: `tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs`
- Test: `tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs`
- Test: `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`

**Step 1: Write failing production propagation tests**

覆盖：

- receiver-to-return：`value.ToString()` 生成一个带 `ExternalSummaryFlowPayload` 的 structured propagated fact；
- ordinary parameter-to-return：为一个显式 project/user summary 的 `External(value)` 生成参数端点事实；
- `ref`/`out` endpoint 只在 Roslyn `RefKind` 和 summary endpoint 一致时解析；
- unknown、signature mismatch、`Blocked` 和 endpoint 无法映射时没有 interprocedural summary edge，也没有普通 default delete decision；
- 一个调用的多个 resolved mappings 按 method key、source kind、ordinal、target kind 稳定排序且不重复；
- summary fact 自身不会被 `DefaultRemovalProposalRule` 当成可直接删除授权。

**Step 2: Run focused propagation tests to verify the baseline fails**

Run:

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryDeletionSafetyTests|FullyQualifiedName~PropagationRuleExpansionTests"
```

Expected: 生产 pipeline 当前没有产生该 payload；现有测试中的自定义 rule 仍可通过，但新增默认 rule 测试失败。

**Step 3: Implement the narrow propagation rule**

规则只消费已有的 `TargetExpression` invocation facts，取得 `IInvocationOperation` 后按确定顺序枚举 receiver 和已绑定参数。参数 endpoint 根据 `RefKind` 选择 `Parameter`、`RefParameter` 或 `OutParameter`，目标只请求 `Return`；不使用方法名猜测，也不读取方法体。

`ExternalSummaryFlowPayload` 增加 source endpoint、source syntax/provenance 和“是否为 input-to-return 保护事实”的明确判断。resolved mapping 只产生 flow fact；unknown/blocked/mismatch 产生带 rejection reason 的保护性事实或 skip 状态，使默认提案不会把缺失摘要误当作无流。`ProposalFacts` 按 endpoint 找到 receiver/argument 对应的 seed，结构化 payload 不进入 generic delete 候选；保留现有 `IsParameterToReturn` 兼容语义，同时覆盖 receiver-to-return。

规则声明 `RequiredCapabilities` 为 `NLCPGCapability.InterproceduralDataFlow`，通过 generated rule catalog 进入默认 pipeline。不要在规则内展开目标方法实现，也不要为集合元素或副作用 API 添加特殊推理。

**Step 4: Run CPG and safety tests**

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryResolverTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryDeletionSafetyTests|FullyQualifiedName~PropagationRuleExpansionTests"
```

Expected: CPG summary edge、payload、evidence 和删除保护全部 PASS；已有 custom-resolver 测试保持 PASS。

**Step 5: Commit the production rule**

```powershell
git add src/NLISSN.Rules/Propagate src/NLISSN.Core/Propagation src/NLISSN.Rules/Propose/Support tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs
git commit -m "feat: protect deletion analysis with external flow facts"
```

## 6. Task 4：补齐 unknown、block、歧义和预算边界

**Files:**

- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs` only if an existing metric/reason needs a new field
- Modify: `src/NLISSN.Core/Decision/AnalysisEvidence.cs` only if current evidence cannot distinguish rejection causes
- Test: `tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs`
- Test: `tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs`

**Step 1: Add regression cases before changing builder behavior**

覆盖真实语义边界：

- unresolved target、missing resolver、missing summary；
- summary key exists but endpoint ref kind/signature mismatch；
- summary mapping kind `Block`；
- multi-target call exceeding `MaxCallTargetsPerSite`；
- `MaxMappingsPerCallSite`、`MaxMappingsPerMethod` 和 `MaxMappingsPerBuild` 截断；
- receiver/argument operation 没有可创建的 graph node；
- summary bridge label 持久化、恢复和 stable key 保持一致。

每个用例同时断言 `LastFlowSummaryMetrics`、evidence state 和最终 decisions；不能只断言 graph edge 数量。

**Step 2: Run the boundary tests**

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryResolverTests|FullyQualifiedName~CpgShardContractTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryDeletionSafetyTests"
```

Expected: 新增边界用例在修复前至少有一项失败，且不会以普通 delete candidate 的形式静默通过。

**Step 3: Make rejection accounting and deletion protection explicit**

保持现有 `UnknownCalls`、`SignatureMismatches`、`BlockedMappings`、`RejectedEndpoints` 和 `TruncatedMappings` 计数；如需区分“摘要拒绝”和“内部跨文件缺少 CPG 边界”，增加稳定 cut reason，而不是复用 `Unknown` 掩盖来源。所有 truncation 都停止新增 summary edge 和 summary-derived deletion fact。

**Step 4: Run the boundary tests again**

Expected: PASS，且 evidence 的 `UsesSummary` 节点对 resolved 为 `Available`，对 blocked 为 `Rejected`，对 unknown/mismatch 为 `Unavailable`；DOP 改变不影响排序和计数。

**Step 5: Commit the safety boundaries**

```powershell
git add src/NLCPG/Builder src/NLISSN.Core/Decision tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs tests/NLISSN.ContractTests/Cpg/FlowSummaryResolverTests.cs tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs
git commit -m "test: preserve conservative flow summary boundaries"
```

## 7. Task 5：用真实项目调用点验证首批摘要，并冻结排除项

**Files:**

- Create: `tests/NLISSN.ContractTests/Cpg/FlowSummaryRealCallsiteTests.cs`
- Modify: `tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs`
- Modify: `tests/NLISSN.Testing/TestCodeSet/Workspace/WorkspaceSourceGenerator.cs` only if a stable multi-file fixture is needed
- Modify: `src/NLCPG/Analysis/FlowSummaries/NLCPGDefaultFlowSummaries.cs` only for candidates that pass this task
- Documentation: `docs/plans/2026-09-09-flow-summaries-execution.md`

**Step 1: Add real-source callsite tests**

从当前 `src` 的真实调用形态建立最小 compilation fixture，至少覆盖：

- `object.ToString()` 的 receiver-to-return；
- `Path.Combine` 的 overload 选择和所有参与返回字符串的参数；
- `Task.FromResult<T>` 的泛型参数-to-return；
- `IMethodSymbol.ToString()` 与 `System.Object.ToString()` 的 key 不相等；
- LINQ `.Select/.Where`、`Dictionary.TryGetValue` 和 `File.WriteAllText` 默认不产生错误的 summary edge；
- 跨文件项目内部 helper 仍走 internal-project/Cpg boundary 诊断，不命中 framework catalog。

**Step 2: Run the real-callsite tests**

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummaryRealCallsiteTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~WorkspaceAnalysisHostTests"
```

Expected: 首批 `System.Object.ToString()` 通过；其余候选只有在每个 overload/泛型/副作用约束有明确证据时才允许改变默认目录。未通过的候选保持 `Unknown` 或显式排除，不以 broad method-name match 通过。

**Step 3: Decide candidate promotion one at a time**

每提升一个候选，必须同时具备：完整符号键、端点语义说明、resolved/unknown/mismatch 测试、删除保护测试和 DOP 稳定性测试。`Path.Combine` 和 `Task.FromResult` 可作为第二批；集合元素、条件性 `out` 和副作用 API 留在 user/project 显式摘要或排除表中。

**Step 4: Record the final inventory**

在本计划的候选表中更新实际加入的 framework summaries、保留为 user/project-only 的 summaries 和明确排除原因；不要把未运行的候选测试写成已支持。

**Step 5: Commit only proven candidates**

```powershell
git add src/NLCPG/Analysis/FlowSummaries tests/NLISSN.ContractTests/Cpg/FlowSummaryRealCallsiteTests.cs tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs docs/plans/2026-09-09-flow-summaries-execution.md
git commit -m "test: validate flow summaries against real callsites"
```

## 8. Task 6：完整验证、性能和文档收口

**Files:**

- Modify if needed: `src/NLCPG/docs/node-edge-catalog.md`
- Modify if needed: `docs/developer-guide.md`, `docs/cli-reference.md`, `docs/quick-start.md`
- Modify: `docs/plans/2026-09-09-flow-summaries-execution.md`
- Test: affected Contract and Host projects

**Step 1: Verify capability and default-path behavior**

Run:

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummary|FullyQualifiedName~CpgShardContractTests|FullyQualifiedName~NLCPGPartitionedBuilderTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~FlowSummary|FullyQualifiedName~WorkspaceAnalysisHostTests|FullyQualifiedName~CommandHostCompositionTests"
```

确认默认 pipeline 的 capability 集合确实包含 `InterproceduralDataFlow`，没有 resolver 的自定义最小 pipeline 仍保持默认行为，显式 resolver 仍可覆盖 framework catalog。

**Step 2: Verify deterministic parallel behavior**

用现有 summary bridge DOP 测试和至少一个真实 callsite，比较 DOP=1、DOP=2/16 的 graph edge stable key、evidence stable key、metrics 和 decisions。确认 resolver registry 是只读的，规则不共享可变跨分析状态。

**Step 3: Run repository documentation and whitespace checks**

Run:

```powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

Expected: harness consistency 和 whitespace check 均通过；若 CLI 或开发流程契约发生变化，再同步检查 `docs/quick-start.md`、`docs/cli-reference.md`、`docs/developer-guide.md` 和 `docs/contributing.md`。

**Step 4: Update implementation evidence**

在计划的 Definition of Done 中只勾选实际运行并通过的项目；记录默认启用的候选、排除项、metrics 行为和跨文件 project boundary 的剩余限制。

## 9. 回滚和故障处理

如果默认摘要造成决策数量、diff、evidence 或性能回归：

1. 保留 `CallFlowResolver`、registry 和自定义 resolver API，但从默认 pipeline 移除 `ExternalSummaryFlowPropagationRule` 或把 framework catalog 恢复为空；
2. 保留 contract/host 回归测试，确保 unknown/blocked 不会产生不安全删除；
3. 不把 `MaxMappings*` 调大来掩盖截断，也不把项目内部跨文件调用改登记为 framework summary；
4. 保存失败 callsite 的完整 method key、summary status、cut reason 和 DOP，再逐个恢复候选。

## 10. Definition of Done

- [x] 当前 `Compilation` 能构造不依赖运行时版本硬编码的 framework summary key；registry precedence、重复键和 signature mismatch 有测试。
- [x] 单文件、目录和 Workspace 生产入口都使用同一默认 resolver；显式 custom resolver 仍可覆盖。
- [x] `InterproceduralDataFlow` 只在 summary 生产规则请求时启用；默认 pipeline 的 capability、CPG edge 和 label 可观察。
- [x] resolved summary 只生成 typed payload/evidence，不直接授权删除；receiver/parameter/ref/out endpoint provenance 完整。
- [x] unknown、blocked、歧义、无法映射和 truncation 都保持保守，metrics/evidence/cut reason 稳定。
- [x] 至少一个真实稳定外部 API（`System.Object.ToString()`）完成默认启用；`Path.Combine`、`Task.FromResult` 等候选已验证未加入默认目录。
- [x] LINQ 元素流、`TryGetValue` 条件 out、副作用 API 和跨文件项目 helper 没有被错误地用摘要覆盖。
- [x] CPG、规则传播、删除保护、证据、持久化、DOP 稳定性和 Workspace 边界测试通过。
- [ ] `check-harness-consistency.ps1` 和 `git diff --check` 通过，文档只记录实际完成的范围。
