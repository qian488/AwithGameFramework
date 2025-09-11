using Cysharp.Threading.Tasks;

namespace AwithGameFrame.Core.Interfaces
{
    /// <summary>
    /// 异步操作提供者接口
    /// 为不同的异步实现提供统一抽象
    /// </summary>
    public interface IAsyncProvider
    {
        /// <summary>
        /// 创建已完成的任务
        /// </summary>
        /// <typeparam name="T">结果类型</typeparam>
        /// <param name="result">结果值</param>
        /// <returns>已完成的任务</returns>
        UniTask<T> FromResult<T>(T result);

        /// <summary>
        /// 延迟指定时间
        /// </summary>
        /// <param name="milliseconds">延迟毫秒数</param>
        /// <returns>延迟任务</returns>
        UniTask Delay(int milliseconds);

        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        /// <param name="tasks">任务数组</param>
        /// <returns>等待任务</returns>
        UniTask WhenAll(params UniTask[] tasks);

        /// <summary>
        /// 等待所有任务完成并返回结果
        /// </summary>
        /// <typeparam name="T">结果类型</typeparam>
        /// <param name="tasks">任务数组</param>
        /// <returns>结果数组</returns>
        UniTask<T[]> WhenAll<T>(params UniTask<T>[] tasks);
    }
}
