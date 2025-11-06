using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Security.Cryptography;
using System.Text;

// Not currently in use

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class PaymentsTaskQueue : IBackgroundTaskQueue
    {
        private readonly int _workerCount;
        private readonly Channel<Func<CancellationToken, Task>>[] _channels;

        public PaymentsTaskQueue(int workerCount = 8)
        {
            _workerCount = workerCount;
            _channels = new Channel<Func<CancellationToken, Task>>[workerCount];

            for (int i = 0; i < _workerCount; i++)
            {
                var channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
                _channels[i] = channel;

                // Start one background worker per partition
                Task.Run(async () =>
                {
                    var reader = channel.Reader;
                    var workerIndex = i;
                    while (await reader.WaitToReadAsync())
                    {
                        while (reader.TryRead(out var task))
                        {
                            try
                            {
                                await task(CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Worker error: {ex.Message}");
                            }
                        }
                    }
                });
            }
        }

        public void QueueTask(string groupKey, Func<CancellationToken, Task> task)
        {
            int index = GetStablePartition(groupKey, _workerCount);
            _channels[index].Writer.TryWrite(task);
        }

        public static int GetStablePartition(string key, int partitionCount)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));

            // Convert first 4 bytes to int
            int value = BitConverter.ToInt32(hash, 0);
            return Math.Abs(value) % partitionCount;
        }
    }
}