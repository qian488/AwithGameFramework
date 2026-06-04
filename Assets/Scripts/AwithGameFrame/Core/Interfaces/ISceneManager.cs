using UnityEngine.Events;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 场景管理器接口
    /// 负责同步/异步场景切换
    /// </summary>
    public interface ISceneManager
    {
        /// <summary>
        /// 同步加载场景
        /// </summary>
        void LoadScene(string sceneName, UnityAction callback = null);

        /// <summary>
        /// 异步加载场景（通过事件中心发送加载进度）
        /// </summary>
        void LoadSceneAsync(string sceneName, UnityAction callback = null);
    }
}
