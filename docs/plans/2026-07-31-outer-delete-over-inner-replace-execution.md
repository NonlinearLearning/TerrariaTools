# 外层 Delete 覆盖内部 Replace 执行提案

## 目标

当一个已提出的 `Delete` 决策严格包含一个 `Replace` 决策时，保留外层
`Delete`。内部 `Replace` 不得使外层删除候选消失。现有覆盖合并随后选择
外层节点，并由最终决策过滤移除被覆盖的内部决策。

同一锚点上的 `Delete` / `Replace` 冲突不属于本次范围，保持现有处理。

## 已确认事实

`AHoverInteractionChecker.cs` 的 Build 121 运行产生了下列候选：

```text
Delete  LocalDeclarationStatement  bool flag4 = ...
Replace LogicalAndExpression       !Main.SmartCursorIsUsed && !PlayerInput.UsingGamepad
```

最终证据只保留了第二项，diff 因而写出：

```text
!Main.SmartCursorIsUsed && !PlayerInput.UsingGamepad
!Main.SmartCursorIsUsed
```

根因在 `RuleDecisionEngine.FilterCompetingAncestors`。它把包含 `Replace`
锚点的任何 `Delete` 候选过滤掉，包括严格外层的
`LocalDeclarationStatement`。见
`src/NLISSN.Core/Decision/DecisionModel.cs:494` 至 `:546`。

这条过滤与现有 `DefaultDecisionPolicy.MergeBySyntaxCoverage` 的规则相反：
后者已按祖先深度和 span 选择覆盖子节点的外层单元。见
`src/NLISSN.Core/Decision/DecisionModel.cs:172` 至 `:213`。

## 不变量

1. 规则已经产出的严格外层 `Delete` 仍是有效候选；内部 `Replace` 不能撤销它。
2. 外层 `Delete` 与内部 `Replace` 位于同一冲突域时，`DefaultDecisionPolicy`
   按语法覆盖选择外层 `Delete`。
3. 两者位于不同冲突域时，`FilterCoveredDecisions` 只保留覆盖范围更大的最终
   `Delete`。
4. 没有外层删除候选的逻辑表达式继续生成 `Replace`，保留短路规约行为。
5. 锚点完全相同的 `Delete` / `Replace` 继续使用当前的同锚点冲突语义；本次
   不能把该情况意外改成按注册顺序取胜。
6. 不增加 Propagate 到 `IfStatement` 的规则，也不改变 `TargetExpression`、
   `FlowLogicalExpression`、Lift payload 或 Rewrite API。

## 改动范围

- 修改：`src/NLISSN.Core/Decision/DecisionModel.cs`
- 修改：`src/NLISSN.Rules/Propose/IfStructureProposalRule.cs`
- 修改：`tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`

不修改 `LogicalExpressionPropagationRule`、`LogicalExpressionLiftingRule`、
`LogicalExpressionProposalRule` 或目录分析入口。

## 实施步骤

### 1. 先加入失败回归测试

在 `DecisionStructureValidationTests` 添加一个完整流水线测试，使用内联源码：

```csharp
bool flag4 = !smartCursorIsUsed && !PlayerInput.UsingGamepad;
if (!flag4) { return 1; }
return 0;
```

以 `--delete-class PlayerInput` 分析，并断言：

- 产生 `LocalDeclarationStatement` 的最终 `Delete`；
- 不存在该初始化器 `LogicalAndExpression` 的最终 `Replace`；
- 重写文本不含 `flag4` 和 `PlayerInput.UsingGamepad`；
- 对应 `if (!flag4)` 仍按现有 if 规则删除。

测试名：
`Analyze_ClassDerivedLogicalInitializerWithOuterDelete_DeletesLocalDeclaration`。

保留既有测试作为反向边界：

- `RuleDecisionEngine_PrefersReducibleLogicalHostInsideSameConflictDomain`：没有外层
  删除时仍为 `Replace`；
- `Analyze_ClassDerivedLogicalIf_PrefersIfDeleteOverLogicalReplacement`：外层 `if`
  删除继续覆盖条件规约。

先运行新测试，确认当前实现失败，且失败表现是最终决策包含逻辑 `Replace` 而缺少
局部声明 `Delete`。

### 2. 收窄候选过滤

修改 `FilterCompetingAncestors`：

- 保留同锚点 `Delete` 被同锚点 `Replace` 排除的现有分支；
- 删除“严格祖先 `Delete` 包含内部 `Replace` 即排除”的分支；
- 让严格外层 `Delete` 流入 `DefaultDecisionPolicy.MergeBySyntaxCoverage`；
- 保持输入、输出顺序和并发调度不变。

不要在此处新建按语法种类的白名单。该规则应适用于局部声明、表达式语句、块、
控制结构及未来能产生合法删除候选的外层节点。

### 3. 删除已失效的 if 特例

`PrefersOuterStructuralDelete` 仅为旧过滤器保留类删除派生的完整 `if` 删除。
在第 2 步后它不再参与决策：

- 从 `DecisionUnit` 及其构造函数移除该字段；
- 从 `DefaultDecisionPolicy.MergeIntoCoveringRoot` 移除传递；
- 从 `IfStructureProposalRule` 移除赋值和不再需要的 origin 判定；
- 删除 `FilterCompetingAncestors` 中依赖该字段的分支。

保留 `IfStructureProposalRule` 的 payload、完整 if 判定和其余 origin 信息。

### 4. 验证决策与改写边界

顺序执行：

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~DecisionComplexTests'
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
```

使用可用的 Build 121 源码副本，分别以 DOP 1 和 DOP 16 运行：

```powershell
dotnet run --project .\src\NLISSN\NLISSN.csproj --no-build -- <Build121Source> --delete-class PlayerInput --max-degree-of-parallelism 1 --cpg-max-degree-of-parallelism 1 --diff-out .\Build\121\outer-delete-dop1\diff --evidence-json .\Build\121\outer-delete-dop1\evidence.json
dotnet run --project .\src\NLISSN\NLISSN.csproj --no-build -- <Build121Source> --delete-class PlayerInput --max-degree-of-parallelism 16 --cpg-max-degree-of-parallelism 16 --diff-out .\Build\121\outer-delete-dop16\diff --evidence-json .\Build\121\outer-delete-dop16\evidence.json
```

比较两个 evidence 中的最终决策和 diff 内容。`AHoverInteractionChecker` 的验收点是
`flag4` 的 `LocalDeclarationStatement` 被删除，且没有只将其初始化器缩减为
`!Main.SmartCursorIsUsed` 的最终替换。

最后执行：

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

## 完成条件

- 新回归测试先失败、实现后通过。
- 既有纯逻辑替换和类删除 if 覆盖测试通过。
- 外层 `Delete` 覆盖任意严格内部 `Replace`，不依赖 `IfStatement` 特例。
- DOP 1 与 DOP 16 的最终决策和 diff 一致。
- Build 121 目标场景不再保留仅含 `!Main.SmartCursorIsUsed` 的 `flag4` 初始化器。
