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
    /// ��ʾ���
    /// </summary>
    /// <typeparam name="T">���ű�����</typeparam>
    /// <param name="panelName">�������</param>
    /// <param name="layer">������ڲ㼶</param>
    /// <param name="callback">��崴������������</param>
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
                    father = top;
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
            GameObject.Destroy(panelDictionary[panelName].gameObject);
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
    /// ���ؼ������Զ����¼�
    /// </summary>
    /// <param name="UIComponent">�ؼ�����</param>
    /// <param name="type">�¼�����</param>
    /// <param name="callback">�¼��Ļص�</param>
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