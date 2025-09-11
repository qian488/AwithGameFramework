# AwithGameFrame 数据持久化 ORM 设计理念

## 🎯 框架定位

AwithGameFrame 是一个**轻量级游戏开发框架**，专注于提供**基础功能**和**统一接口**，而不是完整的ORM解决方案。

## 📋 当前ORM实现

### ✅ 框架提供的功能
- **基础对象映射**: 简单的属性映射（通过反射）
- **基本类型支持**: string, int, float, bool 等直接支持
- **复杂对象支持**: 通过反射进行属性映射
- **错误处理**: 完善的异常处理和日志记录

### ❌ 框架不提供的功能
- **复杂ORM功能**: 关联查询、延迟加载、缓存等
- **性能优化**: 查询优化、批量操作等
- **高级映射**: 复杂类型转换、自定义映射规则等

## 🏗️ 设计原则

### 1. **轻量优先**
- 框架只提供最基础的对象映射
- 避免引入复杂的ORM依赖
- 保持框架的轻量和简单

### 2. **开发者选择**
- 开发者可以根据项目需求选择ORM方案
- 框架提供基础接口，不强制使用特定ORM
- 支持替换和扩展

### 3. **实用主义**
- 满足80%的常见需求
- 复杂需求由开发者自行实现
- 避免过度设计

## 🔧 推荐的ORM方案

### 对于简单项目
- **使用框架内置映射**: 适合简单的数据存储需求
- **优点**: 无额外依赖，简单易用
- **缺点**: 功能有限，性能一般

### 对于复杂项目
- **Entity Framework Core**: 功能完整，性能优秀
- **Dapper**: 轻量级，性能极佳
- **自定义ORM**: 根据具体需求定制

## 📝 使用建议

### 框架内置映射适用场景
```csharp
// ✅ 适合：简单的数据存储
public class PlayerData
{
    public string Name { get; set; }
    public int Level { get; set; }
    public float Score { get; set; }
}

// ❌ 不适合：复杂关联查询
public class Player
{
    public List<Item> Inventory { get; set; }
    public Guild Guild { get; set; }
    public List<Quest> ActiveQuests { get; set; }
}
```

### 复杂项目建议
```csharp
// 使用专门的ORM库
public class PlayerRepository
{
    private readonly DbContext _context;
    
    public async Task<Player> GetPlayerWithInventory(int playerId)
    {
        return await _context.Players
            .Include(p => p.Inventory)
            .Include(p => p.Guild)
            .FirstOrDefaultAsync(p => p.Id == playerId);
    }
}
```

## 🚀 未来扩展

### 可能的改进方向
1. **插件化ORM**: 支持第三方ORM集成
2. **性能优化**: 添加查询缓存和批量操作
3. **高级映射**: 支持更复杂的类型转换

### 保持框架轻量
- 这些功能作为可选扩展包提供
- 核心框架保持简单
- 开发者按需选择

## 💡 总结

AwithGameFrame 的ORM设计遵循**"够用就好"**的原则：

- ✅ **提供基础功能**: 满足大多数简单需求
- ✅ **保持轻量**: 不引入复杂依赖
- ✅ **支持扩展**: 开发者可以替换和扩展
- ✅ **实用优先**: 专注解决实际问题

对于复杂的ORM需求，建议开发者根据项目特点选择合适的专业ORM库。
