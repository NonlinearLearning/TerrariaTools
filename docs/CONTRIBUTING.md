# 贡献指南 (CONTRIBUTING)

感谢您对 **TerrariaTools** 的关注！我们欢迎社区成员提交 PR、报告 Issue 或改进文档。

## **1. 开发环境准备**

### **必备工具**
- **IDE**: Visual Studio 2022 (推荐) 或 JetBrains Rider。
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- **Git**: 用于版本控制。

### **初次设置**
1. 克隆仓库:
   ```bash
   git clone https://github.com/your-repo/TerrariaTools.git
   cd TerrariaTools
   ```
2. 还原依赖:
   ```bash
   dotnet restore
   ```
3. 编译项目:
   ```bash
   dotnet build
   ```

## **2. 核心架构理解**

在开始编码前，请务必阅读以下文档：
- **[架构设计 (ARCHITECTURE.md)](ARCHITECTURE.md)**: 理解分层架构（Services, Configuration, Refactorers）。
- **[设计理念 (DESIGN_CONCEPTS.md)](DESIGN_CONCEPTS.md)**: 理解最小化重构的核心思想。

## **3. 编码规范**

- **命名**: 遵循标准的 C# 命名约定（PascalCase 用于类/方法，camelCase 用于局部变量）。
- **依赖注入**:
  - 优先通过构造函数注入依赖。
  - 文件 IO 操作必须使用 `IWorkspaceLoader` 接口，禁止直接调用 `System.IO.File`。
- **配置**:
  - 新增的可配置项应添加到 `RefactoringSettings` 类，并在 `appsettings.json` 中提供默认值。
- **异步**: 所有的 IO 密集型操作必须是异步的 (`async/await`)。

## **4. 测试要求**

本项目严格要求单元测试覆盖。

### **编写新测试**
- **位置**: `UnitTests/RefactoringTests` (针对新重构逻辑)。
- **Mocking**: 使用 `Moq` 模拟 `IWorkspaceLoader`，避免产生磁盘副作用。
- **Roslyn 测试**: 使用 `AdhocWorkspace` 创建内存中的解决方案进行语义分析测试。

**示例：测试类重构**
```csharp
[Fact]
public async Task Should_Remove_Unreferenced_Class()
{
    // Arrange
    var workspace = new AdhocWorkspace();
    // ... setup project ...
    var mockLoader = new Mock<IWorkspaceLoader>();

    // Act
    await ClassRefactorer.ExecuteSolutionRefactoringAsync(..., mockLoader.Object);

    // Assert
    mockLoader.Verify(l => l.SaveDocumentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
}
```

### **运行测试**
在提交 PR 前，请运行所有测试：
```bash
dotnet test
```

## **5. 提交 PR 流程**

1. Fork 本仓库。
2. 创建新的功能分支 (`git checkout -b feature/AmazingFeature`)。
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)。
4. 推送到分支 (`git push origin feature/AmazingFeature`)。
5. 开启 Pull Request。

## **6. 获取帮助**

如果遇到问题，请查阅 **[FAQ.md](FAQ.md)** 或提交 Issue。
