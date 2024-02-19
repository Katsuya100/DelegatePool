using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Katuusagi.Pool
{
    public static class ConcurrentCountPool<T>
    {
        private class GetHandler : IReferenceHandler
        {
            private T _obj = default;
            private int _count = 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Init(T obj)
            {
                _obj = obj;
                _count = 1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add()
            {
                ++_count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Release()
            {
                --_count;
                if (_count > 0)
                {
                    return;
                }

                _count = 0;
                if (_obj == null)
                {
                    return;
                }

                Return(this, _obj);
                _obj = default;
            }
        }

        private static ConcurrentStack<T> _objStack = new ConcurrentStack<T>();
        private static ConcurrentStack<GetHandler> _handlerStack = new ConcurrentStack<GetHandler>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Get(ref IReferenceHandler h, out T result)
        {
            if (h != null)
            {
                h.Release();
            }

            if (!_objStack.TryPop(out result))
            {
                result = Activator.CreateInstance<T>();
            }

            if (!_handlerStack.TryPop(out var handle))
            {
                handle = new GetHandler();
            }

            handle.Init(result);
            h = handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Return(IReferenceHandler h, T del)
        {
            _objStack.Push(del);
            _handlerStack.Push(h as GetHandler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(IReferenceHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            handler.Release();
        }
    }
}
