using System;
using UnityEngine;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 时间工具类
    /// 提供常用的时间处理函数
    /// </summary>
    public static class TimeHelper
    {
        /// <summary>
        /// 获取当前时间戳（秒）
        /// </summary>
        /// <returns>时间戳</returns>
        public static long GetTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        /// <summary>
        /// 获取当前时间戳（毫秒）
        /// </summary>
        /// <returns>时间戳</returns>
        public static long GetTimestampMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 将时间戳转换为DateTime
        /// </summary>
        /// <param name="timestamp">时间戳（秒）</param>
        /// <returns>DateTime</returns>
        public static DateTime TimestampToDateTime(long timestamp)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
        }

        /// <summary>
        /// 将时间戳转换为DateTime
        /// </summary>
        /// <param name="timestamp">时间戳（毫秒）</param>
        /// <returns>DateTime</returns>
        public static DateTime TimestampToDateTimeMilliseconds(long timestamp)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
        }

        /// <summary>
        /// 将DateTime转换为时间戳
        /// </summary>
        /// <param name="dateTime">DateTime</param>
        /// <returns>时间戳（秒）</returns>
        public static long DateTimeToTimestamp(DateTime dateTime)
        {
            return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
        }

        /// <summary>
        /// 将DateTime转换为时间戳
        /// </summary>
        /// <param name="dateTime">DateTime</param>
        /// <returns>时间戳（毫秒）</returns>
        public static long DateTimeToTimestampMilliseconds(DateTime dateTime)
        {
            return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 格式化时间
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <param name="format">格式</param>
        /// <returns>格式化后的字符串</returns>
        public static string FormatTime(DateTime dateTime, string format = "yyyy-MM-dd HH:mm:ss")
        {
            return dateTime.ToString(format);
        }

        /// <summary>
        /// 格式化时间戳
        /// </summary>
        /// <param name="timestamp">时间戳（秒）</param>
        /// <param name="format">格式</param>
        /// <returns>格式化后的字符串</returns>
        public static string FormatTimestamp(long timestamp, string format = "yyyy-MM-dd HH:mm:ss")
        {
            return TimestampToDateTime(timestamp).ToString(format);
        }

        /// <summary>
        /// 获取相对时间描述
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <returns>相对时间描述</returns>
        public static string GetRelativeTime(DateTime dateTime)
        {
            var now = DateTime.Now;
            var diff = now - dateTime;

            if (diff.TotalSeconds < 60)
                return "刚刚";
            else if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}分钟前";
            else if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}小时前";
            else if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}天前";
            else
                return dateTime.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// 获取相对时间描述
        /// </summary>
        /// <param name="timestamp">时间戳（秒）</param>
        /// <returns>相对时间描述</returns>
        public static string GetRelativeTime(long timestamp)
        {
            return GetRelativeTime(TimestampToDateTime(timestamp));
        }

        /// <summary>
        /// 检查时间是否在指定范围内
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>是否在范围内</returns>
        public static bool IsInTimeRange(DateTime dateTime, DateTime startTime, DateTime endTime)
        {
            return dateTime >= startTime && dateTime <= endTime;
        }

        /// <summary>
        /// 检查时间戳是否在指定范围内
        /// </summary>
        /// <param name="timestamp">时间戳（秒）</param>
        /// <param name="startTimestamp">开始时间戳</param>
        /// <param name="endTimestamp">结束时间戳</param>
        /// <returns>是否在范围内</returns>
        public static bool IsInTimeRange(long timestamp, long startTimestamp, long endTimestamp)
        {
            return timestamp >= startTimestamp && timestamp <= endTimestamp;
        }

        /// <summary>
        /// 获取时间差
        /// </summary>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>时间差</returns>
        public static TimeSpan GetTimeDifference(DateTime startTime, DateTime endTime)
        {
            return endTime - startTime;
        }

        /// <summary>
        /// 获取时间差
        /// </summary>
        /// <param name="startTimestamp">开始时间戳</param>
        /// <param name="endTimestamp">结束时间戳</param>
        /// <returns>时间差（秒）</returns>
        public static long GetTimeDifference(long startTimestamp, long endTimestamp)
        {
            return endTimestamp - startTimestamp;
        }

        /// <summary>
        /// 获取当前游戏时间
        /// </summary>
        /// <returns>游戏时间</returns>
        public static float GetGameTime()
        {
            return Time.time;
        }

        /// <summary>
        /// 获取当前游戏时间（不受时间缩放影响）
        /// </summary>
        /// <returns>游戏时间</returns>
        public static float GetUnscaledGameTime()
        {
            return Time.unscaledTime;
        }

        /// <summary>
        /// 获取帧时间
        /// </summary>
        /// <returns>帧时间</returns>
        public static float GetDeltaTime()
        {
            return Time.deltaTime;
        }

        /// <summary>
        /// 获取帧时间（不受时间缩放影响）
        /// </summary>
        /// <returns>帧时间</returns>
        public static float GetUnscaledDeltaTime()
        {
            return Time.unscaledDeltaTime;
        }

        /// <summary>
        /// 获取固定帧时间
        /// </summary>
        /// <returns>固定帧时间</returns>
        public static float GetFixedDeltaTime()
        {
            return Time.fixedDeltaTime;
        }

        /// <summary>
        /// 获取时间缩放
        /// </summary>
        /// <returns>时间缩放</returns>
        public static float GetTimeScale()
        {
            return Time.timeScale;
        }

        /// <summary>
        /// 设置时间缩放
        /// </summary>
        /// <param name="scale">时间缩放</param>
        public static void SetTimeScale(float scale)
        {
            Time.timeScale = scale;
        }

        /// <summary>
        /// 暂停游戏
        /// </summary>
        public static void PauseGame()
        {
            Time.timeScale = 0f;
        }

        /// <summary>
        /// 恢复游戏
        /// </summary>
        public static void ResumeGame()
        {
            Time.timeScale = 1f;
        }

        /// <summary>
        /// 检查游戏是否暂停
        /// </summary>
        /// <returns>是否暂停</returns>
        public static bool IsGamePaused()
        {
            return Time.timeScale == 0f;
        }

        /// <summary>
        /// 获取帧率
        /// </summary>
        /// <returns>帧率</returns>
        public static float GetFPS()
        {
            return 1f / Time.deltaTime;
        }

        /// <summary>
        /// 获取目标帧率
        /// </summary>
        /// <returns>目标帧率</returns>
        public static int GetTargetFrameRate()
        {
            return Application.targetFrameRate;
        }

        /// <summary>
        /// 设置目标帧率
        /// </summary>
        /// <param name="frameRate">目标帧率</param>
        public static void SetTargetFrameRate(int frameRate)
        {
            Application.targetFrameRate = frameRate;
        }
    }
}
