using System;
using System.Collections.Generic;

namespace AwithGameFrame.Core.DI
{
    /// <summary>
    /// 轻量级服务定位器
    /// 支持接口到实现的注册、解析和替换，实现模块可插拔
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _instances = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>();

        /// <summary>
        /// 注册服务实例（立即创建）
        /// </summary>
        public static void Register<TInterface>(TInterface instance) where TInterface : class
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var type = typeof(TInterface);
            _instances[type] = instance;
            _factories.Remove(type);
        }

        /// <summary>
        /// 注册服务工厂（延迟创建，首次Resolve时调用）
        /// </summary>
        public static void RegisterFactory<TInterface>(Func<TInterface> factory) where TInterface : class
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _factories[typeof(TInterface)] = () => factory();
            _instances.Remove(typeof(TInterface));
        }

        /// <summary>
        /// 解析服务
        /// </summary>
        /// <exception cref="InvalidOperationException">服务未注册时抛出</exception>
        public static TInterface Resolve<TInterface>() where TInterface : class
        {
            if (TryResolve<TInterface>() is TInterface result)
                return result;

            throw new InvalidOperationException(
                $"服务 {typeof(TInterface).Name} 未注册。请先调用 ServiceLocator.Register<{typeof(TInterface).Name}>()。");
        }

        /// <summary>
        /// 尝试解析服务，失败返回null
        /// </summary>
        public static TInterface TryResolve<TInterface>() where TInterface : class
        {
            var type = typeof(TInterface);

            if (_instances.TryGetValue(type, out var instance))
                return (TInterface)instance;

            if (_factories.TryGetValue(type, out var factory))
            {
                var created = (TInterface)factory();
                _instances[type] = created;
                _factories.Remove(type);
                return created;
            }

            return null;
        }

        /// <summary>
        /// 检查服务是否已注册
        /// </summary>
        public static bool IsRegistered<TInterface>() where TInterface : class
        {
            return _instances.ContainsKey(typeof(TInterface)) || _factories.ContainsKey(typeof(TInterface));
        }

        /// <summary>
        /// 移除服务注册
        /// </summary>
        public static void Unregister<TInterface>() where TInterface : class
        {
            var type = typeof(TInterface);
            _instances.Remove(type);
            _factories.Remove(type);
        }

        /// <summary>
        /// 清空所有注册
        /// </summary>
        public static void Clear()
        {
            _instances.Clear();
            _factories.Clear();
        }
    }
}
