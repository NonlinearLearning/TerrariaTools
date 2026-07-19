# 本地 Codex AI 控制平面设计

**状态：已确认设计，尚未实现。**

## 目标

为一个 Git 仓库提供本机网页控制面。网页统一展示每个 Codex CLI 会话的实时终端画面、所属模块、当前提案 Markdown、分支、worktree、验证证据和审批状态。

每个模块在其生命周期内只有一个可写代码的 Owner AI 会话。提案审查和最终 AI 审查使用不同的只读 CLI 会话。

## 范围与边界

- 控制面运行在 `localhost`，不暴露公网端口。
- 代码改动只由模块 Owner 会话完成；同一模块不允许第二个实现会话。
- 模块可以由多个顺序 proposal 组成；每个 proposal 都必须形成可构建、可测试、可提交的变更。
- 人工只在网页上批准提案和关闭模块。最终 AI review 只在人工关闭模块后运行一次。
- PowerShell 负责启动和恢复 Codex CLI；网页不直接执行任意 shell 文本。
- 不依赖 PowerShell 自动操作 Codex App 窗口，也不尝试向 App 对话注入消息。

## 运行组件

```text
浏览器
  <-> localhost 控制面服务
        <-> 状态与审计存储
        <-> ConPTY 会话宿主
              <-> PowerShell 启动的 Codex CLI
```

### 控制面服务

服务持有模块状态机、模块 lease、审批记录和 WebSocket 连接。它验证所有状态转换，再调用一个固定参数的 PowerShell 脚本启动或恢复 Codex CLI。它不能接受网页提交的任意命令行。

### ConPTY 会话宿主

会话宿主为每个 CLI 创建一个 Windows Pseudo Console，并把标准输入输出字节流广播到浏览器终端组件。网页可输入的会话仅限人工审查会话；Owner 会话默认只读显示，控制面只向其发送已批准的受控任务。

### 三类会话

| 会话 | 数量 | 权限 | 职责 |
| --- | --- | --- | --- |
| Owner | 每个活动模块一个 | workspace-write，仅该模块 worktree | 生成提案、顺序实现所有已批准 proposal、运行局部验证、创建 proposal commit |
| 人工审查 | 共享或按模块创建 | read-only | 接收 proposal Markdown，供人工阅读；不实现代码 |
| 最终 AI review | 每个关闭模块一次 | read-only | 审查模块分支 diff 和验证证据；不改代码 |

## 状态机

```text
Draft
  -> AwaitingHumanApproval
  -> Approved
  -> OwnerImplementing
  -> ProposalCommitted
  -> NextProposal | AwaitingModuleClose
  -> HumanClosed
  -> FinalAiReview
  -> ReadyToCommit
  -> ModuleCommitted
```

- `Draft -> AwaitingHumanApproval`：Owner 生成 proposal Markdown；控制面计算 SHA-256 并将正文注入审查 CLI。
- `AwaitingHumanApproval -> Approved`：网页审批操作同时记录 proposal ID、hash、审批人和时间。
- `Approved -> OwnerImplementing`：控制面校验 hash、干净基线、模块 lease 和分支，再向 Owner 会话发送任务。
- `ProposalCommitted -> NextProposal`：仍有未关闭的 proposal。
- `AwaitingModuleClose -> HumanClosed`：网页明确确认“模块完成”。
- `HumanClosed -> FinalAiReview`：启动只读 review 会话；通过后才能进入 `ReadyToCommit`。

任何 hash 不匹配、lease 过期、基线漂移或验证失败都会转到阻断状态，不会自动执行后续步骤。

## Git 与可提交性

每个模块拥有一个分支和一个 worktree，例如 `ai/<module-id>` 与 `../worktrees/<module-id>`。Owner 在同一分支上顺序处理 proposal；每个 proposal 完成时创建一个候选 commit，并记录基线 SHA、head SHA、验证命令和结果。

模块完成不是“所有消息处理结束”，而是人工确认没有后续 proposal，最终 review 通过，并且分支处于可提交状态。只有此时才写入 `module-complete.txt`。

## 状态与审计文件

```text
.ai-flow/
  modules/<module-id>/module.json
  proposals/<proposal-id>.md
  proposals/<proposal-id>.json
  approvals/<proposal-id>.json
  leases/<module-id>.json
  sessions/<session-id>.json
  events.jsonl
  completed/<module-id>.txt
```

`module.json` 是状态真相来源。`completed/<module-id>.txt` 只作面向人的最终标记，不能驱动状态转换。

## 网页能力

- 模块列表：模块、Owner、提案、状态、分支、worktree、最近验证、阻断原因。
- 会话终端：实时输出、连接状态、滚动缓冲和只读/可输入权限提示。
- proposal 审批：展示 Markdown、hash、变更范围、验收命令；提供批准和退回。
- 模块关闭：展示已完成 proposal、未解决风险、候选 commit 和测试证据；需要显式确认。
- 审计视图：按模块展示每一次状态转换和操作者。

## 安全与成本控制

- 服务仅绑定 loopback；首次打开网页需要本地随机令牌。
- 网页动作映射到白名单命令，不允许自由 shell。
- proposal 被当作不可信输入，Owner 接收时附带固定的边界提示和允许的 worktree 路径。
- 审查会话与最终 review 都使用只读 sandbox。
- proposal 阶段只运行机械检查；仅在人工关闭模块后消耗一次 AI review。

## 验收标准

1. 同时运行两个不同模块时，网页能显示两个独立 Owner 终端，且每个模块只有一个活动 lease。
2. 一个 proposal 未经网页审批或 hash 改变时，Owner 不会收到实现任务。
3. 一个模块有两个 proposal 时，第一 proposal 的 commit 可独立验证，第二 proposal 在同一 Owner 和分支上继续。
4. 人工未关闭模块时，最终 AI review 不会启动。
5. 最终 review 通过、分支可提交后，才生成模块完成文本标记。
6. 浏览器刷新、服务重启和终端断连后，状态、审计、会话归属和终端缓冲可恢复。

## 已知实现风险

- ConPTY 是 Windows 专有 API；第一阶段只支持 Windows 10/Server 2019 及以上。
- Codex CLI 会话标识、终端输出和重连行为必须用最小 PoC 验证，不能假定普通进程标准输出等同于交互终端。
- 最终提交与合并的职责不同：控制面可以创建本地 commit；推送或合并仍应保留显式 Git 远端策略。
