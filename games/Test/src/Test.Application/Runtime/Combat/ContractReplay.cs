using System;
using System.Collections.Generic;
using System.Numerics;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public sealed class ContractReplay
    {
        public const int MaximumTicks = CombatRun.StepsPerSecond * 60 * 10 + 1;
        public string ContractId { get; set; }
        public string ContentVersion { get; set; }
        public string Fingerprint { get; set; }
        public string ConfigurationHash { get; set; }
        public DateTime RecordedAtUtc { get; set; }
        public List<ReplayFrame> Frames { get; set; } = new List<ReplayFrame>();
        public List<ReplayMarker> Markers { get; set; } = new List<ReplayMarker>();

        public int PreviousMarker(int tick, ContactResult result)
        {
            for (int index = Markers.Count - 1; index >= 0; index--)
                if (Markers[index].Result == result && Markers[index].Tick < tick) return Markers[index].Tick;
            return -1;
        }
    }

    public sealed class ReplayFrame
    {
        public int PoseTick { get; set; }
        public Encounter Encounter { get; set; }
        public RunStatus Status { get; set; }
        public PlayerWeapon Weapon { get; set; }
        public ReplayActor[] Actors { get; set; }
        public ReplayArrow[] Arrows { get; set; }
    }

    public sealed class ReplayActor
    {
        public Vector3 Position { get; set; }
        public Vector3 Facing { get; set; }
        public int Health { get; set; }
        public int Posture { get; set; }
        public int HealthLimit { get; set; }
        public int PostureLimit { get; set; }
        public AttackPhase Phase { get; set; }
        public float PhaseProgress { get; set; }
        public float AttackArc { get; set; }
        public int Segment { get; set; }
        public bool Guarding { get; set; }
        public bool Dodging { get; set; }
        public bool Moving { get; set; }
        public bool Broken { get; set; }

        public FighterView ToView() => new FighterView(new Vitality(Health, Posture), HealthLimit, PostureLimit,
            Facing, Phase, PhaseProgress, Guarding, Dodging, Moving, Segment, Broken, AttackArc);
    }

    public sealed class ReplayArrow
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
    }

    public sealed class ReplayMarker
    {
        public int Tick { get; set; }
        public ContactResult Result { get; set; }
        public int Attacker { get; set; }
        public int Target { get; set; }
    }

    public sealed class ContractReplayRecorder : IDisposable
    {
        private readonly CombatRun run;
        private readonly ICombatSpace space;
        private readonly ContractReplay replay;

        public ContractReplayRecorder(CombatRun run, ICombatSpace space, InstalledContract installed, string contractId)
            : this(run, space, contractId, installed.Snapshot.Manifest.Version,
                installed.Snapshot.Fingerprint, installed.ConfigurationHash)
        {
        }

        public ContractReplayRecorder(CombatRun run, ICombatSpace space, string contractId, string version,
            string fingerprint, string configurationHash)
        {
            this.run = run;
            this.space = space;
            replay = new ContractReplay
            {
                ContractId = contractId, ContentVersion = version,
                Fingerprint = fingerprint, ConfigurationHash = configurationHash
            };
            Capture();
            run.Cue += Mark;
            run.Stepped += Capture;
        }

        public ContractReplay Finish()
        {
            if (replay.Frames.Count == 0 || replay.Frames.Count > ContractReplay.MaximumTicks ||
                replay.Frames[replay.Frames.Count - 1].Status != RunStatus.Victory) return null;
            replay.RecordedAtUtc = DateTime.UtcNow;
            return replay;
        }

        public void Dispose()
        {
            run.Cue -= Mark;
            run.Stepped -= Capture;
        }

        private void Mark(CombatCue cue)
        {
            if (replay.Frames.Count >= ContractReplay.MaximumTicks) return;
            if (cue.Result == ContactResult.Deflect || cue.Result == ContactResult.Finish)
                replay.Markers.Add(new ReplayMarker
                {
                    Tick = replay.Frames.Count, Result = cue.Result, Attacker = cue.Attacker, Target = cue.Target
                });
        }

        private void Capture()
        {
            if (replay.Frames.Count >= ContractReplay.MaximumTicks) return;
            var actors = new ReplayActor[run.Count];
            for (int actor = 0; actor < actors.Length; actor++)
            {
                var view = run.View(actor);
                actors[actor] = new ReplayActor
                {
                    Position = space.Position(actor), Facing = view.Facing, Health = view.Vitality.Health,
                    Posture = view.Vitality.Posture, HealthLimit = view.HealthLimit, PostureLimit = view.PostureLimit,
                    Phase = view.Phase, PhaseProgress = view.PhaseProgress, AttackArc = view.AttackArc,
                    Segment = view.Segment, Guarding = view.Guarding, Dodging = view.Dodging, Moving = view.Moving, Broken = view.Broken
                };
            }
            var arrows = new ReplayArrow[run.AirborneArrows.Count];
            for (int index = 0; index < arrows.Length; index++)
            {
                var arrow = run.AirborneArrows[index];
                arrows[index] = new ReplayArrow { Position = arrow.Position, Velocity = arrow.Velocity };
            }
            replay.Frames.Add(new ReplayFrame
            {
                PoseTick = run.Tick, Encounter = run.Encounter, Status = run.Status, Weapon = run.Weapon,
                Actors = actors, Arrows = arrows
            });
        }
    }
}
