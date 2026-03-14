# TR 项目快速试跑基线

目标项目：

- `D:\lodes\TR\Backup\New1.27\TR`

这份文档定义首次快速落地试跑时必须覆盖的样本面，以及每组样本需要看的结果。

## 1. 首轮固定样本

### A. Network / Social 注册

固定样本文件：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Social.Base\NetSocialModule.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Social.Steam\NetSocialModule.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Social.WeGame\NetSocialModule.cs`

重点确认：

- 注册类型不进入 `class-mark`
- 框架入口方法不进入 `function-mark`
- 同类型内无引用私有辅助方法仍允许进入 `function-mark`

### B. WorldBuilding 框架入口

固定样本文件：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.WorldBuilding\GenPass.cs`

重点确认：

- `Apply` / `ApplyPass` 等已知入口不误删
- 普通业务类同名方法不被误保护

### C. ItemDropRules 组合器

固定样本文件：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules\LeadingConditionRule.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules\ItemDropResolver.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules\IItemDropRule.cs`

重点确认：

- 被规则链持有的 rule node 类型不进入 `class-mark`
- 仅局部临时 list 装配的不被误保护

### D. Event / Delegate / Callback 容器

固定样本文件：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Achievements\ConditionsCompletedTracker.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria\DelegateMethods.cs`

重点确认：

- handler 方法不进入 `function-mark`
- 局部、不逃逸的 lambda 不被误保护

### E. UI / Creative 生命周期入口

固定样本文件：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.Creative\CreativePowerManager.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.Creative\CreativeUI.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.UI.Elements\UICreativePowerButton.cs`

重点确认：

- 仅位于已知框架类型结构中的入口方法受保护
- 普通同名私有方法仍可删除

## 2. 每组样本固定检查项

每组样本都必须输出并人工确认三类结果：

1. 误删样例
2. 误保护样例
3. rewrite / report / generated artifacts 是否一致

## 3. 首次试跑约束

首次只允许小范围试跑，不做全项目批量改写。

建议顺序：

1. 单文件
2. 小目录
3. 单一子系统
4. 多子系统组合

## 4. 已验证基线

已完成一轮低风险样本试跑：

- 输入目录：`D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules`
- 输出目录：
  - `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-rollout\itemdrop\analyze`
  - `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-rollout\itemdrop\plan`
  - `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-rollout\itemdrop\run`

观察结果：

- `AnalyzeOnly` 成功，`analysis.json` 与 `report.json` 一致
- `PlanOnly` 成功，`audit-plan.json` 为空计划，`report.json` 一致
- `Standard` 成功，`report.json` 中 `GeneratedArtifacts` 与磁盘实际输出一致
- 当前样本下 `PlannedChanges = 0`，但 `run` 仍输出完整 `rewritten/**` 镜像；这在首轮视为“产物一致但语义需后续确认”的已知现象
- 当前样本下 `WorkspaceLoadMode = SourceOnly`

已完成额外 4 组样本的 `AnalyzeOnly` / `PlanOnly` 校验：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Social.Base\NetSocialModule.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.WorldBuilding\GenPass.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Achievements\ConditionsCompletedTracker.cs`
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.Creative\CreativePowerManager.cs`

这些样本当前共同结论：

- `AnalyzeOnly` 成功
- `PlanOnly` 成功
- `WorkspaceLoadMode = SourceOnly`
- 当前样本下未观察到失败产物不一致

已完成第二个单文件 `Standard` 试跑：

- 输入文件：`D:\lodes\TR\Backup\New1.27\TR\Terraria.Achievements\ConditionsCompletedTracker.cs`
- 输出目录：`D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-rollout\event-delegate\run`

观察结果：

- `run` 成功
- 产物包括 `audit-plan.json`、`report.json`、`rewritten\ConditionsCompletedTracker.cs`
- `GeneratedArtifacts` 与磁盘输出一致
- 单文件样本在 `run` 模式下同样会生成 `rewritten\` 结果

已完成目录级试跑：

- `D:\lodes\TR\Backup\New1.27\TR\Terraria.Social.Base`
  - `AnalyzeOnly` 成功
  - `PlanOnly` 成功
  - `Standard` 成功
  - 目录级 `run` 会生成完整 `rewritten\**` 输出，且与 `GeneratedArtifacts` 一致
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.Creative`
  - `AnalyzeOnly` 成功
  - `PlanOnly` 成功
  - `Standard` 成功
- `D:\lodes\TR\Backup\New1.27\TR\Terraria.WorldBuilding`
  - `AnalyzeOnly` 成功
  - `PlanOnly` 成功
  - `Standard` 成功

当前目录级样本共同结论：

- 未观察到失败码或产物不一致
- 当前这些样本仍全部跑在 `WorkspaceLoadMode = SourceOnly`
- 目录级 `run` 与单文件 `run` 一样，在空计划或低变更场景下仍会输出 `rewritten\**` 镜像

当前目录级 `run` 的额外观察：

- `Terraria.GameContent.Creative`
  - `audit-plan.json` 为空计划
  - 仍输出完整 `rewritten\**`
- `Terraria.WorldBuilding`
  - 修复“表达式体属性 getter 未进入分析图”后重新执行 `PlanOnly`
  - `audit-plan.json` 已恢复为空计划
  - 之前的候选 `Terraria.WorldBuilding.WorldGenRange.ScaleValue(int)` 已消失
  - 当前结论：该候选属于分析缺口导致的误删，已被测试和真实样本共同回归覆盖

## 5. 通过标准

满足以下条件，才允许扩大试跑范围：

- 不出现明显误删框架入口
- 不出现大面积误保护导致计划近乎空白
- rewrite 失败时，磁盘产物与 `report.json` 一致
- 规则层当前最小保护集对上述 5 组样本都能给出可解释结果
