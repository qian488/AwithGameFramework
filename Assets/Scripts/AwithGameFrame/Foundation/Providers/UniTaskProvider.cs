using System.Threading.Tasks;
using AwithGameFrame.Core.Interfaces;

namespace AwithGameFrame.Foundation.Providers
{
    /// <summary>
    /// 异步操作提供者
    /// 基于 System.Threading.Tasks.Task
    /// </summary>
    public class UniTaskProvider : IAsyncProvider
    {
        public Task<T> FromResult<T>(T result)
        {
            return Task.FromResult(result);
        }

        public Task Delay(int milliseconds)
        {
            return Task.Delay(milliseconds);
        }

        public Task WhenAll(params Task[] tasks)
        {
            return Task.WhenAll(tasks);
        }

        public Task<T[]> WhenAll<T>(params Task<T>[] tasks)
        {
            return Task.WhenAll(tasks);
        }
    }
}
