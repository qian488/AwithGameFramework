using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.Systems.InputSystem
{
    /// <summary>
    /// 输入管理器
    /// 支持可配置按键绑定，实现IInputManager接口
    /// </summary>
    public class InputManager : BaseManager<InputManager>, IInputManager
    {
        private readonly HashSet<KeyCode> _registeredKeys = new HashSet<KeyCode>();
        private bool _isEnabled;

        public bool IsEnabled => _isEnabled;

        public override int Priority => (int)ModulePriority.Features;

        public InputManager()
        {
            LoggingAPI.Info(LogCategory.Input, "InputManager初始化");
            MonoManager.GetInstance().AddUpdateListener(MyUpdate);

            // 默认注册常用按键
            RegisterKey(KeyCode.W);
            RegisterKey(KeyCode.A);
            RegisterKey(KeyCode.S);
            RegisterKey(KeyCode.D);
            RegisterKey(KeyCode.Q);
            RegisterKey(KeyCode.E);
            RegisterKey(KeyCode.R);
            RegisterKey(KeyCode.T);
            RegisterKey(KeyCode.V);
            RegisterKey(KeyCode.M);
        }

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<IInputManager>(this);
        }

        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            LoggingAPI.Info(LogCategory.Input, $"输入检测: {(enabled ? "开启" : "关闭")}");
        }

        public void RegisterKey(KeyCode key)
        {
            if (_registeredKeys.Add(key))
                LoggingAPI.Info(LogCategory.Input, $"注册按键: {key}");
        }

        public void UnregisterKey(KeyCode key)
        {
            if (_registeredKeys.Remove(key))
                LoggingAPI.Info(LogCategory.Input, $"取消注册按键: {key}");
        }

        public KeyCode[] GetRegisteredKeys()
        {
            var arr = new KeyCode[_registeredKeys.Count];
            _registeredKeys.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// 开始或停止输入检测（兼容旧API）
        /// </summary>
        public void StartOREndCheck(bool isOpen)
        {
            SetEnabled(isOpen);
        }

        private void CheckKeyCode(KeyCode key)
        {
            if (Input.GetKeyDown(key))
                EventCenter.GetInstance().EventTrigger("KeyDown", key);

            if (Input.GetKeyUp(key))
                EventCenter.GetInstance().EventTrigger("KeyUp", key);
        }

        private void MyUpdate()
        {
            if (!_isEnabled) return;

            foreach (var key in _registeredKeys)
            {
                CheckKeyCode(key);
            }
        }
    }
}
