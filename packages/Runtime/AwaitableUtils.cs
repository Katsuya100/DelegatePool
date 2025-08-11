using System;
using UnityEngine;

namespace Katuusagi.Pool.Utils
{
    public static class AwaitableUtils
    {
        [ThreadStatic]
        private static readonly AwaitableCompletionSource completionSource = new();
        public static Awaitable Completed
        {
            get
            {
                completionSource.SetResult();
                var awaitable = completionSource.Awaitable;
                completionSource.Reset();
                return awaitable;
            }
        }
    }
}
