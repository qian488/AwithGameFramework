using UnityEngine;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 数学工具类
    /// 提供常用的数学计算函数
    /// </summary>
    public static class MathHelper
    {
        /// <summary>
        /// 将角度限制在0-360度范围内
        /// </summary>
        /// <param name="angle">角度</param>
        /// <returns>限制后的角度</returns>
        public static float ClampAngle(float angle)
        {
            while (angle < 0) angle += 360;
            while (angle >= 360) angle -= 360;
            return angle;
        }

        /// <summary>
        /// 将角度限制在-180到180度范围内
        /// </summary>
        /// <param name="angle">角度</param>
        /// <returns>限制后的角度</returns>
        public static float ClampAngle180(float angle)
        {
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }

        /// <summary>
        /// 计算两点之间的距离
        /// </summary>
        /// <param name="point1">点1</param>
        /// <param name="point2">点2</param>
        /// <returns>距离</returns>
        public static float Distance(Vector3 point1, Vector3 point2)
        {
            return Vector3.Distance(point1, point2);
        }

        /// <summary>
        /// 计算两点之间的距离（2D）
        /// </summary>
        /// <param name="point1">点1</param>
        /// <param name="point2">点2</param>
        /// <returns>距离</returns>
        public static float Distance2D(Vector2 point1, Vector2 point2)
        {
            return Vector2.Distance(point1, point2);
        }

        /// <summary>
        /// 计算两点之间的距离（忽略Y轴）
        /// </summary>
        /// <param name="point1">点1</param>
        /// <param name="point2">点2</param>
        /// <returns>距离</returns>
        public static float DistanceXZ(Vector3 point1, Vector3 point2)
        {
            float dx = point1.x - point2.x;
            float dz = point1.z - point2.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 将值限制在指定范围内
        /// </summary>
        /// <param name="value">值</param>
        /// <param name="min">最小值</param>
        /// <param name="max">最大值</param>
        /// <returns>限制后的值</returns>
        public static float Clamp(float value, float min, float max)
        {
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>
        /// 将值限制在0-1范围内
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>限制后的值</returns>
        public static float Clamp01(float value)
        {
            return Mathf.Clamp01(value);
        }

        /// <summary>
        /// 线性插值
        /// </summary>
        /// <param name="a">起始值</param>
        /// <param name="b">结束值</param>
        /// <param name="t">插值参数</param>
        /// <returns>插值结果</returns>
        public static float Lerp(float a, float b, float t)
        {
            return Mathf.Lerp(a, b, t);
        }

        /// <summary>
        /// 线性插值（无限制）
        /// </summary>
        /// <param name="a">起始值</param>
        /// <param name="b">结束值</param>
        /// <param name="t">插值参数</param>
        /// <returns>插值结果</returns>
        public static float LerpUnclamped(float a, float b, float t)
        {
            return Mathf.LerpUnclamped(a, b, t);
        }

        /// <summary>
        /// 平滑插值
        /// </summary>
        /// <param name="a">起始值</param>
        /// <param name="b">结束值</param>
        /// <param name="t">插值参数</param>
        /// <returns>插值结果</returns>
        public static float SmoothStep(float a, float b, float t)
        {
            return Mathf.SmoothStep(a, b, t);
        }

        /// <summary>
        /// 将值映射到新的范围
        /// </summary>
        /// <param name="value">原值</param>
        /// <param name="fromMin">原范围最小值</param>
        /// <param name="fromMax">原范围最大值</param>
        /// <param name="toMin">目标范围最小值</param>
        /// <param name="toMax">目标范围最大值</param>
        /// <returns>映射后的值</returns>
        public static float MapRange(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            return (value - fromMin) / (fromMax - fromMin) * (toMax - toMin) + toMin;
        }

        /// <summary>
        /// 检查值是否在范围内
        /// </summary>
        /// <param name="value">值</param>
        /// <param name="min">最小值</param>
        /// <param name="max">最大值</param>
        /// <returns>是否在范围内</returns>
        public static bool IsInRange(float value, float min, float max)
        {
            return value >= min && value <= max;
        }

        /// <summary>
        /// 计算两点之间的角度
        /// </summary>
        /// <param name="from">起始点</param>
        /// <param name="to">结束点</param>
        /// <returns>角度（度）</returns>
        public static float Angle(Vector2 from, Vector2 to)
        {
            return Vector2.Angle(from, to);
        }

        /// <summary>
        /// 计算两点之间的角度（3D）
        /// </summary>
        /// <param name="from">起始点</param>
        /// <param name="to">结束点</param>
        /// <returns>角度（度）</returns>
        public static float Angle(Vector3 from, Vector3 to)
        {
            return Vector3.Angle(from, to);
        }

        /// <summary>
        /// 将弧度转换为角度
        /// </summary>
        /// <param name="radians">弧度</param>
        /// <returns>角度</returns>
        public static float RadToDeg(float radians)
        {
            return radians * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 将角度转换为弧度
        /// </summary>
        /// <param name="degrees">角度</param>
        /// <returns>弧度</returns>
        public static float DegToRad(float degrees)
        {
            return degrees * Mathf.Deg2Rad;
        }

        /// <summary>
        /// 计算平方根
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>平方根</returns>
        public static float Sqrt(float value)
        {
            return Mathf.Sqrt(value);
        }

        /// <summary>
        /// 计算平方
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>平方</returns>
        public static float Pow(float value, float power)
        {
            return Mathf.Pow(value, power);
        }

        /// <summary>
        /// 计算绝对值
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>绝对值</returns>
        public static float Abs(float value)
        {
            return Mathf.Abs(value);
        }

        /// <summary>
        /// 计算最大值
        /// </summary>
        /// <param name="a">值1</param>
        /// <param name="b">值2</param>
        /// <returns>最大值</returns>
        public static float Max(float a, float b)
        {
            return Mathf.Max(a, b);
        }

        /// <summary>
        /// 计算最小值
        /// </summary>
        /// <param name="a">值1</param>
        /// <param name="b">值2</param>
        /// <returns>最小值</returns>
        public static float Min(float a, float b)
        {
            return Mathf.Min(a, b);
        }

        /// <summary>
        /// 四舍五入
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>四舍五入后的值</returns>
        public static float Round(float value)
        {
            return Mathf.Round(value);
        }

        /// <summary>
        /// 向上取整
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>向上取整后的值</returns>
        public static float Ceil(float value)
        {
            return Mathf.Ceil(value);
        }

        /// <summary>
        /// 向下取整
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>向下取整后的值</returns>
        public static float Floor(float value)
        {
            return Mathf.Floor(value);
        }
    }
}
