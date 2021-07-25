using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace Zomg.AsyncTextures.Utils
{
    internal static class MainThreadRegister
    {
        private static SynchronizationContext _Context;
        public static SynchronizationContext Context => _Context ?? SynchronizationContext.Current;

        [RuntimeInitializeOnLoadMethod]
        public static void OnInit()
        {
            _Context = SynchronizationContext.Current;
        }

        public static SynchronizationContextAwaiter GetAwaiter(this SynchronizationContext context)
        {
            return new SynchronizationContextAwaiter(context);
        }
    }

    public interface IAwaitable
    {
        INotifyCompletion GetAwaiter();
    }
}