namespace NLCPG.Model;

/// 捕获调用点支持的边上下文的结构化源码位置。
public readonly record struct NLCPGCallSiteContext(
  string FilePath,
  int SpanStart,
  int SpanEnd,
  string DisplayName)
{
    // 把结构化调用点位置压缩成可持久化的上下文标识。
    public NLCPGContextId ToContextId()
    {
        return new NLCPGContextId(
          $"callsite:{FilePath}:{SpanStart}:{SpanEnd}:{DisplayName}");
    }
}
