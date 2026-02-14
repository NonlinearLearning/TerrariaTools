using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TerrariaTools.DynamicAnalysis
{
#pragma warning disable CS8603
    /// <summary>
    /// 提供基于 Roslyn 的源码级调用链注入功能。
    /// </summary>
    public class CallChainInjector
    {
        private readonly string _solutionPath;
        private readonly Load _loader;

        public CallChainInjector(string solutionPath, Load loader)
        {
            _solutionPath = solutionPath;
            _loader = loader;
        }

        /// <summary>
        /// 执行全解决方案的调用链注入。
        /// </summary>
        public async Task ExecuteInjectionAsync()
        {
            Console.WriteLine("[信息] 正在加载解决方案以进行调用链注入...");
            using var workspace = await _loader.LoadSolutionAsync(_solutionPath);
            if (workspace == null) return;

            var solution = workspace.CurrentSolution;
            int totalFiles = 0;
            int modifiedFiles = 0;

            var createdTrackerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in solution.Projects)
            {
                Console.WriteLine($"[项目] 正在处理: {project.Name}");

                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var document in project.Documents)
                {
                    if (document.FilePath == null || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 避免对自己进行注入
                    if (document.FilePath.Contains("CallTracker.cs") || document.FilePath.Contains("CallChainInjector.cs"))
                        continue;

                    // 每个项目只在第一个处理的文件目录下创建一次 CallTracker.cs，避免重复定义冲突
                    string projectDir = Path.GetDirectoryName(project.FilePath);
                    if (!string.IsNullOrEmpty(projectDir) && !createdTrackerPaths.Contains(projectDir))
                    {
                        CreateCallTracker(projectDir);
                        createdTrackerPaths.Add(projectDir);
                    }

                    totalFiles++;
                    var root = await document.GetSyntaxRootAsync();
                    var model = await document.GetSemanticModelAsync();
                    if (root == null || model == null) continue;

                    var rewriter = new TraceRewriter(model);
                    var newRoot = rewriter.Visit(root);

                    if (!newRoot.IsEquivalentTo(root))
                    {
                        try
                        {
                            File.WriteAllText(document.FilePath, newRoot.NormalizeWhitespace().ToFullString(), System.Text.Encoding.UTF8);
                            modifiedFiles++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 写入文件失败 {document.FilePath}: {ex.Message}");
                        }
                    }

                    if (totalFiles % 50 == 0)
                    {
                        Console.WriteLine($"[进度] 已处理 {totalFiles} 个文件...");
                    }
                }
            }

            Console.WriteLine($"\n[完成] 调用链注入结束。");
            Console.WriteLine($"[统计] 处理文件总数: {totalFiles}");
            Console.WriteLine($"[统计] 已修改文件数: {modifiedFiles}");
        }

        /// <summary>
        /// 在目标目录自动创建 CallTracker.cs 辅助类。
        /// </summary>
        private void CreateCallTracker(string targetDirectory)
        {
            if (string.IsNullOrEmpty(targetDirectory)) return;

            string filePath = Path.Combine(targetDirectory, "CallTracker.cs");
            string logDirectory = @"D:\ProjectItem\SourceCode\Net\TerrariaTools";
            string logPath = Path.Combine(logDirectory, "call_chain.log");

            string content = $@"using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Terraria
{{
    public class CallTracker : IDisposable
    {{
        private static readonly ConcurrentQueue<string> LogQueue = new ConcurrentQueue<string>();
        private static readonly ConcurrentDictionary<string, bool> LoggedMethods = new ConcurrentDictionary<string, bool>();
        private static readonly Timer FlushTimer;
        private static readonly string LogPath = @""{logPath}"";

        static CallTracker()
        {{
            FlushTimer = new Timer(FlushLogs, null, 5000, 5000);
            try
            {{
                if (!Directory.Exists(Path.GetDirectoryName(LogPath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            }}
            catch {{ }}
        }}

        private static void FlushLogs(object state)
        {{
            if (LogQueue.IsEmpty) return;
            var sb = new StringBuilder();
            while (LogQueue.TryDequeue(out var log)) sb.AppendLine(log);
            if (sb.Length > 0)
            {{
                try {{ File.AppendAllText(LogPath, sb.ToString()); }}
                catch {{ }}
            }}
        }}

        public CallTracker(string methodName)
        {{
            if (LoggedMethods.TryAdd(methodName, true))
            {{
                string timestamp = DateTime.Now.ToString(""HH:mm:ss.fff"");
                LogQueue.Enqueue(string.Format(""[{{0}}] [ENTER] {{1}}"", timestamp, methodName));
            }}
        }}

        public void Dispose() {{ }}
    }}
}}";
            try
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
                Console.WriteLine($"[信息] 已更新辅助类并启用缓存机制: {filePath}");
                Console.WriteLine($"[信息] 策略: 每 5s 写入一次，每个函数仅记录首次调用。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 无法创建辅助类: {ex.Message}");
            }
        }

        /// <summary>
        /// 内部语法重写器，执行具体的注入逻辑。
        /// </summary>
        private class TraceRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;

            public TraceRewriter(SemanticModel model)
            {
                _model = model;
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                // 1. 基本过滤：跳过抽象方法、分部方法（无主体）或外部方法
                if (node.Body == null && node.ExpressionBody == null) return base.VisitMethodDeclaration(node);

                // 1. 获取方法全名：命名空间.类名.方法名
                var symbol = _model.GetDeclaredSymbol(node);
                if (symbol == null) return base.VisitMethodDeclaration(node);

                string methodName = node.Identifier.Text;

                // 使用自定义格式确保输出为：Namespace.Class.Method
                var displayFormat = new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted
                );
                string fullName = symbol.ToDisplayString(displayFormat);

                // 过滤高频触发的函数


                // 2. 构造注入语句：直接使用表达式，不声明局部变量名，避免命名冲突
                // using (new global::Terraria.CallTracker("FullName")) { ... }
                var trackerType = SyntaxFactory.ParseTypeName("global::Terraria.CallTracker");
                var methodNameLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(fullName));

                var objectCreation = SyntaxFactory.ObjectCreationExpression(trackerType)
                                         .AddArgumentListArguments(SyntaxFactory.Argument(methodNameLiteral));

                var usingStatement = SyntaxFactory.UsingStatement(
                    null, // 不使用变量声明
                    objectCreation, // 直接使用对象创建表达式
                    GetOriginalBodyAsBlock(node)
                );

                // 3. 返回替换后的方法
                return node.WithBody(SyntaxFactory.Block(usingStatement))
                           .WithExpressionBody(null)
                           .WithSemicolonToken(default)
                           .WithTrailingTrivia(node.GetTrailingTrivia());
            }

            private BlockSyntax GetOriginalBodyAsBlock(MethodDeclarationSyntax node)
            {
                if (node.Body != null) return node.Body;

                // 处理 => 形式的方法体
                if (node.ExpressionBody != null)
                {
                    return SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(node.ExpressionBody.Expression));
                }

                return SyntaxFactory.Block();
            }
        }
    }
}
#pragma warning restore CS8603
