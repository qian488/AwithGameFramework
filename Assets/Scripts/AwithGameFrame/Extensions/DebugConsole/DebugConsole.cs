using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Extensions.DebugConsole
{
    /// <summary>
    /// 调试命令委托
    /// </summary>
    public delegate string DebugCommandHandler(params string[] args);

    /// <summary>
    /// 调试命令描述
    /// </summary>
    public readonly struct DebugCommand
    {
        public string Name { get; }
        public string Description { get; }
        public DebugCommandHandler Handler { get; }

        public DebugCommand(string name, string description, DebugCommandHandler handler)
        {
            Name = name;
            Description = description;
            Handler = handler;
        }
    }

    /// <summary>
    /// 运行时调试控制台管理器
    /// 提供命令行输入、命令执行、日志记录功能
    /// 放在Extensions包中
    /// </summary>
    public class DebugConsole : BaseManager<DebugConsole>
    {
        private const int MAX_LOG_LINES = 200;

        private readonly Dictionary<string, DebugCommand> _commands = new Dictionary<string, DebugCommand>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _logEntries = new List<string>();
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;

        public override int Priority => (int)ModulePriority.GameSpecific;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<DebugConsole>(this);
            RegisterBuiltInCommands();
            LoggingAPI.Info(LogCategory.Core, "DebugConsole 初始化完成");
        }

        public override void Shutdown()
        {
            _commands.Clear();
            _logEntries.Clear();
            _commandHistory.Clear();
            base.Shutdown();
        }

        #region 命令管理

        /// <summary>
        /// 注册调试命令
        /// </summary>
        public void RegisterCommand(string name, string description, DebugCommandHandler handler)
        {
            _commands[name] = new DebugCommand(name, description, handler);
        }

        /// <summary>
        /// 移除调试命令
        /// </summary>
        public void UnregisterCommand(string name)
        {
            _commands.Remove(name);
        }

        /// <summary>
        /// 获取所有已注册命令
        /// </summary>
        public IReadOnlyList<DebugCommand> GetAllCommands()
        {
            return _commands.Values.ToList().AsReadOnly();
        }

        #endregion

        #region 命令执行

        /// <summary>
        /// 执行命令字符串
        /// </summary>
        public string ExecuteCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            _commandHistory.Add(input);
            _historyIndex = _commandHistory.Count;

            var parts = ParseCommandLine(input);
            var cmdName = parts[0];
            var args = parts.Skip(1).ToArray();

            LogEntry($"> {input}");

            if (_commands.TryGetValue(cmdName, out var command))
            {
                try
                {
                    var result = command.Handler(args);
                    if (!string.IsNullOrEmpty(result))
                        LogEntry(result);
                    return result;
                }
                catch (Exception ex)
                {
                    var errorMsg = $"命令执行错误: {ex.Message}";
                    LogEntry(errorMsg);
                    return errorMsg;
                }
            }

            var notFound = $"未知命令: {cmdName}. 输入 help 查看可用命令。";
            LogEntry(notFound);
            return notFound;
        }

        /// <summary>
        /// 获取上一条命令历史
        /// </summary>
        public string GetHistoryPrevious()
        {
            if (_commandHistory.Count == 0) return string.Empty;
            _historyIndex = Mathf.Max(0, _historyIndex - 1);
            return _commandHistory[_historyIndex];
        }

        /// <summary>
        /// 获取下一条命令历史
        /// </summary>
        public string GetHistoryNext()
        {
            if (_commandHistory.Count == 0) return string.Empty;
            _historyIndex = Mathf.Min(_commandHistory.Count - 1, _historyIndex + 1);
            return _historyIndex < _commandHistory.Count ? _commandHistory[_historyIndex] : string.Empty;
        }

        #endregion

        #region 日志输出

        /// <summary>
        /// 获取所有日志条目
        /// </summary>
        public IReadOnlyList<string> GetLogEntries() => _logEntries.AsReadOnly();

        /// <summary>
        /// 获取日志文本（换行分隔）
        /// </summary>
        public string GetLogText()
        {
            var sb = new StringBuilder();
            foreach (var entry in _logEntries)
            {
                sb.AppendLine(entry);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public void ClearLog()
        {
            _logEntries.Clear();
        }

        /// <summary>
        /// 记录日志条目（供命令和调试使用）
        /// </summary>
        public void LogEntry(string entry)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _logEntries.Add($"[{timestamp}] {entry}");

            while (_logEntries.Count > MAX_LOG_LINES)
                _logEntries.RemoveAt(0);
        }

        #endregion

        #region 事件

        /// <summary>
        /// 当日志条目添加时触发（供UI绑定）
        /// </summary>
        public event Action<string> OnLogEntryAdded;

        #endregion

        #region 内置命令

        private void RegisterBuiltInCommands()
        {
            RegisterCommand("help", "显示可用命令列表", args =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== 可用命令 ===");
                foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
                {
                    sb.AppendLine($"  {cmd.Name} - {cmd.Description}");
                }
                return sb.ToString().TrimEnd();
            });

            RegisterCommand("clear", "清空控制台日志", args =>
            {
                ClearLog();
                return "日志已清空";
            });

            RegisterCommand("time", "显示当前时间", args =>
            {
                return $"当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            });

            RegisterCommand("log", "输出日志 [level] [msg]", args =>
            {
                if (args.Length < 2) return "用法: log <info|warn|error> <消息>";
                var msg = string.Join(" ", args.Skip(1));
                switch (args[0].ToLower())
                {
                    case "info": LoggingAPI.Info(msg); break;
                    case "warn": LoggingAPI.Warn(msg); break;
                    case "error": LoggingAPI.Error(msg); break;
                    default: return $"未知日志级别: {args[0]}. 支持: info, warn, error";
                }
                return $"日志已输出: [{args[0]}] {msg}";
            });

            RegisterCommand("commands", "列出已注册命令数", args =>
            {
                return $"已注册命令数: {_commands.Count}";
            });

            RegisterCommand("echo", "回显输入的参数", args =>
            {
                return string.Join(" ", args);
            });

            RegisterCommand("fps", "显示当前帧率", args =>
            {
                return $"FPS: {1f / Time.deltaTime:F1}";
            });

            RegisterCommand("mem", "显示内存使用情况", args =>
            {
                var total = GC.GetTotalMemory(false) / 1024f / 1024f;
                return $"托管内存: {total:F1} MB";
            });
        }

        #endregion

        #region 命令行解析

        /// <summary>
        /// 解析命令行输入，支持引号包裹的参数
        /// </summary>
        private static List<string> ParseCommandLine(string input)
        {
            var result = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        #endregion
    }
}
