# C# class 与 struct：500 轮一手来源检索审计

> 状态：500/500 轮网络检索完成；未把来源数量当作性能基准结果。
>
> 日期：2026-09-17。
>
> 问题：以 C# 设计提案、C# 参考手册和 GitHub 的 C# 语言项目为依据，复核高密度对象中 class 改为 struct 后内存与访问性能变化的说法。

## 结论

500 轮检索支持一个严格但有限的结论：C# 有意区分 reference semantics 与 value semantics。class 变量保存对象引用，struct 变量直接包含值；后者的赋值、按值传参和返回会复制值。由此，密集集合中将小型 class 改为 struct 可能减少独立对象和引用间接，但这不是 C# 对 class 的设计错误，也不是语言承诺的固定内存或速度倍率。

C# 标准明确把 class 与 struct 的差异定义为语义差异：value-type 变量直接包含数据，reference-type 变量保存数据引用；两个 reference-type 变量可以引用同一对象，而 value-type 变量各自持有副本。struct 是 value type，class 是 reference type。见 [C# 标准 types.md](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/types.md) 和 [structs.md](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/structs.md)。

这意味着性能观察必须拆分为两部分：语言层面解释复制、别名与装箱；运行时层面解释对象头、对齐、数组/集合布局、GC 和 CPU 缓存。500 轮资料审计没有测量任何本机性能，也不能替代针对真实类型和真实集合形状的 BenchmarkDotNet 测量。有关当前 CoreCLR 对象布局的独立分析见 [前一份运行时研究](2026-09-17-csharp-class-struct-density-research.md)。

## 直接来源中的事实

| 事实 | 一手来源 | 对高密度对象判断的含义 |
| --- | --- | --- |
| value-type 变量直接包含数据；reference-type 变量保存对象引用 | [C# 标准 types](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/types.md)；[Microsoft Learn reference types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/reference-types) | 这是 class/struct 差异的语义根源，而非某个优化技巧。 |
| struct 是 value type，且不要求为每个实例单独进行堆分配 | [C# 标准 structs](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/structs.md) | 当值嵌入数组、List<T> 或其他对象时，可能避免 N 个独立 wrapper 对象；具体布局仍由运行时决定。 |
| value-type 赋值、默认按值传参和返回复制实例 | [Microsoft Learn value types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/value-types) | 大或可变 struct 的复制成本和行为风险可抵消集合内联收益。 |
| boxing 将值复制到新建的托管堆 object；接口/object 边界可能触发它 | [C# 标准 conversions](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/conversions.md)；[Microsoft Learn boxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing) | 将 class 改为 struct 后，若热路径频繁进入 object 或接口槽位，分配可能重新出现。 |
| ref-like/ref struct 的安全规则限制其逃逸、数组元素、字段和 boxing | [span safety 提案](https://github.com/dotnet/csharplang/blob/0fb3fdb63d09c546b904cc80bc299ad1bd953469/proposals/csharp-7.2/span-safety.md)；[ref struct 参考](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct) | ref struct 是受更强生命周期限制的专用工具，不能作为普通实体 class 的通用替代。 |
| C# 13 允许 ref struct 实现接口，但仍禁止将其转换为接口，因为那会 boxing | [ref struct interfaces 提案](https://github.com/dotnet/csharplang/blob/0fb3fdb63d09c546b904cc80bc299ad1bd953469/proposals/csharp-13.0/ref-struct-interfaces.md) | 设计提案明确把避免 boxing 保留为语言边界。 |
| inline arrays 提供安全的内联数组机制，适用于 class、struct 和 interface 声明 | [inline arrays 提案](https://github.com/dotnet/csharplang/blob/0fb3fdb63d09c546b904cc80bc299ad1bd953469/proposals/csharp-12.0/inline-arrays.md) | C# 的设计方向是提供显式布局工具，而不是宣称普通 struct 永远更快。 |
| low-level struct improvements 引入 ref fields 与 scoped/escape 规则 | [low-level struct improvements](https://github.com/dotnet/csharplang/blob/0fb3fdb63d09c546b904cc80bc299ad1bd953469/proposals/csharp-11.0/low-level-struct-improvements.md) | 一旦用 ref、scoped 或 ref field 回避复制，API 的逃逸与生命周期合同必须接受单独审查。 |
| record struct 仍是 value type，遵守普通 struct 的限制 | [record structs 提案](https://github.com/dotnet/csharplang/blob/0fb3fdb63d09c546b904cc80bc299ad1bd953469/proposals/csharp-10.0/record-structs.md) | record 语法不改变值语义，也不自动解决复制或装箱。 |

## 对原始说法的判定

| 说法 | 判定 | 原因 |
| --- | --- | --- |
| 高密度小 class 改 struct 后内存可能明显下降 | 有条件成立 | 在数组或泛型集合中，值可内联而引用对象通常需要额外实例和引用槽位；实际比例由运行时布局、字段、容量和生命周期决定。 |
| 访问可能更快 | 有条件成立 | 紧凑布局和减少间接访问可能改善扫描局部性，但 C# 规范不承诺具体延迟；必须基准验证。 |
| 内存一定下降一半 | 不成立 | C# 标准未规定对象头、对齐、集合容量或 GC 行为；包含引用字段、装箱和大值复制都会改变结果。 |
| struct 一定在栈上 | 不成立 | 普通 struct 可嵌入托管数组或对象；只有 ref struct 受到不能逃逸到托管堆的特别限制。 |
| 这是 C# 对 class 的错误设计 | 不成立 | reference semantics 提供身份、共享引用和别名；value semantics 以复制和更严格的可变性约束交换内联机会。 |

## 工程边界

1. 先确认类型是否是值而非实体：不需要稳定身份、共享可变状态、继承/多态或 null 表示时，struct 才是候选。
2. 分开测量构造与扫描。前者反映分配和 GC，后者才更接近数据访问局部性。
3. 对任何 object、接口、日志、反射或非泛型集合边界测量 boxing；不要从源码外观推断没有装箱。
4. 对大 struct、可变 struct 和频繁传参的 API 测量复制；ref readonly、in、scoped 和 ref field 是独立的 API 设计，不是默认修复。
5. 不能从本审计推出本仓库或用户对象的实际内存比例。应保持语义等价后，用 BenchmarkDotNet 与运行时计数器验证。

## 500 轮方法与可复现性

每一轮是一次对唯一、固定提交的官方 Markdown 源的 HTTP GET。记录包含轮次、语料类别、仓库相对路径、HTTP 状态与内容 SHA-256 前 16 位。所有内容读取发生于 2026-09-17。

| 语料 | 轮次 | 数量 | 固定来源 |
| --- | ---: | ---: | --- |
| dotnet/csharplang 设计提案 | 1-206 | 206 | main @0fb3fdb63d09c546b904cc80bc299ad1bd953469 |
| dotnet/csharpstandard 标准章节 | 207-238 | 32 | draft-v8 @df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca |
| dotnet/docs C# language-reference 页 | 239-500 | 262 | main @c851dbca2ef621876ccbf171f9b3a491902bf9a1 |

提案和标准章节分别取该目录下全部 Markdown，按路径排序。参考页从 language-reference 目录选取：先按与 class、struct、value、reference、ref、boxing、array、memory、escape、span、layout 等关键词的路径匹配数降序，再按路径升序，取前 262 个。这个选择规则只用于让 500 轮可重现；它不表示每个条目都能支持 class/struct 性能结论。

原始 URL 可由下列固定规则重建：

- 提案：https://raw.githubusercontent.com/dotnet/csharplang/0fb3fdb63d09c546b904cc80bc299ad1bd953469/PATH
- 标准：https://raw.githubusercontent.com/dotnet/csharpstandard/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/PATH
- 参考：https://raw.githubusercontent.com/dotnet/docs/c851dbca2ef621876ccbf171f9b3a491902bf9a1/docs/csharp/PATH

## 每十轮计数

| 已完成 | 本批轮次 | 成功响应 | 首个来源 | 末个来源 |
| ---: | --- | ---: | --- | --- |
| 10 | 1-10 | 10/10 | proposals/anonymous-using-declarations.md | proposals/compound-assignment-in-initializer-and-with.md |
| 20 | 11-20 | 10/10 | proposals/conditional-operator-access-syntax-refinement.md | proposals/csharp-10.0/improved-interpolated-strings.md |
| 30 | 21-30 | 10/10 | proposals/csharp-10.0/lambda-improvements.md | proposals/csharp-11.0/low-level-struct-improvements.md |
| 40 | 31-40 | 10/10 | proposals/csharp-11.0/new-line-in-interpolation.md | proposals/csharp-12.0/collection-expressions.md |
| 50 | 41-50 | 10/10 | proposals/csharp-12.0/experimental-attribute.md | proposals/csharp-13.0/method-group-natural-type-improvements.md |
| 60 | 51-60 | 10/10 | proposals/csharp-13.0/overload-resolution-priority.md | proposals/csharp-14.0/ignored-directives.md |
| 70 | 61-70 | 10/10 | proposals/csharp-14.0/null-conditional-assignment.md | proposals/csharp-15.0/labeled-break-continue.md |
| 80 | 71-80 | 10/10 | proposals/csharp-15.0/unions.md | proposals/csharp-7.0/pattern-matching.md |
| 90 | 81-90 | 10/10 | proposals/csharp-7.0/ref-locals-returns.md | proposals/csharp-7.2/conditional-ref.md |
| 100 | 91-100 | 10/10 | proposals/csharp-7.2/leading-separator.md | proposals/csharp-7.3/blittable.md |
| 110 | 101-110 | 10/10 | proposals/csharp-7.3/enum-delegate-constraints.md | proposals/csharp-8.0/alternative-interpolated-verbatim.md |
| 120 | 111-120 | 10/10 | proposals/csharp-8.0/async-streams.md | proposals/csharp-8.0/nullable-reference-types.md |
| 130 | 121-130 | 10/10 | proposals/csharp-8.0/obsolete-accessor.md | proposals/csharp-9.0/covariant-returns.md |
| 140 | 131-140 | 10/10 | proposals/csharp-9.0/extending-partial-methods.md | proposals/csharp-9.0/nullable-parameter-default-value-analysis.md |
| 150 | 141-150 | 10/10 | proposals/csharp-9.0/nullable-reference-types-specification.md | proposals/csharp-9.0/variance-safety-for-static-interface-members.md |
| 160 | 151-160 | 10/10 | proposals/deconstruction-in-lambda-parameters.md | proposals/final-initializers.md |
| 170 | 161-170 | 10/10 | proposals/immediately-enumerated-collection-expressions.md | proposals/left-right-join-in-query-expressions.md |
| 180 | 171-180 | 10/10 | proposals/mixed-object-and-collection-initializers.md | proposals/ref-struct-closures.md |
| 190 | 181-190 | 10/10 | proposals/rejected/collection-expressions-in-foreach.md | proposals/rejected/param-nullchecking.md |
| 200 | 191-200 | 10/10 | proposals/rejected/params-span.md | proposals/standard-unions.md |
| 210 | 201-210 | 10/10 | proposals/target-typed-generic-type-inference.md | standard/bibliography.md |
| 220 | 211-220 | 10/10 | standard/classes.md | standard/general-description.md |
| 230 | 221-230 | 10/10 | standard/grammar.md | standard/README.md |
| 240 | 231-240 | 10/10 | standard/scope.md | language-reference/builtin-types/nullable-value-types.md |
| 250 | 241-250 | 10/10 | language-reference/builtin-types/ref-struct.md | language-reference/builtin-types/built-in-types.md |
| 260 | 251-260 | 10/10 | language-reference/builtin-types/default-values.md | language-reference/compiler-messages/ref-struct-errors.md |
| 270 | 261-270 | 10/10 | language-reference/keywords/method-parameters.md | language-reference/operators/conditional-operator.md |
| 280 | 271-280 | 10/10 | language-reference/operators/deconstruction.md | language-reference/operators/stackalloc.md |
| 290 | 281-290 | 10/10 | language-reference/operators/subtraction-operator.md | language-reference/builtin-types/enum.md |
| 300 | 291-300 | 10/10 | language-reference/builtin-types/string-operations.md | language-reference/compiler-messages/dynamic-type-and-binding-errors.md |
| 310 | 301-310 | 10/10 | language-reference/compiler-messages/file-local-types.md | language-reference/compiler-messages/ref-modifiers-errors.md |
| 320 | 311-320 | 10/10 | language-reference/compiler-messages/ref-safety-errors.md | language-reference/keywords/ref.md |
| 330 | 321-330 | 10/10 | language-reference/keywords/unsafe.md | language-reference/operators/namespace-alias-qualifier.md |
| 340 | 331-340 | 10/10 | language-reference/operators/null-forgiving.md | language-reference/compiler-messages/cs0019.md |
| 350 | 341-350 | 10/10 | language-reference/compiler-messages/cs0029.md | language-reference/compiler-messages/cs0120.md |
| 360 | 351-360 | 10/10 | language-reference/compiler-messages/cs0122.md | language-reference/compiler-messages/cs0420.md |
| 370 | 361-370 | 10/10 | language-reference/compiler-messages/cs0429.md | language-reference/compiler-messages/cs0675.md |
| 380 | 371-380 | 10/10 | language-reference/compiler-messages/cs0731.md | language-reference/compiler-messages/cs1567.md |
| 390 | 381-390 | 10/10 | language-reference/compiler-messages/cs1591.md | language-reference/compiler-messages/cs1699.md |
| 400 | 391-400 | 10/10 | language-reference/compiler-messages/cs1700.md | language-reference/compiler-messages/cs1750.md |
| 410 | 401-410 | 10/10 | language-reference/compiler-messages/cs1762.md | language-reference/compiler-messages/cs3009.md |
| 420 | 411-420 | 10/10 | language-reference/compiler-messages/cs7003.md | language-reference/compiler-messages/cs8151.md |
| 430 | 421-430 | 10/10 | language-reference/compiler-messages/cs8152.md | language-reference/compiler-messages/cs8170.md |
| 440 | 431-440 | 10/10 | language-reference/compiler-messages/cs8171.md | language-reference/compiler-messages/cs8354.md |
| 450 | 441-450 | 10/10 | language-reference/compiler-messages/cs8355.md | language-reference/compiler-messages/indexer-access-errors.md |
| 460 | 451-460 | 10/10 | language-reference/compiler-messages/invalid-build-command-line.md | language-reference/compiler-messages/pattern-matching-warnings.md |
| 470 | 461-470 | 10/10 | language-reference/compiler-messages/preprocessor-errors.md | language-reference/compiler-messages/warning-waves.md |
| 480 | 471-480 | 10/10 | language-reference/compiler-options/advanced.md | language-reference/compiler-options/security.md |
| 490 | 481-490 | 10/10 | language-reference/configure-language-version.md | language-reference/keywords/ascending.md |
| 500 | 491-500 | 10/10 | language-reference/keywords/async.md | language-reference/keywords/extension.md |

## 轮次账本

| 轮次 | 语料 | 相对路径 | HTTP | 内容 SHA-256 前 16 位 |
| ---: | --- | --- | ---: | --- |
| 1 | 设计提案 | proposals/anonymous-using-declarations.md | 200 | A8C0AA6892FECC26 |
| 2 | 设计提案 | proposals/async-main-update.md | 200 | 63B096CC49E05D94 |
| 3 | 设计提案 | proposals/async-method-ref-parameters.md | 200 | 9A1CD88EB9E1C12F |
| 4 | 设计提案 | proposals/block-bodied-switch-expression-arms.md | 200 | A049E38929E43260 |
| 5 | 设计提案 | proposals/breaking-change-warnings.md | 200 | 85B9D6AE3A3C7A71 |
| 6 | 设计提案 | proposals/capability-safe.md | 200 | 52C9CB8D0646BBE4 |
| 7 | 设计提案 | proposals/case-declarations.md | 200 | BBACAD68138A4FD8 |
| 8 | 设计提案 | proposals/chained-relational-comparison.md | 200 | 99F848E3FDA751F3 |
| 9 | 设计提案 | proposals/closed-enums.md | 200 | 23F40A1CEABEB5BC |
| 10 | 设计提案 | proposals/compound-assignment-in-initializer-and-with.md | 200 | 499F786C4968AE3F |
| 11 | 设计提案 | proposals/conditional-operator-access-syntax-refinement.md | 200 | 5BCCBC931A710EB3 |
| 12 | 设计提案 | proposals/csharp-10.0/async-method-builders.md | 200 | 3009C96C763C1C09 |
| 13 | 设计提案 | proposals/csharp-10.0/caller-argument-expression.md | 200 | 3382A8FB87EFEBF4 |
| 14 | 设计提案 | proposals/csharp-10.0/constant_interpolated_strings.md | 200 | AB8F1E892A412638 |
| 15 | 设计提案 | proposals/csharp-10.0/enhanced-line-directives.md | 200 | 7BC14B928EE5E5E9 |
| 16 | 设计提案 | proposals/csharp-10.0/extended-property-patterns.md | 200 | C4CFDE2503D2498D |
| 17 | 设计提案 | proposals/csharp-10.0/file-scoped-namespaces.md | 200 | 295F3DFB8C3ED3CB |
| 18 | 设计提案 | proposals/csharp-10.0/GlobalUsingDirective.md | 200 | 2737C6906A35776B |
| 19 | 设计提案 | proposals/csharp-10.0/improved-definite-assignment.md | 200 | CCD1C46E7E5BC593 |
| 20 | 设计提案 | proposals/csharp-10.0/improved-interpolated-strings.md | 200 | A9232653B4497EAE |
| 21 | 设计提案 | proposals/csharp-10.0/lambda-improvements.md | 200 | 43E4EAD0E264A6E6 |
| 22 | 设计提案 | proposals/csharp-10.0/parameterless-struct-constructors.md | 200 | 8449FC73D19EAD49 |
| 23 | 设计提案 | proposals/csharp-10.0/record-structs.md | 200 | 6648B21CF551CDA4 |
| 24 | 设计提案 | proposals/csharp-11.0/auto-default-structs.md | 200 | EF94C71A97701691 |
| 25 | 设计提案 | proposals/csharp-11.0/checked-user-defined-operators.md | 200 | A642CF9ACF8C5199 |
| 26 | 设计提案 | proposals/csharp-11.0/extended-nameof-scope.md | 200 | D989D5680F9CF8F4 |
| 27 | 设计提案 | proposals/csharp-11.0/file-local-types.md | 200 | C9907C62543E8698 |
| 28 | 设计提案 | proposals/csharp-11.0/generic-attributes.md | 200 | 80F24F79C8FB3AD0 |
| 29 | 设计提案 | proposals/csharp-11.0/list-patterns.md | 200 | C8A0AA1F21E5D589 |
| 30 | 设计提案 | proposals/csharp-11.0/low-level-struct-improvements.md | 200 | 48173C4AAA1FFB3C |
| 31 | 设计提案 | proposals/csharp-11.0/new-line-in-interpolation.md | 200 | A24B4E72EA09462F |
| 32 | 设计提案 | proposals/csharp-11.0/numeric-intptr.md | 200 | B6FA2F3916AC2F58 |
| 33 | 设计提案 | proposals/csharp-11.0/pattern-match-span-of-char-on-string.md | 200 | B0EC498F85961C9F |
| 34 | 设计提案 | proposals/csharp-11.0/raw-string-literal.md | 200 | 9105B87711BEADF9 |
| 35 | 设计提案 | proposals/csharp-11.0/relaxing_shift_operator_requirements.md | 200 | 71C2A504877FAE09 |
| 36 | 设计提案 | proposals/csharp-11.0/required-members.md | 200 | 0F731D503B589797 |
| 37 | 设计提案 | proposals/csharp-11.0/static-abstracts-in-interfaces.md | 200 | 43691EEE0C3C76CE |
| 38 | 设计提案 | proposals/csharp-11.0/unsigned-right-shift-operator.md | 200 | 4C6B52163795A408 |
| 39 | 设计提案 | proposals/csharp-11.0/utf8-string-literals.md | 200 | 365DC2AA1232A94B |
| 40 | 设计提案 | proposals/csharp-12.0/collection-expressions.md | 200 | 20D62BFC544DCDB9 |
| 41 | 设计提案 | proposals/csharp-12.0/experimental-attribute.md | 200 | DFADC3A0A8AE6FD0 |
| 42 | 设计提案 | proposals/csharp-12.0/inline-arrays.md | 200 | EAEB00C39EE13385 |
| 43 | 设计提案 | proposals/csharp-12.0/lambda-method-group-defaults.md | 200 | 663CA72DB438D27B |
| 44 | 设计提案 | proposals/csharp-12.0/primary-constructors.md | 200 | 24F0B9857E4456EA |
| 45 | 设计提案 | proposals/csharp-12.0/ref-readonly-parameters.md | 200 | A742250323E36F0B |
| 46 | 设计提案 | proposals/csharp-12.0/using-alias-types.md | 200 | ECB198355D329F4F |
| 47 | 设计提案 | proposals/csharp-13.0/collection-expressions-better-conversion.md | 200 | 26733C221FCE730D |
| 48 | 设计提案 | proposals/csharp-13.0/esc-escape-sequence.md | 200 | 814C609898F84EFE |
| 49 | 设计提案 | proposals/csharp-13.0/lock-object.md | 200 | 6A60605A1F68D246 |
| 50 | 设计提案 | proposals/csharp-13.0/method-group-natural-type-improvements.md | 200 | C9931581FFBE9E5B |
| 51 | 设计提案 | proposals/csharp-13.0/overload-resolution-priority.md | 200 | 3AE409649AA5FC38 |
| 52 | 设计提案 | proposals/csharp-13.0/params-collections.md | 200 | A92891AE4D2C1757 |
| 53 | 设计提案 | proposals/csharp-13.0/partial-properties.md | 200 | 743E4EE38F66CC21 |
| 54 | 设计提案 | proposals/csharp-13.0/ref-struct-interfaces.md | 200 | 89DB7C6F20F22A5B |
| 55 | 设计提案 | proposals/csharp-13.0/ref-unsafe-in-iterators-async.md | 200 | ED4DB38AF450526D |
| 56 | 设计提案 | proposals/csharp-14.0/extension-operators.md | 200 | 11F4FFEAE17A4618 |
| 57 | 设计提案 | proposals/csharp-14.0/extensions.md | 200 | 3FDD337FB3FB0C08 |
| 58 | 设计提案 | proposals/csharp-14.0/field-keyword.md | 200 | 3D56C4E42E738FF3 |
| 59 | 设计提案 | proposals/csharp-14.0/first-class-span-types.md | 200 | 3C3EF60C44F7D48F |
| 60 | 设计提案 | proposals/csharp-14.0/ignored-directives.md | 200 | 1E9B3CB474FBAA16 |
| 61 | 设计提案 | proposals/csharp-14.0/null-conditional-assignment.md | 200 | 95DE9584C9E9E98A |
| 62 | 设计提案 | proposals/csharp-14.0/optional-and-named-parameters-in-expression-trees.md | 200 | 0A7589F220ABAE51 |
| 63 | 设计提案 | proposals/csharp-14.0/partial-events-and-constructors.md | 200 | 104E69AE2F93BE87 |
| 64 | 设计提案 | proposals/csharp-14.0/simple-lambda-parameters-with-modifiers.md | 200 | 935DCBF54E8767A3 |
| 65 | 设计提案 | proposals/csharp-14.0/unbound-generic-types-in-nameof.md | 200 | 97C1104338B4CFA8 |
| 66 | 设计提案 | proposals/csharp-14.0/user-defined-compound-assignment.md | 200 | 34CCE152356D38A3 |
| 67 | 设计提案 | proposals/csharp-15.0/closed-hierarchies.md | 200 | 42FEFD4135DF4A6D |
| 68 | 设计提案 | proposals/csharp-15.0/collection-expression-arguments.md | 200 | 491B1497FE1E4EBB |
| 69 | 设计提案 | proposals/csharp-15.0/extension-indexers.md | 200 | 6FDFEF3BD21D446D |
| 70 | 设计提案 | proposals/csharp-15.0/labeled-break-continue.md | 200 | 6F7A0240E013201A |
| 71 | 设计提案 | proposals/csharp-15.0/unions.md | 200 | 410DDF2414C96543 |
| 72 | 设计提案 | proposals/csharp-6.0/empty-params-array.md | 200 | CBE947FC0E794C09 |
| 73 | 设计提案 | proposals/csharp-6.0/enum-base-type.md | 200 | CD9B6FFD316C5D39 |
| 74 | 设计提案 | proposals/csharp-6.0/struct-autoprop-init.md | 200 | F4A01684AEBB8A60 |
| 75 | 设计提案 | proposals/csharp-7.0/binary-literals.md | 200 | 55FC6E056A7AE47C |
| 76 | 设计提案 | proposals/csharp-7.0/digit-separators.md | 200 | E5B78D8A3D04664E |
| 77 | 设计提案 | proposals/csharp-7.0/expression-bodied-everything.md | 200 | CD271DE2B7EEF6C0 |
| 78 | 设计提案 | proposals/csharp-7.0/local-functions.md | 200 | 07BAD2E6E737C1E0 |
| 79 | 设计提案 | proposals/csharp-7.0/out-var.md | 200 | FF98674D72C56D6E |
| 80 | 设计提案 | proposals/csharp-7.0/pattern-matching.md | 200 | 35006167FB2BE5D0 |
| 81 | 设计提案 | proposals/csharp-7.0/ref-locals-returns.md | 200 | 4AE674217BF12D01 |
| 82 | 设计提案 | proposals/csharp-7.0/task-types.md | 200 | 6D9030CB8FC07F11 |
| 83 | 设计提案 | proposals/csharp-7.0/throw-expression.md | 200 | 27DBC88040EE0601 |
| 84 | 设计提案 | proposals/csharp-7.0/tuples.md | 200 | 5061AD34AB308784 |
| 85 | 设计提案 | proposals/csharp-7.1/async-main.md | 200 | 71E3B468C4B32F7E |
| 86 | 设计提案 | proposals/csharp-7.1/generics-pattern-match.md | 200 | 9A2F557E31834955 |
| 87 | 设计提案 | proposals/csharp-7.1/infer-tuple-names.md | 200 | 788D02FDE920DC3F |
| 88 | 设计提案 | proposals/csharp-7.1/README.md | 200 | 14CCD5F5DA6BCFD9 |
| 89 | 设计提案 | proposals/csharp-7.1/target-typed-default.md | 200 | 16B3054A115145A7 |
| 90 | 设计提案 | proposals/csharp-7.2/conditional-ref.md | 200 | 0690F63744B28941 |
| 91 | 设计提案 | proposals/csharp-7.2/leading-separator.md | 200 | 3F851A13BBCBAD2B |
| 92 | 设计提案 | proposals/csharp-7.2/non-trailing-named-arguments.md | 200 | 3AE5349C9BE454D4 |
| 93 | 设计提案 | proposals/csharp-7.2/private-protected.md | 200 | E8784096484526FE |
| 94 | 设计提案 | proposals/csharp-7.2/readonly-ref.md | 200 | D859C5F6E0E03057 |
| 95 | 设计提案 | proposals/csharp-7.2/readonly-struct.md | 200 | 590912B0D7DD7E71 |
| 96 | 设计提案 | proposals/csharp-7.2/ref-extension-methods.md | 200 | 9282F7C4EF1C0DC0 |
| 97 | 设计提案 | proposals/csharp-7.2/ref-struct-and-span.md | 200 | 02EB0D5F03E9D1FB |
| 98 | 设计提案 | proposals/csharp-7.2/span-safety.md | 200 | 73BC8DEB74EE9AC7 |
| 99 | 设计提案 | proposals/csharp-7.3/auto-prop-field-attrs.md | 200 | D008D5C54299F1FE |
| 100 | 设计提案 | proposals/csharp-7.3/blittable.md | 200 | B4E487F453BC07BC |
| 101 | 设计提案 | proposals/csharp-7.3/enum-delegate-constraints.md | 200 | 54C1DE8256DC0334 |
| 102 | 设计提案 | proposals/csharp-7.3/expression-variables-in-initializers.md | 200 | DA3F9E7BEF771A20 |
| 103 | 设计提案 | proposals/csharp-7.3/improved-overload-candidates.md | 200 | A7DBE290AC3F0F66 |
| 104 | 设计提案 | proposals/csharp-7.3/indexing-movable-fixed-fields.md | 200 | F3CD0F1C51B61C82 |
| 105 | 设计提案 | proposals/csharp-7.3/pattern-based-fixed.md | 200 | D82206462B219C18 |
| 106 | 设计提案 | proposals/csharp-7.3/ref-local-reassignment.md | 200 | FA7F34F58471646C |
| 107 | 设计提案 | proposals/csharp-7.3/ref-loops.md | 200 | 646612A3B73721FF |
| 108 | 设计提案 | proposals/csharp-7.3/stackalloc-array-initializers.md | 200 | 85FC8A4E3F4654FC |
| 109 | 设计提案 | proposals/csharp-7.3/tuple-equality.md | 200 | 4ACC24F3F9986D87 |
| 110 | 设计提案 | proposals/csharp-8.0/alternative-interpolated-verbatim.md | 200 | E1B381B5C8F0F327 |
| 111 | 设计提案 | proposals/csharp-8.0/async-streams.md | 200 | 944A31911115989C |
| 112 | 设计提案 | proposals/csharp-8.0/async-using.md | 200 | 736B3B44E717F896 |
| 113 | 设计提案 | proposals/csharp-8.0/constraints-in-overrides.md | 200 | 52F0CB927603C99D |
| 114 | 设计提案 | proposals/csharp-8.0/constructed-unmanaged.md | 200 | FFEFFFC96AEA95F5 |
| 115 | 设计提案 | proposals/csharp-8.0/default-interface-methods.md | 200 | FEEBC11B0983AD0C |
| 116 | 设计提案 | proposals/csharp-8.0/nested-stackalloc.md | 200 | 27681981346BFB88 |
| 117 | 设计提案 | proposals/csharp-8.0/notnull-constraint.md | 200 | 206E23C382F1E635 |
| 118 | 设计提案 | proposals/csharp-8.0/null-coalescing-assignment.md | 200 | 957F0ADA0C05F6CC |
| 119 | 设计提案 | proposals/csharp-8.0/nullable-reference-types-specification.md | 200 | BF70C663712B133F |
| 120 | 设计提案 | proposals/csharp-8.0/nullable-reference-types.md | 200 | A1F6D12D52A0F94C |
| 121 | 设计提案 | proposals/csharp-8.0/obsolete-accessor.md | 200 | 880DF4EF7C852A90 |
| 122 | 设计提案 | proposals/csharp-8.0/patterns.md | 200 | 51AAB3FB3E9834EB |
| 123 | 设计提案 | proposals/csharp-8.0/ranges.md | 200 | 9419E0AC1A7AC530 |
| 124 | 设计提案 | proposals/csharp-8.0/README.md | 200 | E3B0C44298FC1C14 |
| 125 | 设计提案 | proposals/csharp-8.0/readonly-instance-members.md | 200 | 260C7B2E210548A5 |
| 126 | 设计提案 | proposals/csharp-8.0/shadowing-in-nested-functions.md | 200 | 3FDCA4A84CD81AFD |
| 127 | 设计提案 | proposals/csharp-8.0/static-local-functions.md | 200 | 5E621B3AF9BDB0CE |
| 128 | 设计提案 | proposals/csharp-8.0/unconstrained-null-coalescing.md | 200 | 3D5513C879B8C026 |
| 129 | 设计提案 | proposals/csharp-8.0/using.md | 200 | 85BE627F42D371F5 |
| 130 | 设计提案 | proposals/csharp-9.0/covariant-returns.md | 200 | D4CD5B8A1ABCB448 |
| 131 | 设计提案 | proposals/csharp-9.0/extending-partial-methods.md | 200 | 6152B1FD3CCC1C2B |
| 132 | 设计提案 | proposals/csharp-9.0/extension-getenumerator.md | 200 | 44BF6A810E9C49C8 |
| 133 | 设计提案 | proposals/csharp-9.0/function-pointers.md | 200 | BC90E02CF896D5E3 |
| 134 | 设计提案 | proposals/csharp-9.0/init.md | 200 | 2997466351804510 |
| 135 | 设计提案 | proposals/csharp-9.0/lambda-discard-parameters.md | 200 | FEB9A35B7764F82E |
| 136 | 设计提案 | proposals/csharp-9.0/local-function-attributes.md | 200 | 164BECB05FDB0FFD |
| 137 | 设计提案 | proposals/csharp-9.0/module-initializers.md | 200 | 4DB9EFAC47816E62 |
| 138 | 设计提案 | proposals/csharp-9.0/native-integers.md | 200 | D0B0B6F2A77880D7 |
| 139 | 设计提案 | proposals/csharp-9.0/nullable-constructor-analysis.md | 200 | 1EC805406BFDCF3F |
| 140 | 设计提案 | proposals/csharp-9.0/nullable-parameter-default-value-analysis.md | 200 | F5FF2EFDDCE4B2FF |
| 141 | 设计提案 | proposals/csharp-9.0/nullable-reference-types-specification.md | 200 | 42A167FADBEE48EF |
| 142 | 设计提案 | proposals/csharp-9.0/patterns3.md | 200 | CBCA25304CF78378 |
| 143 | 设计提案 | proposals/csharp-9.0/records.md | 200 | A3198542CB3DDBAD |
| 144 | 设计提案 | proposals/csharp-9.0/skip-localsinit.md | 200 | 5E8E7ED8C42A505E |
| 145 | 设计提案 | proposals/csharp-9.0/static-anonymous-functions.md | 200 | CA0EAE820F346368 |
| 146 | 设计提案 | proposals/csharp-9.0/target-typed-conditional-expression.md | 200 | 895488DEDC3D20D0 |
| 147 | 设计提案 | proposals/csharp-9.0/target-typed-new.md | 200 | 57832C908E368A18 |
| 148 | 设计提案 | proposals/csharp-9.0/top-level-statements.md | 200 | 91F3CE9825717877 |
| 149 | 设计提案 | proposals/csharp-9.0/unconstrained-type-parameter-annotations.md | 200 | F19E170E4E62CC7C |
| 150 | 设计提案 | proposals/csharp-9.0/variance-safety-for-static-interface-members.md | 200 | E564E49EDDD1C999 |
| 151 | 设计提案 | proposals/deconstruction-in-lambda-parameters.md | 200 | D570B0724A8D0156 |
| 152 | 设计提案 | proposals/dictionary-expressions.md | 200 | C8DB595C395AA2C4 |
| 153 | 设计提案 | proposals/enhanced-switch-statements.md | 200 | 9BDF911B96A3AC9B |
| 154 | 设计提案 | proposals/expand-ref.md | 200 | 1D6A9807522E2463 |
| 155 | 设计提案 | proposals/extension-constants.md | 200 | F9998C107E62C341 |
| 156 | 设计提案 | proposals/extension-members-on-typeless-receivers.md | 200 | 8956FE9BACA4DD56 |
| 157 | 设计提案 | proposals/extra-accessor-in-property-override.md | 200 | 10B0518FA1A834C7 |
| 158 | 设计提案 | proposals/factory-methods.md | 200 | 4D9505184F9C4AB2 |
| 159 | 设计提案 | proposals/fieldof.md | 200 | 89991F7BF64693EC |
| 160 | 设计提案 | proposals/final-initializers.md | 200 | FF8BA103908D54F7 |
| 161 | 设计提案 | proposals/immediately-enumerated-collection-expressions.md | 200 | BE7E5AA9EF4039F4 |
| 162 | 设计提案 | proposals/inactive/list-patterns-enumerables.md | 200 | B26EA0BACAD90787 |
| 163 | 设计提案 | proposals/inactive/pointer-null-coalescing.md | 200 | 90BA98A55C510119 |
| 164 | 设计提案 | proposals/inactive/README.md | 200 | E3B0C44298FC1C14 |
| 165 | 设计提案 | proposals/inactive/repeated-attributes.md | 200 | 5CC1083D7D1B5181 |
| 166 | 设计提案 | proposals/inference-for-constructor-calls.md | 200 | FF9D4D18639E561C |
| 167 | 设计提案 | proposals/inference-for-type-patterns.md | 200 | 539BCDF0C2F35CBF |
| 168 | 设计提案 | proposals/interpolated-string-handler-argument-value.md | 200 | B398C02DEBC157FE |
| 169 | 设计提案 | proposals/iterators-in-lambdas.md | 200 | 2B01F6B74C546052 |
| 170 | 设计提案 | proposals/left-right-join-in-query-expressions.md | 200 | 4E45A5E177764AD5 |
| 171 | 设计提案 | proposals/mixed-object-and-collection-initializers.md | 200 | EF5D53289862C727 |
| 172 | 设计提案 | proposals/multiple-using-var-discards.md | 200 | 4E99B46D5C8B72D1 |
| 173 | 设计提案 | proposals/null-conditional-await.md | 200 | 6553D88A573142BA |
| 174 | 设计提案 | proposals/partial-extension-members.md | 200 | 15AEA345A79C3648 |
| 175 | 设计提案 | proposals/pattern-variables.md | 200 | 2478811867F727A0 |
| 176 | 设计提案 | proposals/proposal-template.md | 200 | E8A5AF5AEB6A22EA |
| 177 | 设计提案 | proposals/README.md | 200 | FBE8275B41DA42D0 |
| 178 | 设计提案 | proposals/readonly-parameters.md | 200 | 43C7640A9CD3703C |
| 179 | 设计提案 | proposals/readonly-setter-calls-on-non-variables.md | 200 | F890CBAC8E1217DF |
| 180 | 设计提案 | proposals/ref-struct-closures.md | 200 | C10E204E205BF084 |
| 181 | 设计提案 | proposals/rejected/collection-expressions-in-foreach.md | 200 | B66208C3ECE0A18A |
| 182 | 设计提案 | proposals/rejected/declaration-expressions.md | 200 | DC22A9EDDE11BD62 |
| 183 | 设计提案 | proposals/rejected/discriminated-unions.md | 200 | 8CBE9D854126FF22 |
| 184 | 设计提案 | proposals/rejected/fixed-sized-buffers.md | 200 | A529E6E2AD776FC6 |
| 185 | 设计提案 | proposals/rejected/format.md | 200 | ACCC757A3CCE0182 |
| 186 | 设计提案 | proposals/rejected/interpolated-string-handler-method-names.md | 200 | 2A4DF4DF11B7473A |
| 187 | 设计提案 | proposals/rejected/intptr-operators.md | 200 | 5558C2A3A2D79F62 |
| 188 | 设计提案 | proposals/rejected/intrinsics.md | 200 | 3395F729847EF28C |
| 189 | 设计提案 | proposals/rejected/nullable-enhanced-common-type.md | 200 | E459A69B0001AEEB |
| 190 | 设计提案 | proposals/rejected/param-nullchecking.md | 200 | 4267F6C994140320 |
| 191 | 设计提案 | proposals/rejected/params-span.md | 200 | 2F9B8CBCD774AA1C |
| 192 | 设计提案 | proposals/rejected/README.md | 200 | E3B0C44298FC1C14 |
| 193 | 设计提案 | proposals/rejected/readonly-locals.md | 200 | 6CFEC6720F65FF65 |
| 194 | 设计提案 | proposals/rejected/records.md | 200 | CF9E206EFB0D5349 |
| 195 | 设计提案 | proposals/rejected/recordsv2.md | 200 | 8D10C818DE2A0070 |
| 196 | 设计提案 | proposals/rejected/self-constraint.md | 200 | 330A5718E3BC3096 |
| 197 | 设计提案 | proposals/rejected/static-delegates.md | 200 | A17D2706B949D629 |
| 198 | 设计提案 | proposals/relaxed-partial-ref-ordering.md | 200 | E4F26CAB7C8BE366 |
| 199 | 设计提案 | proposals/speclet-disclaimer.md | 200 | F186BAE4446EA616 |
| 200 | 设计提案 | proposals/standard-unions.md | 200 | 1125E2155C28127A |
| 201 | 设计提案 | proposals/target-typed-generic-type-inference.md | 200 | 9B65A66687C82560 |
| 202 | 设计提案 | proposals/target-typed-static-member-access.md | 200 | CA27A82CA3454972 |
| 203 | 设计提案 | proposals/top-level-members.md | 200 | C52FACA988EC338A |
| 204 | 设计提案 | proposals/type-parameter-inference-from-constraints.md | 200 | 417FB442D5746BA7 |
| 205 | 设计提案 | proposals/unsafe-evolution.md | 200 | 79EC9322D575123C |
| 206 | 设计提案 | proposals/unsigned-sizeof.md | 200 | 5E50C696634D253F |
| 207 | C# 标准 | standard/arrays.md | 200 | 95ACEDE4D6D86AFD |
| 208 | C# 标准 | standard/attributes.md | 200 | 85776F11D6EDBE1E |
| 209 | C# 标准 | standard/basic-concepts.md | 200 | B8EDE983B37C72BC |
| 210 | C# 标准 | standard/bibliography.md | 200 | A17B27AFB0E1394F |
| 211 | C# 标准 | standard/classes.md | 200 | 1388A106B05AD806 |
| 212 | C# 标准 | standard/conformance.md | 200 | E208FDF294267F0D |
| 213 | C# 标准 | standard/conversions.md | 200 | 1B816F3311AED8F0 |
| 214 | C# 标准 | standard/delegates.md | 200 | 9455F48C0C2B8994 |
| 215 | C# 标准 | standard/documentation-comments.md | 200 | CB005EEB27926C3A |
| 216 | C# 标准 | standard/enums.md | 200 | BE068DAA3BAC45E4 |
| 217 | C# 标准 | standard/exceptions.md | 200 | 5A94693E01E97376 |
| 218 | C# 标准 | standard/expressions.md | 200 | 86FD8AB8C2608D09 |
| 219 | C# 标准 | standard/foreword.md | 200 | 64EA0D258D624DC3 |
| 220 | C# 标准 | standard/general-description.md | 200 | 1405E97A944FC638 |
| 221 | C# 标准 | standard/grammar.md | 200 | 45576A05B0C828DD |
| 222 | C# 标准 | standard/interfaces.md | 200 | BCDF69F454A7BD58 |
| 223 | C# 标准 | standard/introduction.md | 200 | C95D5B0AE105F551 |
| 224 | C# 标准 | standard/lexical-structure.md | 200 | 48C42F6E5C23FECE |
| 225 | C# 标准 | standard/namespaces.md | 200 | 2916CEEEC2B45FEA |
| 226 | C# 标准 | standard/normative-references.md | 200 | F2D76606F94BCF6C |
| 227 | C# 标准 | standard/patterns.md | 200 | C924663998A1B72C |
| 228 | C# 标准 | standard/portability-issues.md | 200 | DFF5AD879A050128 |
| 229 | C# 标准 | standard/ranges.md | 200 | 6EF6B797D98FFE7A |
| 230 | C# 标准 | standard/README.md | 200 | 5B16290E4E9DFFAD |
| 231 | C# 标准 | standard/scope.md | 200 | D4DC8097FE72D4E9 |
| 232 | C# 标准 | standard/standard-library.md | 200 | F2241EA9AB444A1D |
| 233 | C# 标准 | standard/statements.md | 200 | 57C5D541EC5E3BF7 |
| 234 | C# 标准 | standard/structs.md | 200 | 53DE26BF64C1DDA0 |
| 235 | C# 标准 | standard/terms-and-definitions.md | 200 | 68569E0E19EDB4E3 |
| 236 | C# 标准 | standard/types.md | 200 | A48F6E3F35AAD9A2 |
| 237 | C# 标准 | standard/unsafe-code.md | 200 | 6DBA834B33336BE7 |
| 238 | C# 标准 | standard/variables.md | 200 | 645B44FE578AC721 |
| 239 | 参考手册 | language-reference/builtin-types/nullable-reference-types.md | 200 | 5CB40828CB273F4E |
| 240 | 参考手册 | language-reference/builtin-types/nullable-value-types.md | 200 | A8F20A2FBBF7C247 |
| 241 | 参考手册 | language-reference/builtin-types/ref-struct.md | 200 | 1C2D92E22208EF40 |
| 242 | 参考手册 | language-reference/builtin-types/reference-types.md | 200 | C8DFC045799D4B6F |
| 243 | 参考手册 | language-reference/builtin-types/value-types.md | 200 | D76C7F8E21674A72 |
| 244 | 参考手册 | language-reference/compiler-messages/generic-type-parameters-errors.md | 200 | 231F2FFF9B3A8FE2 |
| 245 | 参考手册 | language-reference/operators/delegate-operator.md | 200 | AA0CD73AD8F05FE4 |
| 246 | 参考手册 | language-reference/operators/user-defined-conversion-operators.md | 200 | 2CFC3C5197D76C49 |
| 247 | 参考手册 | language-reference/attributes/nullable-analysis.md | 200 | 18015539663F8B30 |
| 248 | 参考手册 | language-reference/attributes/pseudo-attributes.md | 200 | A622203AFC983D04 |
| 249 | 参考手册 | language-reference/builtin-types/arrays.md | 200 | 49D386C42421C1EE |
| 250 | 参考手册 | language-reference/builtin-types/built-in-types.md | 200 | D4E7FFD8E02ADE8C |
| 251 | 参考手册 | language-reference/builtin-types/default-values.md | 200 | DD0BB3B2514F1681 |
| 252 | 参考手册 | language-reference/builtin-types/floating-point-numeric-types.md | 200 | A9DDF5D289DC8B3F |
| 253 | 参考手册 | language-reference/builtin-types/integral-numeric-types.md | 200 | 00E8C2D0F6B1EAB8 |
| 254 | 参考手册 | language-reference/builtin-types/numeric-conversions.md | 200 | 8006C070E1F83BF9 |
| 255 | 参考手册 | language-reference/builtin-types/record.md | 200 | 25AB1532D9B242EB |
| 256 | 参考手册 | language-reference/builtin-types/struct.md | 200 | E87E19371576853C |
| 257 | 参考手册 | language-reference/builtin-types/unmanaged-types.md | 200 | FA3F737BCB940686 |
| 258 | 参考手册 | language-reference/builtin-types/value-tuples.md | 200 | A156CA3BE439C219 |
| 259 | 参考手册 | language-reference/compiler-messages/readonly-struct-errors.md | 200 | 9EC68283F49C5721 |
| 260 | 参考手册 | language-reference/compiler-messages/ref-struct-errors.md | 200 | ADDB3248EC830633 |
| 261 | 参考手册 | language-reference/keywords/method-parameters.md | 200 | BE56D89E866C1D44 |
| 262 | 参考手册 | language-reference/keywords/reference-types.md | 200 | B64707709FBB38D3 |
| 263 | 参考手册 | language-reference/keywords/where-generic-type-constraint.md | 200 | 722C77BF17E8F1D6 |
| 264 | 参考手册 | language-reference/operators/addition-operator.md | 200 | EE06C78060D5E951 |
| 265 | 参考手册 | language-reference/operators/arithmetic-operators.md | 200 | 168D744ABE0D5A56 |
| 266 | 参考手册 | language-reference/operators/assignment-operator.md | 200 | 649B69003DFBA162 |
| 267 | 参考手册 | language-reference/operators/bitwise-and-shift-operators.md | 200 | 0C6689A55BA4D33F |
| 268 | 参考手册 | language-reference/operators/boolean-logical-operators.md | 200 | B9322FBEE5F83AEB |
| 269 | 参考手册 | language-reference/operators/comparison-operators.md | 200 | B5CF894E44980D08 |
| 270 | 参考手册 | language-reference/operators/conditional-operator.md | 200 | EAFBB8B325B3AB94 |
| 271 | 参考手册 | language-reference/operators/deconstruction.md | 200 | 8552F16E65B857F5 |
| 272 | 参考手册 | language-reference/operators/equality-operators.md | 200 | 858A4F2A19EE5308 |
| 273 | 参考手册 | language-reference/operators/lambda-operator.md | 200 | A66BEB2B24BD531B |
| 274 | 参考手册 | language-reference/operators/member-access-operators.md | 200 | 63F36E4C870B976B |
| 275 | 参考手册 | language-reference/operators/new-operator.md | 200 | 4812D118939C61CD |
| 276 | 参考手册 | language-reference/operators/null-coalescing-operator.md | 200 | 098112B9561FC0EE |
| 277 | 参考手册 | language-reference/operators/operator-overloading.md | 200 | B747756C3EF3F0E6 |
| 278 | 参考手册 | language-reference/operators/pointer-related-operators.md | 200 | CEF8F1CD9DF61124 |
| 279 | 参考手册 | language-reference/operators/sizeof.md | 200 | 73612B16C7984A46 |
| 280 | 参考手册 | language-reference/operators/stackalloc.md | 200 | ED71554843CD8457 |
| 281 | 参考手册 | language-reference/operators/subtraction-operator.md | 200 | 850B09E1B861765A |
| 282 | 参考手册 | language-reference/operators/true-false-operators.md | 200 | 2EE45BE59C765577 |
| 283 | 参考手册 | language-reference/operators/type-testing-and-cast.md | 200 | B6243094DF0765D7 |
| 284 | 参考手册 | language-reference/attributes/caller-information.md | 200 | 28B8B3E47F1D5086 |
| 285 | 参考手册 | language-reference/attributes/general.md | 200 | 5EBE33611519D7B0 |
| 286 | 参考手册 | language-reference/attributes/global.md | 200 | 27439EDBF26561A5 |
| 287 | 参考手册 | language-reference/builtin-types/bool.md | 200 | B79590132EB294A7 |
| 288 | 参考手册 | language-reference/builtin-types/char.md | 200 | DF2167A9E62D6089 |
| 289 | 参考手册 | language-reference/builtin-types/collections.md | 200 | 5D7F7A913FBF5BB3 |
| 290 | 参考手册 | language-reference/builtin-types/enum.md | 200 | 5D9B0282098F4398 |
| 291 | 参考手册 | language-reference/builtin-types/string-operations.md | 200 | A51615BA0811B246 |
| 292 | 参考手册 | language-reference/builtin-types/union.md | 200 | CE4CE719781D0DC9 |
| 293 | 参考手册 | language-reference/builtin-types/void.md | 200 | 215259B8CFDCF090 |
| 294 | 参考手册 | language-reference/compiler-messages/array-declaration-errors.md | 200 | ED83FF20DCA7FE00 |
| 295 | 参考手册 | language-reference/compiler-messages/assembly-references.md | 200 | E5234A075169B005 |
| 296 | 参考手册 | language-reference/compiler-messages/attribute-usage-errors.md | 200 | C0229D16592A41EC |
| 297 | 参考手册 | language-reference/compiler-messages/constructor-errors.md | 200 | 228764017960E1E7 |
| 298 | 参考手册 | language-reference/compiler-messages/deconstruction-errors.md | 200 | D62624B325F7A1FE |
| 299 | 参考手册 | language-reference/compiler-messages/delegate-function-pointer-diagnostics.md | 200 | A24D2F78D5D5582C |
| 300 | 参考手册 | language-reference/compiler-messages/dynamic-type-and-binding-errors.md | 200 | 37A45837DC78D487 |
| 301 | 参考手册 | language-reference/compiler-messages/file-local-types.md | 200 | CF63F278540FC723 |
| 302 | 参考手册 | language-reference/compiler-messages/inline-array-errors.md | 200 | 9D7CBDF863F2E790 |
| 303 | 参考手册 | language-reference/compiler-messages/interface-implementation-errors.md | 200 | 95CDD8386A14B0DB |
| 304 | 参考手册 | language-reference/compiler-messages/new-object-creation-errors.md | 200 | 3F91CB56A13E90B2 |
| 305 | 参考手册 | language-reference/compiler-messages/nullable-warnings.md | 200 | 96450D7E83AF0B21 |
| 306 | 参考手册 | language-reference/compiler-messages/overloaded-operator-errors.md | 200 | 894EDC626D560E88 |
| 307 | 参考手册 | language-reference/compiler-messages/parameter-argument-mismatch.md | 200 | 9D0188D22EE6ECD0 |
| 308 | 参考手册 | language-reference/compiler-messages/params-arrays.md | 200 | AE7C61B73E2B5442 |
| 309 | 参考手册 | language-reference/compiler-messages/record-declaration-errors.md | 200 | 4F11D596DC51CECD |
| 310 | 参考手册 | language-reference/compiler-messages/ref-modifiers-errors.md | 200 | C550BC72858B2D18 |
| 311 | 参考手册 | language-reference/compiler-messages/ref-safety-errors.md | 200 | 890C1A3419F3A491 |
| 312 | 参考手册 | language-reference/compiler-messages/static-abstract-interfaces.md | 200 | A48675061CF38557 |
| 313 | 参考手册 | language-reference/compiler-messages/unsafe-code-errors.md | 200 | C320175864211874 |
| 314 | 参考手册 | language-reference/keywords/class.md | 200 | 9AB1286F5B36B1C9 |
| 315 | 参考手册 | language-reference/keywords/in-generic-modifier.md | 200 | 699483E2DFD3EFB6 |
| 316 | 参考手册 | language-reference/keywords/interface.md | 200 | C96DEB6CCF184DFC |
| 317 | 参考手册 | language-reference/keywords/out-generic-modifier.md | 200 | 5975CC0BE1430B65 |
| 318 | 参考手册 | language-reference/keywords/partial-type.md | 200 | C0DCA1097C2CD93A |
| 319 | 参考手册 | language-reference/keywords/readonly.md | 200 | 10548199AFF7A185 |
| 320 | 参考手册 | language-reference/keywords/ref.md | 200 | 0417BEF4CED4C9C7 |
| 321 | 参考手册 | language-reference/keywords/unsafe.md | 200 | 66A75E2D5CA0135D |
| 322 | 参考手册 | language-reference/keywords/value.md | 200 | 5B3590993D197449 |
| 323 | 参考手册 | language-reference/operators/await.md | 200 | 68752C4374292E8A |
| 324 | 参考手册 | language-reference/operators/collection-expressions.md | 200 | AB58E19BBEA815A3 |
| 325 | 参考手册 | language-reference/operators/default.md | 200 | 60F2589CF599D218 |
| 326 | 参考手册 | language-reference/operators/index.md | 200 | E48C9EE8136E691B |
| 327 | 参考手册 | language-reference/operators/is.md | 200 | 5E53C3EDD0148A47 |
| 328 | 参考手册 | language-reference/operators/lambda-expressions.md | 200 | DD3C7B7D0E220B76 |
| 329 | 参考手册 | language-reference/operators/nameof.md | 200 | 790623B3F5554CF1 |
| 330 | 参考手册 | language-reference/operators/namespace-alias-qualifier.md | 200 | 0D5A83C488D0CFD3 |
| 331 | 参考手册 | language-reference/operators/null-forgiving.md | 200 | D4B3EEDB4F6F3A19 |
| 332 | 参考手册 | language-reference/operators/patterns.md | 200 | 3BDA790A6327D1C0 |
| 333 | 参考手册 | language-reference/operators/switch-expression.md | 200 | 42AAA8C5A8A09FB9 |
| 334 | 参考手册 | language-reference/operators/with-expression.md | 200 | 3607189DEE309EB6 |
| 335 | 参考手册 | language-reference/statements/fixed.md | 200 | 7A04AAD694171CC2 |
| 336 | 参考手册 | language-reference/unsafe-code.md | 200 | 2067F5674189ADFA |
| 337 | 参考手册 | language-reference/compiler-messages/async-await-errors.md | 200 | AF577A385A8F0821 |
| 338 | 参考手册 | language-reference/compiler-messages/cs0001.md | 200 | 4E7E17AEAF016C80 |
| 339 | 参考手册 | language-reference/compiler-messages/cs0015.md | 200 | D837FC70E961A355 |
| 340 | 参考手册 | language-reference/compiler-messages/cs0019.md | 200 | 4086140293C31393 |
| 341 | 参考手册 | language-reference/compiler-messages/cs0029.md | 200 | 23CE7533411BDB4F |
| 342 | 参考手册 | language-reference/compiler-messages/cs0038.md | 200 | C823333206F257DD |
| 343 | 参考手册 | language-reference/compiler-messages/cs0039.md | 200 | D8CA9FE5AEAF0220 |
| 344 | 参考手册 | language-reference/compiler-messages/cs0050.md | 200 | 769F30661047EE73 |
| 345 | 参考手册 | language-reference/compiler-messages/cs0051.md | 200 | BCCCFF36EF85236A |
| 346 | 参考手册 | language-reference/compiler-messages/cs0052.md | 200 | 16375B0393F9AACA |
| 347 | 参考手册 | language-reference/compiler-messages/cs0103.md | 200 | 038DD12BE61D2B5C |
| 348 | 参考手册 | language-reference/compiler-messages/cs0108.md | 200 | 983FD6D933FDA198 |
| 349 | 参考手册 | language-reference/compiler-messages/cs0115.md | 200 | 6BDC9DB48ADA7501 |
| 350 | 参考手册 | language-reference/compiler-messages/cs0120.md | 200 | 3BD8FDC01287C531 |
| 351 | 参考手册 | language-reference/compiler-messages/cs0122.md | 200 | 989A133019444E82 |
| 352 | 参考手册 | language-reference/compiler-messages/cs0134.md | 200 | CCA6558AEDE6BF89 |
| 353 | 参考手册 | language-reference/compiler-messages/cs0151.md | 200 | A52E09CE0485A509 |
| 354 | 参考手册 | language-reference/compiler-messages/cs0165.md | 200 | 8B22AE386AB6A021 |
| 355 | 参考手册 | language-reference/compiler-messages/cs0173.md | 200 | 216E0DA2B1109A67 |
| 356 | 参考手册 | language-reference/compiler-messages/cs0201.md | 200 | D203B2AA5844FC07 |
| 357 | 参考手册 | language-reference/compiler-messages/cs0229.md | 200 | 2B9571543F034729 |
| 358 | 参考手册 | language-reference/compiler-messages/cs0266.md | 200 | 36BAD79442F911A2 |
| 359 | 参考手册 | language-reference/compiler-messages/cs0269.md | 200 | AF9536AD64116D94 |
| 360 | 参考手册 | language-reference/compiler-messages/cs0420.md | 200 | 01ECB090CF227111 |
| 361 | 参考手册 | language-reference/compiler-messages/cs0429.md | 200 | EC9E1A4A21963EA2 |
| 362 | 参考手册 | language-reference/compiler-messages/cs0433.md | 200 | 5BD0F4E1DB73889A |
| 363 | 参考手册 | language-reference/compiler-messages/cs0445.md | 200 | 899E60B7D109E2DE |
| 364 | 参考手册 | language-reference/compiler-messages/cs0465.md | 200 | 56D58154519BD556 |
| 365 | 参考手册 | language-reference/compiler-messages/cs0504.md | 200 | B26B9CCA217AF0DB |
| 366 | 参考手册 | language-reference/compiler-messages/cs0507.md | 200 | 17B697B2C0DBF54F |
| 367 | 参考手册 | language-reference/compiler-messages/cs0523.md | 200 | 13531FB4E600604E |
| 368 | 参考手册 | language-reference/compiler-messages/cs0570.md | 200 | 389F1B0F67594886 |
| 369 | 参考手册 | language-reference/compiler-messages/cs0618.md | 200 | 7F5D52CDEC548E8E |
| 370 | 参考手册 | language-reference/compiler-messages/cs0675.md | 200 | 7B54D943B58317C0 |
| 371 | 参考手册 | language-reference/compiler-messages/cs0731.md | 200 | FB4943F2E458BC58 |
| 372 | 参考手册 | language-reference/compiler-messages/cs1001.md | 200 | EABBB2B3B6DEF00F |
| 373 | 参考手册 | language-reference/compiler-messages/cs1026.md | 200 | 0DCD1C89A21D7622 |
| 374 | 参考手册 | language-reference/compiler-messages/cs1058.md | 200 | D9016437EC7ACE5A |
| 375 | 参考手册 | language-reference/compiler-messages/cs1060.md | 200 | 8AA562CCDCE3C6C4 |
| 376 | 参考手册 | language-reference/compiler-messages/cs1061.md | 200 | 2B689FEACC522F0B |
| 377 | 参考手册 | language-reference/compiler-messages/cs1519.md | 200 | 0C0E373721CE7785 |
| 378 | 参考手册 | language-reference/compiler-messages/cs1540.md | 200 | 10A1AC9F33A403A6 |
| 379 | 参考手册 | language-reference/compiler-messages/cs1548.md | 200 | D133B0BB14B368EB |
| 380 | 参考手册 | language-reference/compiler-messages/cs1567.md | 200 | 6CC9642CC6F733EF |
| 381 | 参考手册 | language-reference/compiler-messages/cs1591.md | 200 | 5B0E1EBB19C73711 |
| 382 | 参考手册 | language-reference/compiler-messages/cs1598.md | 200 | F9DDC04A4136680F |
| 383 | 参考手册 | language-reference/compiler-messages/cs1607.md | 200 | 467ED1DC82613F0D |
| 384 | 参考手册 | language-reference/compiler-messages/cs1610.md | 200 | D0FECEC7A5D9EAA8 |
| 385 | 参考手册 | language-reference/compiler-messages/cs1612.md | 200 | DA55BBE8A83C8FCA |
| 386 | 参考手册 | language-reference/compiler-messages/cs1644.md | 200 | 83CD2A02F44DA1A0 |
| 387 | 参考手册 | language-reference/compiler-messages/cs1658.md | 200 | FB0DAFC53A4C8E3D |
| 388 | 参考手册 | language-reference/compiler-messages/cs1685.md | 200 | F570EED90BA7A4A4 |
| 389 | 参考手册 | language-reference/compiler-messages/cs1690.md | 200 | 59E121D9B3C4FC2C |
| 390 | 参考手册 | language-reference/compiler-messages/cs1699.md | 200 | 3BA9514CBFCA5B01 |
| 391 | 参考手册 | language-reference/compiler-messages/cs1700.md | 200 | 25345E5B6824E45D |
| 392 | 参考手册 | language-reference/compiler-messages/cs1701.md | 200 | 657DE56767E1A616 |
| 393 | 参考手册 | language-reference/compiler-messages/cs1703.md | 200 | F2D943746066F733 |
| 394 | 参考手册 | language-reference/compiler-messages/cs1705.md | 200 | CE48124F33938334 |
| 395 | 参考手册 | language-reference/compiler-messages/cs1721.md | 200 | E240AEAEE6A8449A |
| 396 | 参考手册 | language-reference/compiler-messages/cs1726.md | 200 | C7F72376EE658B56 |
| 397 | 参考手册 | language-reference/compiler-messages/cs1729.md | 200 | AE2B1348AAE12989 |
| 398 | 参考手册 | language-reference/compiler-messages/cs1736.md | 200 | 93939745F0B5151B |
| 399 | 参考手册 | language-reference/compiler-messages/cs1737.md | 200 | 77F653E64FCDFACA |
| 400 | 参考手册 | language-reference/compiler-messages/cs1750.md | 200 | 07DC4C905474A910 |
| 401 | 参考手册 | language-reference/compiler-messages/cs1762.md | 200 | A3D4F45172A93000 |
| 402 | 参考手册 | language-reference/compiler-messages/cs1926.md | 200 | 958C8210646FF841 |
| 403 | 参考手册 | language-reference/compiler-messages/cs1933.md | 200 | D1DC909FAF7BF849 |
| 404 | 参考手册 | language-reference/compiler-messages/cs1936.md | 200 | E7FB0C26EEA46239 |
| 405 | 参考手册 | language-reference/compiler-messages/cs1941.md | 200 | FA25F218E05947F2 |
| 406 | 参考手册 | language-reference/compiler-messages/cs1942.md | 200 | 207D963994E411C1 |
| 407 | 参考手册 | language-reference/compiler-messages/cs1943.md | 200 | 3F96EA8D662400AF |
| 408 | 参考手册 | language-reference/compiler-messages/cs1956.md | 200 | C1608BD88246E008 |
| 409 | 参考手册 | language-reference/compiler-messages/cs3003.md | 200 | 98A816A303B9D2ED |
| 410 | 参考手册 | language-reference/compiler-messages/cs3009.md | 200 | 269ECD0F8636DFB4 |
| 411 | 参考手册 | language-reference/compiler-messages/cs7003.md | 200 | 1076BE6E9AB2695C |
| 412 | 参考手册 | language-reference/compiler-messages/cs7034.md | 200 | 963E590E7D3D949A |
| 413 | 参考手册 | language-reference/compiler-messages/cs7035.md | 200 | 7EE6C5DDF2D51907 |
| 414 | 参考手册 | language-reference/compiler-messages/cs8070.md | 200 | 40AD7345DA8481C2 |
| 415 | 参考手册 | language-reference/compiler-messages/cs8146.md | 200 | 70AE790EC67082D7 |
| 416 | 参考手册 | language-reference/compiler-messages/cs8147.md | 200 | 9D3C562BB42B9E92 |
| 417 | 参考手册 | language-reference/compiler-messages/cs8148.md | 200 | 9B813607E8F353B6 |
| 418 | 参考手册 | language-reference/compiler-messages/cs8149.md | 200 | C3F9CD324E3C1C95 |
| 419 | 参考手册 | language-reference/compiler-messages/cs8150.md | 200 | 97BEE6476CB7F1BB |
| 420 | 参考手册 | language-reference/compiler-messages/cs8151.md | 200 | 6B2414A7342998B5 |
| 421 | 参考手册 | language-reference/compiler-messages/cs8152.md | 200 | 36E1203D32A192E5 |
| 422 | 参考手册 | language-reference/compiler-messages/cs8156.md | 200 | 92614FA1002A8061 |
| 423 | 参考手册 | language-reference/compiler-messages/cs8157.md | 200 | 521FD6F271DF72A2 |
| 424 | 参考手册 | language-reference/compiler-messages/cs8158.md | 200 | 86E3D894DF7B9B80 |
| 425 | 参考手册 | language-reference/compiler-messages/cs8159.md | 200 | 6BD4540D0BE89A95 |
| 426 | 参考手册 | language-reference/compiler-messages/cs8160.md | 200 | 8B228187293ACA62 |
| 427 | 参考手册 | language-reference/compiler-messages/cs8161.md | 200 | 04A6CBD0658718D1 |
| 428 | 参考手册 | language-reference/compiler-messages/cs8162.md | 200 | 01C67C57A4916102 |
| 429 | 参考手册 | language-reference/compiler-messages/cs8163.md | 200 | 1D247F48DE9F7B7F |
| 430 | 参考手册 | language-reference/compiler-messages/cs8170.md | 200 | 25996202ECDA572B |
| 431 | 参考手册 | language-reference/compiler-messages/cs8171.md | 200 | 15A1CF9739E3504D |
| 432 | 参考手册 | language-reference/compiler-messages/cs8172.md | 200 | 2DE65E12C777DBCB |
| 433 | 参考手册 | language-reference/compiler-messages/cs8173.md | 200 | 7EB8204BF9B025DF |
| 434 | 参考手册 | language-reference/compiler-messages/cs8174.md | 200 | C21EB0F8871EDC57 |
| 435 | 参考手册 | language-reference/compiler-messages/cs8333.md | 200 | BDE48C2BE58E38A6 |
| 436 | 参考手册 | language-reference/compiler-messages/cs8334.md | 200 | 9D90519516472823 |
| 437 | 参考手册 | language-reference/compiler-messages/cs8346.md | 200 | 57EFD81067FE7963 |
| 438 | 参考手册 | language-reference/compiler-messages/cs8347.md | 200 | BB3466B305E7B863 |
| 439 | 参考手册 | language-reference/compiler-messages/cs8352.md | 200 | 35257E5E61450DC5 |
| 440 | 参考手册 | language-reference/compiler-messages/cs8354.md | 200 | 3C9A3307969A2868 |
| 441 | 参考手册 | language-reference/compiler-messages/cs8355.md | 200 | B8C2ECC8ECCAC67D |
| 442 | 参考手册 | language-reference/compiler-messages/cs9043.md | 200 | 901230AABFBB1D55 |
| 443 | 参考手册 | language-reference/compiler-messages/entry-point-errors.md | 200 | B710EC5835F58228 |
| 444 | 参考手册 | language-reference/compiler-messages/expression-form-restrictions.md | 200 | B0746AB37EDEC77E |
| 445 | 参考手册 | language-reference/compiler-messages/expression-tree-restrictions.md | 200 | 06E5F97076DCBCA6 |
| 446 | 参考手册 | language-reference/compiler-messages/extension-declarations.md | 200 | 822A68DD37593B80 |
| 447 | 参考手册 | language-reference/compiler-messages/feature-version-errors.md | 200 | 6192AA083EDB55FE |
| 448 | 参考手册 | language-reference/compiler-messages/foreach-diagnostics.md | 200 | 9C3776A5DAD52B42 |
| 449 | 参考手册 | language-reference/compiler-messages/index.md | 200 | 99A73107C62C5294 |
| 450 | 参考手册 | language-reference/compiler-messages/indexer-access-errors.md | 200 | B2D17B806B9E0813 |
| 451 | 参考手册 | language-reference/compiler-messages/invalid-build-command-line.md | 200 | FEB080CD9BC24D32 |
| 452 | 参考手册 | language-reference/compiler-messages/iterator-yield.md | 200 | 7D94A2C68FB160A6 |
| 453 | 参考手册 | language-reference/compiler-messages/jump-statement-errors.md | 200 | C8B5DC2329CD5F41 |
| 454 | 参考手册 | language-reference/compiler-messages/lambda-expression-errors.md | 200 | E92D17D20F06F405 |
| 455 | 参考手册 | language-reference/compiler-messages/local-function-errors.md | 200 | B09BB3A7E359F2BA |
| 456 | 参考手册 | language-reference/compiler-messages/lock-semantics.md | 200 | 560E9CF64B17C8A3 |
| 457 | 参考手册 | language-reference/compiler-messages/overload-resolution.md | 200 | 06E2BD34DB0E7F12 |
| 458 | 参考手册 | language-reference/compiler-messages/parsing-errors.md | 200 | 71C15F0B8F77C927 |
| 459 | 参考手册 | language-reference/compiler-messages/partial-declarations.md | 200 | FA7817731ACDB446 |
| 460 | 参考手册 | language-reference/compiler-messages/pattern-matching-warnings.md | 200 | 50A122F0507AB8F2 |
| 461 | 参考手册 | language-reference/compiler-messages/preprocessor-errors.md | 200 | E06B285FDC57D172 |
| 462 | 参考手册 | language-reference/compiler-messages/property-declaration-errors.md | 200 | 4EDDDC5A08F2A8C3 |
| 463 | 参考手册 | language-reference/compiler-messages/source-generator-errors.md | 200 | 3CC778E96D9089F2 |
| 464 | 参考手册 | language-reference/compiler-messages/string-interpolations.md | 200 | BD90EC6F871445B0 |
| 465 | 参考手册 | language-reference/compiler-messages/string-literal.md | 200 | 8CB1A85840218E93 |
| 466 | 参考手册 | language-reference/compiler-messages/tuple-errors.md | 200 | 0410BADE7F533886 |
| 467 | 参考手册 | language-reference/compiler-messages/union-declaration-errors.md | 200 | F33CB2FAF56E0ED2 |
| 468 | 参考手册 | language-reference/compiler-messages/using-directive-errors.md | 200 | DB418C319D40B0FD |
| 469 | 参考手册 | language-reference/compiler-messages/using-statement-declaration-errors.md | 200 | CF9760B5C0DEE58F |
| 470 | 参考手册 | language-reference/compiler-messages/warning-waves.md | 200 | 4CECFDFCBA244081 |
| 471 | 参考手册 | language-reference/compiler-options/advanced.md | 200 | 9E8C652B47EF9BE2 |
| 472 | 参考手册 | language-reference/compiler-options/code-generation.md | 200 | DF2305525DC1D4AB |
| 473 | 参考手册 | language-reference/compiler-options/errors-warnings.md | 200 | FC020AA201264CE0 |
| 474 | 参考手册 | language-reference/compiler-options/index.md | 200 | 73D1F739E350EED2 |
| 475 | 参考手册 | language-reference/compiler-options/inputs.md | 200 | C89D0BF6DF6F3AB3 |
| 476 | 参考手册 | language-reference/compiler-options/language.md | 200 | 8B8C4A08EC1A8D99 |
| 477 | 参考手册 | language-reference/compiler-options/miscellaneous.md | 200 | 009445C5DF58EA02 |
| 478 | 参考手册 | language-reference/compiler-options/output.md | 200 | 4F485475D2854EA2 |
| 479 | 参考手册 | language-reference/compiler-options/resources.md | 200 | 6C2CBEACB6742489 |
| 480 | 参考手册 | language-reference/compiler-options/security.md | 200 | FAB59D7C188EBB40 |
| 481 | 参考手册 | language-reference/configure-language-version.md | 200 | 6C70060DD4298BCA |
| 482 | 参考手册 | language-reference/includes/default-langversion-table.md | 200 | 27D795503885137C |
| 483 | 参考手册 | language-reference/includes/initial-version.md | 200 | 3281BA76657CE64E |
| 484 | 参考手册 | language-reference/includes/langversion-table.md | 200 | 4D8CA0DEBEA96E2E |
| 485 | 参考手册 | language-reference/keywords/abstract.md | 200 | CFB61A1A8587A8E9 |
| 486 | 参考手册 | language-reference/keywords/access-modifiers.md | 200 | 5E2F967AFF15A5CF |
| 487 | 参考手册 | language-reference/keywords/accessibility-domain.md | 200 | 6FE9D9DE9CEC3F33 |
| 488 | 参考手册 | language-reference/keywords/accessibility-levels.md | 200 | 03FE0FA3C316B7F2 |
| 489 | 参考手册 | language-reference/keywords/add.md | 200 | F8319B788F3A5F5B |
| 490 | 参考手册 | language-reference/keywords/ascending.md | 200 | 2AFBEBB1A19B13BC |
| 491 | 参考手册 | language-reference/keywords/async.md | 200 | 226D1308CCE25B03 |
| 492 | 参考手册 | language-reference/keywords/base.md | 200 | 1EA70F7C7A5729EF |
| 493 | 参考手册 | language-reference/keywords/by.md | 200 | 2FCB10943D479EC0 |
| 494 | 参考手册 | language-reference/keywords/closed.md | 200 | BF2201B09EA78F00 |
| 495 | 参考手册 | language-reference/keywords/const.md | 200 | 39DF0DD4214086E9 |
| 496 | 参考手册 | language-reference/keywords/default.md | 200 | B9A88C24BB63C345 |
| 497 | 参考手册 | language-reference/keywords/descending.md | 200 | C606A3888E8673C5 |
| 498 | 参考手册 | language-reference/keywords/equals.md | 200 | CE90548FDA09B630 |
| 499 | 参考手册 | language-reference/keywords/event.md | 200 | 32CD6A7DBA1ACBF4 |
| 500 | 参考手册 | language-reference/keywords/extension.md | 200 | A7CA154C06C70601 |

## 审计结果

- 结果条数：500。
- 期望条数：500。
- HTTP 200：500。
- 失败响应：0。
- 相对路径唯一数：500。
- 轮次与预先固定的来源列表顺序一致：是。

本报告把 C# 设计提案与 C# 标准/参考手册区分为不同证据等级：提案可说明设计意图、草案与限制；标准和已发布参考手册用于语言行为结论。任何运行时对象尺寸、缓存命中或 GC 成本需要额外依赖具体运行时源码和实测，不能从语言文件本身推导。

