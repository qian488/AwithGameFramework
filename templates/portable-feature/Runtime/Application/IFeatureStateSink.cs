using AwithGameFrame.FeatureTemplate.Domain;

namespace AwithGameFrame.FeatureTemplate.Application
{
    /// <summary>
    /// A synchronous application effect, such as publishing a host event or updating a device.
    /// Both arguments are independent snapshots. Exceptions reach the command caller after commit.
    /// </summary>
    public interface IFeatureStateSink
    {
        void OnStateChanged(FeatureState previous, FeatureState current);
    }
}
