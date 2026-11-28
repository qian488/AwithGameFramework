using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Test.Application.Combat.Pvp;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;

namespace Test.Application.Tests.Pvp
{
    public sealed class BBadsPvpNetworkMatchTests
    {
        [Test]
        public void SameInputsProduceTheSameBackpackMatchStateAndOneResult()
        {
            IPvpNetworkMatch first = new BBadsPvpNetworkMatch();
            IPvpNetworkMatch second = new BBadsPvpNetworkMatch();
            int lastCue = 0;

            for (int tick = 1; tick <= 300; tick++)
            {
                var one = tick == 1 ? Frame(tick, 0, PvpButtons.AttackPressed | PvpButtons.Finish) : Frame(tick, 0);
                var two = tick switch
                {
                    1 => Frame(tick, 1, PvpButtons.AttackPressed),
                    2 => Frame(tick, 1, PvpButtons.EquipBow | PvpButtons.AttackPressed | PvpButtons.Finish, x: 1),
                    _ => Frame(tick, 1)
                };
                var firstStep = first.Advance(one, two);
                var secondStep = second.Advance(one, two);
                Assert.That(secondStep.Snapshot.StateHash, Is.EqualTo(firstStep.Snapshot.StateHash));
                Assert.That(secondStep.Cues.Select(CueKey), Is.EqualTo(firstStep.Cues.Select(CueKey)));
                foreach (var cue in firstStep.Cues)
                {
                    Assert.That(cue.Sequence, Is.EqualTo(++lastCue));
                    Assert.That(cue.Tick, Is.EqualTo(tick));
                }
                if (firstStep.Snapshot.Result != null) break;
            }

            var terminal = first.Snapshot;
            Assert.That(terminal.Phase, Is.EqualTo(BBadsPvpPhase.Finished));
            Assert.That(terminal.Result, Is.Not.Null);
            Assert.That(terminal.Result.Winner, Is.EqualTo(1));
            Assert.That(terminal.Result.Loser, Is.EqualTo(0));
            Assert.That(terminal.PlayerOne.Health, Is.Zero);
            Assert.That(terminal.PlayerTwo.Health, Is.EqualTo(16));
            Assert.That(lastCue, Is.EqualTo(14));
            Assert.That(second.Snapshot.Result.Winner, Is.EqualTo(terminal.Result.Winner));
            Assert.That(second.Snapshot.Result.Tick, Is.EqualTo(terminal.Result.Tick));
            var repeat = first.Advance(Frame(terminal.Tick, 0), Frame(terminal.Tick, 1));
            Assert.That(repeat.Snapshot, Is.SameAs(terminal));
            Assert.That(repeat.Cues, Is.Empty);
        }

        [Test]
        public void BothPlayersPrepareIndependentlyAndCountdownConsumesExactlyItsFixedTicks()
        {
            var match = new BBadsPvpNetworkMatch();
            Assert.That(match.Snapshot.Phase, Is.EqualTo(BBadsPvpPhase.Preparing));
            var initial = match.Snapshot;
            var firstReady = match.Advance(Frame(1, 0, PvpButtons.AttackPressed | PvpButtons.Finish),
                Frame(1, 1, PvpButtons.EquipBow | PvpButtons.AttackPressed, x: 2, z: 1));
            Assert.That(firstReady.Snapshot.Phase, Is.EqualTo(BBadsPvpPhase.Preparing));
            Assert.That(firstReady.Snapshot.PlayerOne.Backpack.Ready, Is.True);
            Assert.That(firstReady.Snapshot.PlayerTwo.Backpack.Ready, Is.False);
            Assert.That(initial.PlayerOne.Backpack.Placements, Is.Empty);
            var fish = firstReady.Snapshot.PlayerTwo.Backpack.Placements.Single();
            Assert.That((fish.Item, fish.X, fish.Z), Is.EqualTo((BBadsPvpItem.Fish, 2, 1)));

            var countdown = match.Advance(Frame(2, 0, PvpButtons.EquipBow | PvpButtons.AttackPressed, x: 1),
                Frame(2, 1, PvpButtons.Finish));
            Assert.That(countdown.Snapshot.Phase, Is.EqualTo(BBadsPvpPhase.Countdown));
            Assert.That(countdown.Snapshot.CountdownTicksRemaining, Is.EqualTo(BBadsPvpNetworkMatch.CountdownTicks));
            Assert.That(countdown.Snapshot.PlayerOne.Backpack.SelectedItem, Is.EqualTo(BBadsPvpItem.Weapon));
            Assert.That(countdown.Snapshot.PlayerOne.Backpack.Placements, Has.Count.EqualTo(1));

            for (int elapsed = 1; elapsed <= BBadsPvpNetworkMatch.CountdownTicks; elapsed++)
            {
                int tick = elapsed + 2;
                var step = match.Advance(Frame(tick, 0, PvpButtons.Attack), Frame(tick, 1, PvpButtons.AttackPressed));
                Assert.That(step.Cues, Is.Empty);
                Assert.That(step.Snapshot.CountdownTicksRemaining, Is.EqualTo(BBadsPvpNetworkMatch.CountdownTicks - elapsed));
                Assert.That(step.Snapshot.Phase, Is.EqualTo(elapsed == BBadsPvpNetworkMatch.CountdownTicks
                    ? BBadsPvpPhase.Fighting : BBadsPvpPhase.Countdown));
            }
            var afterCountdown = match.Snapshot.PlayerTwo.Backpack.Placements.Single();
            Assert.That((afterCountdown.Item, afterCountdown.X, afterCountdown.Z), Is.EqualTo((BBadsPvpItem.Fish, 2, 1)));
            int firstAttackTick = match.Snapshot.Tick + 1;
            var attack = match.Advance(Frame(firstAttackTick, 0), Frame(firstAttackTick, 1, PvpButtons.Dodge));
            Assert.That(attack.Cues.Select(cue => cue.Result), Is.EqualTo(new[] { ContactResult.Hit, ContactResult.Hit }));
            Assert.That(attack.Snapshot.PlayerOne.Health, Is.EqualTo(94));
            Assert.That(attack.Snapshot.PlayerTwo.Health, Is.EqualTo(88));
        }

        [Test]
        public void PlacementUsesAbsoluteGridCellsAndRejectsOccupiedAndInvalidCells()
        {
            var match = new BBadsPvpNetworkMatch();
            match.Advance(Frame(1, 0, PvpButtons.AttackPressed, x: 2, z: 1), Frame(1, 1));
            match.Advance(Frame(2, 0, PvpButtons.EquipBow | PvpButtons.AttackPressed, x: 2, z: 1), Frame(2, 1));
            match.Advance(Frame(3, 0, PvpButtons.AttackPressed, x: 3), Frame(3, 1));
            match.Advance(Frame(4, 0, PvpButtons.AttackPressed, x: .5f), Frame(4, 1));
            match.Advance(Frame(5, 0, PvpButtons.AttackPressed, z: float.NaN), Frame(5, 1));
            var beforeMove = match.Snapshot;
            Assert.That(beforeMove.PlayerOne.Backpack.Placements, Has.Count.EqualTo(1));
            Assert.That(beforeMove.PlayerTwo.Backpack.Placements, Is.Empty);

            var step = match.Advance(Frame(6, 0, PvpButtons.EquipSword | PvpButtons.AttackPressed), Frame(6, 1));
            Assert.That(beforeMove.PlayerOne.Backpack.Placements.Single().X, Is.EqualTo(2));
            Assert.That(step.Snapshot.PlayerOne.Backpack.Placements.Single().X, Is.Zero);

            var other = new BBadsPvpNetworkMatch();
            for (int tick = 1; tick < 6; tick++) other.Advance(Frame(tick, 0), Frame(tick, 1));
            other.Advance(Frame(6, 0, PvpButtons.AttackPressed, x: 1), Frame(6, 1));
            Assert.That(other.Snapshot.StateHash, Is.Not.EqualTo(step.Snapshot.StateHash));
        }

        [Test]
        public void EqualLoadoutsResolveASharedLethalTickInSlotOrder()
        {
            var match = new BBadsPvpNetworkMatch();
            match.Advance(Frame(1, 0, PvpButtons.AttackPressed | PvpButtons.Finish),
                Frame(1, 1, PvpButtons.AttackPressed | PvpButtons.Finish));
            int lastTick = 2 + BBadsPvpNetworkMatch.CountdownTicks + 8 * 12;
            PvpNetworkStepResult step = null;
            for (int tick = 2; tick <= lastTick; tick++)
                step = match.Advance(Frame(tick, 0), Frame(tick, 1));

            Assert.That(step.Snapshot.Result.Winner, Is.Zero);
            Assert.That(step.Snapshot.Result.Tick, Is.EqualTo(lastTick));
            Assert.That(step.Snapshot.PlayerOne.Health, Is.EqualTo(4));
            Assert.That(step.Cues.Select(CueKey), Is.EqualTo(new[] { (17, lastTick, 0, 1, ContactResult.Finish) }));
        }

        [Test]
        public void FramesStillRequireBothSlotsAndRainPassKeepsItsSnapshotContract()
        {
            IPvpNetworkMatch match = new BBadsPvpNetworkMatch();
            Assert.Throws<ArgumentException>(() => match.Advance(Frame(1, 1), Frame(1, 0)));
            Assert.Throws<ArgumentException>(() => match.Advance(Frame(1, 0), Frame(2, 1)));
            Assert.That(match.Snapshot.Tick, Is.Zero);

            IPvpNetworkMatch rainPass = new RainPassPvpNetworkMatch();
            var snapshot = rainPass.Advance(Frame(1, 0), Frame(1, 1)).Snapshot;
            Assert.That(snapshot.Tick, Is.EqualTo(1));
            Assert.That(snapshot.StateHash, Has.Length.EqualTo(64));
            Assert.That(snapshot.Phase, Is.Null);
            Assert.That(snapshot.CountdownTicksRemaining, Is.Zero);
            Assert.That(snapshot.PlayerOne.Backpack, Is.Null);
            Assert.That(snapshot.PlayerTwo.Backpack, Is.Null);
        }

        private static PvpInputFrame Frame(int tick, int playerId, PvpButtons buttons = PvpButtons.None,
            float x = 0, float z = 0) =>
            new PvpInputFrame(tick, playerId, buttons, new Vector3(x, 0, z), Vector3.Zero);

        private static (int, int, int, int, ContactResult) CueKey(PvpNetworkCue cue) =>
            (cue.Sequence, cue.Tick, cue.Attacker, cue.Target, cue.Result);
    }
}
