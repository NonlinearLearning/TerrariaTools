using Microsoft.CodeAnalysis;

namespace Analysis.Frontend;

/// <summary>
/// 保存一次前端构建过程中需要反复访问的 Roslyn 编译上下文。
///
/// 这个对象存在的目的不是简单包一层，而是把几个容易散掉的事实收拢起来：
/// - 当前使用的 <see cref="Compilation"/>;
/// - 当前项目的语法树集合；
/// - 每棵语法树对应的 <see cref="SemanticModel"/>；
/// - 一组统一的语义查询入口。
///
/// 这样做之后，前端和各个 pass 不需要自己重复创建语义模型，
/// 也不会因为各自的取数方式不同而出现不一致结果。
/// </summary>
public sealed class RoslynCompilationContext
{
    private readonly IReadOnlyDictionary<SyntaxTree, SemanticModel> semanticModels;

    /// <summary>
    /// 使用编译对象和语义模型缓存初始化上下文。
    /// </summary>
    /// <param name="projectName">当前编译上下文对应的项目名称。</param>
    /// <param name="compilation">当前项目的编译对象。</param>
    /// <param name="semanticModels">语法树与语义模型映射。</param>
    public RoslynCompilationContext(
        string projectName,
        Compilation compilation,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> semanticModels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        this.semanticModels = semanticModels ?? throw new ArgumentNullException(nameof(semanticModels));
        ProjectName = projectName;
    }

    /// <summary>
    /// 获取当前项目名。
    /// </summary>
    public string ProjectName { get; }

    /// <summary>
    /// 获取当前编译对象。
    /// </summary>
    public Compilation Compilation { get; }

    /// <summary>
    /// 获取当前上下文持有的全部语法树。
    /// </summary>
    public IReadOnlyCollection<SyntaxTree> SyntaxTrees => semanticModels.Keys.ToArray();

    /// <summary>
    /// 按语法树获取缓存好的语义模型。
    /// </summary>
    /// <param name="syntaxTree">目标语法树。</param>
    /// <returns>对应语义模型。</returns>
    public SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
    {
        ArgumentNullException.ThrowIfNull(syntaxTree);

        if (semanticModels.TryGetValue(syntaxTree, out SemanticModel? semanticModel))
        {
            return semanticModel;
        }

        throw new InvalidOperationException("指定语法树没有对应的语义模型。");
    }

    /// <summary>
    /// 获取语法节点对应的语义模型。
    /// </summary>
    /// <param name="node">目标语法节点。</param>
    /// <returns>对应语义模型。</returns>
    public SemanticModel GetSemanticModel(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return GetSemanticModel(node.SyntaxTree);
    }

    /// <summary>
    /// 获取语法节点声明出来的符号。
    /// </summary>
    /// <param name="node">声明节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对应声明符号；不存在时返回空。</returns>
    public ISymbol? GetDeclaredSymbol(SyntaxNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return GetSemanticModel(node).GetDeclaredSymbol(node, cancellationToken);
    }

    /// <summary>
    /// 获取语法节点对应的符号信息。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Roslyn 返回的符号信息。</returns>
    public SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return GetSemanticModel(node).GetSymbolInfo(node, cancellationToken);
    }

    /// <summary>
    /// 获取语法节点对应的类型信息。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Roslyn 返回的类型信息。</returns>
    public TypeInfo GetTypeInfo(SyntaxNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return GetSemanticModel(node).GetTypeInfo(node, cancellationToken);
    }
}
