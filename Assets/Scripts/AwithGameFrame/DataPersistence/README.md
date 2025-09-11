# 数据持久化模块

## 概述

数据持久化模块提供了统一的数据存储和加载接口，支持多种存储方式，让开发者可以根据数据特点选择最适合的存储方案。该模块已完成核心功能开发，支持PlayerPrefs、JSON文件、二进制文件和SQLite数据库四种存储方式。

## 架构设计

### 核心组件

1. **IStorageProvider** - 存储提供者接口
2. **DataPersistenceConfig** - 全局配置管理
3. **StorageConfig** - 存储配置管理
4. **DataOperationResult** - 操作结果枚举
5. **Storage/** - 存储实现
6. **Serialization/** - 序列化实现
7. **Database/** - 数据库抽象层

### 存储类型

- **PlayerPrefs**: 用户设置、配置数据（Windows注册表/Mac plist）
- **JSON文件**: 游戏存档、复杂数据（Unity持久化目录/Data/）
- **二进制文件**: 缓存数据、性能敏感数据（Unity持久化目录/BinaryData/）
- **SQLite数据库**: 复杂查询、关系数据（Unity持久化目录/test_game.db）

## 使用方法

### 1. 基础使用

```csharp
// PlayerPrefs存储
var playerPrefsStorage = new PlayerPrefsStorage();
await playerPrefsStorage.InitializeAsync();
await playerPrefsStorage.SaveAsync("playerName", "TestPlayer");
var (playerName, result) = await playerPrefsStorage.LoadAsync<string>("playerName");

// JSON文件存储
var jsonConfig = new DataPersistenceConfig
{
    EnableCompression = false,
    EnableEncryption = false,
    PrettyPrint = true,
    FileExtension = ".json"
};
var jsonStorage = new JsonFileStorage();
await jsonStorage.InitializeAsync(jsonConfig);
await jsonStorage.SaveAsync("gameData", gameData);
var (gameData, result) = await jsonStorage.LoadAsync<GameData>("gameData");

// 二进制文件存储
var binaryConfig = new DataPersistenceConfig
{
    EnableCompression = false,
    EnableEncryption = false,
    FileExtension = ".bin"
};
var binaryStorage = new BinaryFileStorage();
await binaryStorage.InitializeAsync(binaryConfig);
await binaryStorage.SaveAsync("cacheData", cacheData);
var (cacheData, result) = await binaryStorage.LoadAsync<CacheData>("cacheData");

// SQLite数据库存储
var databasePath = Path.Combine(Application.persistentDataPath, "game.db");
var dbConfig = new StorageConfig { databasePath = databasePath };
var dbStorage = new DatabaseStorageProvider(DatabaseType.SQLite, "game_data");
await dbStorage.InitializeAsync(dbConfig);
await dbStorage.SaveAsync("playerData", playerData);
var (playerData, result) = await dbStorage.LoadAsync<PlayerData>("playerData");
```

### 2. 支持的数据类型

#### 基本类型
- **string** - 字符串
- **int** - 整数
- **float** - 浮点数
- **bool** - 布尔值
- **long/ulong** - 长整数
- **byte[]** - 字节数组

#### 复杂对象
- **自定义类** - 支持JSON序列化的类
- **Unity类型** - Vector3, Color, Quaternion等
- **集合类型** - List<T>, Dictionary<K,V>等

### 3. 配置选项

#### DataPersistenceConfig（全局配置）
```csharp
var config = new DataPersistenceConfig
{
    EnableCompression = false,        // 是否启用压缩
    EnableEncryption = false,         // 是否启用加密
    PrettyPrint = true,               // JSON格式化
    FileExtension = ".json",          // 文件扩展名
    MaxFileSize = 10 * 1024 * 1024,  // 最大文件大小
    BackupEnabled = true              // 是否启用备份
};
```

#### StorageConfig（存储配置）
```csharp
var storageConfig = new StorageConfig
{
    databasePath = "game.db",         // 数据库路径
    maxConnections = 10,              // 最大连接数
    connectionTimeout = 30,           // 连接超时
    enableWAL = true                  // 启用WAL模式
};
```

## 序列化支持

### 当前实现：JsonUtility + 自定义处理

```csharp
// 基本类型 - 直接处理（性能更好）
string json = $"\"{str}\"";                    // 字符串
string json = obj.ToString();                  // 数值类型

// 复杂对象 - JsonUtility序列化
string json = JsonUtility.ToJson(obj, true);   // 对象
```

### 支持的序列化格式

- **JSON** - 默认格式，支持所有类型
- **Binary** - 二进制格式，性能更好
- **MessagePack** - 可选，需要安装MessagePack包
- **Protobuf** - 可选，需要安装Protobuf包

## 数据库支持

### SQLite数据库

```csharp
// 创建数据库连接
var databasePath = Path.Combine(Application.persistentDataPath, "game.db");
var dbProvider = new SQLiteDatabaseProvider();
await dbProvider.InitializeAsync($"Data Source={databasePath}");

// 执行SQL
var result = await dbProvider.ExecuteAsync("CREATE TABLE IF NOT EXISTS players (id INTEGER PRIMARY KEY, name TEXT)");

// 查询数据
var (players, queryResult) = await dbProvider.QueryAsync<Player>("SELECT * FROM players");

// 事务支持
using (var transaction = await dbProvider.BeginTransactionAsync())
{
    await transaction.ExecuteAsync("INSERT INTO players (name) VALUES (@name)", 
        new Dictionary<string, object> { ["name"] = "Player1" });
    await transaction.CommitAsync();
}
```

### 对象映射

框架提供基础的对象映射功能：

```csharp
// 基本类型直接映射
var (name, result) = await dbProvider.QuerySingleAsync<string>("SELECT name FROM players WHERE id = 1");

// 复杂对象映射（需要无参构造函数）
public class Player
{
    public string Name { get; set; }
    public int Level { get; set; }
    public float Experience { get; set; }
    public bool IsVip { get; set; }
}

var (player, result) = await dbProvider.QuerySingleAsync<Player>("SELECT * FROM players WHERE id = 1");
```

**注意**：框架提供基础映射，复杂项目建议使用专门的ORM库（如Entity Framework Core、Dapper等）。

## 测试和验证

### 测试脚本

项目包含完整的数据持久化测试脚本 `DataPersistenceTest.cs`：

```csharp
// 挂载到GameObject上运行测试
public class DataPersistenceTest : MonoBehaviour
{
    // 测试所有存储方式
    public void TestPlayerPrefsStorage();
    public void TestJsonFileStorage();
    public void TestBinaryFileStorage();
    public void TestDatabaseStorage();
    
    // 数据修改和验证
    public void ModifyData();        // 修改数据并保存
    public void LoadAllData();       // 加载所有数据
    public void ClearAllData();      // 清除所有数据
}
```

### 测试流程

1. **保存数据** - 点击存储按钮保存测试数据
2. **修改数据** - 点击"修改数据"按钮修改并保存
3. **重启验证** - 停止运行，重新启动
4. **加载验证** - 点击"加载数据"验证数据持久化

## 文件存储位置

### Unity持久化目录
```
{Application.persistentDataPath}/
├── Data/                    # JSON文件存储
│   ├── TestData_String.json
│   ├── TestData_Int.json
│   └── ...
├── BinaryData/              # 二进制文件存储
│   ├── TestData_String.bin
│   ├── TestData_Int.bin
│   └── ...
└── test_game.db             # SQLite数据库
```

### PlayerPrefs
- **Windows**: 注册表 `HKEY_CURRENT_USER\Software\Unity\UnityEditor\DefaultCompany\AwithGameFrame`
- **Mac**: `~/Library/Preferences/com.DefaultCompany.AwithGameFrame.plist`

## 性能优化

### 1. 异步操作
- 使用UniTask提供高性能异步编程
- 避免阻塞主线程

### 2. 序列化优化
- 基本类型直接处理，避免JSON序列化开销
- 复杂对象使用JsonUtility，性能优于Newtonsoft.Json

### 3. 存储选择
- **PlayerPrefs**: 适合小量配置数据
- **JSON文件**: 适合游戏存档，可读性好
- **二进制文件**: 适合缓存数据，性能最好
- **数据库**: 适合复杂查询，支持事务

### 4. 压缩和加密
- 支持GZip压缩减少存储空间
- 支持AES加密保护敏感数据
- 可配置启用/禁用

## 错误处理

### DataOperationResult枚举

```csharp
public enum DataOperationResult
{
    Success,        // 操作成功
    Failed,         // 操作失败
    NotFound,       // 数据未找到
    NotInitialized, // 未初始化
    InvalidData,    // 数据无效
    AccessDenied,   // 访问被拒绝
    OutOfSpace,     // 存储空间不足
    NetworkError    // 网络错误
}
```

### 使用示例

```csharp
var (data, result) = await storage.LoadAsync<PlayerData>("playerData");
if (result == DataOperationResult.Success)
{
    // 处理数据
    Debug.Log($"Player name: {data.Name}");
}
else
{
    // 处理错误
    Debug.LogError($"Failed to load data: {result}");
}
```

## 扩展开发

### 自定义存储提供者

```csharp
public class CustomStorageProvider : IStorageProvider
{
    public async UniTask<DataOperationResult> InitializeAsync()
    {
        // 初始化逻辑
        return DataOperationResult.Success;
    }
    
    public async UniTask<DataOperationResult> SaveAsync<T>(string key, T data)
    {
        // 保存逻辑
        return DataOperationResult.Success;
    }
    
    public async UniTask<(T data, DataOperationResult result)> LoadAsync<T>(string key)
    {
        // 加载逻辑
        return (default(T), DataOperationResult.Success);
    }
    
    // 实现其他接口方法...
}
```

### 自定义序列化器

```csharp
public class CustomSerializer : ISerializer
{
    public SerializationFormat Format => SerializationFormat.Custom;
    public bool SupportsCompression => true;
    
    public async UniTask<byte[]> SerializeAsync<T>(T obj)
    {
        // 自定义序列化逻辑
        return new byte[0];
    }
    
    public async UniTask<T> DeserializeAsync<T>(byte[] data)
    {
        // 自定义反序列化逻辑
        return default(T);
    }
    
    // 实现其他接口方法...
}
```

## 注意事项

### 1. 数据类型限制
- 基本类型支持：string, int, float, bool, long, ulong, byte[]
- 复杂对象需要无参构造函数（用于数据库映射）
- Unity类型需要标记[Serializable]

### 2. 存储选择建议
- **配置数据**: PlayerPrefs
- **游戏存档**: JSON文件
- **缓存数据**: 二进制文件
- **复杂查询**: SQLite数据库

### 3. 性能考虑
- 大量数据使用二进制文件
- 频繁读写使用数据库
- 小量数据使用PlayerPrefs

### 4. 安全考虑
- 敏感数据启用加密
- 重要数据启用备份
- 定期清理过期数据

## 版本历史

### v1.0.0 (当前版本)
- ✅ 完成四种存储方式实现
- ✅ 支持基本类型和复杂对象
- ✅ 提供完整的测试脚本
- ✅ 支持SQLite数据库
- ✅ 支持压缩和加密
- ✅ 提供详细的错误处理

### 未来计划
- 🔄 支持更多数据库类型（MySQL, PostgreSQL）
- 🔄 支持云存储（AWS S3, Azure Blob）
- 🔄 支持数据同步和冲突解决
- 🔄 支持数据版本管理和迁移

## 相关文档

- [ORM设计理念](README_ORM.md) - 框架的ORM设计说明
- [测试脚本](../Test/DataPersistenceTest.cs) - 完整的功能测试
- [示例项目](../../../Examples/) - 使用示例和最佳实践