using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Unity.Hosting;
using Test.Application;

namespace Test.Client
{
    public sealed class TestBootstrap : GameHostBehaviour
    {
        protected override void ConfigureHost(GameHost host)
        {
            host.AddModule(new TestModule());
        }
    }
}
