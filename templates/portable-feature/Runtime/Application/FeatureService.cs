using System;
using AwithGameFrame.FeatureTemplate.Domain;

namespace AwithGameFrame.FeatureTemplate.Application
{
    /// <summary>
    /// Owns state and runs effects after commit. The caller serializes access; this is not thread-safe.
    /// A sink may read Snapshot, but cannot reenter state modification.
    /// </summary>
    public sealed class FeatureService
    {
        private readonly IFeatureStateSink sink;
        private FeatureState state;
        private bool notifying;

        public FeatureService(FeatureState initialState, IFeatureStateSink sink)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            state = FeatureRules.Copy(initialState);
        }

        public FeatureState Snapshot => FeatureRules.Copy(state);

        /// <summary>
        /// Returns false without notification when unchanged. A sink failure does not roll back state
        /// or retry the effect; the caller observes the exception and decides how to recover.
        /// </summary>
        public bool SetEnabled(bool isEnabled)
        {
            if (notifying) throw new InvalidOperationException("A state sink cannot reenter feature modification.");

            var candidate = FeatureRules.SetEnabled(state, isEnabled);
            if (candidate.IsEnabled == state.IsEnabled) return false;

            var previous = state;
            state = candidate;
            notifying = true;
            try
            {
                sink.OnStateChanged(FeatureRules.Copy(previous), FeatureRules.Copy(candidate));
            }
            finally
            {
                notifying = false;
            }
            return true;
        }
    }
}
