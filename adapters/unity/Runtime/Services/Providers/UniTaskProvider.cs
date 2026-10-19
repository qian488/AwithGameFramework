using System;
using AwithGameFrame.Core.Providers;

namespace AwithGameFrame.Foundation.Providers
{
    /// <summary>旧名称兼容入口；标准 Task 实现在 Core 中。</summary>
    [Obsolete("Use AwithGameFrame.Core.Providers.TaskAsyncProvider.")]
    public class UniTaskProvider : TaskAsyncProvider { }
}
