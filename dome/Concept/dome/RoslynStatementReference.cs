using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// Roslyn StatementSyntax 子类参考指南
    /// 包含 C# 中各种语句类型的中文解释及代码示例
    /// </summary>
    public class RoslynStatementReference
    {
        public void StatementExamples()
        {
            /* 
             * 1. BlockSyntax
             * 代表由大括号 { } 包围的一组语句序列。
             * 例子: { int x = 1; Console.WriteLine(x); }
             */
            {
                int x = 1;
                Console.WriteLine(x);
            }

            /* 
             * 2. BreakStatementSyntax
             * 代表 break 语句，用于跳出循环或 switch。
             * 例子: break;
             */
            while (true) { break; }

            /* 
             * 3. CheckedStatementSyntax
             * 代表 checked 或 unchecked 块，用于控制算术溢出检查。
             * 例子: checked { int i = int.MaxValue; i++; }
             */
            checked { int i = 10; }

            /* 
             * 4. CommonForEachStatementSyntax
             * 为 ForEachStatementSyntax 和 ForEachVariableStatementSyntax 的基类。
             * 代表 foreach 循环。
             * 例子: foreach (var item in list) { }
             */
            var list = new List<int> { 1 };
            foreach (var item in list) { }

            /* 
             * 5. ContinueStatementSyntax
             * 代表 continue 语句，跳过当前循环的剩余部分。
             * 例子: continue;
             */
            for (int i = 0; i < 5; i++) { continue; }

            /* 
             * 6. DoStatementSyntax
             * 代表 do-while 循环。
             * 例子: do { ... } while (condition);
             */
            do { } while (false);

            /* 
             * 7. EmptyStatementSyntax
             * 代表空语句（只有一个分号）。
             * 例子: ;
             */
            ;

            /* 
             * 8. ExpressionStatementSyntax
             * 代表由表达式组成的语句（如方法调用、赋值等）。
             * 例子: Console.WriteLine("Hello");
             */
            Console.WriteLine("Hello");

            /* 
             * 9. FixedStatementSyntax
             * 代表 fixed 语句，用于固定变量地址，常用于指针操作。
             * 例子: fixed (int* p = &arr[0]) { }
             */
            unsafe { int[] arr = { 1 }; fixed (int* p = arr) { } }

            /* 
             * 10. ForStatementSyntax
             * 代表标准的 for 循环。
             * 例子: for (int i = 0; i < 10; i++) { }
             */
            for (int i = 0; i < 1; i++) { }

            /* 
             * 11. GotoStatementSyntax
             * 代表 goto 语句。
             * 例子: goto myLabel;
             */
            goto myLabel;
            myLabel: Console.WriteLine("Label reached");

            /* 
             * 12. IfStatementSyntax
             * 代表 if 语句。
             * 例子: if (condition) { } else { }
             */
            if (true) { } else { }

            /* 
             * 13. LabeledStatementSyntax
             * 代表带标签的语句。
             * 例子: start: Console.WriteLine("Start");
             */
            start: ;

            /* 
             * 14. LocalDeclarationStatementSyntax
             * 代表局部变量声明语句。
             * 例子: int a = 10;
             */
            int a = 10;

            /* 
             * 15. LocalFunctionStatementSyntax
             * 代表局部函数定义。
             * 例子: void LocalFunc() { }
             */
            void LocalFunc() { }

            /* 
             * 16. LockStatementSyntax
             * 代表 lock 语句，用于线程同步。
             * 例子: lock (obj) { }
             */
            object obj = new object();
            lock (obj) { }

            /* 
             * 17. ReturnStatementSyntax
             * 代表 return 语句。
             * 例子: return;
             */
            return;

            /* 
             * 18. SwitchStatementSyntax
             * 代表标准的 switch 语句。
             * 例子: switch (val) { case 1: break; }
             */
            int val = 1;
            switch (val) { case 1: break; }

            /* 
             * 19. ThrowStatementSyntax
             * 代表 throw 语句。
             * 例子: throw new Exception();
             */
            // throw new Exception();

            /* 
             * 20. TryStatementSyntax
             * 代表 try 语句块。
             * 例子: try { } catch { } finally { }
             */
            try { } catch { } finally { }

            /* 
             * 21. UnsafeStatementSyntax
             * 代表 unsafe 代码块。
             * 例子: unsafe { ... }
             */
            unsafe { }

            /* 
             * 22. UsingStatementSyntax
             * 代表 using 语句（用于 IDisposable 对象）。
             * 例子: using (var res = new Resource()) { }
             */
            using (new System.IO.MemoryStream()) { }

            /* 
             * 23. WhileStatementSyntax
             * 代表 while 循环。
             * 例子: while (condition) { }
             */
            while (false) { }

            /* 
             * 24. YieldStatementSyntax
             * 代表 yield return 或 yield break 语句。
             * 例子: yield return 1;
             */
        }

        public IEnumerable<int> YieldExample()
        {
            yield return 1;
            yield break;
        }
    }
}
