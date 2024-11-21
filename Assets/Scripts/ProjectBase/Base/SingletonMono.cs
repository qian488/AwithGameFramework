using UnityEngine;

// �̳���mono�ĵ���ģʽ���� ��Ҫ�Լ���֤Ψһ�� �����ܶ�ι���
public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T GetInstance()
    {
        // �̳���Mono�Ľű� ���ܹ�ֱ��new
        // ֱ����ק����Addcomponent U3d�ڲ���ȥʵ��
        return instance; 
    }

    // ��������дawake
    protected virtual void Awake()
    {
        instance = this as T;
    }
}
