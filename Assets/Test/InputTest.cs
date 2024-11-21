using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputTest : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        InputManager.GetInstance().StartOREndCheck(true);

        EventCenter.GetInstance().AddEventListener<KeyCode>("KeyDown", CheckInputDown);
        EventCenter.GetInstance().AddEventListener<KeyCode>("KeyUp", CheckInputUp);
    }

    private void CheckInputUp(KeyCode key)
    {
        switch(key)
        {
            case KeyCode.W:
                Debug.Log("W Dwon");
                break;
            case KeyCode.A:
                Debug.Log("A Dwon");
                break;
            case KeyCode.S:
                Debug.Log("S Dwon");
                break;
            case KeyCode.D:
                Debug.Log("D Dwon");
                break;
            case KeyCode.Q:
                Debug.Log("Q Down");
                break;
            case KeyCode.E:
                Debug.Log("E Down");
                break;
            case KeyCode.R:
                Debug.Log("R Down");
                PoolManager.GetInstance().Clear();
                break;
            case KeyCode.T:
                Debug.Log("T Down");
                MusicManager.GetInstance().StopBGM();
                Debug.Log("StopBGM");
                break;
            case KeyCode.V:
                Debug.Log("V Down");
                MusicManager.GetInstance().PauseBGM();
                Debug.Log("PauseBGM");
                break;
            case KeyCode.M:
                Debug.Log("M Down");
                MusicManager.GetInstance().PlayBGM("For River - Piano (Johnny's Version)");
                Debug.Log("PlayBGM");
                break;
        }
    }

    private void CheckInputDown(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.W:
                Debug.Log("W Up");
                break;
            case KeyCode.A:
                Debug.Log("A Up");
                break;
            case KeyCode.S:
                Debug.Log("S Up");
                break;
            case KeyCode.D:
                Debug.Log("D Up");
                break;
            case KeyCode.Q:
                Debug.Log("Q Up");
                break;
            case KeyCode.E:
                Debug.Log("E Up");
                break;
            case KeyCode.R:
                Debug.Log("R Up");
                break;
            case KeyCode.T:
                Debug.Log("T Up");
                break;
            case KeyCode.V:
                Debug.Log("V Up");
                break;
            case KeyCode.M:
                Debug.Log("M Up");
                break;
        }
    }

}
