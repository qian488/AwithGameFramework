using System;
using AwithGameFrame.Core.Interfaces;
using AwithGameFrame.Foundation.Providers;

namespace AwithGameFrame.Foundation
{
    /// <summary>
    /// Provider管理器
    /// 负责注册和管理各种Provider实现
    /// </summary>
    public static class ProviderManager
    {
        private static IAsyncProvider _asyncProvider;
        private static IAnimationProvider _animationProvider;
        private static ISerializationProvider _serializationProvider;

        /// <summary>
        /// 异步操作提供者
        /// </summary>
        public static IAsyncProvider AsyncProvider
        {
            get => _asyncProvider ?? (_asyncProvider = new UniTaskProvider());
            set => _asyncProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 动画操作提供者
        /// </summary>
        public static IAnimationProvider AnimationProvider
        {
            get => _animationProvider ?? (_animationProvider = new DOTweenProvider());
            set => _animationProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 序列化操作提供者
        /// </summary>
        public static ISerializationProvider SerializationProvider
        {
            get => _serializationProvider ?? (_serializationProvider = new NewtonsoftJsonProvider());
            set => _serializationProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 初始化默认Provider
        /// </summary>
        public static void InitializeDefaultProviders()
        {
            _asyncProvider = new UniTaskProvider();
            _animationProvider = new DOTweenProvider();
            _serializationProvider = new NewtonsoftJsonProvider();
        }

        /// <summary>
        /// 重置所有Provider为null
        /// </summary>
        public static void Reset()
        {
            _asyncProvider = null;
            _animationProvider = null;
            _serializationProvider = null;
        }
    }
}
