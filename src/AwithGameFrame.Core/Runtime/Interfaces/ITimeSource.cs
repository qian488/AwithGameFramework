namespace AwithGameFrame.Core
{
    /// <summary>
    /// The host supplies its simulation clock and frame-rate controls.
    /// Calendar time and elapsed performance measurements do not depend on this interface.
    /// </summary>
    public interface ITimeSource
    {
        float GameTime { get; }
        float UnscaledGameTime { get; }
        float DeltaTime { get; }
        float UnscaledDeltaTime { get; }
        float FixedDeltaTime { get; }
        float TimeScale { get; set; }
        int TargetFrameRate { get; set; }
    }
}
