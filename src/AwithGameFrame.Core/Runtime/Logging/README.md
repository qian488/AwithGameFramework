# 日志输出

`FrameworkLogger` 将运行时诊断发送到 `LoggingHost.Output`。默认写入标准输出或标准错误，Unity 适配安装自己的输出回调。回调按调用线程同步执行。实例宿主的生命周期事件可通过 Data 的 `HostEventJournal` 记录。 [Data](../../../AwithGameFrame.Data/README.md)
