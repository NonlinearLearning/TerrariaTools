# 语义能力即身份执行提案

> 状态：提案，未实施。

## 目标

移除 `src/Rules/RuleIds/` 与 `src/Rules/RuleMetadata/` 的镜像常量机制。每条规则以稳定的语义能力标识自身，`RuleSet` 继续作为唯一的规则发现与组合入口。

完成后，类名、文件名、实现拆分和 RuleSet 排列不决定规则身份；规则记录、禁用配置、日志与持久化使用语义能力标识。

## 当前问题

以 `DeleteSObject` 规则族为例：

1. `DeleteSObjectRuleMetadata` 保存 `GroupKey` 与所有 ID 字符串。
2. `DeleteSObjectRuleIds` 逐项转发同一批常量。
3. 每个规则再从 `RuleIds` 读取 `RuleId` 和 `GroupKey`。
4. `RuleSet` 已显式列出规则，却没有提供规则身份的正式边界。

这使一个语义变更需要同时维护类型名、两个常量表、规则实现、RuleSet 和测试。`GroupKey` 同时承担运行期数据流隔离与规则族命名，含义混杂。

## 目标模型

```text
RuleSet
  └─ RuleDefinition
       └─ RuleDescriptor
            ├─ CapabilityId        规则完成的语义能力
            ├─ Stage               Mark / Propagate / Lift / Propose
            ├─ ExecutionGroupKey   阶段产物的运行期连通域
            └─ LegacyRuleId        仅用于旧日志/持久化兼容，迁移后可删除

RuleCatalog
  └─ 从已启用的 RuleSet 建立 CapabilityId -> RuleBinding 索引并校验
```

建议的类型边界：

```csharp
public readonly record struct CapabilityId(string Value);

public sealed record RuleDescriptor(
  CapabilityId Capability,
  RuleStage Stage,
  string ExecutionGroupKey,
  string? LegacyRuleId = null);

public sealed record RuleBinding(
  string RuleSetId,
  RuleDescriptor Descriptor,
  Type ImplementationType);
```

`CapabilityId` 描述能力，不描述实现或编号。例如：

```text
match.sobject.identifier
propagate.sobject.symbol-reference
propose.class.parameter-shrink
```

一个激活流水线内，每个 `CapabilityId` 只能绑定一个实现。实验替代实现必须通过 profile 或 RuleSet 显式选择，不能与正式实现同时注册同一能力。

`ExecutionGroupKey` 保留当前 `DEL-SOBJ`、`DEL-CLASS` 等运行语义；它不再被用作配置组名。`RuleSet.Id` 表示配置与组合边界，例如 `s-object`、`class`。

## 非目标

- 本轮不改变 Mark、Propagate、Lift、Propose 的判定、顺序或 rewrite 结果。
- 不引入运行期程序集扫描。
- 不用随机 GUID 替换可读的稳定能力标识。
- 不在迁移中同时重写 CPG、Host 日志或目录分析算法。

## 实施步骤

### 阶段 0：冻结当前身份与输出基线

**文件：**

- 修改：`tests/Roslyn Prototype.HostTests/Application/PipelineComponentTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Architecture/ArchitectureBoundaryTests.cs`
- 新建：`tests/Roslyn Prototype.UnitTests/Rules/RuleIdentityContractTests.cs`

**动作：**

1. 枚举 `DefaultRuleSets.Create()` 的所有四阶段规则，快照当前 `RuleId`、`GroupKey`、类型名和所属 RuleSet。
2. 加入回归测试，锁定当前 RuleId/GroupKey 集合、唯一性和每阶段数量。
3. 记录 CLI `disabled-rule-types`、日志 telemetry、`MarkRecord`、`PropagatedMarkRecord`、`LiftedMarkRecord`、`DecisionUnit` 对 RuleId 的读取点。

**验收门：**

- 当前 RuleSet 内所有 RuleId 唯一。
- 快照测试能指出新增、删除、变更的具体规则。
- 不开始生产迁移，直到基线测试通过。

### 阶段 1：引入身份值对象和 descriptor 契约

**文件：**

- 新建：`src/Rules/Identity/CapabilityId.cs`
- 新建：`src/Rules/Identity/RuleDescriptor.cs`
- 新建：`src/Rules/Identity/RuleStage.cs`
- 修改：`src/RoslynPrototype/RuleServices/RuleDefinition.cs`
- 测试：`tests/Roslyn Prototype.UnitTests/Rules/RuleDescriptorTests.cs`

**动作：**

1. 定义 `CapabilityId`，构造时拒绝空白、首尾空格与非小写点分词格式。
2. 定义 `RuleDescriptor`，包含 `Capability`、`Stage`、`ExecutionGroupKey` 和过渡期 `LegacyRuleId`。
3. 四个 `RuleDefinition*` 基类增加抽象 `Descriptor`；过渡期保留 `RuleId`、`GroupKey` 只读兼容属性，分别投影 `Descriptor.LegacyRuleId ?? Descriptor.Capability.Value` 与 `Descriptor.ExecutionGroupKey`。
4. 让基类或 stage 专用基类校验 descriptor 的 `Stage` 与规则基类匹配。

**验收门：**

- 无规则能构造空或非法能力标识。
- Stage 不匹配时构造失败并给出规则类型与 capability。
- 既有运行期代码仍可通过兼容属性读取 RuleId 与 GroupKey。

### 阶段 2：建立 RuleCatalog，并把唯一性校验放在组合边界

**文件：**

- 新建：`src/Rules/Identity/RuleCatalog.cs`
- 修改：`src/Rules/RuleSet.cs`
- 修改：`src/Host/RuleRegistry.cs`
- 测试：`tests/Roslyn Prototype.UnitTests/Rules/RuleCatalogTests.cs`
- 测试：`tests/Roslyn Prototype.HostTests/Application/PipelineComponentTests.cs`

**动作：**

1. `RuleCatalog.Create(IEnumerable<IRuleSet>)` 只枚举传入 RuleSet 的成员，不读取程序集类型。
2. 生成 `RuleBinding`，记录 RuleSet、descriptor 和实现类型。
3. 在构建时拒绝以下情况：重复 `CapabilityId`、重复 `LegacyRuleId`、同一实例被多个 RuleSet 注册、RuleSet Id 重复、descriptor stage 与实际阶段不一致。
4. `RuleRegistry.CreateRules(...)` 先构建 catalog，再由 catalog 生成 ` RulePipeline`。
5. 错误消息必须包括 capability、两个 RuleSet Id、两个实现类型与阶段。

**验收门：**

- 未被 RuleSet 显式登记的规则永远不会出现在 catalog 或 pipeline。
- 两个实现注册同一 capability 的测试稳定失败。
- 默认 RuleSet 的 catalog 与阶段 0 基线的规则数量一致。

### 阶段 3：迁移 SObject RuleSet，验证完整链路

**文件：**

- 修改：`src/Rules/Implementations/Mark/TargetAtomicMarkRules.cs`
- 修改：`src/Rules/Implementations/Propagate/TargetPropagationRules.cs`
- 修改：`src/Rules/Implementations/Lift/SObjectExpressionHostLiftingRule.cs`
- 修改：`src/Rules/Implementations/Lift/SObjectIfStructureLiftingRule.cs`
- 修改：`src/Rules/Implementations/Lift/SObjectSwitchStructureLiftingRule.cs`
- 修改：`src/Rules/Implementations/Propose/LogicalExpressionProposalRule.cs`
- 修改：`src/Rules/Implementations/Propose/IfStructureProposalRule.cs`
- 修改：`src/Rules/Implementations/Propose/ControlStructureRemovalProposalRule.cs`
- 修改：`src/Rules/Implementations/Propose/DefaultRemovalProposalRule.cs`
- 删除：`src/Rules/RuleIds/DeleteSObjectRuleIds.cs`
- 删除：`src/Rules/RuleMetadata/DeleteSObjectRuleMetadata.cs`

**动作：**

1. 为每条 SObject 规则声明其 `RuleDescriptor`；能力标识按 `match.sobject.*`、`propagate.sobject.*`、`lift.sobject.*`、`propose.sobject.*` 命名。
2. `LegacyRuleId` 保留当前 `DEL-SOBJ-*` 字符串，确保现有记录与禁用路径可迁移。
3. 将所有 SObject 规则改为只读取 descriptor，不再引用 `RuleIds` 或 `RuleMetadata`。
4. 调整禁用参数解析：接受 capability；旧类型名参数保留一次过渡映射并产生明确警告。

**验收门：**

- SObject 的 seed mark、propagation、lift、proposal 和 rewrite 结果与阶段 0 相同。
- `DEL-SOBJ-*` 旧日志字段仍可解析。
- `disabled-rule-types` 旧参数和新的 capability 参数都能禁用同一条规则。

### 阶段 4：迁移 Class 与其余 RuleSet

**文件：**

- 修改：`src/Rules/Implementations/Mark/TypeMarkRules.cs`
- 修改：`src/Rules/Implementations/Propagate/Class*.cs`
- 修改：`src/Rules/Implementations/Lift/TypeLiftingRules.cs`
- 修改：`src/Rules/Implementations/Propose/TypeProposalRules.cs`
- 修改：`src/Rules/Implementations/Mark/UnreachableMethodMarkRule.cs`
- 修改：`src/Rules/Implementations/Mark/UnreferencedMethodMarkRule.cs`
- 修改：`src/Rules/Implementations/Mark/ClearUnusedInterfaceImplementationRule.cs`
- 修改：`src/Rules/Implementations/Mark/PrivatizeInternalOnlyPublicMethodRule.cs`
- 修改：对应 propose 文件
- 删除：`src/Rules/RuleIds/`
- 删除：`src/Rules/RuleMetadata/`

**动作：**

1. 依次迁移 `ClassRuleSet`、`UnreachableMethodRuleSet`、`UnreferencedMethodRuleSet`、`ClearUnusedInterfaceImplementationRuleSet`、`PrivatizeInternalOnlyPublicMethodRuleSet`。
2. 每迁移一个 RuleSet，立即删除其旧常量源与转发层，避免双源长期共存。
3. 目录分析中直接写死的 `DEL-UNREF-METHOD` 改为通过 catalog 查询对应 capability 的 execution group。

**验收门：**

- 每个 RuleSet 迁移后，基线规则数量、旧 RuleId 和执行组都保持一致。
- 所有 `RuleIds`、`RuleMetadata` 的生产引用归零。
- 目录分析、禁用规则、telemetry、diff/rewrite 测试保持结果一致。

### 阶段 5：完成外部契约迁移与守护

**文件：**

- 修改：`src/Host/ ApplicationOptions.cs`
- 修改：`src/Host/ CommandHost.cs`
- 修改：`docs/quick-start.md`
- 修改：`docs/cli-reference.md`
- 修改：`docs/developer-guide.md`
- 修改：`tests/Roslyn Prototype.ContractTests/Architecture/ArchitectureBoundaryTests.cs`
- 新建：`tests/Roslyn Prototype.ContractTests/Rules/RuleCatalogContractTests.cs`

**动作：**

1. 将公开禁用入口定为 capability，例如 `--disabled-capabilities match.sobject.member-access`。
2. 保留旧类型名选项一个已定义的弃用窗口；日志显示 capability 和 legacy ID，方便交叉排障。
3. 当不再需要旧格式后，删除 `LegacyRuleId` 与类型名兼容解析。
4. 架构测试禁止重新创建 `RuleIds`、`RuleMetadata`、基于程序集扫描的自动注册。

**验收门：**

- 仅已注册 RuleSet 的能力可被列出、启用或禁用。
- `CapabilityId`、RuleSet Id、stage、execution group 在 contract 测试中唯一且可追踪。
- CLI 文档与实现使用同一 capability 示例。

## 验证顺序

每个阶段按以下顺序执行，依赖项串行运行：

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\Host\Host.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test .\tests\Roslyn Prototype.UnitTests\Roslyn Prototype.UnitTests.csproj --no-restore --filter "FullyQualifiedName~RuleDescriptor|FullyQualifiedName~RuleCatalog"
dotnet test .\tests\Roslyn Prototype.HostTests\Roslyn Prototype.HostTests.csproj --no-restore --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~LogicalConditionMarkAnalyzerTests|FullyQualifiedName~PropagationRuleExpansionTests|FullyQualifiedName~DecisionStructureValidationTests"
dotnet test .\tests\Roslyn Prototype.ContractTests\Roslyn Prototype.ContractTests.csproj --no-restore --filter "FullyQualifiedName~ArchitectureBoundaryTests|FullyQualifiedName~RuleCatalogContractTests"
pwsh -File .\scripts\check-harness-consistency.ps1
```

每次规则族迁移还要运行其最小 owning test；SObject 先运行 mark 与 propagation 测试，Class 先运行 pipeline 和 directory/parameter shrink 测试。共享 `Build` 输出时，构建与测试必须串行。

## 完成条件

- `RuleIds/` 和 `RuleMetadata/` 已删除，且不存在遗留生产引用。
- 每条激活规则拥有一个合法、全局唯一的 `CapabilityId`。
- 每个能力只通过一个 RuleSet 的显式成员进入默认流水线。
- RuleCatalog 能诊断重复能力、重复旧 ID、阶段错配和未登记规则。
- 规则结果、执行组隔离、rewrite 输出与阶段 0 基线一致。
- Host、Unit、Contract 定向测试和 harness consistency 检查全部通过。

## 风险与约束

- 能力命名是外部契约；新增时必须先确认语义边界，不能从类名机械转换。
- `LegacyRuleId` 删除前，需要确认日志、持久化记录、禁用参数和外部脚本的迁移窗口。
- Class RuleSet 有大量 proposal 规则，迁移应分小批次进行；一次只迁移一个 RuleSet，并在每批后比较基线。
- 提案不授权删除旧兼容字段；每个删除点必须由对应的兼容测试和消费方审计证明。
