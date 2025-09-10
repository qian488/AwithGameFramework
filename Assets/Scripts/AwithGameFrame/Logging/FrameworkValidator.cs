using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using AwithGameFrame.Core;

namespace AwithGameFrame.Logging
{
    /// <summary>
    /// 框架验证器 - 检测框架使用中的不规范行为，辅助框架开发
    /// </summary>
    public class FrameworkValidator : BaseManager<FrameworkValidator>
    {

        #region 字段
        private bool _enableValidation = true;
        private bool _enableWarnings = true;
        private bool _enableErrors = true;
        private Dictionary<ValidationType, bool> _validationEnabled = new Dictionary<ValidationType, bool>();
        private Dictionary<string, int> _warningCounts = new Dictionary<string, int>();
        private Dictionary<string, int> _errorCounts = new Dictionary<string, int>();
        private List<string> _validationRules = new List<string>();
        #endregion

        #region 属性
        /// <summary>
        /// 是否启用验证
        /// </summary>
        public bool EnableValidation
        {
            get => _enableValidation;
            set => _enableValidation = value;
        }

        /// <summary>
        /// 是否启用警告
        /// </summary>
        public bool EnableWarnings
        {
            get => _enableWarnings;
            set => _enableWarnings = value;
        }

        /// <summary>
        /// 是否启用错误检测
        /// </summary>
        public bool EnableErrors
        {
            get => _enableErrors;
            set => _enableErrors = value;
        }
        #endregion

        #region 构造函数
        public FrameworkValidator()
        {
            InitializeValidationRules();
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 验证框架使用
        /// </summary>
        /// <param name="type">验证类型</param>
        /// <param name="message">验证消息</param>
        /// <param name="context">上下文对象</param>
        public void ValidateFrameworkUsage(ValidationType type, string message, object context = null)
        {
            if (!_enableValidation) return;

            // 检查该类型是否启用验证
            if (!_validationEnabled.GetValueOrDefault(type, true)) return;

            switch (type)
            {
                case ValidationType.Singleton:
                    ValidateSingletonUsage(message, context);
                    break;
                case ValidationType.Event:
                    ValidateEventUsage(message, context);
                    break;
                case ValidationType.Resource:
                    ValidateResourceUsage(message, context);
                    break;
                case ValidationType.UI:
                    ValidateUIUsage(message, context);
                    break;
                case ValidationType.Performance:
                    ValidatePerformanceUsage(message, context);
                    break;
                case ValidationType.Memory:
                    ValidateMemoryUsage(message, context);
                    break;
                case ValidationType.Threading:
                    ValidateThreadingUsage(message, context);
                    break;
            }
        }

        /// <summary>
        /// 设置验证类型是否启用
        /// </summary>
        /// <param name="type">验证类型</param>
        /// <param name="enabled">是否启用</param>
        public void SetValidationEnabled(ValidationType type, bool enabled)
        {
            _validationEnabled[type] = enabled;
        }

        /// <summary>
        /// 获取验证统计
        /// </summary>
        /// <returns>验证统计信息</returns>
        public string GetValidationStats()
        {
            int totalWarnings = 0;
            int totalErrors = 0;

            foreach (var count in _warningCounts.Values)
                totalWarnings += count;

            foreach (var count in _errorCounts.Values)
                totalErrors += count;

            return $"框架验证统计:\n" +
                   $"- 警告总数: {totalWarnings}\n" +
                   $"- 错误总数: {totalErrors}\n" +
                   $"- 验证规则: {_validationRules.Count}";
        }

        /// <summary>
        /// 重置验证统计
        /// </summary>
        public void ResetValidationStats()
        {
            _warningCounts.Clear();
            _errorCounts.Clear();
        }
        #endregion

        #region 私有验证方法
        private void ValidateSingletonUsage(string message, object context)
        {
            // 检查在Awake中调用GetInstance
            if (IsInAwakeMethod())
            {
                LogWarning("单例使用警告", "在Awake中调用GetInstance可能导致初始化顺序问题", context);
            }

            // 检查重复初始化
            if (message.Contains("初始化") && IsRepeatedInitialization(message))
            {
                LogWarning("单例重复初始化", "检测到可能的重复初始化", context);
            }
        }

        private void ValidateEventUsage(string message, object context)
        {
            // 检查事件监听是否在OnDestroy中移除
            if (message.Contains("添加事件监听") && !HasOnDestroyMethod(context))
            {
                LogWarning("事件监听警告", "添加事件监听但未在OnDestroy中移除，可能导致内存泄漏", context);
            }

            // 检查事件名称规范
            if (message.Contains("事件") && !IsValidEventName(message))
            {
                LogWarning("事件命名警告", "事件名称不符合框架命名规范", context);
            }
        }

        private void ValidateResourceUsage(string message, object context)
        {
            // 检查资源路径
            if (message.Contains("加载资源") && !IsValidResourcePath(message))
            {
                LogWarning("资源路径警告", "资源路径可能无效或不符合规范", context);
            }

            // 检查资源回收
            if (message.Contains("加载") && !message.Contains("回收") && IsResourceLoading(context))
            {
                LogWarning("资源管理警告", "加载资源但未在适当时机回收", context);
            }
        }

        private void ValidateUIUsage(string message, object context)
        {
            // 检查UI面板名称
            if (message.Contains("面板") && !IsValidPanelName(message))
            {
                LogWarning("UI命名警告", "UI面板名称不符合框架命名规范", context);
            }

            // 检查UI层级使用
            if (message.Contains("层级") && !IsValidUILayer(message))
            {
                LogWarning("UI层级警告", "UI层级使用可能不符合框架规范", context);
            }
        }

        private void ValidatePerformanceUsage(string message, object context)
        {
            // 检查Update中的操作
            if (IsInUpdateMethod() && IsHeavyOperation(message))
            {
                LogWarning("性能警告", "在Update中执行可能影响性能的操作", context);
            }

            // 检查频繁调用
            if (IsFrequentCall(message))
            {
                LogWarning("性能警告", "检测到频繁调用，可能影响性能", context);
            }
        }

        private void ValidateMemoryUsage(string message, object context)
        {
            // 检查可能的内存泄漏
            if (message.Contains("创建") && !message.Contains("销毁") && IsMemoryLeakRisk(context))
            {
                LogWarning("内存警告", "创建对象但未在适当时机销毁，可能导致内存泄漏", context);
            }
        }

        private void ValidateThreadingUsage(string message, object context)
        {
            // 检查线程安全
            if (IsThreadUnsafeOperation(message, context))
            {
                LogWarning("线程安全警告", "检测到可能的线程不安全操作", context);
            }
        }
        #endregion

        #region 辅助方法
        private void InitializeValidationRules()
        {
            // 初始化所有验证类型为启用状态
            foreach (ValidationType type in Enum.GetValues(typeof(ValidationType)))
            {
                _validationEnabled[type] = true;
            }

            // 添加验证规则
            _validationRules.Add("单例模式：避免在Awake中调用GetInstance");
            _validationRules.Add("事件系统：确保在OnDestroy中移除事件监听");
            _validationRules.Add("资源管理：及时回收不再使用的资源");
            _validationRules.Add("UI管理：使用规范的命名和层级");
            _validationRules.Add("性能优化：避免在Update中执行重操作");
            _validationRules.Add("内存管理：及时销毁创建的对象");
        }

        private void LogWarning(string title, string message, object context)
        {
            if (!_enableWarnings) return;

            string key = $"{title}:{message}";
            _warningCounts[key] = _warningCounts.GetValueOrDefault(key, 0) + 1;

            FrameworkLogger.Warn($"[框架验证] {title}: {message}");
        }

        private void LogError(string title, string message, object context)
        {
            if (!_enableErrors) return;

            string key = $"{title}:{message}";
            _errorCounts[key] = _errorCounts.GetValueOrDefault(key, 0) + 1;

            FrameworkLogger.Error($"[框架验证] {title}: {message}");
        }

        private bool IsInAwakeMethod()
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                if (method?.Name == "Awake")
                    return true;
            }
            return false;
        }

        private bool IsInUpdateMethod()
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                if (method?.Name == "Update")
                    return true;
            }
            return false;
        }

        private bool IsRepeatedInitialization(string message)
        {
            // 简单的重复检测逻辑
            return message.Contains("初始化") && message.Contains("开始");
        }

        private bool HasOnDestroyMethod(object context)
        {
            if (context == null) return false;
            
            var type = context.GetType();
            return type.GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) != null;
        }

        private bool IsValidEventName(string message)
        {
            // 检查事件名称是否符合规范（简单示例）
            return !string.IsNullOrEmpty(message) && message.Length > 3;
        }

        private bool IsValidResourcePath(string message)
        {
            // 检查资源路径是否有效（简单示例）
            return !string.IsNullOrEmpty(message) && !message.Contains("null");
        }

        private bool IsValidPanelName(string message)
        {
            // 检查UI面板名称是否符合规范（简单示例）
            return !string.IsNullOrEmpty(message) && message.Length > 2;
        }

        private bool IsValidUILayer(string message)
        {
            // 检查UI层级是否有效（简单示例）
            return message.Contains("层级") && !message.Contains("无效");
        }

        private bool IsResourceLoading(object context)
        {
            // 检查是否在资源加载过程中（简单示例）
            return context != null;
        }

        private bool IsHeavyOperation(string message)
        {
            // 检查是否是重操作（简单示例）
            return message.Contains("加载") || message.Contains("创建") || message.Contains("计算");
        }

        private bool IsFrequentCall(string message)
        {
            // 检查是否是频繁调用（简单示例）
            return message.Contains("Update") || message.Contains("每帧");
        }

        private bool IsMemoryLeakRisk(object context)
        {
            // 检查是否有内存泄漏风险（简单示例）
            return context != null && context.GetType().Name.Contains("Manager");
        }

        private bool IsThreadUnsafeOperation(string message, object context)
        {
            // 检查是否是线程不安全操作（简单示例）
            return message.Contains("多线程") || message.Contains("并发");
        }
        #endregion
    }
}
