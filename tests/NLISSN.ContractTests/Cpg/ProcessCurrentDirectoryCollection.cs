using Xunit;

namespace RoslynPrototype.Tests;

// CWD 是进程级状态。任何依赖 Directory.SetCurrentDirectory() 的用例都必须归入本集合，
// 否则它们会与并行运行的其他集合并发改写 CWD；而那些集合把相对路径交给
// Path.GetFullPath 解析（见 NLCPGBuildContext.Create），于是会读到一个临时目录。
// DisableParallelization 使本集合独占运行，且不与任何其他集合重叠。
[CollectionDefinition("ProcessCurrentDirectory", DisableParallelization = true)]
public sealed class ProcessCurrentDirectoryCollection
{
}
