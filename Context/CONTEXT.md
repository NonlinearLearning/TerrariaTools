# NLISSN Decision Domain

## Core Terms

- **Fact**：对原始源码、符号、调用关系或控制流的观察结果。Fact 可以被多个分析过程读取，不是一次性资源，也不代表某个动作。
- **Region**：一个需要共同判断的语义范围。Region 由目标、相关义务和边界组成，不等同于某个 AST 节点的全部 descendants。
- **Coverage contract**：一个目标声明需要哪些事实、允许哪些事实满足、哪些未知状态必须阻止完成。
- **Proof**：针对一个目标和一组义务的可追溯证明。Proof 必须区分 Complete、Partial、Unknown 和 Rejected。
- **Authorization**：Proof 针对一个具体候选动作的授权。Authorization 不改变 Fact，也不决定其他候选的优先级。
- **Intent**：候选提出的源码变换请求，包含原始锚点、读取范围、擦除范围、写入结果、保留义务和组合方式。
- **Plan**：在所有 Intent 之间应用原子性、组合、支配和冲突约束后得到的可执行结果。Plan 必须保留被拒绝、冲突、支配和未知的原因。
- **Residual mapping**：父变换后，原始子区域在新树中的对应关系；可能是 Retained、Replaced、Removed 或 Unknown。
- **Atomic group**：必须全选或全不选的一组相关 Intent，例如声明与其调用点的同步编辑。
- **Unknown**：分析范围、绑定、实现能力或预算不足导致无法判定。Unknown 不是空集合，也不能升级为 Complete。
- **Authority funnel**：事实、证明、候选和计划按阶段单向升级的约束通道；只有经过当前阶段验证的结果才能成为下一阶段的输入，可执行变换只能从已授权计划产生。

## Domain Invariants

1. Fact 和 Mark 不拥有 Delete、Replace 或候选优先级。
2. Proof 只证明明确声明的目标和义务；拓扑关系不能冒充结构完整性。
3. Authorization 必须绑定具体候选、允许动作和原始 Region。
4. Intent 不得通过原始 span 自行支配另一个 Intent；支配必须有显式 Proof 和擦除范围。
5. 保留子区域的父变换必须提供 Residual mapping；无法映射时进入冲突或 Unknown。
6. Atomic group 的任一成员未知、失败或冲突时，默认整组不执行。
7. Plan 是唯一的候选组合与冲突裁决结果；Rewrite 只执行并验证 Plan。
8. 阶段输出默认是不可信的事实或候选；诊断开关不能关闭可执行变换的安全约束。
