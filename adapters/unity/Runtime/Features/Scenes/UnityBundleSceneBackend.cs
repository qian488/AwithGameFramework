using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Unity.Hosting;

namespace AwithGameFrame.Unity.Scenes
{
    /// <summary>The scene scope pins its scene bundle and dependencies until native unloading completes.</summary>
    public sealed class UnityBundleSceneBackend : ISceneBackend
    {
        private readonly UnitySceneBackend native = new UnitySceneBackend();
        public async Task<ISceneInstance> LoadAsync(string key, ResourceScope resources, CancellationToken cancellationToken)
        {
            var scene = await resources.LoadAsync<UnityBundleScene>(key, cancellationToken);
            return await native.LoadAsync(scene.Value.Path, resources, cancellationToken);
        }
    }
}
