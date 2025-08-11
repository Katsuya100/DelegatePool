using NUnit.Framework;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Katuusagi.Pool.Tests
{
    public class DelegatePoolTest
    {
        [Test]
        public void Recursive()
        {
            TestFunctions.RecursiveMethod(3);
        }

        [Test]
        public void Instance()
        {
            {
                var t = new TestFunctions.Test();
                using (DelegatePool<Func<int>>.Get(t.Return1, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }

            {
                var t = new TestFunctions.Test2();
                using (DelegatePool<Func<int>>.Get(t.Return2, out var f))
                {
                    Assert.AreEqual(f(), 2);
                }
            }
        }

        [Test]
        public void Static()
        {
            {
                using (DelegatePool<Func<int>>.Get(TestFunctions.Return1, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }

            {
                using (DelegatePool<Func<int>>.Get(TestFunctions.Return2, out var f))
                {
                    Assert.AreEqual(f(), 2);
                }
            }
        }

        [Test]
        public void ThisCheck()
        {
            {
                var t = new TestFunctions.Test();
                using (DelegatePool<Func<Type>>.Get(t.GetThisType, out var f))
                {
                    Assert.AreEqual(f(), typeof(TestFunctions.Test));
                }
            }

            {
                var t = new TestFunctions.Test2();
                using (DelegatePool<Func<Type>>.Get(t.GetThisType, out var f))
                {
                    Assert.AreEqual(f(), typeof(TestFunctions.Test2));
                }
            }
        }

        [Test]
        public void Arg()
        {
            {
                var t = new TestFunctions.Test();
                using (DelegatePool<Func<int, int>>.Get(t.Threw, out var f))
                {
                    Assert.AreEqual(f(1), 1);
                }
            }

            {
                var t = new TestFunctions.Test2();
                using (DelegatePool<Func<int, int>>.Get(t.Add1, out var f))
                {
                    Assert.AreEqual(f(1), 2);
                }
            }
        }

        [Test]
        public void Lambda()
        {
            {
                using (DelegatePool<Func<int>>.Get(() => 1, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }

            {
                using (DelegatePool<Func<int>>.Get(() => 2, out var f))
                {
                    Assert.AreEqual(f(), 2);
                }
            }
        }

        [Test]
        public void CapturedLambda()
        {
            int a = 1;
            {
                using (DelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }
            int b = 2;
            {
                using (DelegatePool<Func<int>>.Get(() => b, out var f))
                {
                    Assert.AreEqual(f(), 2);
                }
            }
        }

        [Test]
        public void CapturedLambda_Concurrent()
        {
            int a = 1;
            {
                using (DelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }
            int b = 2;
            {
                using (ConcurrentDelegatePool<Func<int>>.Get(() => b, out var f))
                {
                    Assert.AreEqual(f(), 2);
                }
            }
        }

        [Test]
        public void CapturedLambda_ThreadStatic()
        {
            int a = 1;
            {
                using (ThreadStaticDelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }
            int b = 2;
            {
                using (DelegatePool<Func<int>>.Get(() => b, out var f))
                {
                    Assert.AreEqual(f(), 2);
                }
            }
        }

        [Test]
        public void CapturedLambda_DupOperation()
        {
            int a = 1;
            {
                using (DelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    Assert.AreEqual(f(), 1);
                }
            }
        }

        [Test]
        public void VirtualMethod()
        {
            var target = new TestFunctions.Sub();
            using (var d = DelegatePool<Func<int>>.Get(target.ReturnValue, out var f))
            {
                Assert.AreEqual(f(), 2);
            }
        }

        [Test]
        public void StructEquals()
        {
            int target = 10;
            using (var d = DelegatePool<Func<int, bool>>.Get(target.Equals, out var f))
            {
                Assert.IsTrue(f(10));
            }
        }

        [Test]
        public void Parallel_()
        {
            var wait = new SpinWait();
            var result = Parallel.For(0, 10000, (i) =>
            {
                using (ConcurrentDelegatePool<Func<int>>.Get(() => i, out var v))
                {
                    Assert.AreEqual(v(), i);
                }
            });

            while (!result.IsCompleted)
            {
                wait.SpinOnce();
            }

            result = Parallel.For(0, 10000, (i) =>
            {
                using (ThreadStaticDelegatePool<Func<int>>.Get(() => i, out var v))
                {
                    Assert.AreEqual(v(), i);
                }
            });

            while (!result.IsCompleted)
            {
                wait.SpinOnce();
            }
        }
    }
}
