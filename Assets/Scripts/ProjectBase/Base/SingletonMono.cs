using UnityEngine;

// 继承了mono的单例模式对象 需要自己保证唯一性 即不能多次挂载
public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T GetInstance()
    {
        // 继承了Mono的脚本 不能够直接new
        // 直接拖拽或者Addcomponent U3d内部会去实现
        return instance; 
    }

    // 子类需重写awake
    protected virtual void Awake()
    {
        instance = this as T;
    }
}
