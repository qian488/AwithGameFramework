using System;
using System.Collections.Generic;
using System.Numerics;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;

namespace Test.Application.Combat.Pvp
{
    public sealed class PvpNetworkPlayerState
    {
        internal PvpNetworkPlayerState(int playerId, float health, int posture, int arrows, int actionState,
            Vector3 position, bool alive, BBadsPvpBackpackSnapshot backpack = null)
        {
            PlayerId = playerId;
            Health = health;
            Posture = posture;
            Arrows = arrows;
            ActionState = actionState;
            Position = position;
            Alive = alive;
            Backpack = backpack;
        }

        public int PlayerId { get; }
        public float Health { get; }
        public int Posture { get; }
        public int Arrows { get; }
        public int ActionState { get; }
        public Vector3 Position { get; }
        public bool Alive { get; }
        public BBadsPvpBackpackSnapshot Backpack { get; }
        public Vector3 Facing { get; internal set; }
        public int Weapon { get; internal set; }
        public float PhaseProgress { get; internal set; }
        public bool Guarding { get; internal set; }
        public bool Dodging { get; internal set; }
        public bool Moving { get; internal set; }
        public bool Broken { get; internal set; }
        public int LockedTarget { get; internal set; } = -1;
    }

    public sealed class PvpNetworkMatchResult
    {
        internal PvpNetworkMatchResult(int tick, int winner, int loser)
        {
            Tick = tick;
            Winner = winner;
            Loser = loser;
        }

        public int Tick { get; }
        public int Winner { get; }
        public int Loser { get; }
    }

    public sealed class PvpNetworkSnapshot
    {
        internal PvpNetworkSnapshot(int tick, string stateHash, PvpNetworkPlayerState playerOne,
            PvpNetworkPlayerState playerTwo, PvpNetworkMatchResult result,
            BBadsPvpPhase? phase = null, int countdownTicksRemaining = 0)
        {
            Tick = tick;
            StateHash = stateHash;
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            Result = result;
            Phase = phase;
            CountdownTicksRemaining = countdownTicksRemaining;
        }

        public int Tick { get; }
        public string StateHash { get; }
        public PvpNetworkPlayerState PlayerOne { get; }
        public PvpNetworkPlayerState PlayerTwo { get; }
        public PvpNetworkMatchResult Result { get; }
        public BBadsPvpPhase? Phase { get; }
        public int CountdownTicksRemaining { get; }
        public IReadOnlyList<PvpArrowSnapshot> Arrows { get; internal set; } = Array.Empty<PvpArrowSnapshot>();
    }

    public sealed class PvpNetworkCue
    {
        internal PvpNetworkCue(int sequence, int tick, int attacker, int target, ContactResult result)
        {
            Sequence = sequence;
            Tick = tick;
            Attacker = attacker;
            Target = target;
            Result = result;
        }

        public int Sequence { get; }
        public int Tick { get; }
        public int Attacker { get; }
        public int Target { get; }
        public ContactResult Result { get; }
        public bool IsArrow { get; internal set; }
    }

    public sealed class PvpNetworkStepResult
    {
        internal PvpNetworkStepResult(PvpNetworkSnapshot snapshot, IReadOnlyList<PvpNetworkCue> cues)
        {
            Snapshot = snapshot;
            Cues = cues;
        }

        public PvpNetworkSnapshot Snapshot { get; }
        public IReadOnlyList<PvpNetworkCue> Cues { get; }
    }

    public interface IPvpNetworkMatch
    {
        PvpNetworkSnapshot Snapshot { get; }
        PvpNetworkStepResult Advance(PvpInputFrame playerOne, PvpInputFrame playerTwo);
    }

    public sealed class RainPassPvpNetworkMatch : IPvpNetworkMatch
    {
        private readonly PvpCombatMatch match = new PvpCombatMatch();
        private PvpNetworkSnapshot snapshot;

        public RainPassPvpNetworkMatch()
        {
            snapshot = Convert(match.Snapshot);
        }

        public PvpNetworkSnapshot Snapshot => snapshot;

        public PvpNetworkStepResult Advance(PvpInputFrame playerOne, PvpInputFrame playerTwo)
        {
            var step = match.Advance(playerOne, playerTwo);
            snapshot = Convert(step.Snapshot);
            var cues = new PvpNetworkCue[step.Cues.Count];
            for (int index = 0; index < cues.Length; index++)
            {
                var cue = step.Cues[index];
                cues[index] = new PvpNetworkCue(cue.Sequence, cue.Tick, cue.Attacker, cue.Target, cue.Result) { IsArrow = cue.IsArrow };
            }
            return new PvpNetworkStepResult(snapshot, Array.AsReadOnly(cues));
        }

        private static PvpNetworkSnapshot Convert(PvpAuthoritativeSnapshot value)
        {
            return new PvpNetworkSnapshot(value.Tick, value.StateHash,
                Convert(value.PlayerOne), Convert(value.PlayerTwo),
                value.Result == null ? null : new PvpNetworkMatchResult(value.Result.Tick, value.Result.Winner, value.Result.Loser))
                { Arrows = value.Arrows };
        }

        private static PvpNetworkPlayerState Convert(PvpPlayerSnapshot value) =>
            new PvpNetworkPlayerState(value.PlayerId, value.Health, value.Posture, value.ArrowsRemaining,
                (int)value.Phase, value.Position, value.Health > 0)
            {
                Facing = value.Facing, Weapon = (int)value.Weapon, PhaseProgress = value.PhaseProgress,
                Guarding = value.Guarding, Dodging = value.Dodging, Moving = value.Moving, Broken = value.Broken,
                LockedTarget = value.LockedTarget
            };
    }
}
