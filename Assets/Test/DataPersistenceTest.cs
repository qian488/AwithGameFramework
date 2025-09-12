using UnityEngine;
using UnityEngine.UI;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Core.Logging;

using System;
using System.IO;

namespace AwithGameFrame.Test
{
    /// <summary>
    /// 数据持久化测试脚本
    /// 测试真正的数据持久化功能：保存 -> 修改 -> 重启 -> 验证
    /// </summary>
    public class DataPersistenceTest : MonoBehaviour
    {
        [Header("UI组件 (可选,不设置会自动创建) - UI Components (Optional, not created automatically)")]
        public Button playerPrefsButton;
        public Button jsonFileButton;
        public Button binaryFileButton;
        public Button databaseButton;
        public Button clearDataButton;
        public Button loadDataButton;
        public Button modifyDataButton;
        public Button restartTestButton;
        public Text resultText;
        public ScrollRect resultScrollRect;

        [Header("自动UI设置 - Automatic UI Settings")]
        public bool createUIAutomatically = true;
        public KeyCode testKeyCode = KeyCode.T;

        [Header("测试数据 - Test Data")]
        public string testKey = "TestData";
        public string testString = "Hello AwithGameFrame!";
        public int testInt = 42;
        public float testFloat = 3.14f;
        public bool testBool = true;
        public TestPlayerData testPlayerData = new TestPlayerData
        {
            playerName = "TestPlayer",
            level = 10,
            experience = 1000,
            isVip = true
        };

        [Header("数据修改设置 - Data Modification Settings")]
        public string modifiedString = "Modified Data!";
        public int modifiedInt = 999;
        public float modifiedFloat = 9.99f;
        public bool modifiedBool = false;
        public TestPlayerData modifiedPlayerData = new TestPlayerData
        {
            playerName = "ModifiedPlayer",
            level = 99,
            experience = 9999,
            isVip = false
        };

        private bool _isDataModified = false;
        private bool _isFirstRun = true;

        void Start()
        {
            // 设置日志过滤
            SetupLogFiltering();
            
            // 显示存储位置信息
            ShowAllStorageLocations();
            
            // 创建UI
            if (createUIAutomatically)
            {
                CreateTestUI();
            }
            
            // 检查是否是重启后的运行
            CheckRestartStatus();
        }

        void Update()
        {
            if (Input.GetKeyDown(testKeyCode))
            {
                RunAllTests();
            }
        }

        /// <summary>
        /// 检查重启状态
        /// </summary>
        private void CheckRestartStatus()
        {
            LogResult("=== 🔍 检查重启状态 ===");
            
            // 检查是否有持久化数据
            bool hasPlayerPrefs = PlayerPrefs.HasKey($"{testKey}_String");
            bool hasJsonFiles = Directory.Exists(Path.Combine(Application.persistentDataPath, "Data")) && 
                               Directory.GetFiles(Path.Combine(Application.persistentDataPath, "Data"), "*.json").Length > 0;
            bool hasBinaryFiles = Directory.Exists(Path.Combine(Application.persistentDataPath, "BinaryData")) && 
                                 Directory.GetFiles(Path.Combine(Application.persistentDataPath, "BinaryData"), "*.bin").Length > 0;
            bool hasDatabase = File.Exists(Path.Combine(Application.persistentDataPath, "test_game.db"));
            
            if (hasPlayerPrefs || hasJsonFiles || hasBinaryFiles || hasDatabase)
            {
                LogResult("✅ 检测到持久化数据！这是重启后的运行");
                LogResult("💡 点击'加载数据'按钮查看之前保存的数据");
                LogResult("💡 点击'修改数据'按钮修改数据并保存");
                LogResult("💡 点击'重启测试'按钮清除数据并重新开始");
                _isFirstRun = false;
            }
            else
            {
                LogResult("🆕 首次运行，没有检测到持久化数据");
                LogResult("💡 点击各个存储按钮保存数据");
                LogResult("💡 然后停止运行，重新启动来测试持久化");
                _isFirstRun = true;
            }
            
            // 显示当前状态
            LogResult($"📊 当前状态: 首次运行={_isFirstRun}, 数据已修改={_isDataModified}");
            LogResult("===============================================");
        }

        /// <summary>
        /// 运行所有测试
        /// </summary>
        public void RunAllTests()
        {
            LogResult("=== 🚀 开始运行所有测试 ===");
            TestPlayerPrefsStorage();
            TestJsonFileStorage();
            TestBinaryFileStorage();
            TestDatabaseStorage();
            LogResult("=== ✅ 所有测试完成! ===");
        }

        /// <summary>
        /// 测试PlayerPrefs存储
        /// </summary>
        public async void TestPlayerPrefsStorage()
        {
            LogResult("=== 开始测试PlayerPrefs存储 ===");
            
            try
            {
                var storage = new PlayerPrefsStorage();
                await storage.InitializeAsync();
                
                LogResult("测试基本数据类型...");
                var saveResult1 = await storage.SaveAsync($"{testKey}_String", testString);
                var saveResult2 = await storage.SaveAsync($"{testKey}_Int", testInt);
                var saveResult3 = await storage.SaveAsync($"{testKey}_Float", testFloat);
                var saveResult4 = await storage.SaveAsync($"{testKey}_Bool", testBool);
                
                LogResult($"保存结果: 字符串={saveResult1}, 整数={saveResult2}, 浮点数={saveResult3}, 布尔值={saveResult4}");
                
                // 验证数据
                var (loadedString, result1) = await storage.LoadAsync<string>($"{testKey}_String");
                var (loadedInt, result2) = await storage.LoadAsync<int>($"{testKey}_Int");
                var (loadedFloat, result3) = await storage.LoadAsync<float>($"{testKey}_Float");
                var (loadedBool, result4) = await storage.LoadAsync<bool>($"{testKey}_Bool");
                
                LogResult($"加载结果: 字符串='{loadedString}' ({result1}), 整数={loadedInt} ({result2}), 浮点数={loadedFloat} ({result3}), 布尔值={loadedBool} ({result4})");
                
                LogResult("✅ PlayerPrefs存储测试完成");
            }
            catch (Exception ex)
            {
                LogResult($"❌ PlayerPrefs存储测试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试JSON文件存储
        /// </summary>
        public async void TestJsonFileStorage()
        {
            LogResult("=== 开始测试JSON文件存储 ===");
            
            try
            {
                var config = new DataPersistenceConfig
                {
                    EnableCompression = false,
                    EnableEncryption = false,
                    PrettyPrint = true,
                    FileExtension = ".json"
                };
                
                var storage = new JsonFileStorage();
                await storage.InitializeAsync(config);
                
                var jsonDir = Path.Combine(Application.persistentDataPath, "Data");
                LogResult($"📁 JSON文件存储位置: {jsonDir}");
                
                LogResult("测试基本数据类型...");
                var saveResult1 = await storage.SaveAsync($"{testKey}_String", testString);
                var saveResult2 = await storage.SaveAsync($"{testKey}_Int", testInt);
                var saveResult3 = await storage.SaveAsync($"{testKey}_Float", testFloat);
                var saveResult4 = await storage.SaveAsync($"{testKey}_Bool", testBool);
                var saveResult5 = await storage.SaveAsync($"{testKey}_PlayerData", testPlayerData);
                
                LogResult($"保存结果: 字符串={saveResult1}, 整数={saveResult2}, 浮点数={saveResult3}, 布尔值={saveResult4}, 玩家数据={saveResult5}");
                
                // 验证数据
                var (loadedString, result1) = await storage.LoadAsync<string>($"{testKey}_String");
                var (loadedInt, result2) = await storage.LoadAsync<int>($"{testKey}_Int");
                var (loadedFloat, result3) = await storage.LoadAsync<float>($"{testKey}_Float");
                var (loadedBool, result4) = await storage.LoadAsync<bool>($"{testKey}_Bool");
                var (loadedPlayerData, result5) = await storage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"加载结果: 字符串='{loadedString}' ({result1}), 整数={loadedInt} ({result2}), 浮点数={loadedFloat} ({result3}), 布尔值={loadedBool} ({result4})");
                LogResult($"玩家数据: {loadedPlayerData} ({result5})");
                
                // 检查文件
                if (Directory.Exists(jsonDir))
                {
                    var jsonFiles = Directory.GetFiles(jsonDir, "*.json");
                    LogResult($"📁 创建了 {jsonFiles.Length} 个JSON文件");
                    foreach (var file in jsonFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        LogResult($"  • {fileInfo.Name} ({fileInfo.Length} 字节)");
                    }
                }
                
                LogResult("✅ JSON文件存储测试完成");
            }
            catch (Exception ex)
            {
                LogResult($"❌ JSON文件存储测试失败: {ex.Message}");
                LogResult($"🔍 错误详情: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 测试二进制文件存储
        /// </summary>
        public async void TestBinaryFileStorage()
        {
            LogResult("=== 开始测试二进制文件存储 ===");
            
            try
            {
                var config = new DataPersistenceConfig
                {
                    EnableCompression = false,
                    EnableEncryption = false,
                    FileExtension = ".bin"
                };
                
                var storage = new BinaryFileStorage();
                await storage.InitializeAsync(config);
                
                var binaryDir = Path.Combine(Application.persistentDataPath, "BinaryData");
                LogResult($"📁 二进制文件存储位置: {binaryDir}");
                
                LogResult("测试基本数据类型...");
                var saveResult1 = await storage.SaveAsync($"{testKey}_String", testString);
                var saveResult2 = await storage.SaveAsync($"{testKey}_Int", testInt);
                var saveResult3 = await storage.SaveAsync($"{testKey}_Float", testFloat);
                var saveResult4 = await storage.SaveAsync($"{testKey}_Bool", testBool);
                var saveResult5 = await storage.SaveAsync($"{testKey}_PlayerData", testPlayerData);
                
                LogResult($"保存结果: 字符串={saveResult1}, 整数={saveResult2}, 浮点数={saveResult3}, 布尔值={saveResult4}, 玩家数据={saveResult5}");
                
                // 验证数据
                var (loadedString, result1) = await storage.LoadAsync<string>($"{testKey}_String");
                var (loadedInt, result2) = await storage.LoadAsync<int>($"{testKey}_Int");
                var (loadedFloat, result3) = await storage.LoadAsync<float>($"{testKey}_Float");
                var (loadedBool, result4) = await storage.LoadAsync<bool>($"{testKey}_Bool");
                var (loadedPlayerData, result5) = await storage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"加载结果: 字符串='{loadedString}' ({result1}), 整数={loadedInt} ({result2}), 浮点数={loadedFloat} ({result3}), 布尔值={loadedBool} ({result4})");
                LogResult($"玩家数据: {loadedPlayerData} ({result5})");
                
                // 检查文件
                if (Directory.Exists(binaryDir))
                {
                    var binaryFiles = Directory.GetFiles(binaryDir, "*.bin");
                    LogResult($"📁 创建了 {binaryFiles.Length} 个二进制文件");
                    foreach (var file in binaryFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        LogResult($"  • {fileInfo.Name} ({fileInfo.Length} 字节)");
                    }
                }
                
                LogResult("✅ 二进制文件存储测试完成");
            }
            catch (Exception ex)
            {
                LogResult($"❌ 二进制文件存储测试失败: {ex.Message}");
                LogResult($"🔍 错误详情: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 测试数据库存储
        /// </summary>
        public async void TestDatabaseStorage()
        {
            LogResult("=== 开始测试数据库存储 ===");
            
            try
            {
                var databasePath = Path.Combine(Application.persistentDataPath, "test_game.db");
                var config = new StorageConfig { databasePath = databasePath };
                var storage = new DatabaseStorageProvider(DatabaseType.SQLite, "test_data");
                
                LogResult($"🔧 开始初始化数据库，路径: {databasePath}");
                var initResult = await storage.InitializeAsync(config);
                LogResult($"🔧 数据库初始化结果: {initResult}");
                
                if (initResult != DataOperationResult.Success)
                {
                    LogResult("❌ 数据库初始化失败，跳过测试");
                    return;
                }
                
                LogResult("✅ 数据库连接成功！");
                
                LogResult("测试基本数据类型...");
                var saveResult1 = await storage.SaveAsync($"{testKey}_String", testString);
                var saveResult2 = await storage.SaveAsync($"{testKey}_Int", testInt);
                var saveResult3 = await storage.SaveAsync($"{testKey}_Float", testFloat);
                var saveResult4 = await storage.SaveAsync($"{testKey}_Bool", testBool);
                var saveResult5 = await storage.SaveAsync($"{testKey}_PlayerData", testPlayerData);
                
                LogResult($"保存结果: 字符串={saveResult1}, 整数={saveResult2}, 浮点数={saveResult3}, 布尔值={saveResult4}, 玩家数据={saveResult5}");
                
                // 验证数据
                var (loadedString, result1) = await storage.LoadAsync<string>($"{testKey}_String");
                var (loadedInt, result2) = await storage.LoadAsync<int>($"{testKey}_Int");
                var (loadedFloat, result3) = await storage.LoadAsync<float>($"{testKey}_Float");
                var (loadedBool, result4) = await storage.LoadAsync<bool>($"{testKey}_Bool");
                var (loadedPlayerData, result5) = await storage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"加载结果: 字符串='{loadedString}' ({result1}), 整数={loadedInt} ({result2}), 浮点数={loadedFloat} ({result3}), 布尔值={loadedBool} ({result4})");
                LogResult($"玩家数据: {loadedPlayerData} ({result5})");
                
                // 测试统计信息
                var stats = await storage.GetStatisticsAsync();
                LogResult($"存储统计: 项目数={stats.ItemCount}, 总大小={stats.TotalSize}字节");
                
                // 检查数据库文件
                if (File.Exists(databasePath))
                {
                    var fileInfo = new FileInfo(databasePath);
                    LogResult($"📁 SQLite数据库文件已创建: {fileInfo.Name} ({fileInfo.Length} 字节)");
                }
                else
                {
                    LogResult("❌ SQLite数据库文件未创建");
                }
                
                LogResult("✅ 数据库存储测试完成");
            }
            catch (Exception ex)
            {
                LogResult($"❌ 数据库存储测试失败: {ex.Message}");
                LogResult($"🔍 错误详情: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 修改数据并保存
        /// </summary>
        public async void ModifyData()
        {
            LogResult("=== 🔄 开始修改数据 ===");
            
            try
            {
                // 修改PlayerPrefs
                LogResult("修改PlayerPrefs数据...");
                var playerPrefsStorage = new PlayerPrefsStorage();
                await playerPrefsStorage.InitializeAsync();
                
                await playerPrefsStorage.SaveAsync($"{testKey}_String", modifiedString);
                await playerPrefsStorage.SaveAsync($"{testKey}_Int", modifiedInt);
                await playerPrefsStorage.SaveAsync($"{testKey}_Float", modifiedFloat);
                await playerPrefsStorage.SaveAsync($"{testKey}_Bool", modifiedBool);
                await playerPrefsStorage.SaveAsync($"{testKey}_PlayerData", modifiedPlayerData);
                
                // 修改JSON文件
                LogResult("修改JSON文件数据...");
                var jsonConfig = new DataPersistenceConfig { EnableCompression = false, EnableEncryption = false };
                var jsonStorage = new JsonFileStorage();
                await jsonStorage.InitializeAsync(jsonConfig);
                
                await jsonStorage.SaveAsync($"{testKey}_String", modifiedString);
                await jsonStorage.SaveAsync($"{testKey}_Int", modifiedInt);
                await jsonStorage.SaveAsync($"{testKey}_Float", modifiedFloat);
                await jsonStorage.SaveAsync($"{testKey}_Bool", modifiedBool);
                await jsonStorage.SaveAsync($"{testKey}_PlayerData", modifiedPlayerData);
                
                // 修改二进制文件
                LogResult("修改二进制文件数据...");
                var binaryConfig = new DataPersistenceConfig { EnableCompression = false, EnableEncryption = false };
                var binaryStorage = new BinaryFileStorage();
                await binaryStorage.InitializeAsync(binaryConfig);
                
                await binaryStorage.SaveAsync($"{testKey}_String", modifiedString);
                await binaryStorage.SaveAsync($"{testKey}_Int", modifiedInt);
                await binaryStorage.SaveAsync($"{testKey}_Float", modifiedFloat);
                await binaryStorage.SaveAsync($"{testKey}_Bool", modifiedBool);
                await binaryStorage.SaveAsync($"{testKey}_PlayerData", modifiedPlayerData);
                
                // 修改数据库
                LogResult("修改数据库数据...");
                var databasePath = Path.Combine(Application.persistentDataPath, "test_game.db");
                var dbConfig = new StorageConfig { databasePath = databasePath };
                var dbStorage = new DatabaseStorageProvider(DatabaseType.SQLite, "test_data");
                await dbStorage.InitializeAsync(dbConfig);
                
                await dbStorage.SaveAsync($"{testKey}_String", modifiedString);
                await dbStorage.SaveAsync($"{testKey}_Int", modifiedInt);
                await dbStorage.SaveAsync($"{testKey}_Float", modifiedFloat);
                await dbStorage.SaveAsync($"{testKey}_Bool", modifiedBool);
                await dbStorage.SaveAsync($"{testKey}_PlayerData", modifiedPlayerData);
                
                _isDataModified = true;
                LogResult("✅ 数据修改完成！");
                LogResult($"📊 当前状态: 首次运行={_isFirstRun}, 数据已修改={_isDataModified}");
                LogResult("💡 现在停止运行，重新启动来验证数据是否持久化");
                LogResult("💡 重启后点击'加载数据'按钮查看修改后的数据");
            }
            catch (Exception ex)
            {
                LogResult($"❌ 数据修改失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载所有数据
        /// </summary>
        public async void LoadAllData()
        {
            LogResult("=== 📥 开始加载所有数据 ===");
            
            try
            {
                // 加载PlayerPrefs
                LogResult("加载PlayerPrefs数据...");
                var playerPrefsStorage = new PlayerPrefsStorage();
                await playerPrefsStorage.InitializeAsync();
                
                var (ppString, ppResult1) = await playerPrefsStorage.LoadAsync<string>($"{testKey}_String");
                var (ppInt, ppResult2) = await playerPrefsStorage.LoadAsync<int>($"{testKey}_Int");
                var (ppFloat, ppResult3) = await playerPrefsStorage.LoadAsync<float>($"{testKey}_Float");
                var (ppBool, ppResult4) = await playerPrefsStorage.LoadAsync<bool>($"{testKey}_Bool");
                var (ppPlayerData, ppResult5) = await playerPrefsStorage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"PlayerPrefs: 字符串='{ppString}' ({ppResult1}), 整数={ppInt} ({ppResult2}), 浮点数={ppFloat} ({ppResult3}), 布尔值={ppBool} ({ppResult4})");
                LogResult($"PlayerPrefs玩家数据: {ppPlayerData} ({ppResult5})");
                
                // 加载JSON文件
                LogResult("加载JSON文件数据...");
                var jsonConfig = new DataPersistenceConfig { EnableCompression = false, EnableEncryption = false };
                var jsonStorage = new JsonFileStorage();
                await jsonStorage.InitializeAsync(jsonConfig);
                
                var (jsonString, jsonResult1) = await jsonStorage.LoadAsync<string>($"{testKey}_String");
                var (jsonInt, jsonResult2) = await jsonStorage.LoadAsync<int>($"{testKey}_Int");
                var (jsonFloat, jsonResult3) = await jsonStorage.LoadAsync<float>($"{testKey}_Float");
                var (jsonBool, jsonResult4) = await jsonStorage.LoadAsync<bool>($"{testKey}_Bool");
                var (jsonPlayerData, jsonResult5) = await jsonStorage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"JSON文件: 字符串='{jsonString}' ({jsonResult1}), 整数={jsonInt} ({jsonResult2}), 浮点数={jsonFloat} ({jsonResult3}), 布尔值={jsonBool} ({jsonResult4})");
                LogResult($"JSON玩家数据: {jsonPlayerData} ({jsonResult5})");
                
                // 加载二进制文件
                LogResult("加载二进制文件数据...");
                var binaryConfig = new DataPersistenceConfig { EnableCompression = false, EnableEncryption = false };
                var binaryStorage = new BinaryFileStorage();
                await binaryStorage.InitializeAsync(binaryConfig);
                
                var (binString, binResult1) = await binaryStorage.LoadAsync<string>($"{testKey}_String");
                var (binInt, binResult2) = await binaryStorage.LoadAsync<int>($"{testKey}_Int");
                var (binFloat, binResult3) = await binaryStorage.LoadAsync<float>($"{testKey}_Float");
                var (binBool, binResult4) = await binaryStorage.LoadAsync<bool>($"{testKey}_Bool");
                var (binPlayerData, binResult5) = await binaryStorage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"二进制文件: 字符串='{binString}' ({binResult1}), 整数={binInt} ({binResult2}), 浮点数={binFloat} ({binResult3}), 布尔值={binBool} ({binResult4})");
                LogResult($"二进制玩家数据: {binPlayerData} ({binResult5})");
                
                // 加载数据库
                LogResult("加载数据库数据...");
                var databasePath = Path.Combine(Application.persistentDataPath, "test_game.db");
                var dbConfig = new StorageConfig { databasePath = databasePath };
                var dbStorage = new DatabaseStorageProvider(DatabaseType.SQLite, "test_data");
                await dbStorage.InitializeAsync(dbConfig);
                
                var (dbString, dbResult1) = await dbStorage.LoadAsync<string>($"{testKey}_String");
                var (dbInt, dbResult2) = await dbStorage.LoadAsync<int>($"{testKey}_Int");
                var (dbFloat, dbResult3) = await dbStorage.LoadAsync<float>($"{testKey}_Float");
                var (dbBool, dbResult4) = await dbStorage.LoadAsync<bool>($"{testKey}_Bool");
                var (dbPlayerData, dbResult5) = await dbStorage.LoadAsync<TestPlayerData>($"{testKey}_PlayerData");
                
                LogResult($"数据库: 字符串='{dbString}' ({dbResult1}), 整数={dbInt} ({dbResult2}), 浮点数={dbFloat} ({dbResult3}), 布尔值={dbBool} ({dbResult4})");
                LogResult($"数据库玩家数据: {dbPlayerData} ({dbResult5})");
                
                LogResult("✅ 所有数据加载完成！");
            }
            catch (Exception ex)
            {
                LogResult($"❌ 数据加载失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除所有数据
        /// </summary>
        public async void ClearAllData()
        {
            LogResult("=== 🗑️ 开始清除所有数据 ===");
            
            try
            {
                // 清除PlayerPrefs
                LogResult("清除PlayerPrefs数据...");
                var playerPrefsStorage = new PlayerPrefsStorage();
                await playerPrefsStorage.InitializeAsync();
                await playerPrefsStorage.ClearAsync();
                
                // 清除JSON文件
                LogResult("清除JSON文件数据...");
                var jsonConfig = new DataPersistenceConfig { EnableCompression = false, EnableEncryption = false };
                var jsonStorage = new JsonFileStorage();
                await jsonStorage.InitializeAsync(jsonConfig);
                await jsonStorage.ClearAsync();
                
                // 清除二进制文件
                LogResult("清除二进制文件数据...");
                var binaryConfig = new DataPersistenceConfig { EnableCompression = false, EnableEncryption = false };
                var binaryStorage = new BinaryFileStorage();
                await binaryStorage.InitializeAsync(binaryConfig);
                await binaryStorage.ClearAsync();
                
                // 清除数据库
                LogResult("清除数据库数据...");
                var databasePath = Path.Combine(Application.persistentDataPath, "test_game.db");
                var dbConfig = new StorageConfig { databasePath = databasePath };
                var dbStorage = new DatabaseStorageProvider(DatabaseType.SQLite, "test_data");
                await dbStorage.InitializeAsync(dbConfig);
                await dbStorage.ClearAsync();
                
                // 删除文件
                var jsonDir = Path.Combine(Application.persistentDataPath, "Data");
                var binaryDir = Path.Combine(Application.persistentDataPath, "BinaryData");
                
                if (Directory.Exists(jsonDir))
                {
                    Directory.Delete(jsonDir, true);
                    LogResult("🗑️ 删除了JSON文件目录");
                }
                
                if (Directory.Exists(binaryDir))
                {
                    Directory.Delete(binaryDir, true);
                    LogResult("🗑️ 删除了二进制文件目录");
                }
                
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                    LogResult("🗑️ 删除了数据库文件");
                }
                
                _isDataModified = false;
                LogResult("✅ 所有数据清除完成！");
                LogResult($"📊 当前状态: 首次运行={_isFirstRun}, 数据已修改={_isDataModified}");
                LogResult("💡 现在可以重新开始测试");
            }
            catch (Exception ex)
            {
                LogResult($"❌ 数据清除失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重启测试
        /// </summary>
        public void RestartTest()
        {
            LogResult("=== 🔄 重启测试 ===");
            LogResult("💡 请停止运行，然后重新启动来测试数据持久化");
            LogResult("💡 重启后点击'加载数据'按钮查看之前保存的数据");
        }

        /// <summary>
        /// 显示所有存储位置
        /// </summary>
        private void ShowAllStorageLocations()
        {
            LogResult("=== 📁 数据存储位置信息 ===");
            LogResult($"📂 Unity持久化路径: {Application.persistentDataPath}");
            LogResult($"📂 项目根目录: {Application.dataPath.Replace("/Assets", "")}");
            LogResult("");
            LogResult("🗂️ 各存储方式文件位置:");
            LogResult($"  • PlayerPrefs: Windows注册表 / Mac plist文件");
            LogResult($"  • JSON文件: {Path.Combine(Application.persistentDataPath, "Data")}");
            LogResult($"  • 二进制文件: {Path.Combine(Application.persistentDataPath, "BinaryData")}");
            LogResult($"  • SQLite数据库: {Path.Combine(Application.persistentDataPath, "test_game.db")}");
            LogResult("");
            LogResult("💡 提示: 运行测试后可以到对应目录查看生成的文件");
            LogResult("💡 注意: 数据存储在Unity持久化目录，不是项目目录");
            LogResult("===============================================");
        }

        /// <summary>
        /// 设置日志过滤
        /// </summary>
        private void SetupLogFiltering()
        {
            var loggingManager = LoggingManager.GetInstance();
            
            // 禁用其他模块的日志
            loggingManager.SetCategoryEnabled(LogCategory.UI, false);
            loggingManager.SetCategoryEnabled(LogCategory.Audio, false);
            loggingManager.SetCategoryEnabled(LogCategory.Input, false);
            loggingManager.SetCategoryEnabled(LogCategory.Network, false);
            loggingManager.SetCategoryEnabled(LogCategory.Performance, false);
            loggingManager.SetCategoryEnabled(LogCategory.Resource, false);
            
            // 确保Core模块日志启用
            loggingManager.SetCategoryEnabled(LogCategory.Core, true);
            
            LogResult("🔧 日志过滤已设置：只显示Core模块日志");
        }

        /// <summary>
        /// 恢复所有日志
        /// </summary>
        [ContextMenu("恢复所有日志")]
        public void RestoreAllLogs()
        {
            var loggingManager = LoggingManager.GetInstance();
            
            // 启用所有模块的日志
            loggingManager.SetCategoryEnabled(LogCategory.Core, true);
            loggingManager.SetCategoryEnabled(LogCategory.UI, true);
            loggingManager.SetCategoryEnabled(LogCategory.Audio, true);
            loggingManager.SetCategoryEnabled(LogCategory.Input, true);
            loggingManager.SetCategoryEnabled(LogCategory.Network, true);
            loggingManager.SetCategoryEnabled(LogCategory.Performance, true);
            loggingManager.SetCategoryEnabled(LogCategory.Resource, true);
            
            LogResult("🔧 所有日志已恢复");
        }

        /// <summary>
        /// 创建测试UI
        /// </summary>
        private void CreateTestUI()
        {
            // 检查是否已有EventSystem
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                // 创建EventSystem
                GameObject eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystemGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            
            // 创建Canvas
            GameObject canvasGO = new GameObject("DataPersistenceTestCanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            
            canvasGO.AddComponent<GraphicRaycaster>();
            
            // 创建主面板
            GameObject mainPanelGO = new GameObject("MainPanel");
            mainPanelGO.transform.SetParent(canvasGO.transform, false);
            RectTransform mainPanelRect = mainPanelGO.AddComponent<RectTransform>();
            mainPanelRect.anchorMin = new Vector2(0.1f, 0.1f);
            mainPanelRect.anchorMax = new Vector2(0.9f, 0.9f);
            mainPanelRect.offsetMin = Vector2.zero;
            mainPanelRect.offsetMax = Vector2.zero;
            
            Image mainPanelImage = mainPanelGO.AddComponent<Image>();
            mainPanelImage.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            
            // 创建标题
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(mainPanelGO.transform, false);
            RectTransform titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.9f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMin = Vector2.zero;
            
            Text titleText = titleGO.AddComponent<Text>();
            titleText.text = "数据持久化测试";
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 24;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            
            // 创建按钮面板
            GameObject buttonPanelGO = new GameObject("ButtonPanel");
            buttonPanelGO.transform.SetParent(mainPanelGO.transform, false);
            RectTransform buttonPanelRect = buttonPanelGO.AddComponent<RectTransform>();
            buttonPanelRect.anchorMin = new Vector2(0, 0.7f);
            buttonPanelRect.anchorMax = new Vector2(1, 0.9f);
            buttonPanelRect.offsetMin = Vector2.zero;
            buttonPanelRect.offsetMax = Vector2.zero;
            
            // 创建存储测试按钮
            CreateButton(buttonPanelGO, "PlayerPrefs", new Vector2(0.05f, 0.5f), new Vector2(0.2f, 0.9f), TestPlayerPrefsStorage);
            CreateButton(buttonPanelGO, "JSON文件", new Vector2(0.25f, 0.5f), new Vector2(0.4f, 0.9f), TestJsonFileStorage);
            CreateButton(buttonPanelGO, "二进制文件", new Vector2(0.45f, 0.5f), new Vector2(0.6f, 0.9f), TestBinaryFileStorage);
            CreateButton(buttonPanelGO, "数据库", new Vector2(0.65f, 0.5f), new Vector2(0.8f, 0.9f), TestDatabaseStorage);
            CreateButton(buttonPanelGO, "运行全部", new Vector2(0.85f, 0.5f), new Vector2(0.95f, 0.9f), RunAllTests);
            
            // 创建数据操作按钮
            CreateButton(buttonPanelGO, "修改数据", new Vector2(0.05f, 0.1f), new Vector2(0.2f, 0.4f), ModifyData);
            CreateButton(buttonPanelGO, "加载数据", new Vector2(0.25f, 0.1f), new Vector2(0.4f, 0.4f), LoadAllData);
            CreateButton(buttonPanelGO, "清除数据", new Vector2(0.45f, 0.1f), new Vector2(0.6f, 0.4f), ClearAllData);
            CreateButton(buttonPanelGO, "重启测试", new Vector2(0.65f, 0.1f), new Vector2(0.8f, 0.4f), RestartTest);
            
            // 创建日志控制按钮
            CreateButton(buttonPanelGO, "恢复日志", new Vector2(0.85f, 0.1f), new Vector2(0.95f, 0.4f), RestoreAllLogs);

            // 创建结果显示区域
            GameObject resultPanelGO = new GameObject("ResultPanel");
            resultPanelGO.transform.SetParent(mainPanelGO.transform, false);
            RectTransform resultPanelRect = resultPanelGO.AddComponent<RectTransform>();
            resultPanelRect.anchorMin = new Vector2(0, 0);
            resultPanelRect.anchorMax = new Vector2(1, 0.7f);
            resultPanelRect.offsetMin = Vector2.zero;
            resultPanelRect.offsetMax = Vector2.zero;

            // 创建ScrollRect
            ScrollRect scrollRect = resultPanelGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // 创建Viewport
            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(resultPanelGO.transform, false);
            RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportGO.AddComponent<UnityEngine.UI.Image>();
            viewportGO.AddComponent<UnityEngine.UI.Mask>();
            scrollRect.viewport = viewportRect;

            // 创建Content
            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            scrollRect.content = contentRect;

            // 创建结果文本
            GameObject resultTextGO = new GameObject("ResultText");
            resultTextGO.transform.SetParent(contentGO.transform, false);
            RectTransform resultTextRect = resultTextGO.AddComponent<RectTransform>();
            resultTextRect.anchorMin = Vector2.zero;
            resultTextRect.anchorMax = Vector2.one;
            resultTextRect.offsetMin = Vector2.zero;
            resultTextRect.offsetMax = Vector2.zero;

            Text resultText = resultTextGO.AddComponent<Text>();
            resultText.text = "点击按钮开始测试...\n按T键运行所有测试\n\n测试流程：\n1. 点击存储按钮保存数据\n2. 点击'修改数据'修改并保存\n3. 停止运行，重新启动\n4. 点击'加载数据'验证持久化\n5. 点击'清除数据'重新开始";
            resultText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            resultText.fontSize = 14;
            resultText.color = Color.white;
            resultText.alignment = TextAnchor.UpperLeft;
            resultText.verticalOverflow = VerticalWrapMode.Overflow;
            resultText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 设置引用
            this.resultText = resultText;
            this.resultScrollRect = scrollRect;
        }

        /// <summary>
        /// 创建按钮的辅助方法
        /// </summary>
        private void CreateButton(GameObject parent, string text, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonGO = new GameObject(text + "Button");
            buttonGO.transform.SetParent(parent.transform, false);
            
            RectTransform buttonRect = buttonGO.AddComponent<RectTransform>();
            buttonRect.anchorMin = anchorMin;
            buttonRect.anchorMax = anchorMax;
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
            
            Image buttonImage = buttonGO.AddComponent<Image>();
            buttonImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            Button button = buttonGO.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(onClick);
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            
            Text buttonText = textGO.AddComponent<Text>();
            buttonText.text = text;
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonText.fontSize = 12;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
        }

        /// <summary>
        /// 记录测试结果
        /// </summary>
        private void LogResult(string message)
        {
            Debug.Log($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            
            if (resultText != null)
            {
                resultText.text += message + "\n";
                
                // 自动滚动到底部
                if (resultScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    resultScrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }
    }

    /// <summary>
    /// 测试玩家数据类
    /// </summary>
    [Serializable]
    public class TestPlayerData
    {
        public string playerName;
        public int level;
        public float experience;
        public bool isVip;

        public override string ToString()
        {
            return $"PlayerData(Name={playerName}, Level={level}, Exp={experience}, VIP={isVip})";
        }
    }
}
