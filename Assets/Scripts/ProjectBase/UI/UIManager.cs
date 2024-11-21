using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public enum E_UI_Layer
{
    Bot,
    Mid,
    Top,
    Syestem,
}

public class UIManager : BaseManager<UIManager>
{
    public Dictionary<string,BasePanel> panelDictionary = new Dictionary<string,BasePanel>();

    private Transform bot;
    private Transform mid;
    private Transform top;
    private Transform system;

    public RectTransform canvas;

    public UIManager()
    {
        GameObject go = ResourcesManager.GetInstance().Load<GameObject>("UI/Canvas");
        canvas = go.transform as RectTransform;
        GameObject.DontDestroyOnLoad(go);

        bot = canvas.Find("Bot");
        mid = canvas.Find("Mid");
        top = canvas.Find("Top");
        system = canvas.Find("System");

        go = ResourcesManager.GetInstance().Load<GameObject>("UI/EventSystem");
        GameObject.DontDestroyOnLoad(go);
    }

    public Transform GetUILayerFather(E_UI_Layer layer)
    {
        switch(layer)
        {
            case E_UI_Layer.Bot:
                return this.bot;
            case E_UI_Layer.Mid:
                return this.mid;
            case E_UI_Layer.Top:
                return this.top;
            case E_UI_Layer.Syestem:
                return this.system;
        }
        return null;
    }

    /// <summary>
    /// 显示面板
    /// </summary>
    /// <typeparam name="T">面板脚本类型</typeparam>
    /// <param name="panelName">面板名字</param>
    /// <param name="layer">面板所在层级</param>
    /// <param name="callback">面板创建后所作的事</param>
    public void ShowPanel<T>(string panelName, E_UI_Layer layer = E_UI_Layer.Mid, UnityAction<T> callback = null) where T : BasePanel 
    {
        if (panelDictionary.ContainsKey(panelName))
        {
            panelDictionary[panelName].ShowMe();
            if (callback != null)
            {
                callback(panelDictionary[panelName] as T);
            }
            return;
        }

        ResourcesManager.GetInstance().LoadAsync<GameObject>("UI/" + panelName, (go) =>
        {
            Transform father = bot;
            switch(layer)
            {
                case E_UI_Layer.Mid:
                    father = mid;
                    break;
                case E_UI_Layer.Top:
                    father = top;
                    break;
                case E_UI_Layer.Syestem:
                    father = system;
                    break;
            }
            go.transform.SetParent(father);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;
            (go.transform as RectTransform).offsetMax = Vector2.one;
            (go.transform as RectTransform).offsetMin = Vector2.one;

            T panel = go.GetComponent<T>();
            if(callback != null) callback(panel);
            panel.ShowMe();
            panelDictionary.Add(panelName, panel);
        });
    }

    public void HidePanel(string panelName)
    {
        if (panelDictionary.ContainsKey(panelName))
        {
            panelDictionary[panelName].HideMe();
            ResourcesManager.GetInstance().Recycle("UI/" + panelName, panelDictionary[panelName].gameObject);
            panelDictionary.Remove(panelName);
        }
    }

    public T GetPanel<T>(string panelName) where T : BasePanel
    {
        if (panelDictionary.ContainsKey(panelName))
        {
            return panelDictionary[panelName] as T;
        }
        return null;
    }

    /// <summary>
    /// 给控件增添自定义事件
    /// </summary>
    /// <param name="UIComponent">控件对象</param>
    /// <param name="type">事件类型</param>
    /// <param name="callback">事件的回调</param>
    public static void AddCustomEventListener(UIBehaviour UIComponent, EventTriggerType type, UnityAction<BaseEventData> callback)
    {
        EventTrigger eventTrigger = UIComponent.GetComponent<EventTrigger>();
        if (eventTrigger == null)
        {
            eventTrigger = UIComponent.gameObject.AddComponent<EventTrigger>();
        }
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = type;
        entry.callback.AddListener(callback);

        eventTrigger.triggers.Add(entry);
    }
}
