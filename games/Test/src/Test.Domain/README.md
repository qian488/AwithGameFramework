# Test.Domain

本游戏的纯领域规则，目标为 .NET Standard 2.1 / C# 9，仅使用 BCL。源码写入 `Runtime`。

`Runtime/Mathematics` 提供二维发射计算。`ILaunchFormula2D.Sample(fraction, speed)` 接收 0–1 的归一化序号和非负速度，返回由两个 `System.Numerics.Vector2` 组成的初始位置和初速度。`ProjectileFormula2D.LaunchVelocity(speed, angleDegrees)` 也可以单独调用，例如由另一个模块计算发射方向与速度。两个公式都通过构造参数配置。

同一游戏内的其他外部模块可以用项目引用访问 `Test.Domain.csproj`。只需要单项计算的模块可直接调用计算方法，需要替换采样策略的模块可接收 `ILaunchFormula2D`。跨游戏共享的公式等真实复用需求出现后，再按职责提取为独立库。

计算采用显式输入与输出，不修改调用方输入，不访问时间、文件、网络或全局服务。所需事实由应用层采集，计算结果由应用层提交。状态快照与集合的所有权由公开契约说明。
