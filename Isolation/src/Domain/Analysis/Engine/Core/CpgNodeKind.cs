namespace Domain.Analysis.Engine.Core;

/// <summary>
/// 定义当前内存态 CPG 使用的核心节点类型。
///
/// 这里故意沿用 Joern 的核心 schema 命名习惯，而不是改成 Roslyn 的对象名，
/// 原因是当前阶段的目标不是“复刻 Roslyn 对象树”，而是“先做一个和 Joern
/// 核心概念接近的最小 CPG”。
///
/// 当前先使用闭集枚举，有三个考虑：
/// 1. 目前只做 C#，节点集合相对可控。
/// 2. 闭集比字符串标签更容易让 pass 保持一致。
/// 3. 真正需要扩展时，应该通过 schema 演化，而不是运行时随意塞新值。
/// </summary>
public enum CpgNodeKind
{
    Unknown = 0,
    MetaData,
    File,
    ConfigFile,
    Namespace,
    NamespaceBlock,
    Import,
    Tag,
    Type,
    TypeDecl,
    Method,
    MethodParameterIn,
    MethodParameterOut,
    MethodReturn,
    Member,
    Local,
    Identifier,
    MethodRef,
    Literal,
    Call,
    ControlStructure,
    Block,
}
