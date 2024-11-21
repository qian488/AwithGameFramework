using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 面板基类
/// </summary>
public class BasePanel : MonoBehaviour
{
    // 里氏转换原则 使用基类装各种子类
    private Dictionary<string,List<UIBehaviour>> UIComponentDictionary = new Dictionary<string,List<UIBehaviour>>();
    // Start is called before the first frame update
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

    public virtual void ShowMe() { }
    public virtual void HideMe() { }

    protected virtual void OnClick(string name) { }
    protected virtual void OnValueChanged(string name,bool value) { }

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
                    OnValueChanged(name, value);
                });
            }

        }
    }
}
