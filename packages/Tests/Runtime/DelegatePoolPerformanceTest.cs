using NUnit.Framework;
using System;
using Unity.PerformanceTesting;

namespace Katuusagi.Pool.Tests
{
    public class DelegatePoolPerformanceTest
    {
        [Test]
        [Performance]
        public void Instance_Legacy()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                Func<int> f = t.Return1;
                f();
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Instance_Pool()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                using (DelegatePool<Func<int>>.Get(t.Return1, out var f))
                {
                    f();
                }
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Instance_ThreadStatic()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                using (ThreadStaticDelegatePool<Func<int>>.Get(t.Return1, out var f))
                {
                    f();
                }
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Instance_Concurrent()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                using (ConcurrentDelegatePool<Func<int>>.Get(t.Return1, out var f))
                {
                    f();
                }
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Lambda_Legacy()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                int a = 1;
                Func<int> f = () => a;
                f();
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Lambda_Pool()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                int a = 1;
                using (DelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    f();
                }
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Lambda_ThreadStatic()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                int a = 1;
                using (ThreadStaticDelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    f();
                }
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }

        [Test]
        [Performance]
        public void Lambda_Concurrent()
        {
            var t = new TestFunctions.Test();
            Measure.Method(() =>
            {
                int a = 1;
                using (ConcurrentDelegatePool<Func<int>>.Get(() => a, out var f))
                {
                    f();
                }
            })
            .WarmupCount(1)
            .IterationsPerMeasurement(5000)
            .MeasurementCount(20)
            .Run();
        }
    }
}
