using UnityEngine.Events;

namespace AwithGameFrame.Foundation.Systems.UI
{
    /// <summary>
    /// UI管理器接口
    /// </summary>
    public interface IUIManager
    {
        /// <summary>
        /// 显示面板
        /// </summary>
        void ShowPanel<T>(string panelName, UILayer layer, UnityAction<T> callback = null) where T : BasePanel;

        /// <summary>
        /// 隐藏面板
        /// </summary>
        void HidePanel(string panelName);

        /// <summary>
        /// 获取面板
        /// </summary>
        T GetPanel<T>(string panelName) where T : BasePanel;
    }
}
