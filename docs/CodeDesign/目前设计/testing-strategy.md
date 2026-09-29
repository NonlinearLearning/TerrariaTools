# 测试策略

> 状态：当前设计。
>
> 读者：新增规则、fixture、测试组件或跨项目契约的维护者。
>
> 原则：验证行为和安全边界，不把绿色退出码当作 rewrite 安全证明。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 测试层级

| 层级 | 项目/入口 | 负责什么 |
| --- | --- | --- |
| 共享资产 | `tests/NLISSN.Testing/RoslynDeletionPrototype.Testing.csproj` | fixture、临时工作区、源码目录和公共断言；不产出规则结论 |
| 组件组合 | `src/NLISSN.Infrastructure/Testing/NLISSN.TestComponents.csproj` | syntax-valid composition、prerequisite、组合生成和最终 diff contract |
| Unit | `tests/NLISSN.UnitTests/` | 单个分析器、拓扑、cache、logging、结构 helper 的快速回归 |
| Contract | `tests/NLISSN.ContractTests/` | 项目边界、CPG、artifact、validator、持久化和跨模块契约 |
| Host | `tests/NLISSN.HostTests/` | 规则图、配置、目录分析、decision、rewrite 和端到端编排 |
| Performance | `tests/NLISSN.PerformanceTests/` | 受控 fixture 的性能、并发、预算和多文件 I/O；不替代真实工程测量 |

测试项目只依赖被测生产项目和共享测试资产，测试项目之间不互相引用。源码目录、测试目录和 feature 名称以当前 `.csproj` 为准，不复用已经删除的 `RoslynDeletionPrototype.Tests` 路径。

## 一个规则的 Test Plan

新增或修改规则时按以下顺序写测试：

1. 一个最小安全命中 fixture。
2. 一个相同语法但证据不足、应保留源码的 negative fixture。
3. 所属阶段的 mark、payload、lift 或 decision 断言。
4. 最终 `Decision -> RewriteEdit -> rewritten source -> diff` 断言。
5. 需要跨文件、全局引用或 replay 时，补 compilation、manifest、hash 和失配拒绝路径。

逻辑表达式至少覆盖 `&&` direct sibling、`||` barrier、比较、括号、unary、await、conditional 和 assignment terminal。Rule graph/CPG/cache 改动至少覆盖 DOP 1、2、16、取消/失败排空和稳定顺序。修改 rewrite 时不能只断言 diff 文本；必须保留 decision、edit span 和原始/重写源码证据。

## Test components

`TestComponentSource` 使用规范化相对路径，并把组件 ID、文件、文本和 prerequisite 放入稳定 manifest。composer 先拒绝重复 ID、缺 prerequisite、循环 prerequisite、路径越界和 Roslyn syntax error，再组合源片段。最终 contract 分两类：Golden 比较完整文件字节，Structured 比较 required/forbidden file、text、edit kind、span 和 provenance。

组合生成器当前保持确定性的全组合 API。若要引入 pairwise/t-wise，必须先定义因素、取值、seed、覆盖率、生成顺序和失败复现文件；不能为了减少运行时间静默替换现有测试集合。

## 断言重点

优先断言外部可观察行为：返回结果、decision action、source text、文件字节、异常/诊断类型、状态变化和依赖调用。避免通过 private helper、内部集合顺序或实现细节证明行为。对于删除分析，“没有删除”不等于通过；要断言目标未被误删、非目标文件未出现 edit、hunk 有 target-derived provenance。

## 验证顺序

共享 `Build` 输出导致并行 build/test 可能产生 `CS2012`；命令必须串行并使用 `-p:UseSharedCompilation=false`：

```powershell
pwsh -File .\Miscellaneous\init.ps1
dotnet build .\src\NLISSN.Infrastructure\Testing\NLISSN.TestComponents.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

focused test 应先覆盖受影响项目，再扩大到对应完整层级。最终报告应说明命令、通过数量、fixture/diff artifact、未执行的真实工程测量和已知环境边界。当前测试组件的完成条件和数字记录在 [`Context/feature_list.json`](../../../Context/feature_list.json)。
