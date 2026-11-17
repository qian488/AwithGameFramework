using AwithGameFrame.Core.Hosting;

namespace Test.Application
{
    /// <summary>Game composition entry. Add game-specific application modules as requirements emerge.</summary>
    public sealed class TestModule : GameModule
    {
        public override string Id => "Test.application";
    }
}
