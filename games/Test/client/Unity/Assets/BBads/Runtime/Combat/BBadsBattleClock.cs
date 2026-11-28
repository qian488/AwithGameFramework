using System;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Unity.Hosting;
using UnityEngine;

namespace BBads.Combat
{
    [DefaultExecutionOrder(-100)]
    public sealed class BBadsBattleClock : GameHostBehaviour
    {
        bool reportedPause;

        public readonly struct BattlePlaybackChanged
        {
            public BattlePlaybackChanged(bool paused) => IsPaused = paused;
            public bool IsPaused { get; }
        }

        public event Action<bool> PlaybackChanged;
        public bool IsPaused => Host?.State != GameHostState.Running || Host.Clock.TimeScale == 0;
        public float PlaybackSpeed => IsPaused ? 0f : (float)Host.Clock.TimeScale;
        public float Delta => IsPaused ? 0f : (float)Host.Clock.ScaledDeltaTime;
        public float UnscaledDelta => IsPaused ? 0f : (float)Host.Clock.UnscaledDeltaTime;
        public double Elapsed => Host?.Clock.ScaledTime ?? 0d;

        public void SetPlaybackSpeed(float speed)
        {
            Host.Clock.TimeScale = speed;
            ReportPauseChange();
        }

        protected override void Update()
        {
            base.Update();
            ReportPauseChange();
        }

        void ReportPauseChange()
        {
            if (Host == null || Host.Events.IsDisposed || reportedPause == IsPaused)
                return;
            reportedPause = IsPaused;
            var publication = Host.Events.Publish(new BattlePlaybackChanged(reportedPause));
            foreach (var error in publication.Errors)
                Debug.LogException(error, this);
            PlaybackChanged?.Invoke(reportedPause);
        }
    }
}
