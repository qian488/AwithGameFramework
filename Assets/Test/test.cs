using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.UI;
using AwithGameFrame.Utils;

namespace AwithGameFrame.Tests
{
    public class Test : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
            UIManager.GetInstance().ShowPanel<TestPanel>(GameConstants.UI_PANEL_TEST, E_UI_Layer.Mid, ShowPanelOver);
        }

        private void ShowPanelOver(TestPanel panel)
        {
            panel.InitInfo();
            Invoke("DelayHideTestPanel", GameConstants.DEFAULT_PANEL_DELAY);
        }

        private void DelayHideTestPanel()
        {
            UIManager.GetInstance().HidePanel(GameConstants.UI_PANEL_TEST);
        }

        // Update is called once per frame
        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                PoolManager.GetInstance().GetGameObject(GameConstants.TEST_CUBE_PATH, (go) =>
                {
                    go.transform.localScale = Vector3.one * GameConstants.DEFAULT_UI_SCALE;
                    UIManager.GetInstance().ShowPanel<TestPanel>(GameConstants.UI_PANEL_TEST, E_UI_Layer.Mid, ShowPanelOver);
                });
            }

            if (Input.GetMouseButtonDown(1))
            {
                PoolManager.GetInstance().GetGameObject(GameConstants.TEST_SPHERE_PATH, (go) => 
                { 
                    go.transform.localScale = Vector3.one * GameConstants.DEFAULT_UI_SCALE; 
                });
            }
        }
    }
}