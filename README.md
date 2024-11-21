## AwithGameFramework

我的游戏框架，不断完善ing

### 单例模式基类

单例模式的核心思想是**一个类只能有一个实例，并且提供一个全局的访问点来获取这个实例**。

单例模式分为饿汉式和懒汉式两种。

- 饿汉式单例模式：
  在程序一开始的时候就创建了单例对象。但这样一来，这些对象就会在程序一开始时就存在于内存之中，占据着一定的内存。

- 懒汉式单例模式：
  在用到单例对象的时候才会创建单例对象。

一个游戏可能有很多管理类，都需要使用到单例，为避免重复代码，通过泛型可以管理不同类型的单例实例。

```csharp
public class BaseManager<T> where T : new()
{
    private static T instance;
    public static T GetInstance()
    {
        if (instance == null)
        {
            instance = new T();
        }
        return instance; 
    }
}
```

约束 `where T : new()` 表明，`T` 类型必须具备一个无参构造函数。这样的好处就是可以自动创建实例，保证实例化。

注意，该代码实现没有继承MonoBehaviour，所以update等相关的生命周期函数不能使用。这会比较麻烦。

如果要继承MonoBehaviour的单例，可以如下实现

```csharp
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

```

注意，继承了Mono的脚本，不能够直接new，直接拖拽或者Addcomponent，需要自己保证唯一性，即不能多次挂载，这会破坏单例模式。因此，可以优化。

```csharp
// 继承了mono的单例模式对象 但自动添加脚本
public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T GetInstance()
    {
        if (instance == null)
        {
            GameObject go = new GameObject();
            go.name = typeof(T).ToString();
            DontDestroyOnLoad(go);
            instance = go.AddComponent<T>();
        }
        return instance;
    }

}
```

除了继承Mono，实现单例来使用，还可以将Mono封装起来，实现一个公共的Mono,供全局访问。

### 公共Mono模块

封装一个公共的Mono模块，很明显是要实现一个单例，但在此之前，需要思考，怎么将Update等函数在不继承Mono的类中使用？我们可以使用事件机制提供动态添加和移除帧更新事件的能力。允许外部在每帧调用时注册和注销更新方法。

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MonoControl : MonoBehaviour
{
    private event UnityAction updateEvent;

    void Start()
    {
        DontDestroyOnLoad(this.gameObject);
    }

    void Update()
    {
        if (updateEvent != null)
        {
            updateEvent();
        }
    }

    /// <summary>
    /// 给外部提供 添加帧更新事件
    /// </summary>
    /// <param name="function"></param>
    public void AddUpdateListener(UnityAction function)
    {
        updateEvent += function;
    }

    /// <summary>
    /// 给外部提供 移除帧更新事件
    /// </summary>
    /// <param name="function"></param>
    public void RemoveUpdateListener(UnityAction function) 
    { 
        updateEvent -= function; 
    }
}

```

这里以最常用的Update举例，但是fixedupdate等亦可以如法炮制。

接下来，就是进行公共Mono的封装。

```csharp
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 公共Mono管理
/// </summary>
public class MonoManager : BaseManager<MonoManager>
{
    private MonoControl controller;

    public MonoManager()
    {
        // 单例保证了MonoControl对象的唯一性
        GameObject go = new GameObject("MonoController");
        controller = go.AddComponent<MonoControl>();
    }

    /// <summary>
    /// 给外部提供 添加帧更新事件
    /// </summary>
    /// <param name="function"></param>
    public void AddUpdateListener(UnityAction function)
    {
        controller.AddUpdateListener(function);
    }

    /// <summary>
    /// 给外部提供 移除帧更新事件
    /// </summary>
    /// <param name="function"></param>
    public void RemoveUpdateListener(UnityAction function)
    {
        controller.RemoveUpdateListener(function);
    }

    #region 协程相关
    public Coroutine StartCoroutine(string methodName)
    {
        return controller.StartCoroutine(methodName);
    }

    public Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
    {
        return controller.StartCoroutine(methodName, value);
    }

    public Coroutine StartCoroutine(IEnumerator routine)
    {
        return controller.StartCoroutine(routine);
    }

    public void StopCoroutine(IEnumerator routine)
    {
        controller.StopCoroutine(routine);
    }

    public void StopCoroutine(Coroutine routine)
    {
        controller.StopCoroutine(routine);
    }
    #endregion
}

```

可以看出，我们这样做的目的，实际上是用一个MonoManager 的单例来裹住继承了Mono的Monoctrol。实际上无论使用Update还是开启协程等，都是使用的Monoctrol。这样的设计可以使得有一个全局的Mono供外部访问。

- 外部可以随时添加和移除需要每帧调用的回调函数，进行帧更新事件管理。

- 通过封装协程的启动和停止操作，允许外部在一个统一的管理器中处理所有协程。

**协程（Coroutine）** 是一种用于在多个帧之间执行代码的机制，它使得你能够在 Unity 的主线程中暂停代码的执行，等待一段时间或者等待某些条件成立，然后继续执行。这使得协程在需要处理时间相关的任务（如动画、延迟操作、异步加载等）时非常有用。

### 事件中心模块

框架的事件系统主要负责高效的方法调用与数据传递，实现各功能之间的解耦。

通常在调用某个实例的方法时，必须先获得这个实例的引用或者新实例化一个对象。我们希望程序本身不去关注被调用的方法所依托的实例对象是否存在，通过事件系统做中转将功能的调用封装成事件，使用事件监听注册、移除和事件触发完成模块间的功能调用管理。

基于 **观察者设计模式** ，我们可以设计一个事件中心管理系统来实现。

- 访问其它脚本时，不直接访问，而是通过发送一条类似“命令”，让监听了这条“命令”的脚本自动执行对应的逻辑。

- 让脚本向事件中心添加事件，监听对应的“命令”。
  发送“命令”，事件中心就会通知监听了这条“命令”的脚本，让它们自动执行对应的逻辑。

- 简单来说，事件中心就像是一个公告栏。它负责发布公告（事件），其他人关注了公告（监听事件），一旦公告内容如任务被实现（某个事件被触发），所有关注者（注册了该事件的监听器）就会执行相应的处理逻辑。

事件中心在系统中的作用是一个集中管理、发布和监听事件的地方，让各个组件之间的耦合度保持较低，只通过事件的形式进行通信。

```csharp
public class EventCenter : BaseManager<EventCenter>
{
    // key 事件名字
    // value 监听事件的委托函数
    // 用基类装子类 想要使用泛型 开一个空接口 封装一个泛型 原因：避免原来的object类型拆装箱
    private Dictionary<string, IEventInfo> evenDictionary = new Dictionary<string, IEventInfo>();

    /// <summary>
    /// 添加事件监听
    /// </summary>
    /// <param name="eventName">事件名字</param>
    /// <param name="action">处理事件的委托函数</param>
    public void AddEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions += action;
        }
        else
        {
            evenDictionary.Add(eventName, new EventInfo<T>(action));
        }
    }

    /// <summary>
    /// 移除事件监听
    /// </summary>
    /// <param name="eventName">事件名字</param>
    /// <param name="action">对应之前添加的委托函数</param>
    public void RemoveEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions -= action;
        }
    }

    /// <summary>
    /// 事件触发
    /// </summary>
    /// <param name="eventName">哪个名字的事件触发</param>
    /// <param name="info">委托函数附带的信息</param>
    public void EventTrigger<T>(string eventName,T info)
    {
        if(evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions?.Invoke(info);
        }

    }

    public void Clear()
    {
        evenDictionary.Clear();
    }
}
```

事件中心使用一个 `Dictionary` 存储事件名称和对应的事件信息。`Dictionary` 的键是事件的名称（`string` 类型），值是事件的封装信息（`IEventInfo` 类型），这样可以灵活支持不同类型的事件。

- **添加事件监听**，如果事件已经存在于字典中，就将新的处理方法添加到现有事件的 `actions` 委托中；如果事件不存在，就创建一个新的 `EventInfo<T>` 并添加到字典中。

- **移除事件监听**，如果事件存在，就从 `actions` 委托中移除对应的处理方法。

- **事件触发**，如果事件已经在字典中注册，就通过 `actions` 委托触发事件，并传递事件信息（`info`）作为参数。

**有关`IEventInfo` 类型的实现**

我们想要传递带有一个参数的事件，自然而然的想到泛型来表示，但是因为事件中心不是泛型类，直接使用泛型会报错。那么可以怎么思考？里氏替换原则，即子类可以替换父类而不会影响程序的正确性。因为C#中几乎所有的类型都可以追溯到`object`作为它们的最终基类。因此，可以使用object来存取。但是，这样会涉及拆箱装箱，增加内存开销和降低性能。那么怎么办？不能直接使用泛型，那就间接使用！创建一个泛型类，但是有个空接口作为基类。使用基类装子类，可以间接使用。

```csharp
public interface IEventInfo
{

}
// 传递泛型参数
public class EventInfo<T> : IEventInfo
{
    public UnityAction<T> actions;

    public EventInfo(UnityAction<T> action)
    {
        actions += action;
    }

}
```

- **`IEventInfo` 接口** 是一个空接口，作为所有事件信息类的基类。它用于避免原来的 `object` 类型拆装箱问题，并使得不同类型的事件信息可以统一存储。

- **`EventInfo<T>` 类** 封装了一个带参数的事件信息，`T` 是事件触发时传递的参数类型。`actions` 是一个 `UnityAction<T>` 委托，用来存储所有注册的事件处理方法。构造函数中将事件处理方法添加到 `actions` 中。

类似的，我们还可以写一个不带参数的事件信息以及相关的事件监听等。

完整示例代码

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#region 事件信息封装 基类装子类
public interface IEventInfo
{

}
// 传递泛型参数
public class EventInfo<T> : IEventInfo
{
    public UnityAction<T> actions;

    public EventInfo(UnityAction<T> action)
    {
        actions += action;
    }

}
// 不传递参数
public class EventInfo : IEventInfo
{
    public UnityAction actions;

    public EventInfo(UnityAction action)
    {
        actions += action;
    }

}
#endregion

/// <summary>
/// 事件中心 单例模式对象
/// 观察者设计模式
/// </summary>
public class EventCenter : BaseManager<EventCenter>
{
    // key 事件名字
    // value 监听事件的委托函数
    // 用基类装子类 想要使用泛型 开一个空接口 封装一个泛型 原因：避免原来的object类型拆装箱
    private Dictionary<string, IEventInfo> evenDictionary = new Dictionary<string, IEventInfo>();

    #region 事件监听
    /// <summary>
    /// 添加事件监听
    /// </summary>
    /// <param name="eventName">事件名字</param>
    /// <param name="action">处理事件的委托函数</param>
    public void AddEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions += action;
        }
        else
        {
            evenDictionary.Add(eventName, new EventInfo<T>(action));
        }
    }

    public void AddEventListener(string eventName, UnityAction action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo).actions += action;
        }
        else
        {
            evenDictionary.Add(eventName, new EventInfo(action));
        }
    }
    #endregion

    #region 移除事件监听
    /// <summary>
    /// 移除事件监听
    /// </summary>
    /// <param name="eventName">事件名字</param>
    /// <param name="action">对应之前添加的委托函数</param>
    public void RemoveEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions -= action;
        }
    }
    public void RemoveEventListener(string eventName, UnityAction action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo).actions -= action;
        }
    }
    #endregion

    #region 事件触发
    /// <summary>
    /// 事件触发
    /// </summary>
    /// <param name="eventName">哪个名字的事件触发</param>
    /// <param name="info">委托函数附带的信息</param>
    public void EventTrigger<T>(string eventName,T info)
    {
        if(evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions?.Invoke(info);
        }

    }
    public void EventTrigger(string eventName)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo).actions?.Invoke();
        }

    }
    #endregion

    public void Clear()
    {
        evenDictionary.Clear();
    }
}

```

这里仅封装了无参和一个参数的事件，同理还可以实现两个甚至三个参数的事件。

如这个实现两个参数的例子

```csharp
public class EventInfo<T0,T1> : IEventInfo
{
    public UnityAction<T0,T1> actions;

    public EventInfo(UnityAction<T0,T1> action)
    {
        actions += action;
    }

}

public void AddEventListener<T0,T1>(string eventName, UnityAction<T0,T1> action){
    if (evenDictionary.ContainsKey(eventName))
	{
    	(evenDictionary[eventName] as EventInfo<T0,T1>).actions += action;
	}
	else
	{
    	evenDictionary.Add(eventName, new EventInfo<T0,T1>(action));
	}
}
```



### 场景切换模块

在游戏开发中，场景的切换是非常常见的操作。通过将场景切换的逻辑封装在一个单独的模块中（`MyScenesManager`），可以让场景切换的操作变得更加简洁和一致。开发者只需要调用这个模块的方法即可实现场景加载，而不需要重复编写加载代码。这样可以使得场景管理更加集中化，便于后续维护和扩展。

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景切换模块
/// </summary>
public class MyScenesManager : BaseManager<MyScenesManager>
{
    // 同步加载场景
    public void LoadScene(string sceneName,UnityAction function)
    {
        SceneManager.LoadScene(sceneName);
        function();
    }

    // 异步加载场景
    public void LoadSceneAsync(string sceneName, UnityAction function)
    {
        MonoManager.GetInstance().StartCoroutine(ReallyLoadSceneAsync(sceneName, function));
    }

    private IEnumerator ReallyLoadSceneAsync(string sceneName,UnityAction function)
    {
        AsyncOperation ao = SceneManager.LoadSceneAsync(sceneName);
        while(ao.isDone)
        {
            EventCenter.GetInstance().EventTrigger("Loading",ao.progress);
            yield return ao.progress;
        }
        function();
    }
}

```

场景的加载有两种主要方式：**同步加载** 和 **异步加载**。同步加载会阻塞当前线程，直到场景完全加载。而异步加载则允许游戏在加载新场景时继续执行其他任务（如显示加载画面、播放过渡动画等）。

- **同步加载**：适用于加载较小的场景或不需要用户交互的情况。
- **异步加载**：适用于需要加载较大场景，或者在加载过程中需要与玩家交互（如显示进度条或加载动画）的情况。

通过 `MyScenesManager` 模块，开发者能够方便地切换这两种加载方式，只需调用不同的方法 (`LoadScene` 或 `LoadSceneAsync`)，而无需关心内部的实现细节。

### 资源加载模块

资源加载同样需要考虑同步和异步。

- **同步加载资源**：`Load<T>` 方法同步加载资源，并判断资源类型。如果资源是 `GameObject`，会自动实例化；否则直接返回资源。

- **异步加载资源**：`LoadAsync<T>` 方法异步加载资源，避免阻塞主线程。使用协程来加载资源，加载完成后通过回调函数通知调用者。

在加载完成后，无论资源是 `GameObject` 还是其他类型，都会执行相应的处理（实例化或直接返回资源）。

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : BaseManager<ResourcesManager>
{
    // 同步加载资源
    public T Load<T>(string name) where T : Object
    {
        T res = Resources.Load<T>(name);
        if (res is GameObject)
        {
            return GameObject.Instantiate(res);
        }
        else
        {
            return res;
        }
    }

    // 异步加载资源
    public void LoadAsync<T>(string name,UnityAction<T> callback) where T : Object
    {
        MonoManager.GetInstance().StartCoroutine(ReallyLoadAsync(name, callback));
    }

    private IEnumerator ReallyLoadAsync<T>(string name, UnityAction<T> callback) where T : Object
    {
        ResourceRequest request = Resources.LoadAsync<T>(name);
        yield return request;

        if(request.asset is GameObject)
        {
            callback(GameObject.Instantiate(request.asset) as T);
        }
        else
        {
            callback(request.asset as T);
        }
    }
}

```



### 缓存池模块

**什么是对象池？对象池有什么用？**
频繁创建和销毁对象会造成性能的开销。

创建对象的时候，系统会为这个对象开辟一片新的空间。销毁对象的时候，这个对象会变成内存垃圾，当内存垃圾达到一定程度，就会触发垃圾回收机制（及GC），清理内存垃圾，由于此时在清理垃圾，所以程序有可能就会变卡。

为了改善这个问题，我们就可以使用对象池。使用了它之后，程序的性能就能得到提升不那么容易变卡。

**对象池的原理**

- 当要创建对象的时候，不直接创建，而是先从对象池里面找，如果对象池里有这种类型的对象，就从对象池中取出来用。如果对象池里面没有这种类型的对象，才创建该对象。
- 当要销毁对象的时候，不直接销毁，而是把这个对象放进对象池里面存着，以便下一次创建对象时使用。



```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 缓存池模块
/// </summary>
public class PoolManager : BaseManager<PoolManager>
{
    public Dictionary<string,PoolData> poolDictionary = new Dictionary<string, PoolData>();

    private GameObject poolGO;
    /// <summary>
    /// 获取池中对象
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public void GetGameObject(string name,UnityAction<GameObject> callback)
    {
        if (poolDictionary.ContainsKey(name) && poolDictionary[name].poolList.Count > 0)
        {
            callback(poolDictionary[name].GetGameObject());
        }
        else
        {
            ResourcesManager.GetInstance().LoadAsync<GameObject>(name, (go) =>
            {
                go.name = name;
                callback(go);
            });
        }
    }
    /// <summary>
    /// 将对象压入池中
    /// </summary>
    /// <param name="name"></param>
    /// <param name="go"></param>
    public void PushGameObject(string name,GameObject go)
    {
        if(poolGO == null) poolGO = new GameObject("Pool");

        if (poolDictionary.ContainsKey(name))
        {
            poolDictionary[name].PushGameObject(go);
        }
        else
        {
            poolDictionary.Add(name, new PoolData(go,poolGO));
        }
    }

    public void Clear()
    {
        poolDictionary.Clear();
        poolGO = null;
    }
}

```

**`GetGameObject` 方法 - 从对象池获取对象**

这个方法的作用是从缓存池中获取一个对象。具体流程如下：

- **检查对象池**：首先检查 `poolDictionary` 中是否已经有指定名称的对象池（`poolDictionary[name]`），并且该池中是否有可用的对象（即池中有剩余的对象）。

- **池中有对象**：如果对象池存在且池中有剩余的对象，就从池中取出一个对象，调用 `callback(poolDictionary[name].GetGameObject())` 返回给调用者。

- **池中没有对象**：如果池中没有对象，则调用 `ResourcesManager.GetInstance().LoadAsync<GameObject>` 异步加载指定名称的资源。如果加载成功，则将加载的资源作为一个新的对象返回，并将其添加到池中。加载完成后的回调 `callback(go)` 会被调用，返回加载的 `GameObject`。

  **异步加载**：使用异步加载 (`LoadAsync`) 可以避免阻塞主线程，从而提高性能，尤其是当对象较大或者需要较长加载时间时。

**`PushGameObject` 方法 - 将对象压入对象池**

该方法将一个对象放回池中，供以后使用。具体流程如下：

- **检查对象池**：首先检查 `poolDictionary` 中是否已存在指定名称的对象池。如果池中已经存在，则调用池中的 `PushGameObject(go)` 方法将对象添加到池中。
- **创建新的池**：如果池中没有指定名称的对象池，则会创建一个新的 `PoolData` 实例，并将新对象添加到池中。
- **poolGO**：`poolGO` 是一个空的 `GameObject`，用来承载池中的所有对象。每个对象池的对象会被组织在这个 `poolGO` 下，以便于场景管理和对象的生命周期控制。

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// fatherGameObject 是这一类对象的容器 父节点
/// poolList 是池中的对象容器
/// </summary>
public class PoolData
{ 
    public GameObject fatherGameObject;
    public List<GameObject> poolList;

    public PoolData(GameObject go, GameObject poolGO)
    {
        fatherGameObject = new GameObject(go.name);
        fatherGameObject.transform.parent = poolGO.transform;
        poolList = new List<GameObject>() { };
        PushGameObject(go);
    }

    public void PushGameObject(GameObject go)
    {
        go.SetActive(false);
        poolList.Add(go);
        go.transform.parent = fatherGameObject.transform;
    }

    // 默认拿取第一个
    public GameObject GetGameObject()
    {
        GameObject go = poolList[0];
        poolList.RemoveAt(0);
        go.SetActive(true);
        go.transform.parent = null;
        return go;
    }
}

```

`PoolData` 是一个用于管理对象池的类，负责存储池中的对象并提供获取和归还对象的功能。

每个对象池有一个父节点 `fatherGameObject`，所有池中的对象都作为这个父节点的子物体进行管理，确保对象在场景中的组织结构。

对象池使用 `List<GameObject>` 来存储池中的所有对象，支持按需获取和归还对象。

当从池中获取对象时，对象会被激活，并从池中移除；当对象归还池时，它会被禁用，并添加回池中。

### 输入控制模块

通过前面的公共mono和事件中心，我们可以很容易实现输入控制模块的封装管理。

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputManager : BaseManager<InputManager>
{
    private bool isStart = false;
    public InputManager()
    {
        MonoManager.GetInstance().AddUpdateListener(MyUpdate);
    }

    public void StartOREndCheck(bool isOpen)
    {
        isStart = isOpen;
    }

    private void CheckKeyCode(KeyCode key)
    {
        if (Input.GetKeyDown(key))
        {
            EventCenter.GetInstance().EventTrigger("KeyDown",key);
        }
        if (Input.GetKeyUp(key))
        {
            EventCenter.GetInstance().EventTrigger("KeyUp",key);
        }
    }

    private void MyUpdate()
    {
        if (!isStart) return;

        CheckKeyCode(KeyCode.W);
        CheckKeyCode(KeyCode.A);
        CheckKeyCode(KeyCode.S);
        CheckKeyCode(KeyCode.D);
        CheckKeyCode(KeyCode.Q);
        CheckKeyCode(KeyCode.E);
        CheckKeyCode(KeyCode.R);
        CheckKeyCode(KeyCode.T);
        CheckKeyCode(KeyCode.V);
        CheckKeyCode(KeyCode.M);
    }
}

```

这个 `InputManager` 类的设计目的是将键盘输入的管理模块化，并通过事件驱动的方式将按键事件传递出去。事件中心 (`EventCenter`) 负责监听并处理这些事件，从而解耦了输入检测和响应的具体实现。

- **输入管理**：通过集中管理所有输入事件（如按键按下和释放），简化了游戏中多个地方对输入的处理。
- **事件驱动**：利用事件中心发布按键事件，可以让游戏中的其他部分订阅这些事件，并根据需要进行响应。这种方式避免了将输入逻辑直接写入每个需要响应的地方，减少了代码耦合。
- **启停控制**：通过 `StartOREndCheck` 方法控制输入监听的开启与关闭，增加了灵活性。例如，可以在游戏暂停时停止监听输入，或在某些场景中禁止输入。

简单测试如下

```csharp
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
```

代码中部分涉及MusicManager是我用于测试音频模块的内容，可以忽略。

### 音频管理模块

基本上绝大多数游戏都是需要音频的，而且，一般而言，音频主要分三类，背景音乐、音效、角色声音。因此，我们主要封装这部分内容。同样是管理者，故继承BaseManager实现单例。

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MusicManager : BaseManager<MusicManager>
{
    private AudioSource BGM = null;
    private float BGMValue = 1f;

    private GameObject SFXGO = null;
    private List<AudioSource> SFXList = new List<AudioSource>();
    private float SFXValue = 1f;

    private GameObject VoiceGO = null;
    private List<AudioSource> VoiceList = new List<AudioSource>();
    private float VoiceValue = 1f;

    public MusicManager()
    {
        MonoManager.GetInstance().AddUpdateListener(Update);
    }

    private void Update()
    {
        for(int i = SFXList.Count - 1; i >= 0; i--)
        {
            if (!SFXList[i].isPlaying)
            {
                GameObject.Destroy(SFXList[i]);
                SFXList.RemoveAt(i);
            }
        }

        for (int i = VoiceList.Count - 1; i >= 0; i--)
        {
            if (!VoiceList[i].isPlaying)
            {
                GameObject.Destroy(VoiceList[i]);
                VoiceList.RemoveAt(i);
            }
        }
    }

    #region BGM -- 背景音乐
    public void PlayBGM(string name)
    {
        if (BGM == null)
        {
            GameObject go = new GameObject();
            go.name = "BGM";
            BGM = go.AddComponent<AudioSource>();
        }

        ResourcesManager.GetInstance().LoadAsync<AudioClip>("Music/BGM/" + name, (clip) =>
        {
            BGM.clip = clip;
            BGM.volume = BGMValue;
            BGM.loop = true;
            BGM.Play();
        });
    }

    public void PauseBGM()
    {
        if (BGM == null) return;
        BGM.Pause();
    }

    public void StopBGM() 
    {
        if (BGM == null) return;
        BGM.Stop();
    }

    public void ChangeBGMValue(float value)
    {
        BGMValue = value;
        if (BGM == null) return;
        BGM.volume = BGMValue;
    }
    #endregion

    #region SFX -- 音效
    public void PlaySFX(string name, bool isloop, UnityAction<AudioSource> callback = null)
    {
        if(SFXGO == null)
        {
            SFXGO = new GameObject();
            SFXGO.name = name;
        }

        ResourcesManager.GetInstance().LoadAsync<AudioClip>("Music/SFX/" + name, (clip) =>
        {
            AudioSource SFX = SFXGO.AddComponent<AudioSource>();
            SFX.clip = clip;
            SFX.volume = SFXValue;
            SFX.loop = isloop;
            SFX.Play();
            SFXList.Add(SFX);

            if (callback != null)
            {
                callback(SFX);
            }
        });
    }

    public void StopSFX(AudioSource SFX)
    {
        if (SFXList.Contains(SFX))
        {
            SFXList.Remove(SFX);
            SFX.Stop();
            GameObject.Destroy(SFX);
        }
    }

    public void ChangeSFXValue(float value)
    {
        SFXValue = value;
        for (int i = 0; i < SFXList.Count; i++)
        {
            SFXList[i].volume = SFXValue;
        }
    }
    #endregion

    #region Voice -- 角色音频
    public void PlayVoice(string name, bool isloop, UnityAction<AudioSource> callback = null)
    {
        if (VoiceGO == null)
        {
            VoiceGO = new GameObject();
            VoiceGO.name = name;
        }

        ResourcesManager.GetInstance().LoadAsync<AudioClip>("Music/Voice/" + name, (clip) =>
        {
            AudioSource voice = VoiceGO.AddComponent<AudioSource>();
            voice.clip = clip;
            voice.volume = VoiceValue;
            voice.loop = isloop;
            voice.Play();
            VoiceList.Add(voice);

            if (callback != null)
            {
                callback(voice);
            }
        });
    }

    public void StopVoice(AudioSource voice)
    {
        if (VoiceList.Contains(voice))
        {
            VoiceList.Remove(voice);
            voice.Stop();
            GameObject.Destroy(voice);
        }
    }

    public void ChangeVoiceValue(float value)
    {
        VoiceValue = value;
        for (int i = 0; i < VoiceList.Count; i++)
        {
            VoiceList[i].volume = VoiceValue;
        }
    }
    #endregion

}

```

`MusicManager` 类是一个音频管理器，用来集中管理背景音乐、音效和角色语音的播放控制。它包括：

- 异步加载音频资源。
- 播放、暂停、停止音频。
- 控制音量。
- 自动清理已经停止播放的音效和语音，避免内存泄漏。

**注意注意注意**加载资源的路径结构一定要清晰

### UI模块

UI模块的封装，这里是基于UGUI

不过，在写UIManager之前，应该先制作面板基类

- **面板管理**：`BasePanel` 类负责管理和绑定所有 UI 组件，通过字典 `UIComponentDictionary` 来存储组件，以便快速访问和操作。

- **UI 组件的事件监听**：通过遍历面板上的所有子组件，绑定适当的事件（如 `Button` 的点击事件，`Toggle` 的值改变事件），确保 UI 交互能够触发相应的逻辑。

- **扩展性**：子类可以继承并覆盖 `ShowMe`、`HideMe`、`OnClick` 和 `OnValueChanged` 等方法，以实现面板的具体行为。

```csharp
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

```

`FindChildrenUIComponent<T>` 方法用于查找当前面板（即 `GameObject`）的所有子对象中指定类型的 UI 组件，并将它们添加到 `UIComponentDictionary` 字典中。

`GetComponentsInChildren<T>()` 方法返回所有子对象中类型为 `T` 的组件。

这里比较巧妙的是，像Button.onClick.AddListener()这个方法是要接受一个无参的函数，才能进行监听。但是通过匿名函数Lambdav表达式，用一个无参函数包裹一个含参函数，就可以间接实现传递信息。在这里的作用是传递当前Button的名字，使得这个信息传递给子类，即子类重写父类的时候，就可以判断是哪个Button发生了点击。

在找到每个组件后，它会根据组件的类型（如 `Button` 或 `Toggle`）绑定事件监听器：

- 如果是 `Button`，绑定 `onClick` 事件，点击按钮时调用 `OnClick` 方法。
- 如果是 `Toggle`，绑定 `onValueChanged` 事件，切换选项时调用 `OnValueChanged` 方法。

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public enum E_UI_Layer
{
    Bot,
    Mid,
    Top,
    Syestem,
}

public class UIManager : BaseManager<UIManager>
{
    public Dictionary<string,BasePanel> panelDictionary = new Dictionary<string,BasePanel>();

    private Transform bot;
    private Transform mid;
    private Transform top;
    private Transform system;

    public RectTransform canvas;

    public UIManager()
    {
        GameObject go = ResourcesManager.GetInstance().Load<GameObject>("UI/Canvas");
        canvas = go.transform as RectTransform;
        GameObject.DontDestroyOnLoad(go);

        bot = canvas.Find("Bot");
        mid = canvas.Find("Mid");
        top = canvas.Find("Top");
        system = canvas.Find("System");

        go = ResourcesManager.GetInstance().Load<GameObject>("UI/EventSystem");
        GameObject.DontDestroyOnLoad(go);
    }

    public Transform GetUILayerFather(E_UI_Layer layer)
    {
        switch(layer)
        {
            case E_UI_Layer.Bot:
                return this.bot;
            case E_UI_Layer.Mid:
                return this.mid;
            case E_UI_Layer.Top:
                return this.top;
            case E_UI_Layer.Syestem:
                return this.system;
        }
        return null;
    }

    /// <summary>
    /// 显示面板
    /// </summary>
    /// <typeparam name="T">面板脚本类型</typeparam>
    /// <param name="panelName">面板名字</param>
    /// <param name="layer">面板所在层级</param>
    /// <param name="callback">面板创建后所作的事</param>
    public void ShowPanel<T>(string panelName, E_UI_Layer layer = E_UI_Layer.Mid, UnityAction<T> callback = null) where T : BasePanel 
    {
        if (panelDictionary.ContainsKey(panelName))
        {
            panelDictionary[panelName].ShowMe();
            if (callback != null)
            {
                callback(panelDictionary[panelName] as T);
            }
            return;
        }

        ResourcesManager.GetInstance().LoadAsync<GameObject>("UI/" + panelName, (go) =>
        {
            Transform father = bot;
            switch(layer)
            {
                case E_UI_Layer.Mid:
                    father = mid;
                    break;
                case E_UI_Layer.Top:
                    father = top;
                    break;
                case E_UI_Layer.Syestem:
                    father = top;
                    break;
            }
            go.transform.SetParent(father);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;
            (go.transform as RectTransform).offsetMax = Vector2.one;
            (go.transform as RectTransform).offsetMin = Vector2.one;

            T panel = go.GetComponent<T>();
            if(callback != null) callback(panel);
            panel.ShowMe();
            panelDictionary.Add(panelName, panel);
        });
    }

    public void HidePanel(string panelName)
    {
        if (panelDictionary.ContainsKey(panelName))
        {
            panelDictionary[panelName].HideMe();
            GameObject.Destroy(panelDictionary[panelName].gameObject);
            panelDictionary.Remove(panelName);
        }
    }

    public T GetPanel<T>(string panelName) where T : BasePanel
    {
        if (panelDictionary.ContainsKey(panelName))
        {
            return panelDictionary[panelName] as T;
        }
        return null;
    }

    /// <summary>
    /// 给控件增添自定义事件
    /// </summary>
    /// <param name="UIComponent">控件对象</param>
    /// <param name="type">事件类型</param>
    /// <param name="callback">事件的回调</param>
    public static void AddCustomEventListener(UIBehaviour UIComponent, EventTriggerType type, UnityAction<BaseEventData> callback)
    {
        EventTrigger eventTrigger = UIComponent.GetComponent<EventTrigger>();
        if (eventTrigger == null)
        {
            eventTrigger = UIComponent.gameObject.AddComponent<EventTrigger>();
        }
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = type;
        entry.callback.AddListener(callback);

        eventTrigger.triggers.Add(entry);
    }
}

```

- **UI 面板管理**：`UIManager` 负责管理不同层级的 UI 面板的显示和隐藏，并通过 `panelDictionary` 存储当前显示的面板。

- **动态加载和显示面板**：`ShowPanel` 方法动态加载并显示面板，支持指定层级和回调。

- **事件监听**：通过 `AddCustomEventListener` 方法，UI 组件可以绑定自定义事件，处理用户交互。

- **UI 层级管理**：面板显示时，可以选择将其放置在不同的层级中，如底层、中层、顶层等，方便管理 UI 的显示顺序。

为了进行测试，我们需要制作一个测试面板作为预制体，放入Resources文件夹对应的路径下

测试面板

```csharp
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
            Debug.Log("进入ButtonStart");
        });
        UIManager.AddCustomEventListener(GetUIComponent<Button>("ButtonStart"), EventTriggerType.PointerExit, (data) =>
        {
            Debug.Log("离开ButtonStart");
        });
    }

    protected override void OnClick(string name)
    {
        switch(name)
        {
            case "ButtonStart":
                Debug.Log("Start被点击");
                break;
            case "ButtonQuit":
                Debug.Log("Quit被点击");
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
        Debug.Log("初始化面板数据");
    }
}

```

那么究竟如何使用这个UIManager?

```csharp
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

```

音频和UI模块的加载资源和卸载资源应该都可以使用对象池优化，但是目前在考虑是直接在资源加载模块进行对象池优化还是在各自的模块分别使用？

先鸽了。

