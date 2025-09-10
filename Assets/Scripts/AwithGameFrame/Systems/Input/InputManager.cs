using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Logging;

namespace AwithGameFrame.InputSystem
{
    public class InputManager : BaseManager<InputManager>
    {
        private bool isStart = false;
        public InputManager()
        {
            FrameworkLogger.LogInput("InputManager初始化开始");
            MonoManager.GetInstance().AddUpdateListener(MyUpdate);
            FrameworkLogger.LogInput("InputManager初始化完成");
        }

        public void StartOREndCheck(bool isOpen)
        {
            isStart = isOpen;
            FrameworkLogger.LogInput($"输入检测状态: {(isOpen ? "开启" : "关闭")}");
        }

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
    }
}
