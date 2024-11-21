using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class testpanel : BasePanel
{
    protected override void Awake()
    {
        base.Awake();

    }
    // Start is called before the first frame update
    void Start()
    {
        //GetUIComponent<Button>("ButtonStart").onClick.AddListener(ClickStart);
        //GetUIComponent<Button>("ButtonQuit").onClick.AddListener(ClickQiut);

        UIManager.AddCustomEventListener(GetUIComponent<Button>("ButtonStart"), EventTriggerType.PointerEnter, (data) =>
        {
            Debug.Log("����ButtonStart");
        });
        UIManager.AddCustomEventListener(GetUIComponent<Button>("ButtonStart"), EventTriggerType.PointerExit, (data) =>
        {
            Debug.Log("�뿪ButtonStart");
        });
    }

    protected override void OnClick(string name)
    {
        switch(name)
        {
            case "ButtonStart":
                Debug.Log("Start�����");
                break;
            case "ButtonQuit":
                Debug.Log("Quit�����");
                break;
        }
    }

    protected override void OnValueChanged(string name, bool value)
    {

    }

    private void ClickQiut()
    {
        Debug.Log("Quit");
    }

    private void ClickStart()
    {
        Debug.Log("Start");
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void InitInfo()
    {
        Debug.Log("��ʼ���������");
    }
}
