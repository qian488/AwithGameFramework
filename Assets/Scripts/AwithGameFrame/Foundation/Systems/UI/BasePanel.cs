using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AwithGameFrame.Foundation.Systems.UI
{
    /// <summary>
    /// 面板基类
    /// 提供自动UI组件绑定和事件处理功能
    /// </summary>
    public class BasePanel : MonoBehaviour
    {
        #region 字段
        /// <summary>UI组件字典，使用里氏转换原则存储各种UI组件</summary>
        private Dictionary<string,List<UIBehaviour>> UIComponentDictionary = new Dictionary<string,List<UIBehaviour>>();
        #endregion
        
        #region 生命周期
        /// <summary>
        /// 初始化UI组件绑定
        /// 子类可重写此方法添加自定义初始化逻辑
        /// </summary>
        protected virtual void Awake()
        {
            FindChildrenUIComponent<Button>();
            FindChildrenUIComponent<Image>();
            FindChildrenUIComponent<Text>();
            FindChildrenUIComponent<Toggle>();
            FindChildrenUIComponent<Slider>();
            FindChildrenUIComponent<ScrollRect>();
            FindChildrenUIComponent<InputField>();
        }

        #endregion
        
        #region 公共方法
        /// <summary>
        /// 显示面板
        /// 子类可重写此方法添加自定义显示逻辑
        /// </summary>
        public virtual void ShowMe() { }
        
        /// <summary>
        /// 隐藏面板
        /// 子类可重写此方法添加自定义隐藏逻辑
        /// </summary>
        public virtual void HideMe() { }
        #endregion
        
        #region 事件处理
        /// <summary>
        /// 按钮点击事件处理
        /// 子类可重写此方法处理按钮点击
        /// </summary>
        /// <param name="name">按钮名称</param>
        protected virtual void OnClick(string name) { }
        
        /// <summary>
        /// 值改变事件处理
        /// 子类可重写此方法处理值改变事件
        /// </summary>
        /// <param name="name">组件名称</param>
        /// <param name="value">新值</param>
        protected virtual void OnValueChanged(string name,bool value) { }
        #endregion
        
        #region 私有方法

        /// <summary>
        /// 获取指定名称的UI组件
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="name">组件名称</param>
        /// <returns>找到的组件，未找到返回null</returns>
        protected T GetUIComponent<T>(string name) where T : UIBehaviour
        {
            if (UIComponentDictionary.ContainsKey(name))
            {
                for (int i = 0; i < UIComponentDictionary[name].Count; i++)
                {
                    if (UIComponentDictionary[name][i] is T)
                    {
                        return UIComponentDictionary[name][i] as T;
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// 查找子对象中的UI组件并自动绑定事件
        /// </summary>
        /// <typeparam name="T">UI组件类型</typeparam>
        private void FindChildrenUIComponent<T>() where T : UIBehaviour
        {
            T[] Components = this.GetComponentsInChildren<T>();
            for (int i = 0; i < Components.Length; i++)
            {
                string itemName = Components[i].gameObject.name;
                if (UIComponentDictionary.ContainsKey(itemName))
                {
                    UIComponentDictionary[itemName].Add(Components[i]);
                }
                else
                {
                    UIComponentDictionary.Add(itemName, new List<UIBehaviour>() { Components[i] });
                }

                if (Components[i] is Button)
                {
                    (Components[i] as Button).onClick.AddListener(() =>
                    {
                        OnClick(itemName);
                    });
                }
                else if (Components[i] is Toggle)
                {
                    (Components[i] as Toggle).onValueChanged.AddListener((value) =>
                    {
                        OnValueChanged(itemName, value);
                    });
                }
            }
        }
        #endregion
    }
}
