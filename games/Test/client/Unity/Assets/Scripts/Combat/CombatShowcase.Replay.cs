using Test.Application.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    public sealed partial class CombatShowcase
    {
        private ContractReplayRecorder recording;
        private ContractReplay playingReplay;
        private Encounter? displayedEncounter;
        private int displayedActorCount;

        internal void RecordReplay(InstalledContract installed, string contractId) =>
            recording = new ContractReplayRecorder(run, space, installed, contractId);

        internal ContractReplay FinishReplay()
        {
            var replay = recording?.Finish();
            recording?.Dispose();
            recording = null;
            return replay;
        }

        internal void BeginReplay(ContractReplay replay)
        {
            playingReplay = replay;
            audio.Sleep();
            LockCursor(false);
            ShowReplayFrame(0);
        }

        internal void ShowReplayFrame(int index)
        {
            var frame = playingReplay.Frames[index];
            var encounter = frame.Encounter == Encounter.Cleared ? Encounter.Gate : frame.Encounter;
            if (displayedEncounter != encounter || displayedActorCount != frame.Actors.Length)
            {
                space.Reset(encounter, frame.Actors.Length);
                displayedEncounter = encounter;
                displayedActorCount = frame.Actors.Length;
            }
            for (int indexActor = 0; indexActor < frame.Actors.Length; indexActor++)
            {
                var actor = frame.Actors[indexActor];
                var fighter = space.Fighter(indexActor);
                fighter.Controller.enabled = false;
                fighter.Root.localPosition = new Vector3(actor.Position.X, actor.Position.Y, actor.Position.Z);
                fighter.Pose(actor.ToView(), frame.PoseTick / (float)CombatRun.StepsPerSecond, 1,
                    indexActor == 0 && frame.Weapon == PlayerWeapon.Bow);
            }
            space.Gates(frame.Status == RunStatus.Proceed || encounter == Encounter.Gate, 1);
            space.DrawReplayArrows(frame.Arrows);
            var player = frame.Actors[0];
            yaw = Mathf.Atan2(player.Facing.X, player.Facing.Z) * Mathf.Rad2Deg;
            CameraPosition(0);
        }
    }
}
