using System;

namespace AwithGameFrame.FeatureTemplate.Domain
{
    /// <summary>Pure calculations: no services, events, files, clocks, or shared mutable state.</summary>
    public static class FeatureRules
    {
        public static FeatureState Copy(FeatureState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new FeatureState { IsEnabled = source.IsEnabled };
        }

        /// <summary>Returns a new candidate even when the requested value is unchanged.</summary>
        public static FeatureState SetEnabled(FeatureState current, bool isEnabled)
        {
            var candidate = Copy(current);
            candidate.IsEnabled = isEnabled;
            return candidate;
        }
    }
}
