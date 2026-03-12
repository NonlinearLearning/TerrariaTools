using System;

namespace TerrariaTools.Rules.Dome
{
    /// <summary>
    /// 规则与重写相关的常量定义
    /// </summary>
    public static class RuleConstants
    {
        /// <summary>
        /// 重写标记的批注类型
        /// </summary>
        public const string RewriteAnnotationKind = "TerrariaTools.Rewrite.Metadata";

        /// <summary>
        /// 重置属性/变量的批注类型
        /// </summary>
        public const string ResetAnnotationKind = "TerrariaTools.Rewrite.Reset";

        /// <summary>
        /// 删除动作
        /// </summary>
        public const string ActionDelete = "Action=Delete";

        /// <summary>
        /// 注释掉动作
        /// </summary>
        public const string ActionCommentOut = "Action=CommentOut";

        /// <summary>
        /// 添加返回值动作
        /// </summary>
        public const string ActionAddReturn = "Action=AddReturn";

        /// <summary>
        /// 标记来源的批注类型 (用于调试或追踪)
        /// </summary>
        public const string SourceAnnotationKind = "TerrariaTools.Source.Trace";
    }
}
