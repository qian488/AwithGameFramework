using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;

namespace AwithGameFrame.Core
{
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

        /// <summary>
        /// 延迟执行（便捷方法）
        /// </summary>
        /// <param name="delay">延迟时间（秒）</param>
        /// <param name="action">要执行的操作</param>
        /// <returns>协程引用</returns>
        public Coroutine StartDelay(float delay, System.Action action)
        {
            return StartCoroutine(DelayCoroutine(delay, action));
        }

        /// <summary>
        /// 下一帧执行（便捷方法）
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <returns>协程引用</returns>
        public Coroutine StartNextFrame(System.Action action)
        {
            return StartCoroutine(NextFrameCoroutine(action));
        }

        /// <summary>
        /// 延迟执行协程
        /// </summary>
        private IEnumerator DelayCoroutine(float delay, System.Action action)
        {
            yield return new WaitForSeconds(delay);
            action?.Invoke();
        }

        /// <summary>
        /// 下一帧执行协程
        /// </summary>
        private IEnumerator NextFrameCoroutine(System.Action action)
        {
            yield return null;
            action?.Invoke();
        }
        #endregion
    }
}
