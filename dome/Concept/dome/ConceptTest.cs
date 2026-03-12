namespace TerrariaTools.Dome
{

    public class ConceptTest
    {
        //
    }
    //简单的类间依赖关系,例如一个类依赖于另一个类的实例
    public class ClassA
    {
        public ClassA()
        {
            B = new ClassB();
        }
        public ClassB B { get; set; }
    }
    public class ClassB
    {
        public int Value { get; set; }
        public ClassB()
        {
            Value = 20;
        }
        public int add()
        {
            return Value + 10;
        }
        public int add(int a, int b)
        {
            return a + b;
        }
        public static int sub(int a, int b)
        {
            return a - b;
        }
    }
    //更复杂的情况,例如一个类依赖于多个其他类的实例
    public class ClassC
    {
        public ClassA A { get; set; }
        public ClassB B { get; set; }
        public int Value { get; set; }
        public ClassC()
        {
            A = new ClassA();
            B = new ClassB();
            Value = 40;
        }
    }
    //类间依赖关系,例如一个类依赖于另一个类的成员
    //在构造函数种中使用了其他类的成员
    public class ClassD
    {
        public ClassB B { get; set; }
        public int Value { get; set; }
        public ClassD()
        {
            B = new ClassB();
            this.Value = B.Value;
        }
    }
    //更复杂的情况,例如一个类依赖于多个其他类的成员
    public class ClassE
    {
        public ClassB B { get; set; }
        public ClassC C { get; set; }
        public int Value { get; set; }
        public ClassE()
        {
            B = new ClassB();
            C = new ClassC();
            this.Value = B.Value + C.Value;//这里是依赖于多个其他类的成员
        }
    }


    //类函数依赖情况
    public class ClassF
    {
        public ClassB B { get; set; }
        public ClassF()
        {
            B = new ClassB();
        }
        public int add()
        {
            return B.add();
        }
        public int sub(int a, int b)
        {
            return ClassB.sub(a, b);
        }
    }
    //函数中语句间依赖关系
    public class ClassG
    {
        public ClassB B { get; set; }
        public ClassG()
        {
            B = new ClassB();
        }
        //主要看这里
        public int add()
        {
            int a = B.Value;//<-特殊情况不属于语句间依赖关系,因为Value是一个成员,而不是一个语句
            int b = 10;//<-这个语句不依赖任何语句
            return B.add(a, b);//<-这个语句了
        }
    }

}