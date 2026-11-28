using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;

namespace Test.Application.Combat.Pvp
{
    public readonly struct PvpPlayerSnapshot
    {
        internal PvpPlayerSnapshot(PvpPlayerState state)
        {
            PlayerId = state.PlayerId;
            Health = state.Vitality.Health;
            Posture = state.Vitality.Posture;
            HealthLimit = state.HealthLimit;
            PostureLimit = state.PostureLimit;
            Position = state.Position;
            Facing = state.Facing;
            Weapon = state.Weapon;
            ArrowsRemaining = state.ArrowsRemaining;
            Phase = state.Phase;
            PhaseProgress = state.PhaseProgress;
            Guarding = state.Guarding;
            Dodging = state.Dodging;
            Moving = state.Moving;
            Broken = state.Broken;
            LockedTarget = state.LockedTarget;
        }

        public int PlayerId { get; }
        public int Health { get; }
        public int Posture { get; }
        public int HealthLimit { get; }
        public int PostureLimit { get; }
        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public PvpWeapon Weapon { get; }
        public int ArrowsRemaining { get; }
        public AttackPhase Phase { get; }
        public float PhaseProgress { get; }
        public bool Guarding { get; }
        public bool Dodging { get; }
        public bool Moving { get; }
        public bool Broken { get; }
        public int LockedTarget { get; }
    }

    public readonly struct PvpArrowSnapshot
    {
        internal PvpArrowSnapshot(PvpArrowState state)
        {
            Owner = state.Owner;
            Position = state.Position;
            Velocity = state.Velocity;
            Age = state.Age;
        }

        public int Owner { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float Age { get; }
    }

    public readonly struct PvpCombatCue
    {
        internal PvpCombatCue(PvpCue cue)
        {
            Sequence = cue.Sequence;
            Tick = cue.Tick;
            Attacker = cue.Attacker;
            Target = cue.Target;
            Result = cue.Result;
            IsArrow = cue.IsArrow;
        }

        public int Sequence { get; }
        public int Tick { get; }
        public int Attacker { get; }
        public int Target { get; }
        public ContactResult Result { get; }
        public bool IsArrow { get; }
    }

    public sealed class PvpMatchResult
    {
        internal PvpMatchResult(Test.Domain.Combat.Pvp.PvpMatchResult result)
        {
            Tick = result.Tick;
            Winner = result.Winner;
            Loser = result.Loser;
        }

        public int Tick { get; }
        public int Winner { get; }
        public int Loser { get; }
    }

    public sealed class PvpAuthoritativeSnapshot
    {
        internal PvpAuthoritativeSnapshot(PvpState state)
        {
            Tick = state.Tick;
            Status = state.Status;
            PlayerOne = new PvpPlayerSnapshot(state.PlayerOne);
            PlayerTwo = new PvpPlayerSnapshot(state.PlayerTwo);
            var arrowSnapshots = new PvpArrowSnapshot[state.Arrows.Count];
            for (int index = 0; index < arrowSnapshots.Length; index++)
                arrowSnapshots[index] = new PvpArrowSnapshot(state.Arrows[index]);
            Arrows = Array.AsReadOnly(arrowSnapshots);
            Result = state.Result == null ? null : new PvpMatchResult(state.Result);
            StateHash = Hash(state);
        }

        public int Tick { get; }
        public PvpMatchStatus Status { get; }
        public PvpPlayerSnapshot PlayerOne { get; }
        public PvpPlayerSnapshot PlayerTwo { get; }
        public IReadOnlyList<PvpArrowSnapshot> Arrows { get; }
        public string StateHash { get; }
        public PvpMatchResult Result { get; }

        private static string Hash(PvpState state)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                writer.Write(state.Tick);
                writer.Write((int)state.Status);
                WritePlayer(writer, state.PlayerOne);
                WritePlayer(writer, state.PlayerTwo);
                writer.Write(state.Arrows.Count);
                for (int index = 0; index < state.Arrows.Count; index++)
                {
                    var arrow = state.Arrows[index];
                    writer.Write(arrow.Owner);
                    WriteVector(writer, arrow.Position);
                    WriteVector(writer, arrow.Velocity);
                    writer.Write(arrow.Age);
                }
                writer.Write(state.Result != null);
                if (state.Result != null)
                {
                    writer.Write(state.Result.Tick);
                    writer.Write(state.Result.Winner);
                    writer.Write(state.Result.Loser);
                }
                writer.Flush();
                buffer.Position = 0;
                using (var sha = SHA256.Create())
                    return ToHex(sha.ComputeHash(buffer));
            }
        }

        private static void WritePlayer(BinaryWriter writer, PvpPlayerState player)
        {
            writer.Write(player.PlayerId);
            writer.Write(player.Vitality.Health);
            writer.Write(player.Vitality.Posture);
            writer.Write(player.Vitality.BrokenUntil);
            writer.Write(player.HealthLimit);
            writer.Write(player.PostureLimit);
            WriteVector(writer, player.Position);
            WriteVector(writer, player.Facing);
            writer.Write((int)player.Weapon);
            writer.Write(player.ArrowsRemaining);
            writer.Write((int)player.Phase);
            writer.Write(player.PhaseProgress);
            writer.Write(player.Guarding);
            writer.Write(player.Dodging);
            writer.Write(player.Moving);
            writer.Write(player.Broken);
            writer.Write(player.LockedTarget);
            writer.Write(player.AttackStartTick);
            writer.Write(player.AttackId);
            writer.Write(player.Segment);
            writer.Write(player.GuardPressedTick);
            writer.Write(player.DodgeUntilTick);
            writer.Write(player.ActionUntilTick);
            writer.Write(player.FinishHoldTarget);
            writer.Write(player.FinishHoldStarted);
            writer.Write(player.ContactMask);
            writer.Write(player.LastContactTick);
            writer.Write(player.WasGuarding);
            writer.Write(player.ProjectileLaunched);
            WriteVector(writer, player.DodgeDirection);
            WriteVector(writer, player.Aim);
        }

        private static void WriteVector(BinaryWriter writer, Vector3 vector)
        {
            // Mono and .NET can produce different signed zeros for vector negation.
            writer.Write(vector.X == 0f ? 0f : vector.X);
            writer.Write(vector.Y == 0f ? 0f : vector.Y);
            writer.Write(vector.Z == 0f ? 0f : vector.Z);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++) builder.Append(bytes[index].ToString("x2"));
            return builder.ToString();
        }
    }

    public sealed class PvpStepResult
    {
        internal PvpStepResult(PvpAuthoritativeSnapshot snapshot, IReadOnlyList<PvpCombatCue> cues,
            PvpMatchResult result)
        {
            Snapshot = snapshot;
            Cues = cues;
            Result = result;
        }

        public PvpAuthoritativeSnapshot Snapshot { get; }
        public IReadOnlyList<PvpCombatCue> Cues { get; }
        public PvpMatchResult Result { get; }
    }

    public sealed class PvpCombatMatch
    {
        private readonly PvpCombatSimulation simulation;
        private PvpAuthoritativeSnapshot snapshot;

        public PvpCombatMatch(PvpCombatConfig config = null)
        {
            simulation = new PvpCombatSimulation(config);
            snapshot = new PvpAuthoritativeSnapshot(simulation.State);
        }

        public PvpAuthoritativeSnapshot Snapshot => snapshot;
        public PvpMatchResult Result => snapshot.Result;

        public PvpStepResult Advance(PvpInputFrame playerOne, PvpInputFrame playerTwo)
        {
            if (Result != null) return new PvpStepResult(snapshot, Array.Empty<PvpCombatCue>(), null);
            var step = simulation.Step(playerOne, playerTwo);
            snapshot = new PvpAuthoritativeSnapshot(step.State);
            var cues = new PvpCombatCue[step.Cues.Count];
            for (int index = 0; index < cues.Length; index++) cues[index] = new PvpCombatCue(step.Cues[index]);
            return new PvpStepResult(snapshot, Array.AsReadOnly(cues),
                step.Result == null ? null : new PvpMatchResult(step.Result));
        }
    }
}
