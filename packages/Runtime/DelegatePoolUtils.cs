using Katuusagi.MemoizationForUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Katuusagi.Pool.Utils
{
    public static partial class DelegatePoolUtils
    {
        private class CalcHeaderDummy
        {
            public long DummyMember = 0;
        }

        private struct ClearInfo
        {
            public int Count64;
            public bool Copy32;
            public bool Copy16;
            public bool Copy8;
        }

        private static Type[] ConstructorArgTypes = new Type[2] { typeof(object), typeof(IntPtr) };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void Clear<T>(T obj)
            where T: class
        {
            if (obj == null)
            {
                return;
            }

            var headerSize = CalcClassHeaderSize();
            var top = (byte*)(UnsafeUtility.As<T, IntPtr>(ref obj) + headerSize);
            var clearInfo = GetClearInfo<T>();
            for (int i = 0; i < clearInfo.Count64; ++i)
            {
                ((long*)top)[i] = 0;
            }

            top += clearInfo.Count64 * sizeof(long);
            if (clearInfo.Copy32)
            {
                (*(int*)top) = 0;
                top += sizeof(int);
            }
            if (clearInfo.Copy16)
            {
                (*(short*)top) = 0;
                top += sizeof(short);
            }
            if (clearInfo.Copy8)
            {
                *top = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T CreateDelegate<T>(object target, IntPtr pFunc)
            where T : Delegate
        {
            using (ThreadStaticBoxingPool<IntPtr>.Get(pFunc, out var pFuncObj))
            {
                return Activator.CreateInstance(typeof(T), target, pFuncObj) as T;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void InitDelegate<T>(T del, object target, IntPtr pFunc)
            where T : Delegate
        {
            var fp = (delegate*<T, object, IntPtr, void>)GetConstructorPointer<T>();
            fp(del, target, pFunc);
        }

        [Memoization(Modifier = "public static")]
        private static Action<object> GetStructOnlyBoxingPoolReturnerRaw<T>()
            where T : struct
        {
            return StructOnlyBoxingPool<T>.Return;
        }

        [Memoization(Modifier = "public static")]
        private static Action<object> GetThreadStaticStructOnlyBoxingPoolReturnerRaw<T>()
            where T : struct
        {
            return ThreadStaticStructOnlyBoxingPool<T>.Return;
        }

        [Memoization(Modifier = "public static")]
        private static Action<object> GetConcurrentStructOnlyBoxingPoolReturnerRaw<T>()
            where T : struct
        {
            return ConcurrentStructOnlyBoxingPool<T>.Return;
        }

        [Memoization]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IntPtr GetConstructorPointerRaw<T>()
            where T : Delegate
        {
            var constructorInfo = typeof(T).GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, ConstructorArgTypes, null);

#if ENABLE_IL2CPP
            return constructorInfo.MethodHandle.Value;
#else
            return constructorInfo.MethodHandle.GetFunctionPointer();
#endif
        }

        [Memoization]
        private static unsafe int CalcClassHeaderSizeRaw()
        {
            var dummy = new CalcHeaderDummy();
            var address = (byte*)UnsafeUtility.As<CalcHeaderDummy, IntPtr>(ref dummy).ToPointer();
            var memberAddress = (byte*)UnsafeUtility.AddressOf(ref dummy.DummyMember);
            var result = (int)(memberAddress - address);
            return result;
        }

        [Memoization]
        private static int CalcClassBodySizeRaw<T>()
        {
            var max = GetInstanceFieldsAll(typeof(T))
                            .Select<FieldInfo, (FieldInfo field, int offset)>(v => (v, UnsafeUtility.GetFieldOffset(v)))
                            .OrderBy(v => -v.offset)
                            .FirstOrDefault();
            var maxFieldOffset = max.offset;
            var maxFieldSize = max.field.FieldType.IsValueType ? Marshal.SizeOf(max.field.FieldType) : IntPtr.Size;

            return maxFieldOffset + maxFieldSize - CalcClassHeaderSize();
        }

        private static IEnumerable<FieldInfo> GetInstanceFieldsAll(Type type)
        {
            if (type == null)
            {
                return Array.Empty<FieldInfo>();
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return GetInstanceFieldsAll(type.BaseType).Concat(fields);
        }

        [Memoization]
        private static ClearInfo GetClearInfoRaw<T>()
        {
            var classBodySize = CalcClassBodySize<T>();
            var result = new ClearInfo();

            result.Count64 = classBodySize / sizeof(long);

            var remain = classBodySize - (result.Count64 * sizeof(long));
            if (remain >= sizeof(int))
            {
                result.Copy32 = true;
                remain -= sizeof(int);
            }

            if (remain >= sizeof(short))
            {
                result.Copy16 = true;
                remain -= sizeof(short);
            }

            if (remain >= sizeof(byte))
            {
                result.Copy8 = true;
            }

            return result;
        }
    }
}
