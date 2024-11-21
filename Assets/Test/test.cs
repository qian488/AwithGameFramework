using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class test : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        UIManager.GetInstance().ShowPanel<testpanel>("testpanel",E_UI_Layer.Mid,ShowPanelOver);
    }

    private void ShowPanelOver(testpanel panel)
    {
        panel.InitInfo();
        Invoke("DelayHideTestPanel", 3);
    }

    private void DelayHideTestPanel()
    {
        UIManager.GetInstance().HidePanel("testpanel");
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            PoolManager.GetInstance().GetGameObject("Test/Cube", (go) =>
            {
                go.transform.localScale = Vector3.one * 2;
                UIManager.GetInstance().ShowPanel<testpanel>("testpanel", E_UI_Layer.Mid, ShowPanelOver);
            });
        }

        if (Input.GetMouseButtonDown(1))
        {
            PoolManager.GetInstance().GetGameObject("Test/Sphere", (go) => 
            { 
                go.transform.localScale = Vector3.one * 2; 
            });
        }
    }
}
