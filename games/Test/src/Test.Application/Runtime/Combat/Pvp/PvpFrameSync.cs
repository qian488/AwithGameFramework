using System;
using System.Numerics;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using DomainInput = Test.Domain.Combat.Pvp.PvpInputFrame;
using WirePlayer = Test.Application.Protocol.PvpPlayerSnapshot;

namespace Test.Application.Combat.Pvp
{
    public static class PvpFrameSync
    {
        public static DomainInput Decode(int tick, int slot, PvpFrameControls controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            var aim = new Vector3(controls.AimX / 1000f, 0, controls.AimY / 1000f);
            return new DomainInput(tick, slot, (PvpButtons)controls.Buttons,
                new Vector3(controls.MoveX / 1000f, 0, controls.MoveY / 1000f), aim);
        }

        public static WirePlayer Project(PvpSnapshotMessage header, PvpNetworkPlayerState state, string playerId)
        {
            return new WirePlayer
            {
                ProtocolVersion = header.ProtocolVersion, RoomId = header.RoomId, MatchId = header.MatchId,
                SessionEpoch = header.SessionEpoch, SnapshotSequence = header.SnapshotSequence,
                PlayerId = playerId, PlayerSlot = state.PlayerId, PositionX = state.Position.X, PositionY = state.Position.Z,
                Health = (int)Math.Ceiling(state.Health), Posture = state.Posture, Arrows = state.Arrows,
                ActionState = state.ActionState, Alive = state.Alive, FacingX = state.Facing.X, FacingY = state.Facing.Z,
                Weapon = state.Weapon, PhaseProgress = state.PhaseProgress, Guarding = state.Guarding,
                Dodging = state.Dodging, Moving = state.Moving, Broken = state.Broken, LockedTarget = state.LockedTarget
            };
        }
    }
}
