using System;
using System.Threading;
using System.Threading.Tasks;

// Not in Use

namespace BTCPayServer.Plugins.Nano.Payments
{
    public interface IBackgroundTaskQueue
    {
        void QueueTask(string groupKey, Func<CancellationToken, Task> task);
    }
}
