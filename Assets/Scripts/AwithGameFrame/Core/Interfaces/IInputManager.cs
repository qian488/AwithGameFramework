namespace AwithGameFrame.Core
{
    /// <summary>
    /// 输入管理器接口
    /// 负责键盘输入检测和事件触发
    /// </summary>
    public interface IInputManager
    {
        /// <summary>
        /// 是否启用输入检测
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// 开启或停止输入检测
        /// </summary>
        void SetEnabled(bool enabled);

        /// <summary>
        /// 注册需要检测的按键
        /// </summary>
        void RegisterKey(UnityEngine.KeyCode key);

        /// <summary>
        /// 取消注册按键
        /// </summary>
        void UnregisterKey(UnityEngine.KeyCode key);

        /// <summary>
        /// 获取当前注册的所有按键
        /// </summary>
        UnityEngine.KeyCode[] GetRegisteredKeys();
    }
}
