# 规则身份收敛：删除 `CapabilityId`、保留 `RuleId` 执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**目标：** 删除 `IRuleDefinition.CapabilityId`，把重新定义后的 `RuleId` 作为唯一、全局稳定的具体规则生产者身份，同时保持 `RuleKind`、`RuleSemanticTag`、`EvidenceAnchor`、`RuleNodeId` 和事实去重键的独立职责。

**架构：** 规则定义只声明一个 `RuleId`。`RuleId` 标识一个具体的 Mark、Propagate、Lift 或 Propose 规则定义，并在整个规则目录中唯一；规则阶段由强类型的 `RuleKind` 提供，事实语义由 `RuleSemanticTag` 提供，执行图节点由结构化的 `(RuleKind, RuleId)` 派生。Mark、传播、提升、决策、证据和 rewrite 工件继续使用 `ruleId` 记录生产者来源，不再维护第二个 `CapabilityId` 字段。

**技术栈：** .NET SDK 10.0.200-preview.0.26103.119、C#、Roslyn、xUnit、PowerShell、NLISSN 的 Unit/Contract/Host/Performance 测试层，以及现有的 harness 和 CLI smoke 验证脚本。

---

## 1. 执行前的当前决策

本计划是对早期“删除 `RuleId`、保留 `CapabilityId`”方案的修订。早期文档仍保留在仓库中作为历史讨论，但不得按其步骤执行：

- [旧设计文档](2026-09-08-rule-identity-capabilityid-design.md)
- [旧执行文档](2026-09-08-rule-identity-capabilityid-execution.md)
- [旧研究文档](2026-09-08-rule-identity-capabilityid-research.md)

最新目标是：

~~~text
删除 CapabilityId
保留 RuleId
不增加 RuleKey、LegacyRuleId、RuleFamilyId 或 RuleInstanceId
~~~

这不是兼容窗口，也不是双读单写迁移。实施完成后，生产代码、测试代码和新工件只保留 `RuleId` 作为规则来源键。

当前工作区存在大量用户已有修改和未跟踪文件。执行者必须先保存 `git status --short` 输出，不能使用 `git reset --hard`、`git checkout --`、全目录删除或其他会覆盖既有修改的操作。

当前快照还包含与事实端口/事实种类和规则结构合同相关的源码改动，例如 `MarkRecord`、`PropagationFactKey`、`RuleBindingValidator`、`RuleStructureContract` 以及若干具体规则文件。它们不属于本身份迁移的授权范围；实施者必须在这些改动之上重新生成 `RuleId` 消费者清单，只改身份相关部分，不回退事实模型、端口模型或结构合同改动。

如果发现仓库外部插件、配置或脚本依赖 `CapabilityId`，必须先停止并重新确认外部契约；当前计划只覆盖仓库内 `src/`、`tests/` 和本仓库文档。

## 2. 目标契约

### 2.1 `IRuleDefinition` 的最终接口

目标接口应收敛为：

~~~csharp
public interface IRuleDefinition
{
    string RuleId { get; }

    RuleInputCardinality InputCardinality { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }
}
~~~

四个阶段基类继续实现 `IRuleDefinition`，但删除：

~~~csharp
public virtual string CapabilityId => RuleId;
~~~

具体规则只保留一个身份声明：

~~~csharp
public override string RuleId { get; } = "mark.unreachable-method";
~~~

不得在规则类中保留 `CapabilityId` 别名、兼容属性或通过类型名推导 ID 的实现。

### 2.2 `RuleId` 的不变量

`RuleId` 是一个具体规则定义和规则结果生产者的稳定规范键，必须满足：

1. 非空、非空白；
2. 在整个规则目录中全局唯一，比较器为 `StringComparer.Ordinal`；
3. 不随实现类名称、注册顺序、并行度、命中数量、文件位置或运行实例变化；
4. 不包含需要由运行时解析的隐含阶段协议；
5. 不承担事实端口、源码位置、冲突域或证据锚点职责；
6. 规则语义发生身份级变化时直接使用新的 `RuleId`，不保留旧 ID 别名；
7. 结果、证据、rewrite 和 diff 归因均使用同一个 `RuleId` 值。

当前具体规则中已有的 `CapabilityId` 值是本次迁移的规范值来源。对每个具体规则执行：

~~~text
新的 RuleId = 迁移前该规则的有效 CapabilityId
~~~

因此，旧的 `DEL-*`、`CLR-*`、`PRIV-*` 字符串只作为迁移前观察值，不进入新的兼容字段或映射表。不可达方法规则的目标示例为：

~~~text
Mark    RuleId = mark.unreachable-method
Propose RuleId = propose.unreachable-method
~~~

它们通过 `UnreachableMethod` 语义端口连接，而不再通过共享旧的 `DEL-DEAD-001` 表示同一身份。

### 2.3 身份字段的职责边界

| 对象 | 最终身份/字段 | 职责 |
| --- | --- | --- |
| 具体规则定义 | `RuleId` | 谁产生了这个规则结果 |
| 执行阶段 | `RuleKind` | 规则处于 Mark、Propagate、Lift 或 Propose 哪个阶段 |
| 事实端口 | `RuleSemanticTag` | 规则产生或消费的事实是什么 |
| 执行图节点 | `RuleNodeId(RuleKind, RuleId)` | 本次图执行中的节点身份 |
| 源码命中 | `EvidenceAnchor` | 命中了哪个文件、span 和语法节点 |
| 传播事实 | `PropagationFactKey` | 生产者、位置、语法种类和语义标签构成的去重键 |
| 决策归并 | `ConflictKey` / `MergeKey` | 哪些决策处于同一冲突/合并域 |
| 构建与运行 | `BuildId` / `RunId` | 哪次构建或分析运行产生了结果 |

禁止把 `RuleSemanticTag`、`EvidenceAnchor` 或 `RuleKind` 填入 `RuleId` 以外的身份职责，也禁止新增同义字符串字段。

### 2.4 结构化 `RuleNodeId`

目标模型为：

~~~csharp
public sealed record RuleNodeId
{
    public RuleNodeId(RuleKind kind, string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException("Rule node rule ID cannot be empty.", nameof(ruleId));
        }

        Kind = kind;
        RuleId = ruleId;
    }

    public RuleKind Kind { get; }

    public string RuleId { get; }

    public string Value => $"{Kind}:{RuleId}";

    public static RuleNodeId For(RuleKind kind, string ruleId) =>
        new(kind, ruleId);
}
~~~

`Value` 只用于日志、快照和展示。内部逻辑必须使用 `Kind` 和 `RuleId`，不得通过 `IndexOf(':')`、切片、`StartsWith("Mark:")` 或 `Split(':')` 从 `Value` 反解析阶段和规则来源。

### 2.5 工件策略

`RewritePlanEdit.RuleId` 的字段名保留为 `ruleId`，但值切换到新的规范 `RuleId`。由于旧工件中的 `ruleId` 值与新规则身份不兼容，工件 schema 必须升级并拒绝旧版本：

~~~text
旧 schema：拒绝
新 schema：只写 ruleId，且值必须是新的规范 RuleId
~~~

不得新增 `capabilityId`、`legacyRuleId` 或双字段工件格式；不得根据旧 `DEL-*` 值进行回退查找。

## 3. 影响面与文件地图

| 区域 | 主要文件 | 执行目标 |
| --- | --- | --- |
| 声明合同 | `src/NLISSN.Rule/IRuleDefinition.cs`；四个 `RuleDefinition*` 基类 | 删除 `CapabilityId`，保留唯一 `RuleId` |
| 目录校验 | `src/NLISSN.Application/Catalog/RuleCatalog.cs`；`src/NLISSN/Composition/RuleRegistry.cs` | 校验全局唯一 `RuleId` |
| 图身份 | `src/NLISSN.Rule/RuleGraph.cs`；`src/NLISSN.Application/Analysis/RulePipeline.cs`；`RuleGraphAnalysisExecutor.cs` | 使用结构化 `(RuleKind, RuleId)`，消除字符串反解析 |
| 阶段执行 | `MarkingEngine.cs`；`PropagationEngine.cs`；`PropagationFixedPointExecutor.cs`；`MarkLiftingEngine.cs`；`DecisionModel.cs` | 继续使用 `RuleId` 传递生产者归因 |
| 结果与证据 | `MarkRecord.cs`；`PropagatedMarkRecord.cs`；`LiftedMarkRecord.cs`；`AnalysisEvidence.cs`；`AnalysisValidationReport.cs` | 保留 `RuleId` 字段，不增加第二身份 |
| 验证 | `RuleBindingValidator.cs`；`DecisionBindingValidator.cs`；`RuleStructureContract.cs` | 直接比较结构化节点中的 `RuleId` |
| 传播去重 | `PropagationFactKey.cs`；`PropagationFixedPointExecutor.cs` | 保留 `RuleId` 作为生产者维度 |
| rewrite 工件 | `RewritePlanModel.cs`；`RewritePlanArtifactService.cs`；`RewritePlanReplayService.cs`；`RuleDiffCategory.cs`；`CategoryDiffArtifactService.cs` | 以新 `RuleId` 分类，旧 schema 直接拒绝 |
| 具体规则 | `src/NLISSN.Rules/Mark/**`、`Propagate/**`、`Lift/**`、`Propose/**` | 用有效 `CapabilityId` 替换旧 `RuleId` 值并删除 `CapabilityId` 声明 |
| 测试 | `tests/NLISSN.ContractTests/**`；`HostTests/**`；`UnitTests/**`；`PerformanceTests/**` | 先红后绿，覆盖身份、图、工件、DOP 和输出等价 |
| 文档 | 本文件和旧 CapabilityId 文档 | 标记旧方案废弃，保留本文件为当前执行入口 |

实现时必须重新执行以下扫描，不能只按本表机械替换：

~~~powershell
rg -n --glob '*.cs' 'CapabilityId|\bRuleId\b' .\src .\tests
rg -l --glob '*.cs' 'CapabilityId' .\src .\tests | Sort-Object
~~~

`RuleDefinition*`、`RuleNodeId` 等类型名不是身份字段，不能因为包含 `Rule` 就机械改名。

## 4. 分步执行

### Task 0：冻结工作区边界和迁移基线

**Files:**

- Read: `Context/AGENTS.md`、`Context/progress.md`、`Context/feature_list.json`、`Miscellaneous/init.ps1`、`docs/harness-runtime.md`、`src/NLISSN/AGENTS.md`、`tests/AGENTS.md`、`docs/AGENTS.md`。
- Create: `tests/NLISSN.ContractTests/Identity/RuleIdentityContractTests.cs`。
- Create: `tests/NLISSN.ContractTests/Identity/RuleIdentitySnapshot.json`（仅测试基线，不进入生产运行时）。

#### Step 0.1：保存状态并运行启动检查

Run:

~~~powershell
git status --short
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
pwsh -File .\Miscellaneous\init.ps1
~~~

Expected：

- 既有用户改动仍存在，未执行覆盖性 git 操作；
- `Miscellaneous/init.ps1` 的 NLISSN 健康检查通过；
- 如果 restore 或环境再次阻塞，记录“已读源码确认契约，但未完成端到端运行验证”，不能把静态扫描写成运行验证。

#### Step 0.2：收集双字段快照

Run:

~~~powershell
rg -n --glob '*.cs' 'CapabilityId|\bRuleId\b' .\src .\tests
rg -l --glob '*.cs' '\bCapabilityId\b' .\src\NLISSN.Rules | Sort-Object
~~~

对每个具体规则记录：阶段、完整类型名、迁移前有效 `CapabilityId`、迁移前 `RuleId` 和目标 `RuleId`。目标值必须满足：

~~~text
TargetRuleId = EffectiveCapabilityId
~~~

特别核对：

- `mark.unreachable-method` 与 `propose.unreachable-method` 是两个不同的目标 `RuleId`；
- 不把 `DEL-DEAD-001` 搬到任何新规则身份字段；
- 当前已显式覆盖 `CapabilityId` 的规则保留其字面值，只改变属性名；
- 当前 `RuleId => CapabilityId` 的规则使用其有效 `CapabilityId` 字面值，不保留别名。

把这个映射保存为测试基线或审查附件。它不能成为生产运行时的旧 ID 映射表。

#### Step 0.3：先写目标契约测试

在 `RuleIdentityContractTests.cs` 添加反射型接口合同测试，避免在删除 `CapabilityId` 后测试自身无法编译：

~~~csharp
[Fact]
public void IRuleDefinition_ExposesOnlyRuleIdAsRuleIdentity()
{
    var propertyNames = typeof(IRuleDefinition)
        .GetProperties()
        .Select(property => property.Name)
        .ToHashSet(StringComparer.Ordinal);

    Assert.Contains(nameof(IRuleDefinition.RuleId), propertyNames);
    Assert.DoesNotContain("CapabilityId", propertyNames);
}
~~~

同时增加目标行为合同：默认规则目录的 `RuleId` 非空且全局唯一；每个具体规则的 `RuleId` 等于基线中的目标值；规则实现没有独立的 `CapabilityId` 属性。

Run:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleIdentityContractTests"
~~~

Expected：当前源码按预期失败，因为 `IRuleDefinition` 仍然同时暴露两个属性或默认规则仍未完成目标值迁移。记录失败原因，再继续。

#### Step 0.4：Checkpoint

建议提交边界：

~~~text
test: freeze RuleId-only identity baseline
~~~

该 checkpoint 只建立测试基线，不删除生产字段。

### Task 1：收敛接口、阶段基类和具体规则声明

**Files:**

- Modify: `src/NLISSN.Rule/IRuleDefinition.cs`。
- Modify: `src/NLISSN.Core/Marking/RuleDefinitionMark.cs`。
- Modify: `src/NLISSN.Core/Propagation/RuleDefinitionPropagate.cs`。
- Modify: `src/NLISSN.Core/Lifting/RuleDefinitionLift.cs`。
- Modify: `src/NLISSN.Core/Decision/RuleDefinitionPropose.cs`。
- Modify: all concrete rule files returned by `rg -l --glob '*.cs' '\bCapabilityId\b' .\src\NLISSN.Rules`.

#### Step 1.1：删除接口和基类中的 `CapabilityId`

保留 `RuleId`、`InputCardinality`、`Consumes` 和 `Produces`。四个阶段基类继续保留 `RequiredCapabilities`、`Name`、阶段专属合同和执行方法，不改变 CPG capability 需求。

目标结果：

~~~text
IRuleDefinition                 只包含 RuleId 和已有执行合同
RuleDefinitionMark              只声明 abstract RuleId
RuleDefinitionPropagate         只声明 abstract RuleId
RuleDefinitionLift              只声明 abstract RuleId
RuleDefinitionPropose           只声明 abstract RuleId
~~~

#### Step 1.2：迁移具体规则值

对于每个具体规则：

1. 删除 `CapabilityId` 属性；
2. 把迁移前有效 `CapabilityId` 的字面值写入 `RuleId`；
3. 删除 `RuleId => CapabilityId` 这种别名实现；
4. 不使用类名、文件名、注册序号或自动生成值替代稳定字面值；
5. 检查所有具体规则的目标值全局唯一。

示例：

~~~csharp
// 迁移前
public override string CapabilityId { get; } = "mark.unreachable-method";
public override string RuleId { get; } = "DEL-DEAD-001";

// 迁移后
public override string RuleId { get; } = "mark.unreachable-method";
~~~

#### Step 1.3：先编译声明层

Run:

~~~powershell
dotnet build .\src\NLISSN.Rule\NLISSN.Rule.csproj --no-restore -v:minimal
~~~

Expected：接口和四个阶段基类不再产生 `CapabilityId` 合同错误；后续项目可能仍因消费者未迁移而失败，这是预期的编译红灯。

#### Step 1.4：Checkpoint

~~~text
refactor: make RuleId the sole rule declaration identity
~~~

### Task 2：改造规则目录和结构化图节点

**Files:**

- Modify: `src/NLISSN.Application/Catalog/RuleCatalog.cs`。
- Modify: `src/NLISSN.Rule/RuleGraph.cs`。
- Modify: `src/NLISSN.Application/Analysis/RulePipeline.cs`。
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`。
- Modify: `src/NLISSN.Core/Marking/MarkingEngine.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationEngine.cs`。
- Modify: `src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`。
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`。

#### Step 2.1：把目录校验切换到 `RuleId`

`RuleCatalog.ValidateRules` 必须校验：

~~~csharp
var ruleOwners = new Dictionary<string, Type>(StringComparer.Ordinal);

foreach (var rule in rules)
{
    if (string.IsNullOrWhiteSpace(rule.RuleId))
    {
        throw new InvalidOperationException(
            $"Rule '{rule.GetType().FullName}' has a blank RuleId.");
    }

    if (!ruleOwners.TryAdd(rule.RuleId, rule.GetType()))
    {
        throw new InvalidOperationException(
            $"Duplicate RuleId '{rule.RuleId}' in rules " +
            $"'{ruleOwners[rule.RuleId].FullName}' and '{rule.GetType().FullName}'.");
    }
}
~~~

不要把唯一性降级为 `(RuleKind, RuleId)`；本计划要求扁平工件和结果仅凭 `RuleId` 仍能识别生产者。

#### Step 2.2：结构化 `RuleNodeId`

删除只接受 `string value` 的构造函数，改为接受 `RuleKind kind, string ruleId`。保留 `Value` 作为展示文本和 `For` 工厂，但不允许任何生产代码或测试从 `Value` 反解析字段。

替换以下模式：

~~~csharp
node.NodeId.Value.IndexOf(':')
node.NodeId.Value["Mark:".Length..]
node.NodeId.Value["Propagate:".Length..]
node.NodeId.Value.StartsWith("Propose:", StringComparison.Ordinal)
~~~

为：

~~~csharp
node.NodeId.RuleId
node.NodeId.Kind == RuleKind.Mark
node.NodeId.Kind == RuleKind.Propagate
node.NodeId.Kind == RuleKind.Propose
~~~

#### Step 2.3：保留图构造顺序和依赖关系

在 `RulePipeline`、`RuleGraphAnalysisExecutor`、Marking、Propagation、Lifting 和 Decision 中仅替换身份来源，不改变：

- Mark → Propagate → Lift → Propose 的阶段顺序；
- contract graph 的 producer/consumer 关系；
- marker source node 的建立方式；
- 固定点区域、DOP、稳定排序和禁用规则节点；
- `RuleSemanticTag` 的匹配逻辑。

#### Step 2.4：运行图身份定向测试

Run:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleGraph"
~~~

Expected：结构化节点相等性、依赖边和阶段筛选通过；任何仍依赖冒号字符串切割的代码必须在本任务内修复，不得通过保留解析器掩盖。

#### Step 2.5：Checkpoint

~~~text
refactor: make RuleNodeId structural
~~~

### Task 3：保持结果、证据、传播去重和验证模型的 `RuleId` 归因

**Files:**

- Modify as required: `src/NLISSN.Core/Marking/MarkRecord.cs`。
- Modify as required: `src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`。
- Modify as required: `src/NLISSN.Core/Lifting/LiftedMarkRecord.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationFactKey.cs`。
- Modify: `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs`。
- Modify: `src/NLISSN.Core/Decision/AnalysisEvidence.cs`。
- Modify: `src/NLISSN.Core/Validation/AnalysisValidationReport.cs`。
- Modify: `src/NLISSN.Core/Validation/RuleBindingValidator.cs`。
- Modify: `src/NLISSN.Core/Validation/DecisionBindingValidator.cs`。
- Modify: `src/NLISSN.Rule/RuleStructureContract.cs`。

#### Step 3.1：确认结果模型不新增第二身份

这些模型继续使用 `RuleId`：

~~~text
MarkRecord.RuleId
PropagatedMarkRecord.RuleId
LiftedMarkRecord.RuleId
DecisionUnit.RuleId
RuleDecision.RuleId
ValidationIssue.RuleId
AnalysisEvidenceNode.RuleId
PropagationFactKey.RuleId
~~~

不要把这些字段改为 `CapabilityId`，也不要在它们中同时添加两个字段。改变的是生产者写入的值，而不是结果模型的字段名称。

#### Step 3.2：保持传播事实键稳定且不冲突

`PropagationFactKey` 继续使用：

~~~text
RuleId + FilePath + SpanStart + SpanLength + RawKind + SemanticTag
~~~

验证两个不同具体规则即使命中同一 span 和相同语义标签，也不会因错误复用旧 `RuleId` 而被合并。全局 `RuleId` 唯一性是这条不变量的前提。

#### Step 3.3：验证输出来源时使用结构化节点

`RuleBindingValidator` 直接比较：

~~~csharp
string.Equals(mark.RuleId, node.NodeId.RuleId, StringComparison.Ordinal)
~~~

阶段检查使用 `node.Kind`。错误 key 可以继续使用 `node.NodeId.Value` 作为诊断展示，但不能从它恢复规则来源。

#### Step 3.4：保持证据 key 的组成和排序

`AnalysisEvidence` 继续将 `RuleId`、`AnalysisEvidenceKind`、`EvidenceAnchor` 和 summary 作为证据键组成部分。不得把 `CapabilityId` 或旧 `DEL-*` 值作为回退输入；不得改变证据节点排序、预算截断和 DOP 归并规则。

### Task 4：迁移 rewrite plan、diff category 和 CLI 工件

**Files:**

- Modify: `src/NLISSN.Core/Rewrite/RewritePlanModel.cs`（保持 `RuleId` 字段）。
- Modify: `src/NLISSN/Artifacts/RewritePlanArtifactService.cs`。
- Modify: `src/NLISSN/Artifacts/RewritePlanReplayService.cs`。
- Modify: `src/NLISSN/Artifacts/RuleDiffCategory.cs`。
- Modify: `src/NLISSN/Artifacts/CategoryDiffArtifactService.cs`。
- Modify: `src/NLISSN/Hosting/CommandHost.cs`。
- Modify: `src/NLISSN.Application/Analysis/PostRewriteCleanupService.cs`（若编译清单显示需要）。

#### Step 4.1：升级 rewrite plan schema

将 `RewritePlanArtifactService.SchemaVersion` 从 `1` 提升为 `2`。新 JSON 继续使用：

~~~json
{
  "ruleId": "propose.unreachable-method"
}
~~~

不得写出：

~~~json
{
  "capabilityId": "...",
  "legacyRuleId": "..."
}
~~~

`ReadAndValidate` 遇到旧 schema 必须在读取入口抛出确定性错误：

~~~text
Rewrite-plan manifest schema is not supported.
~~~

不能自动转换、忽略旧字段或以旧 ID 回退查找。

#### Step 4.2：迁移 diff category key

`RuleDiffCategoryRegistry` 的字典 key 必须改成新的 proposal `RuleId`。映射来源只能是 Task 0 的目标快照，不得依赖字符串前缀推断 category。

例如：

~~~text
旧：DEL-DEAD-001
新：propose.unreachable-method
~~~

`Resolve` 只接受新值；未知或空 `RuleId` 继续失败。 `RewritePlanReplayService` 仍按 `edit.RuleId` 分组和解析 category。

#### Step 4.3：更新 CLI 操作来源

`CommandHost`、`CategoryDiffArtifactService` 和 replay 逻辑继续读取 `RuleId`。不要在 CLI 层重新引入 `CapabilityId` 或旧 ID 兼容分支。

### Task 5：更新测试合同并覆盖失败路径

**Files:**

- Create/Modify: `tests/NLISSN.ContractTests/Identity/RuleIdentityContractTests.cs`。
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`。
- Modify: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`。
- Modify: `tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs`。
- Modify: `tests/NLISSN.HostTests/Artifacts/RuleDiffCategoryTests.cs`。
- Modify: `tests/NLISSN.HostTests/Artifacts/CategoryDiffArtifactServiceTests.cs`。
- Modify: `tests/NLISSN.HostTests/Rewrite/RewritePlanPersistenceTests.cs`。
- Modify: all tests returned by `rg -l --glob '*.cs' '\bCapabilityId\b|\bRuleId\b' .\tests` where the expected production identity changed.

#### Step 5.1：目录合同

增加或更新以下行为测试：

- 空白 `RuleId` 被拒绝；
- 同一目录中重复 `RuleId` 被拒绝，即使两个测试规则属于不同阶段；
- 所有默认规则的 `RuleId` 全局唯一；
- 默认规则 `RuleId` 集合等于 Task 0 的规范快照；
- `CapabilityId` 不存在于 `IRuleDefinition` 和生产规则定义。

#### Step 5.2：图合同

覆盖以下行为：

- 相同 `(RuleKind, RuleId)` 得到相同 `RuleNodeId`；
- 不同 `RuleKind` 得到不同 `RuleNodeId`；
- `RuleNodeId.Value` 变化不影响内部字段比较；
- graph compiler、validator 和 executor 不依赖 `Value` 反解析；
- 跨阶段旧的 `DEL-DEAD-001` 不再作为两个具体规则的身份。

#### Step 5.3：工件合同

覆盖以下行为：

- schema 2 写出 `ruleId`；
- schema 2 不写 `capabilityId`；
- schema 1 manifest 在回放入口确定性失败；
- 新 proposal `RuleId` 可以解析 diff category；
- 未知 `RuleId` 不能静默归入默认 category；
- rewrite plan 的源码哈希、编辑排序和回放内容不因字段迁移而改变。

#### Step 5.4：更新现有硬编码断言

所有依赖旧 `DEL-*`、`CLR-*`、`PRIV-*` 规则值的测试都必须切换到 Task 0 生成的规范 `RuleId`。不能为了让旧测试通过而在生产代码中保留旧 ID 映射。

特别检查：

- `MarkRuleRegistryCoverageTests`；
- `PipelineComponentTests` 中的身份唯一性测试；
- `PropagationRuleExpansionTests`；
- `CpgExecutionMatrixTests`；
- `DirectoryAnalysisUseCaseTests`；
- `RewritePlanPersistenceTests`；
- `RuleDiffCategoryTests`。

### Task 6：验证 DOP、输出和 CLI 行为等价

#### Step 6.1：运行定向 Contract/Unit/Host 测试

Run:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
~~~

Expected：三个项目实际通过；如果失败是现有工作区改动或 restore 阻塞造成，明确记录失败边界，不得把未执行的测试写成通过。

#### Step 6.2：运行全量快速层

Run:

~~~powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
~~~

Expected：Unit、Contract 和脚本定义的 Fast 层全部通过。测试计数以命令实际输出为准，不在文档中预填旧计数。

#### Step 6.3：验证 DOP 等价

使用现有 CPG/删除规则矩阵或 Host fixture，分别执行 DOP 1 和较高 DOP（项目已有的 2/16 配置），比较：

~~~text
RuleId 集合
RuleNodeId 集合和边
seed / propagated / lifted marks
Decision 集合
Evidence 节点和边
RewritePlan edits
Diff category 输出
~~~

允许的差异只有日志耗时和并行测量值；规则结果、顺序、证据和 rewrite 输出必须一致。

#### Step 6.4：CLI smoke

Run:

~~~powershell
Push-Location .\Miscellaneous
dotnet run --project ..\src\NLISSN\NLISSN.csproj --no-build --no-restore
Pop-Location
~~~

Expected：使用仓库示例配置的 CLI 入口成功，生成的新工件含 `ruleId`，不含 `capabilityId`，旧 schema 回放失败。

### Task 7：全仓库清理、文档和最终验收

#### Step 7.1：身份字段扫描

Run:

~~~powershell
rg -n --glob '*.cs' '\bCapabilityId\b' .\src .\tests
rg -n --glob '*.cs' '\bRuleId\b' .\src .\tests
rg -n --glob '*.json' 'capabilityId|legacyRuleId' .\src .\tests
rg -n --glob '*.cs' 'NodeId\.Value.*(IndexOf|StartsWith|Substring)|NodeId\.Value\[(.*Mark:|.*Propagate:|.*Lift:)' .\src .\tests
~~~

Expected：

- 第一条对生产/测试 C# 无输出；
- 第二条仍有 `RuleId` 结果、图和测试使用，这是目标状态；
- 第三条无新工件模型或序列化代码输出；
- 第四条无通过 `RuleNodeId.Value` 反解析阶段的代码。

静默扫描本身不是完成证明，必须与 build、test、CLI 和输出快照结合判断。

#### Step 7.2：标记旧文档并回读

将旧的 CapabilityId-only 设计、执行和研究文档标记为历史方案，顶部明确写出：

~~~text
本方案已被 2026-09-08 的 RuleId-only 决策取代，不得按本文执行。
~~~

并链接本文件。不要删除历史研究，避免丢失此前的取舍证据。

#### Step 7.3：harness 和 diff 检查

Run:

~~~powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check -- .\docs\plans .\src .\tests
~~~

Expected：harness consistency 和 diff whitespace 检查通过。 `bin/`、`obj/` 等生成物不能被当成源码变更证据。

#### Step 7.4：最终完成条件

只有以下条件全部满足才可以把迁移标记为完成：

- `IRuleDefinition`、四个阶段基类和具体规则中不存在 `CapabilityId`；
- 所有规则定义和规则结果只使用 `RuleId` 作为规范规则来源；
- 默认规则目录 `RuleId` 全局唯一且与规范快照一致；
- `RuleCatalog` 校验的是 `RuleId`；
- `RuleNodeId` 的内部身份是 `(RuleKind, RuleId)`，没有字符串反解析；
- `RuleSemanticTag` 仍只负责事实端口，不承担规则身份；
- `EvidenceAnchor` 仍只负责命中位置；
- 传播去重、证据、决策、rewrite 和 diff category 均使用新 `RuleId`；
- 旧 rewrite schema 被拒绝，新工件只写 `ruleId`；
- DOP 1 与较高 DOP 的图、证据、决策和 rewrite 输出一致；
- 定向测试、Fast 测试、必要的 Performance/CLI smoke 和 harness 检查都有实际输出证据；
- 旧 CapabilityId-only 文档已标记为历史方案；
- `Context/progress.md` 只记录当前事实、验证边界和下一步，`Context/feature_list.json` 只有在实际开始实现时才更新对应 feature 状态。

## 5. 停止条件和风险处理

遇到以下情况必须暂停，不得通过增加字段或兼容别名绕过：

1. 发现仓库外部使用 `CapabilityId`；
2. 当前有效 `CapabilityId` 不是全局唯一；
3. 某个旧 `RuleId` 无法确定对应的规范 `CapabilityId`；
4. 规则 category 只能通过旧字符串推断，且没有明确的目标规则声明；
5. 旧 rewrite plan 必须在同一运行中继续支持；
6. 结构化 `RuleNodeId` 改动导致 DOP 1/16 结果不一致；
7. 编译失败来自工作区已有修改且无法在不覆盖它们的前提下隔离；
8. restore 阻塞导致无法完成端到端验证。

停止时应报告：已完成的静态源码核对、最后一个通过的验证、具体阻塞错误和未验证范围。不得把“源码已替换”表述为“迁移完成”。

## 6. 建议提交边界

如果项目决定按小提交执行，建议顺序为：

~~~text
test: freeze RuleId-only identity baseline
refactor: make RuleId the sole rule declaration identity
refactor: make RuleNodeId structural
refactor: propagate canonical RuleId through results and validation
refactor: migrate rewrite plan and diff category identities
test: cover RuleId-only artifacts and DOP equivalence
docs: supersede CapabilityId-only identity plan
~~~

每个提交前只提交自己负责的文件，先运行该任务的最小验证，再进入下一任务。不要使用重置或整目录 checkout 来清理与本任务无关的工作区变更。
