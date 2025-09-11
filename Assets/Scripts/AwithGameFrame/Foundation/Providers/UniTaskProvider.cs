using Cysharp.Threading.Tasks;
using AwithGameFrame.Core.Interfaces;

namespace AwithGameFrame.Foundation.Providers
{
    /// <summary>
    /// UniTask异步操作提供者
    /// 基于UniTask的高性能异步实现
    /// </summary>
    public class UniTaskProvider : IAsyncProvider
    {
        public UniTask<T> FromResult<T>(T result)
        {
            return UniTask.FromResult(result);
        }

        public UniTask Delay(int milliseconds)
        {
            return UniTask.Delay(milliseconds);
        }

        public UniTask WhenAll(params UniTask[] tasks)
        {
            return UniTask.WhenAll(tasks);
        }

        public UniTask<T[]> WhenAll<T>(params UniTask<T>[] tasks)
        {
            return UniTask.WhenAll(tasks);
        }
    }
}
