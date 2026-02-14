using System;
using System.IO;
using System.Threading;

namespace Terraria
{
    /// <summary>
    /// 自动追踪方法进入与退出的辅助类。
    /// 请将此文件添加到 Terraria 项目中。
    /// </summary>
    public class CallTracker : IDisposable
    {
        [ThreadStatic]
        private static int _depth = 0;

        private readonly string _methodName;
        private static readonly string LogPath = @"D:\ProjectItem\SourceCode\Net\TerrariaTools\call_chain.log";
        private static readonly object FileLock = new object();

        public CallTracker(string methodName)
        {
            _methodName = methodName;
            Log($"[ENTER] {new string(' ', _depth * 2)}{_methodName}");
            _depth++;
        }

        public void Dispose()
        {
            _depth--;
            // 如果需要记录退出，可以取消注释
            // Log($"[EXIT]  {new string(' ', _depth * 2)}{_methodName}");
        }

        private void Log(string message)
        {
            try
            {
                // 使用简单的同步写入，确保顺序（虽然性能较低）
                lock (FileLock)
                {
                    File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // 忽略日志写入错误，避免干扰主程序
            }
        }
    }
}
