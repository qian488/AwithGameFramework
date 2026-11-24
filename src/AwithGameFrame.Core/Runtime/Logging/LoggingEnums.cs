

namespace AwithGameFrame.Core.Logging
{
    /// <summary>
    /// 日志级别枚举
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 跟踪级别 - 最详细的调试信息
        /// </summary>
        Trace = 0,
        
        /// <summary>
        /// 调试级别 - 调试信息
        /// </summary>
        Debug = 1,
        
        /// <summary>
        /// 信息级别 - 一般信息
        /// </summary>
        Info = 2,
        
        /// <summary>
        /// 警告级别 - 警告信息
        /// </summary>
        Warn = 3,
        
        /// <summary>
        /// 错误级别 - 错误信息
        /// </summary>
        Error = 4,
        
        /// <summary>
        /// 致命级别 - 致命错误
        /// </summary>
        Fatal = 5
    }
    
    /// <summary>
    /// 日志分类枚举
    /// </summary>
    public enum LogCategory
    {
        /// <summary>
        /// 核心系统
        /// </summary>
        Core,
        
        /// <summary>
        /// 用户界面
        /// </summary>
        UI,
        
        /// <summary>
        /// 音频系统
        /// </summary>
        Audio,
        
        /// <summary>
        /// 输入系统
        /// </summary>
        Input,
        
        /// <summary>
        /// 网络通信
        /// </summary>
        Network,
        
        /// <summary>
        /// 性能监控
        /// </summary>
        Performance,
        
        /// <summary>
        /// 资源管理
        /// </summary>
        Resource,
        
        /// <summary>
        /// 对象池管理
        /// </summary>
        Pool,
        
        /// <summary>
        /// 配置管理
        /// </summary>
        Config,
        
        /// <summary>
        /// 数据持久化
        /// </summary>
        DataPersistence,
        
        /// <summary>
        /// 事件系统
        /// </summary>
        Event,
        
        /// <summary>
        /// 场景管理
        /// </summary>
        Scene,
        
        /// <summary>
        /// 工具类
        /// </summary>
        Utils,
        
        /// <summary>
        /// 提供者管理
        /// </summary>
        Provider
    }
}
