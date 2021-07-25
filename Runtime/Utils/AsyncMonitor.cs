using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zomg.AsyncTextures.Utils
{
    internal class AsyncMonitor : IDisposable
    {
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(1, 1);

        public void Pulse()
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _signal?.Dispose();
        }
    }
}