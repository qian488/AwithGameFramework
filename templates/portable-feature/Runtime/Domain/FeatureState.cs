namespace AwithGameFrame.FeatureTemplate.Domain
{
    /// <summary>A plain data snapshot. Ownership is copied at rule and service boundaries.</summary>
    public sealed class FeatureState
    {
        public bool IsEnabled { get; set; }
    }
}
