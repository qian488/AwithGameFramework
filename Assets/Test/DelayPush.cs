using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Foundation.Pool;
using AwithGameFrame.Core;

public class DelayPush : MonoBehaviour
{
    void OnEnable()
    {
        Invoke("Push", 1);
    }

    void Push()
    {
        PoolManagerAPI.PushGameObject(this.gameObject.name,this.gameObject);
    }
}
