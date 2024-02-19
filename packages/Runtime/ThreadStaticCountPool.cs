using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Katuusagi.Pool
{
    public static class ThreadStaticCountPool<T>
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

        [ThreadStatic]
        private static Stack<T> _objStack;

        [ThreadStatic]
        private static Stack<GetHandler> _handlerStack;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Get(ref IReferenceHandler h, out T result)
        {
            if (_objStack == null)
            {
                _objStack = new Stack<T>(32);
            }

            if (_handlerStack == null)
            {
                _handlerStack = new Stack<GetHandler>(32);
            }

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
            if (_objStack == null)
            {
                _objStack = new Stack<T>(32);
            }

            if (_handlerStack == null)
            {
                _handlerStack = new Stack<GetHandler>(32);
            }

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
