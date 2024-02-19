using System;

namespace Katuusagi.Pool.Tests
{
    public class TestFunctions
    {
        public abstract class Base
        {
            public virtual int ReturnValue()
            {
                return 1;
            }
        }

        public class Sub : Base
        {
            public override int ReturnValue()
            {
                return 2;
            }
        }

        public class Test
        {
            public int Return1()
            {
                return 1;
            }

            public int Threw(int a)
            {
                return a;
            }

            public Type GetThisType()
            {
                return GetType();
            }
        }

        public struct Test2
        {
            public int Return2()
            {
                return 2;
            }

            public int Add1(int a)
            {
                return a + 1;
            }

            public Type GetThisType()
            {
                return GetType();
            }
        }

        public static int Return1()
        {
            return 1;
        }

        public static int Return2()
        {
            return 2;
        }

        public static void RecursiveMethod(int count)
        {
            if (count == 0)
            {
                return;
            }

            using (var d = DelegatePool<Func<int>>.Get(() => count - 1, out var f))
            {
                var c = f();
                RecursiveMethod(c);
            }
        }
    }
}
