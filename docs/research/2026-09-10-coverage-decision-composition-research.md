# Coverage 与 Decision 组合模型的 GitHub 一手资料

> 状态：研究完成；只新增研究文档，未修改生产代码或测试。
>
> 日期：2026-09-10。
>
> 目标：核对事实/标记、树编辑、重叠 replacement、原子变更和 patch 组合的官方实现语义，为 NLISSN 的 coverage、decision footprint、residual mapping 和 atomic group 设计提供依据。

## 结论

四个项目的实现共同支持以下分层：

```text
metadata/facts -> proof or edit target -> explicit patch footprint -> resolver/editor
```

它们没有采用“父 span 更大所以父动作永远赢”的通用规则：

- Roslyn 通过 annotation/tracked node 在持续更新的 syntax tree 中定位后续编辑，并明确规定父子编辑的顺序和 callback 语义；
- Clang `Replacement` 对重叠 replacement 默认报告冲突，只在能证明顺序无关时合并；
- Clang `AtomicChange` 把相关 replacements 聚合成一个变更，组合失败会返回 error；
- OpenRewrite 的 `Markers`/`SearchResult` 是树上的元数据或搜索结果，不等同于 rewrite action；
- Scalafix 将 patch 定义为可组合的代数结构，并明确保留 `AtomicPatch` 作为不可拆分的 patch unit。

对 NLISSN 的直接映射是：Mark 可以发布 typed fact 和 provenance，但 `Delete`/`Replace`、父子支配、residual mapping 和原子组必须留在后续的 Proof/EditIntent/DecisionPlan 层。

## 1. Roslyn SyntaxEditor

来源：

- [SyntaxEditor.cs](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/Portable/Editing/SyntaxEditor.cs)
- [raw source](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Workspaces/Core/Portable/Editing/SyntaxEditor.cs)

截至本次读取，源码注释和实现给出以下可核对语义：

1. editor 以 original root 创建一个持续更新的 current root；original root 不被修改。
2. 每个 change 的原始节点会通过 `SyntaxAnnotation` 被跟踪，因此后续 change 可以在 prior changes 已经改变树之后重新找到对应节点。
3. change 按加入顺序应用；compute callback 接收到的是包含前序编辑结果的 current node。
4. 源码注释明确指出：先更新 parent 再更新 child 通常是错误的，除非 parent change 确定保留 child；若必须同时更新，应先加入 child，再由 parent 的 compute callback 看到 child 的结果。
5. replacement 如果需要找到放入树中的新节点，应给新节点增加 annotation，再在 callback 中定位。

这不是建议 NLISSN 直接改用 Roslyn `SyntaxEditor`，而是验证 residual mapping 的必要性：父 `Replace` 若保留子节点，就必须在更新后的 replacement tree 中找到并组合 child edit；只依据原始 span 删除 child candidate 不足以保证结果正确。

## 2. Clang Replacement

来源：

- [Replacement.cpp](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Core/Replacement.cpp)
- [Replacement.h](https://github.com/llvm/llvm-project/blob/main/clang/include/clang/Tooling/Core/Replacement.h)

本次从官方源码核对到的关键实现点包括：

- `replacement_error::overlap_conflict` 的错误文本是新 replacement 与已有 replacement 重叠；
- `Replacements::add` 检查待加入 replacement 与已有 replacement 的 overlap；
- `Replacements::mergeIfOrderIndependent` 只有在两个合并顺序产生相同 canonical replacements 时才返回合并结果，否则返回 overlap conflict；
- `Replacements::merge` 有专门的 merged replacement 逻辑，并在原始 coordinate space 中处理多个 replacement 的组合。

对 NLISSN 的约束：

1. 非包含的 partial overlap 应默认为 conflict；
2. “两个编辑都覆盖同一片段所以可以合并”不是充分条件，还必须证明组合顺序无关或提供 tree-level composition；
3. rewrite 层可以防御性报告 overlap，但不应从 overlap 自动推导业务上的父子 winner。

## 3. Clang AtomicChange

来源：

- [AtomicChange.cpp](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Refactoring/AtomicChange.cpp)
- [AtomicChange.h](https://github.com/llvm/llvm-project/blob/main/clang/include/clang/Tooling/Refactoring/AtomicChange.h)

官方实现提供了以下依据：

- `AtomicChange` 持有 key、file path、error、header changes 和一组 `Replacements`；
- `AtomicChange::replace`/`insert` 将 replacement 加入同一个 change，加入失败时返回 `llvm::Error`；
- `applyAtomicChanges` 先合并各 `AtomicChange` 的 replacements，再统一应用；组合或应用失败时返回 error，而不是静默选择一部分；
- `AtomicChange` 的序列化也保留整组 replacement 和 error 状态。

对 NLISSN 的约束：

- 方法声明、调用点、method group/lambda/delegate binding 可以建模成一个 `AtomicGroup`；
- 任一 member 发生未知、冲突或 replacement 失败时，默认拒绝整组，而不是只写入声明；
- group identity、source identity 和错误原因应成为计划结果的一部分。

## 4. OpenRewrite Markers 与 SearchResult

来源：

- [Markers.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/marker/Markers.java)
- [SearchResult.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/marker/SearchResult.java)
- [TreeVisitor.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/TreeVisitor.java)

源码组织表达了两个不同角色：

- `Markers` 是附着在 tree 上的一组 marker metadata，可以添加、移除或按类型查找；
- `SearchResult` 是搜索命中的 marker/结果上下文；
- visitor/rewrite 仍然返回新的 tree，实际树变换不由 marker 本身执行。

对 NLISSN 的约束：

`MarkRecord` 可以继续承载目标、来源、事实类型和 payload，但不能把 Mark 当作最终 `DecisionUnit`。如果事实需要“可以参与逻辑规约”或“可以作为全局目标”，应使用 typed capability；如果需要“删除整个 if”或“支配 child Replace”，应由后续 proof 和 planner 授权。

本次网络读取对 OpenRewrite 文件的访问存在间歇性超时，使用的是官方仓库路径；相关语义也与仓库已有的 [2026-09-09-mark-decision-boundary-and-replace-conflict.md](2026-09-09-mark-decision-boundary-and-replace-conflict.md) 交叉核对。没有把网络超时当作额外实现事实。

## 5. Scalafix Patch

来源：

- [Patch.scala](https://github.com/scalacenter/scalafix/blob/main/scalafix-core/src/main/scala/scalafix/patch/Patch.scala)
- [PatchInternals.scala](https://github.com/scalacenter/scalafix/blob/main/scalafix-core/src/main/scala/scalafix/internal/patch/PatchInternals.scala)

官方 patch internals 展示了几个与本问题直接相关的语义：

- `Concat` 表示 patch 的组合；
- `AtomicPatch(underlying)` 是一个独立的 patch unit；
- `foreachPatchUnit` 明确注释为 “don't decompose Atomic Patch”，说明 atomic patch 不能在普通遍历中拆成独立候选；
- token patch merge 只对同一 token 的特定组合定义规则，其他组合抛出 merge error；
- tree patch 先转换成 token patch，再按 token identity 建立 patch map，避免把任意 tree replacement 当作可无条件叠加的文本编辑。

对 NLISSN 的约束：

- `EditIntent` 的 composition mode 应显式区分普通组合和 atomic transaction；
- 一个 parent replacement 不应同时被当作 opaque parent 和可直接叠加的 child patch；
- list 内多个已证明独立的删除可以有 `ListComposable`，但部分重叠和不同语义目标必须进入 conflict。

## 6. 设计结论

GitHub 一手实现支持以下最小接口约束：

```text
Fact
  -> typed Proof
  -> EditIntent(anchor, consumed, composition, residual mapping)
  -> DecisionPlan(selected, composed, rejected, conflicts, atomic groups)
  -> Rewrite
```

其中：

1. `Fact`/Marker 是 metadata，不拥有动作优先级；
2. coverage 必须绑定具体 goal，`TopologyHost` 不能满足 `StructureComplete`；
3. parent Delete 只有在 proof 证明它消费 child anchor 时才可以支配 child；
4. parent Replace 保留 child 时必须使用 tracked node 或等价 residual mapping；
5. partial overlap 默认 error/conflict；
6. declaration/callsite 等相关 edits 应是 atomic group；
7. 所有冲突、拒绝、合并和未知状态必须可观察。

## 7. 研究限制

- 本研究只核对官方 GitHub 仓库的源码路径和关键实现语义，不把第三方博客或二手总结作为证据。
- `main` 分支是移动目标；实现文档应在落地时记录实际使用的 commit SHA，避免未来路径或行为变化导致引用漂移。
- 本研究不证明 NLISSN 必须采用任何一个外部项目的完整 API，只提取其节点身份、组合、重叠和原子性方面的可迁移约束。

## 8. 本轮网络复核记录

本轮通过 GitHub 官方仓库的 `raw.githubusercontent.com` 源码入口复核了上述结论。读取到的文件包括：

- [Roslyn `SyntaxEditor.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Workspaces/Core/Portable/Editing/SyntaxEditor.cs)，HTTP 200；
- [LLVM `Replacement.cpp`](https://raw.githubusercontent.com/llvm/llvm-project/main/clang/lib/Tooling/Core/Replacement.cpp)，HTTP 200；
- [LLVM `AtomicChange.cpp`](https://raw.githubusercontent.com/llvm/llvm-project/main/clang/lib/Tooling/Refactoring/AtomicChange.cpp)，HTTP 200；
- [OpenRewrite `Markers.java`](https://raw.githubusercontent.com/openrewrite/rewrite/main/rewrite-core/src/main/java/org/openrewrite/marker/Markers.java)，HTTP 200；
- [Scalafix `Patch.scala`](https://raw.githubusercontent.com/scalacenter/scalafix/main/scalafix-core/src/main/scala/scalafix/patch/Patch.scala)，HTTP 200。

关键原始实现语义：

1. Roslyn `SyntaxEditor` 的源码注释把 original root、current root、`SyntaxAnnotation` tracking、change order 和 callback 接收到的 current node 写成了明确合同；这为 parent Replace 后的 residual mapping 提供了实现参照。
2. LLVM `Replacements::add` 对 overlap 做显式检查；`mergeIfOrderIndependent` 只有在两种应用顺序等价时才合并，否则返回 overlap conflict。它没有使用“范围更大”的通用 winner 规则。
3. LLVM `AtomicChange::replace` 将 replacement 加入同一个 change 并返回 `llvm::Error`；`applyAtomicChanges` 合并失败时返回错误，相关 replacements 不会静默拆成成功的一部分。
4. OpenRewrite 的 `Markers` 是树上的 metadata，visitor/rewrite 仍然返回新的 tree；搜索 marker 不是 rewrite action 的授权。
5. Scalafix 的 `AtomicPatch` 在 patch traversal 中保持不可拆分，普通 token patch merge 对不支持的组合报错；这支持 NLISSN 把声明/调用点同步表示成 atomic group。

这些实现只能作为组合和编辑边界的对照，不能代替 NLISSN 自己的语义 proof：Roslyn tracking 能解决节点重定位，但不能自动证明删除 if 的行为安全；LLVM overlap error 能阻止文本冲突，但不能决定哪个业务候选应获胜。
