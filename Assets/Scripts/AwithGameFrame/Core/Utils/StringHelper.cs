using System;
using System.Text;
using UnityEngine;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 字符串工具类
    /// 提供常用的字符串处理函数
    /// </summary>
    public static class StringHelper
    {
        /// <summary>
        /// 检查字符串是否为空或null
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>是否为空</returns>
        public static bool IsNullOrEmpty(string str)
        {
            return string.IsNullOrEmpty(str);
        }

        /// <summary>
        /// 检查字符串是否为空、null或只包含空白字符
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>是否为空或空白</returns>
        public static bool IsNullOrWhiteSpace(string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        /// <summary>
        /// 安全地获取字符串，如果为null则返回空字符串
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>安全的字符串</returns>
        public static string SafeString(string str)
        {
            return str ?? string.Empty;
        }

        /// <summary>
        /// 安全地获取字符串，如果为null或空则返回默认值
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>安全的字符串</returns>
        public static string SafeString(string str, string defaultValue)
        {
            return string.IsNullOrEmpty(str) ? defaultValue : str;
        }

        /// <summary>
        /// 格式化字符串
        /// </summary>
        /// <param name="format">格式字符串</param>
        /// <param name="args">参数</param>
        /// <returns>格式化后的字符串</returns>
        public static string Format(string format, params object[] args)
        {
            return string.Format(format, args);
        }

        /// <summary>
        /// 连接字符串
        /// </summary>
        /// <param name="separator">分隔符</param>
        /// <param name="values">值数组</param>
        /// <returns>连接后的字符串</returns>
        public static string Join(string separator, params string[] values)
        {
            return string.Join(separator, values);
        }

        /// <summary>
        /// 连接字符串
        /// </summary>
        /// <param name="separator">分隔符</param>
        /// <param name="values">值数组</param>
        /// <returns>连接后的字符串</returns>
        public static string Join(string separator, params object[] values)
        {
            return string.Join(separator, values);
        }

        /// <summary>
        /// 分割字符串
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="separator">分隔符</param>
        /// <returns>分割后的字符串数组</returns>
        public static string[] Split(string str, string separator)
        {
            if (string.IsNullOrEmpty(str))
                return new string[0];
            
            return str.Split(new string[] { separator }, StringSplitOptions.None);
        }

        /// <summary>
        /// 分割字符串
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="separator">分隔符</param>
        /// <param name="options">分割选项</param>
        /// <returns>分割后的字符串数组</returns>
        public static string[] Split(string str, string separator, StringSplitOptions options)
        {
            if (string.IsNullOrEmpty(str))
                return new string[0];
            
            return str.Split(new string[] { separator }, options);
        }

        /// <summary>
        /// 替换字符串
        /// </summary>
        /// <param name="str">原字符串</param>
        /// <param name="oldValue">要替换的值</param>
        /// <param name="newValue">新值</param>
        /// <returns>替换后的字符串</returns>
        public static string Replace(string str, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str.Replace(oldValue, newValue);
        }

        /// <summary>
        /// 转换为小写
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>小写字符串</returns>
        public static string ToLower(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str.ToLower();
        }

        /// <summary>
        /// 转换为大写
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>大写字符串</returns>
        public static string ToUpper(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str.ToUpper();
        }

        /// <summary>
        /// 去除前后空白字符
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>去除空白后的字符串</returns>
        public static string Trim(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str.Trim();
        }

        /// <summary>
        /// 去除前导空白字符
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>去除前导空白后的字符串</returns>
        public static string TrimStart(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str.TrimStart();
        }

        /// <summary>
        /// 去除尾随空白字符
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>去除尾随空白后的字符串</returns>
        public static string TrimEnd(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str.TrimEnd();
        }

        /// <summary>
        /// 检查字符串是否以指定值开始
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="value">要检查的值</param>
        /// <returns>是否以指定值开始</returns>
        public static bool StartsWith(string str, string value)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(value))
                return false;
            
            return str.StartsWith(value);
        }

        /// <summary>
        /// 检查字符串是否以指定值结束
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="value">要检查的值</param>
        /// <returns>是否以指定值结束</returns>
        public static bool EndsWith(string str, string value)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(value))
                return false;
            
            return str.EndsWith(value);
        }

        /// <summary>
        /// 检查字符串是否包含指定值
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="value">要检查的值</param>
        /// <returns>是否包含指定值</returns>
        public static bool Contains(string str, string value)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(value))
                return false;
            
            return str.Contains(value);
        }

        /// <summary>
        /// 获取字符串长度
        /// </summary>
        /// <param name="str">字符串</param>
        /// <returns>长度</returns>
        public static int Length(string str)
        {
            return str?.Length ?? 0;
        }

        /// <summary>
        /// 截取字符串
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="startIndex">开始索引</param>
        /// <param name="length">长度</param>
        /// <returns>截取后的字符串</returns>
        public static string Substring(string str, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            if (startIndex < 0 || startIndex >= str.Length)
                return string.Empty;
            
            if (length <= 0 || startIndex + length > str.Length)
                return str.Substring(startIndex);
            
            return str.Substring(startIndex, length);
        }

        /// <summary>
        /// 截取字符串
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="startIndex">开始索引</param>
        /// <returns>截取后的字符串</returns>
        public static string Substring(string str, int startIndex)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            if (startIndex < 0 || startIndex >= str.Length)
                return string.Empty;
            
            return str.Substring(startIndex);
        }

        /// <summary>
        /// 查找字符串中指定值的索引
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="value">要查找的值</param>
        /// <returns>索引，未找到返回-1</returns>
        public static int IndexOf(string str, string value)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(value))
                return -1;
            
            return str.IndexOf(value);
        }

        /// <summary>
        /// 查找字符串中指定值的最后索引
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="value">要查找的值</param>
        /// <returns>索引，未找到返回-1</returns>
        public static int LastIndexOf(string str, string value)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(value))
                return -1;
            
            return str.LastIndexOf(value);
        }

        /// <summary>
        /// 使用StringBuilder高效拼接字符串
        /// </summary>
        /// <param name="values">要拼接的值</param>
        /// <returns>拼接后的字符串</returns>
        public static string Concat(params object[] values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;
            
            var sb = new StringBuilder();
            foreach (var value in values)
            {
                sb.Append(value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 使用StringBuilder高效拼接字符串
        /// </summary>
        /// <param name="separator">分隔符</param>
        /// <param name="values">要拼接的值</param>
        /// <returns>拼接后的字符串</returns>
        public static string Concat(string separator, params object[] values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;
            
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(separator);
                sb.Append(values[i]);
            }
            return sb.ToString();
        }
    }
}
