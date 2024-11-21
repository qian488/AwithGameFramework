using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DelayPush : MonoBehaviour
{
    void OnEnable()
    {
        Invoke("Push", 1);
    }

    void Push()
    {
        PoolManager.GetInstance().PushGameObject(this.gameObject.name,this.gameObject);
    }
}
