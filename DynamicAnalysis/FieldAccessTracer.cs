using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TerrariaTools.DynamicAnalysis
{
    /// <summary>
    /// 基于 Harmony AOP 的动态字段访问追踪器。
    /// 用于记录运行时实际访问的字段，以验证静态分析结果。
    /// </summary>
    public class FieldAccessTracer
    {
        private readonly Harmony _harmony;
        private static readonly HashSet<string> _accessedFields = new HashSet<string>();
        private static readonly object _lock = new object();

        public FieldAccessTracer(string id = "com.terrariatools.fieldtracer")
        {
            _harmony = new Harmony(id);
        }

        /// <summary>
        /// 获取当前已追踪到的所有字段名称。
        /// </summary>
        public static IEnumerable<string> GetAccessedFields()
        {
            lock (_lock)
            {
                return new List<string>(_accessedFields);
            }
        }

        /// <summary>
        /// 清空追踪记录。
        /// </summary>
        public static void ClearRecords()
        {
            lock (_lock)
            {
                _accessedFields.Clear();
            }
        }

        /// <summary>
        /// 开启对目标方法的追踪。
        /// </summary>
        /// <param name="targetMethod">要拦截的目标方法信息</param>
        /// <param name="targetTypes">感兴趣的类型（如 Player, Entity），仅记录对这些类型的字段访问</param>
        public void StartTracing(MethodBase targetMethod, List<Type> targetTypes)
        {
            var transpiler = typeof(FieldAccessTracer).GetMethod(nameof(FieldAccessTranspiler), BindingFlags.Static | BindingFlags.NonPublic);
            
            // 使用 Harmony 注入拦截逻辑
            _harmony.Patch(targetMethod, transpiler: new HarmonyMethod(transpiler));
        }

        /// <summary>
        /// 核心 IL 重写逻辑：在 ldfld 指令后插入日志记录。
        /// </summary>
        private static IEnumerable<CodeInstruction> FieldAccessTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var logMethod = typeof(FieldAccessTracer).GetMethod(nameof(RecordFieldAccess), BindingFlags.Static | BindingFlags.Public);

            foreach (var instruction in instructions)
            {
                yield return instruction;

                // 检查是否为加载字段指令 (ldfld 或 ldflda)
                if (instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldflda)
                {
                    if (instruction.operand is FieldInfo fieldInfo)
                    {
                        // 这里可以根据需求过滤感兴趣的类型
                        // 记录字段访问：[类型名].[字段名]
                        yield return new CodeInstruction(OpCodes.Ldstr, $"{fieldInfo.DeclaringType?.Name}.{fieldInfo.Name}");
                        yield return new CodeInstruction(OpCodes.Call, logMethod);
                    }
                }
            }
        }

        /// <summary>
        /// 运行时被注入代码调用的日志方法。
        /// </summary>
        /// <param name="fieldIdentifier">字段标识符</param>
        public static void RecordFieldAccess(string fieldIdentifier)
        {
            lock (_lock)
            {
                _accessedFields.Add(fieldIdentifier);
            }
        }

        /// <summary>
        /// 停止所有追踪并撤销补丁。
        /// </summary>
        public void StopAll()
        {
            _harmony.UnpatchAll(_harmony.Id);
        }
    }
}
