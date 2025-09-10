using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using AwithGameFrame.Core;

namespace AwithGameFrame.Logging
{
    /// <summary>
    /// 文件日志输出器 - 支持日志文件写入、轮转和清理
    /// </summary>
    public class FileLogger : BaseManager<FileLogger>
    {
        #region 字段
        private string _logDirectory;
        private string _currentLogFile;
        private StreamWriter _logWriter;
        private readonly object _lockObject = new object();
        private bool _isEnabled = false;
        private LogLevel _minLevel = LogLevel.Debug;
        private HashSet<LogCategory> _enabledCategories = new HashSet<LogCategory>();
        private int _maxFileSize = 10 * 1024 * 1024; // 10MB
        private int _maxFiles = 10;
        private bool _enableTimestamp = true;
        private bool _enableStackTrace = false;
        private DateTime _lastCleanupTime = DateTime.MinValue;
        private float _cleanupInterval = 3600f; // 1小时
        #endregion
        
        #region 属性
        /// <summary>
        /// 是否启用文件日志
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    if (value)
                    {
                        Initialize();
                    }
                    else
                    {
                        CloseCurrentFile();
                    }
                }
            }
        }
        
        /// <summary>
        /// 最小日志级别
        /// </summary>
        public LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }
        
        /// <summary>
        /// 最大文件大小（字节）
        /// </summary>
        public int MaxFileSize
        {
            get => _maxFileSize;
            set => _maxFileSize = Math.Max(1024 * 1024, value); // 最小1MB
        }
        
        /// <summary>
        /// 最大文件数量
        /// </summary>
        public int MaxFiles
        {
            get => _maxFiles;
            set => _maxFiles = Math.Max(1, value);
        }
        
        /// <summary>
        /// 是否启用时间戳
        /// </summary>
        public bool EnableTimestamp
        {
            get => _enableTimestamp;
            set => _enableTimestamp = value;
        }
        
        /// <summary>
        /// 是否启用堆栈跟踪
        /// </summary>
        public bool EnableStackTrace
        {
            get => _enableStackTrace;
            set => _enableStackTrace = value;
        }
        #endregion
        
        #region 构造函数
        public FileLogger()
        {
            InitializeSettings();
        }
        #endregion
        
        #region 公共方法
        /// <summary>
        /// 初始化文件日志器
        /// </summary>
        /// <param name="logDirectory">日志目录，默认为Application.persistentDataPath/Logs</param>
        public void Initialize(string logDirectory = null)
        {
            _logDirectory = logDirectory ?? Path.Combine(Application.persistentDataPath, "Logs");
            
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                
                CreateNewLogFile();
                _isEnabled = true;
                
                FrameworkLogger.Info($"文件日志器初始化成功，日志目录: {_logDirectory}");
            }
            catch (Exception ex)
            {
                FrameworkLogger.LogException("文件日志器初始化失败", ex);
                _isEnabled = false;
            }
        }
        
        /// <summary>
        /// 写入日志到文件
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public void WriteLog(LogLevel level, LogCategory category, string message, object context = null)
        {
            if (!_isEnabled) return;
            if (level < _minLevel) return;
            if (!_enabledCategories.Contains(category)) return;
            
            lock (_lockObject)
            {
                try
                {
                    if (_logWriter == null)
                    {
                        CreateNewLogFile();
                    }
                    
                    string formattedMessage = FormatLogMessage(level, category, message, context);
                    _logWriter.WriteLine(formattedMessage);
                    _logWriter.Flush();
                    
                    // 检查文件大小，如果超过限制则轮转
                    CheckAndRotateFile();
                }
                catch (Exception ex)
                {
                    FrameworkLogger.LogException("写入日志文件失败", ex);
                }
            }
        }
        
        /// <summary>
        /// 写入异常日志到文件
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文对象</param>
        public void WriteException(LogLevel level, LogCategory category, string message, Exception exception, object context = null)
        {
            if (!_isEnabled) return;
            if (level < _minLevel) return;
            if (!_enabledCategories.Contains(category)) return;
            
            string exceptionMessage = $"{message}\nException: {exception.Message}";
            if (_enableStackTrace)
            {
                exceptionMessage += $"\nStackTrace: {exception.StackTrace}";
            }
            
            WriteLog(level, category, exceptionMessage, context);
        }
        
        /// <summary>
        /// 设置启用的分类
        /// </summary>
        /// <param name="category">分类</param>
        /// <param name="enabled">是否启用</param>
        public void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            if (enabled)
            {
                _enabledCategories.Add(category);
            }
            else
            {
                _enabledCategories.Remove(category);
            }
        }
        
        /// <summary>
        /// 手动轮转日志文件
        /// </summary>
        public void RotateLogFile()
        {
            lock (_lockObject)
            {
                CloseCurrentFile();
                CreateNewLogFile();
            }
        }
        
        /// <summary>
        /// 清理旧日志文件
        /// </summary>
        public void CleanupOldFiles()
        {
            if (string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory))
                return;
            
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "*.log");
                if (logFiles.Length <= _maxFiles) return;
                
                // 按修改时间排序，删除最旧的文件
                Array.Sort(logFiles, (x, y) => File.GetLastWriteTime(x).CompareTo(File.GetLastWriteTime(y)));
                
                int filesToDelete = logFiles.Length - _maxFiles;
                for (int i = 0; i < filesToDelete; i++)
                {
                    File.Delete(logFiles[i]);
                    FrameworkLogger.Info($"删除旧日志文件: {Path.GetFileName(logFiles[i])}");
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.LogException("清理旧日志文件失败", ex);
            }
        }
        
        /// <summary>
        /// 更新文件日志器（在Update中调用）
        /// </summary>
        public void Update()
        {
            if (!_isEnabled) return;
            
            // 定期清理旧文件
            if (Time.realtimeSinceStartup - _lastCleanupTime.Ticks / TimeSpan.TicksPerSecond > _cleanupInterval)
            {
                CleanupOldFiles();
                _lastCleanupTime = DateTime.Now;
            }
        }
        
        /// <summary>
        /// 关闭文件日志器
        /// </summary>
        public void Shutdown()
        {
            lock (_lockObject)
            {
                CloseCurrentFile();
                _isEnabled = false;
            }
        }
        #endregion
        
        #region 私有方法
        /// <summary>
        /// 初始化设置
        /// </summary>
        private void InitializeSettings()
        {
            _minLevel = LogLevel.Debug;
            _maxFileSize = 10 * 1024 * 1024; // 10MB
            _maxFiles = 10;
            _enableTimestamp = true;
            _enableStackTrace = false;
            _cleanupInterval = 3600f; // 1小时
            
            // 默认启用所有分类
            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                _enabledCategories.Add(category);
            }
        }
        
        /// <summary>
        /// 创建新的日志文件
        /// </summary>
        private void CreateNewLogFile()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _currentLogFile = Path.Combine(_logDirectory, $"game_{timestamp}.log");
                
                _logWriter = new StreamWriter(_currentLogFile, true, Encoding.UTF8);
                _logWriter.WriteLine($"=== 日志文件创建时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _logWriter.Flush();
            }
            catch (Exception ex)
            {
                FrameworkLogger.LogException("创建日志文件失败", ex);
                _logWriter = null;
            }
        }
        
        /// <summary>
        /// 关闭当前日志文件
        /// </summary>
        private void CloseCurrentFile()
        {
            if (_logWriter != null)
            {
                try
                {
                    _logWriter.WriteLine($"=== 日志文件结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _logWriter.Close();
                    _logWriter.Dispose();
                }
                catch (Exception ex)
                {
                    FrameworkLogger.LogException("关闭日志文件失败", ex);
                }
                finally
                {
                    _logWriter = null;
                }
            }
        }
        
        /// <summary>
        /// 格式化日志消息
        /// </summary>
        private string FormatLogMessage(LogLevel level, LogCategory category, string message, object context)
        {
            var parts = new List<string>();
            
            // 时间戳
            if (_enableTimestamp)
            {
                parts.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]");
            }
            
            // 日志级别
            parts.Add($"[{level.ToString().ToUpper()}]");
            
            // 日志分类
            parts.Add($"[{category.ToString()}]");
            
            // 上下文信息
            if (context != null)
            {
                string contextInfo = context is UnityEngine.Object ? context.GetType().Name : context.ToString();
                parts.Add($"[{contextInfo}]");
            }
            
            // 消息内容
            parts.Add(message);
            
            return string.Join(" ", parts);
        }
        
        /// <summary>
        /// 检查并轮转文件
        /// </summary>
        private void CheckAndRotateFile()
        {
            if (_logWriter == null || string.IsNullOrEmpty(_currentLogFile)) return;
            
            try
            {
                var fileInfo = new FileInfo(_currentLogFile);
                if (fileInfo.Length >= _maxFileSize)
                {
                    RotateLogFile();
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.LogException("检查文件大小失败", ex);
            }
        }
        #endregion
    }
}
