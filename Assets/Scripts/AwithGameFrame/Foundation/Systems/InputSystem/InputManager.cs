using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Foundation.Logging;

namespace AwithGameFrame.Foundation.Systems.InputSystem
{
    /// <summary>
    /// 输入管理器
    /// 负责键盘输入检测和事件触发
    /// </summary>
    public class InputManager : BaseManager<InputManager>
    {
        #region 字段
        /// <summary>是否开始检测输入</summary>
        private bool isStart = false;
        #endregion
        
        #region 构造函数
        /// <summary>
        /// 初始化输入管理器
        /// </summary>
        public InputManager()
        {
            FrameworkLogger.LogInput("InputManager初始化开始");
            MonoManager.GetInstance().AddUpdateListener(MyUpdate);
            FrameworkLogger.LogInput("InputManager初始化完成");
        }
        #endregion
        
        #region 公共方法
        /// <summary>
        /// 开始或停止输入检测
        /// </summary>
        /// <param name="isOpen">是否开启检测</param>
        public void StartOREndCheck(bool isOpen)
        {
            isStart = isOpen;
            FrameworkLogger.LogInput($"输入检测状态: {(isOpen ? "开启" : "关闭")}");
        }
        #endregion
        
        #region 私有方法

        /// <summary>
        /// 检查指定按键的按下和抬起状态
        /// </summary>
        /// <param name="key">要检查的按键</param>
        private void CheckKeyCode(KeyCode key)
        {
            if (Input.GetKeyDown(key))
            {
                EventCenter.GetInstance().EventTrigger("KeyDown", key);
                FrameworkLogger.LogInput($"按键按下: {key}");
            }
            if (Input.GetKeyUp(key))
            {
                EventCenter.GetInstance().EventTrigger("KeyUp", key);
                FrameworkLogger.LogInput($"按键抬起: {key}");
            }
        }

        /// <summary>
        /// 更新方法，检测所有配置的按键
        /// </summary>
        private void MyUpdate()
        {
            if (!isStart) return;

            CheckKeyCode(KeyCode.W);
            CheckKeyCode(KeyCode.A);
            CheckKeyCode(KeyCode.S);
            CheckKeyCode(KeyCode.D);
            CheckKeyCode(KeyCode.Q);
            CheckKeyCode(KeyCode.E);
            CheckKeyCode(KeyCode.R);
            CheckKeyCode(KeyCode.T);
            CheckKeyCode(KeyCode.V);
            CheckKeyCode(KeyCode.M);
        }
        #endregion
    }
}
