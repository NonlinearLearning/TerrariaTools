namespace NLCPG.Builder.Passes;

/// 定义一个可按顺序挂入 builder 流水线的 CPG pass。
internal interface INLCPGPass
{
  /// 返回 pass 的稳定名称，便于日志和遥测区分阶段。
  string Name { get; }

  /// 在给定构建上下文上执行当前 pass。
  void Run(NLCPGBuilder builder, NLCPGBuildContext context);
}
