using Katuusagi.Pool.Utils;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Katuusagi.Pool
{
    public static class ConcurrentDelegatePool<T>
        where T : MulticastDelegate
    {
        public readonly struct ReadOnlyHandler
        {
            private readonly T _delegate;
            private readonly object _target;
            private readonly Action<object> _returner;
            private readonly IReferenceHandler _lambda;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ReadOnlyHandler(T del, object target, Action<object> returner, IReferenceHandler lambda)
            {
                _delegate = del;
                _target = target;
                _lambda = lambda;
                _returner = returner;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                TryReturn(_delegate);
                _returner?.Invoke(_target);
                if (_lambda != null)
                {
                    _lambda.Release();
                }
            }
        }

        public readonly ref struct GetHandler
        {
            private readonly T _delegate;
            private readonly object _target;
            private readonly Action<object> _returner;
            private readonly IReferenceHandler _lambda;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public GetHandler(T del, object target, Action<object> returner, IReferenceHandler lambda)
            {
                _delegate = del;
                _target = target;
                _returner = returner;
                _lambda = lambda;
                if (_lambda != null)
                {
                    _lambda.Add();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                TryReturn(_delegate);
                _returner?.Invoke(_target);
                if (_lambda != null)
                {
                    _lambda.Release();
                }
            }

            public static implicit operator ReadOnlyHandler(GetHandler obj)
            {
                return new ReadOnlyHandler(obj._delegate, obj._target, obj._returner, obj._lambda);
            }
        }

        private static ConcurrentStack<T> _stack = new ConcurrentStack<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GetHandler Get(T del, out T result)
        {
            // dummy
            result = del;
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GetHandler GetClassOnly<TTarget>(TTarget target, IntPtr pFunc, IReferenceHandler lambda, out T result)
            where TTarget : class
        {
            Get(target, pFunc, out result);
            return new GetHandler(result, null, null, lambda);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GetHandler GetStructOnly<TTarget>(ref TTarget target, IntPtr pFunc, out T result)
            where TTarget : struct
        {
            object targetObj = ConcurrentStructOnlyBoxingPool<TTarget>.Get(target);
            Get(targetObj, pFunc, out result);
            var returner = DelegatePoolUtils.GetConcurrentStructOnlyBoxingPoolReturner<TTarget>();
            return new GetHandler(result, targetObj, returner, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Get(object target, IntPtr pFunc, out T result)
        {
            if (!_stack.TryPop(out result))
            {
                result = DelegatePoolUtils.CreateDelegate<T>(target, pFunc);
            }
            else
            {
                DelegatePoolUtils.InitDelegate(result, target, pFunc);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryReturn(T del)
        {
            if (del == null)
            {
                return false;
            }

            DelegatePoolUtils.Clear<MulticastDelegate>(del);
            _stack.Push(del);
            return true;
        }
    }
}
