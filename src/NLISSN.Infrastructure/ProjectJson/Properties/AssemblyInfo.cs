using System.Runtime.CompilerServices;

// 单文档分片的接缝（ProjectJsonExporter.BuildDocumentShardProjections）与分片 DTO 是 internal：
// 契约测试必须直接调用**生产实现**来断言「分片不改变任何一条记录的取值」，
// 而不是在测试里复制一份分片逻辑（那样只能证明副本自洽）。
[assembly: InternalsVisibleTo("RoslynDeletionPrototype.ContractTests")]
