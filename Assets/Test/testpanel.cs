using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AwithGameFrame.Core;
using AwithGameFrame.Foundation.Systems.UI;

namespace AwithGameFrame.Tests
{
    public class TestPanel : BasePanel
    {
        protected override void Awake()
        {
            base.Awake();
        }
        
        // Start is called before the first frame update
        void Start()
        {
            //GetUIComponent<Button>("ButtonStart").onClick.AddListener(ClickStart);
            //GetUIComponent<Button>("ButtonQuit").onClick.AddListener(ClickQuit);

            UIManager.AddCustomEventListener(GetUIComponent<Button>(GameConstants.UI_BUTTON_START), EventTriggerType.PointerEnter, (data) =>
            {
                Debug.Log("进入ButtonStart");
            });
            UIManager.AddCustomEventListener(GetUIComponent<Button>(GameConstants.UI_BUTTON_START), EventTriggerType.PointerExit, (data) =>
            {
                Debug.Log("离开ButtonStart");
            });
        }

        protected override void OnClick(string name)
        {
            switch(name)
            {
                case GameConstants.UI_BUTTON_START:
                    Debug.Log("Start被点击");
                    break;
                case GameConstants.UI_BUTTON_QUIT:
                    Debug.Log("Quit被点击");
                    break;
            }
        }

        protected override void OnValueChanged(string name, bool value)
        {

        }

        private void ClickQuit()
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
            Debug.Log("初始化面板数据");
        }
    }
}