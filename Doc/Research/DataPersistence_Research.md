# 数据持久化模块调研报告

## 调研概述

**调研时间**: 2024年9月10日  
**调研目标**: 为AwithGameFrame框架选择合适的数据持久化解决方案  
**调研范围**: 本地存储、数据库、序列化方案  

## 1. 需求分析

### 1.1 功能需求
- **本地存储**: 支持游戏设置、存档、用户数据等本地存储
- **数据安全**: 防止数据被恶意修改或删除
- **跨平台**: 支持PC、移动端、WebGL等平台
- **性能**: 快速读写，不影响游戏性能
- **易用性**: 提供简洁的API接口

### 1.2 技术需求
- **存储格式**: 支持多种数据格式（JSON、二进制、数据库）
- **数据量**: 支持从小文件到大型数据库
- **加密**: 支持数据加密存储
- **备份**: 支持数据备份和恢复
- **版本控制**: 支持数据格式版本管理

## 2. 市场调研结果

### 2.1 Unity内置解决方案

#### PlayerPrefs
- **开发商**: Unity Technologies
- **存储位置**: 系统注册表/配置文件
- **数据类型**: 字符串、整数、浮点数
- **平台支持**: 全平台支持
- **加密**: 不支持

**优点**:
- 简单易用，无需额外配置
- 跨平台兼容性好
- 自动处理平台差异
- 无需额外依赖

**缺点**:
- 数据类型有限
- 不支持复杂数据结构
- 数据安全性差
- 存储容量有限

#### Unity Cloud Build
- **开发商**: Unity Technologies
- **功能**: 云端数据存储
- **特点**: 与Unity服务集成
- **费用**: 按使用量计费

### 2.2 第三方开源解决方案

#### SQLite
- **开发商**: SQLite Development Team
- **类型**: 嵌入式数据库
- **特点**: 轻量级、无服务器、跨平台
- **许可证**: 公共域

**优点**:
- 轻量级，无需服务器
- 支持SQL查询
- 跨平台兼容性好
- 性能优秀
- 数据安全性好

**缺点**:
- 需要额外的C#包装库
- 学习成本较高
- 不适合复杂查询

#### JSON.NET (Newtonsoft.Json)
- **开发商**: James Newton-King
- **功能**: JSON序列化/反序列化
- **特点**: 高性能、功能丰富
- **许可证**: MIT

**优点**:
- 性能优秀
- 功能丰富，支持复杂对象
- 社区活跃，文档完善
- 易于使用

**缺点**:
- 需要额外依赖
- 文件大小相对较大
- 不支持二进制数据

#### MessagePack
- **开发商**: MessagePack Team
- **功能**: 二进制序列化
- **特点**: 高效、紧凑、跨语言
- **许可证**: MIT

**优点**:
- 性能优秀，体积小
- 支持多种数据类型
- 跨语言兼容
- 序列化速度快

**缺点**:
- 人类不可读
- 调试困难
- 社区相对较小

### 2.3 商业解决方案

#### Unity Cloud Save
- **开发商**: Unity Technologies
- **功能**: 云端数据存储
- **特点**: 与Unity服务集成
- **费用**: 按使用量计费

**优点**:
- 与Unity深度集成
- 支持跨设备同步
- 数据安全性高
- 无需自建服务器

**缺点**:
- 需要网络连接
- 费用较高
- 依赖Unity服务

#### PlayFab
- **开发商**: Microsoft
- **功能**: 游戏后端服务
- **特点**: 包含数据存储、用户管理等功能
- **费用**: 免费额度 + 按使用量计费

**优点**:
- 功能全面
- 支持跨平台
- 数据安全性高
- 提供分析工具

**缺点**:
- 功能复杂，学习成本高
- 费用较高
- 依赖第三方服务

## 3. 存储方案对比

### 3.1 本地存储方案

| 方案 | 数据类型 | 安全性 | 性能 | 易用性 | 跨平台 | 推荐度 |
|------|----------|--------|------|--------|--------|--------|
| PlayerPrefs | 基础类型 | 低 | 高 | 高 | 好 | ⭐⭐ |
| JSON文件 | 任意 | 中 | 中 | 高 | 好 | ⭐⭐⭐ |
| 二进制文件 | 任意 | 高 | 高 | 中 | 好 | ⭐⭐⭐⭐ |
| SQLite | 结构化 | 高 | 高 | 中 | 好 | ⭐⭐⭐⭐⭐ |

### 3.2 云端存储方案

| 方案 | 功能 | 费用 | 易用性 | 安全性 | 推荐度 |
|------|------|------|--------|--------|--------|
| Unity Cloud Save | 基础 | 中 | 高 | 高 | ⭐⭐⭐ |
| PlayFab | 全面 | 高 | 中 | 高 | ⭐⭐⭐⭐ |
| 自建服务器 | 定制 | 中 | 低 | 高 | ⭐⭐ |

## 4. 技术选型建议

### 4.1 本地存储方案
**推荐方案**: SQLite + JSON + 二进制文件

**选择理由**:
1. **分层存储**: 不同类型数据使用不同存储方式
2. **性能优化**: 根据数据特点选择最优方案
3. **安全性**: 支持数据加密和完整性校验
4. **灵活性**: 支持复杂查询和数据结构

**具体方案**:
- **用户设置**: 使用PlayerPrefs存储简单配置
- **游戏存档**: 使用JSON文件存储结构化数据
- **缓存数据**: 使用二进制文件存储性能敏感数据
- **复杂数据**: 使用SQLite存储需要查询的数据

### 4.2 序列化方案
**推荐方案**: JSON + MessagePack

**选择理由**:
1. **开发阶段**: 使用JSON便于调试
2. **生产环境**: 使用MessagePack提升性能
3. **兼容性**: 支持多种数据类型
4. **维护性**: 易于版本升级和数据迁移

### 4.3 云端存储方案
**推荐方案**: 框架不内置云端存储，提供自建服务器指导

**选择理由**:
1. **通用性**: 框架保持通用性，不绑定特定云端服务
2. **灵活性**: 开发者可根据需求选择云端方案
3. **成本控制**: 避免框架使用成本影响开发者选择
4. **定制化**: 开发者可完全控制云端存储方案

## 5. 架构设计

### 5.1 数据持久化架构

```
DataPersistenceManager
├── LocalStorage
│   ├── PlayerPrefsStorage (简单配置)
│   ├── JSONFileStorage (结构化数据)
│   ├── BinaryFileStorage (性能数据)
│   └── SQLiteStorage (复杂数据)
├── Serialization
│   ├── JSONSerializer
│   └── MessagePackSerializer
├── Security
│   ├── DataEncryption
│   └── DataValidation
└── CloudStorage (扩展接口)
    ├── ICloudStorage (云端存储接口)
    └── CloudStorageGuide (自建服务器指导)
```

### 5.2 数据分类存储策略

| 数据类型 | 存储方案 | 序列化格式 | 加密 | 备份 |
|----------|----------|------------|------|------|
| 用户设置 | PlayerPrefs | 原生 | 否 | 否 |
| 游戏存档 | JSON文件 | JSON | 是 | 是 |
| 缓存数据 | 二进制文件 | MessagePack | 是 | 否 |
| 统计数据 | SQLite | 原生 | 是 | 是 |
| 云端数据 | 扩展接口 | 开发者自定 | 开发者自定 | 开发者自定 |

## 6. 实施计划

### 6.1 第一阶段 (1-2周)
- 设计数据持久化架构
- 实现基础存储接口
- 集成SQLite和序列化库

### 6.2 第二阶段 (2-3周)
- 实现各种存储方案
- 添加数据加密功能
- 开发数据迁移工具

### 6.3 第三阶段 (2-3周)
- 实现云端存储功能
- 添加数据备份和恢复
- 性能优化和测试

### 6.4 第四阶段 (1-2周)
- 完善文档和示例
- 集成测试和压力测试
- 部署和上线准备

## 7. 风险评估

### 7.1 技术风险
- **SQLite集成风险**: 需要处理不同平台的兼容性
- **数据迁移风险**: 版本升级时数据格式变更
- **性能风险**: 大量数据读写可能影响游戏性能

### 7.2 缓解措施
- **充分测试**: 在不同平台上进行充分测试
- **版本管理**: 建立完善的数据版本管理机制
- **性能监控**: 建立性能监控和优化机制

## 8. 自建服务器云端存储指导

### 8.1 技术架构推荐

#### 简单方案 (推荐新手)
**技术栈**: Node.js + Express + MongoDB + MinIO
**开发时间**: 2-3周
**服务器成本**: $20-50/月

```javascript
// 基础API服务器示例
const express = require('express');
const mongoose = require('mongoose');
const MinIO = require('minio');

const app = express();

// 用户数据API
app.get('/api/user/:userId', async (req, res) => {
    const userData = await UserData.findOne({ userId: req.params.userId });
    res.json(userData);
});

app.post('/api/user/:userId', async (req, res) => {
    await UserData.findOneAndUpdate(
        { userId: req.params.userId },
        req.body,
        { upsert: true }
    );
    res.json({ success: true });
});
```

#### 中等方案 (推荐生产)
**技术栈**: ASP.NET Core + PostgreSQL + AWS S3
**开发时间**: 4-6周
**服务器成本**: $50-100/月

```csharp
// ASP.NET Core API示例
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<UserData>> GetUserData(string userId)
    {
        var userData = await _userDataService.GetAsync(userId);
        return Ok(userData);
    }
}
```

### 8.2 框架集成接口

```csharp
// 云端存储接口定义
namespace AwithGameFrame.DataPersistence
{
    public interface ICloudStorage
    {
        Task<T> GetDataAsync<T>(string path);
        Task SaveDataAsync<T>(string path, T data);
        Task DeleteDataAsync(string path);
        Task<bool> ExistsAsync(string path);
        Task<List<string>> ListAsync(string directory);
    }
    
    // 自建服务器实现示例
    public class CustomCloudStorage : ICloudStorage
    {
        private HttpClient _httpClient;
        private string _apiBaseUrl;
        
        public async Task<T> GetDataAsync<T>(string path)
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/data/{path}");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(json);
        }
        
        public async Task SaveDataAsync<T>(string path, T data)
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/data/{path}", content);
            response.EnsureSuccessStatusCode();
        }
    }
}
```

### 8.3 部署方案

#### Docker部署 (推荐)
```dockerfile
# Dockerfile示例
FROM node:18-alpine
WORKDIR /app
COPY package*.json ./
RUN npm install
COPY . .
EXPOSE 3000
CMD ["npm", "start"]
```

#### 云服务器部署
- **阿里云ECS**: 2核4G，$30-50/月
- **腾讯云CVM**: 2核4G，$25-40/月
- **AWS EC2**: t3.medium，$30-60/月

### 8.4 安全考虑

```csharp
// 安全措施示例
public class SecureCloudStorage : ICloudStorage
{
    private readonly string _encryptionKey;
    
    public async Task<T> GetDataAsync<T>(string path)
    {
        // 1. 身份验证
        await AuthenticateAsync();
        
        // 2. 权限检查
        await CheckPermissionAsync(path);
        
        // 3. 数据解密
        var encryptedData = await GetEncryptedDataAsync(path);
        var decryptedData = Decrypt(encryptedData);
        
        return JsonConvert.DeserializeObject<T>(decryptedData);
    }
}
```

### 8.5 监控和维护

```csharp
// 监控和日志
public class MonitoredCloudStorage : ICloudStorage
{
    private readonly ILogger _logger;
    
    public async Task<T> GetDataAsync<T>(string path)
    {
        try
        {
            _logger.LogInformation($"Getting data from path: {path}");
            var result = await _innerStorage.GetDataAsync<T>(path);
            _logger.LogInformation($"Successfully retrieved data from path: {path}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get data from path: {path}");
            throw;
        }
    }
}
```

## 9. 总结

通过全面的市场调研和技术分析，我们选择了分层存储方案作为AwithGameFrame框架的数据持久化解决方案。该方案结合了多种存储技术的优点，能够满足不同类型数据的存储需求，同时保证了性能、安全性和易用性。

框架专注于本地存储功能，为云端存储提供扩展接口和详细的自建服务器指导，让开发者可以根据自己的需求选择合适的云端方案。

下一步将按照实施计划，逐步完成数据持久化模块的开发工作。
