using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Test.Application.Combat.Pvp;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;

namespace Test.Application.Tests.Pvp
{
    public sealed class CombatDualControlTests
    {
        [Test]
        public void TwoRunsWithTheSameFramesHaveTheSameHashesAndCueOrder()
        {
            var first = new PvpCombatMatch();
            var second = new PvpCombatMatch();
            var firstCues = new List<PvpCombatCue>();
            var secondCues = new List<PvpCombatCue>();

            for (int tick = 1; tick <= 90; tick++)
            {
                PvpButtons playerOne = PvpButtons.None;
                PvpButtons playerTwo = PvpButtons.None;
                if (tick == 1) playerOne = PvpButtons.Attack;
                if (tick >= 5 && tick <= 20) playerTwo |= PvpButtons.Guard;
                if (tick == 35) playerOne = PvpButtons.EquipBow;
                if (tick == 36) playerOne = PvpButtons.Attack;
                if (tick == 67) playerTwo = PvpButtons.Dodge;

                var firstStep = first.Advance(Frame(tick, 0, playerOne, Vector3.UnitX),
                    Frame(tick, 1, playerTwo, -Vector3.UnitX));
                var secondStep = second.Advance(Frame(tick, 0, playerOne, Vector3.UnitX),
                    Frame(tick, 1, playerTwo, -Vector3.UnitX));
                Assert.That(secondStep.Snapshot.StateHash, Is.EqualTo(firstStep.Snapshot.StateHash));
                Assert.That(secondStep.Cues.Select(CueKey), Is.EqualTo(firstStep.Cues.Select(CueKey)));
                firstCues.AddRange(firstStep.Cues);
                secondCues.AddRange(secondStep.Cues);
            }

            Assert.That(second.Snapshot.StateHash, Is.EqualTo(first.Snapshot.StateHash));
            Assert.That(secondCues.Select(CueKey), Is.EqualTo(firstCues.Select(CueKey)));
            Assert.That(first.Snapshot.StateHash, Has.Length.EqualTo(64));
        }

        [Test]
        public void GuardAndDeflectUseTheExistingCombatRulesWindows()
        {
            var deflect = new PvpCombatMatch();
            PvpStepResult deflectStep = null;
            for (int tick = 1; tick <= 12; tick++)
            {
                var step = deflect.Advance(Frame(tick, 0, tick == 1 ? PvpButtons.Attack : PvpButtons.None, Vector3.UnitX),
                    Frame(tick, 1, tick >= 5 ? PvpButtons.Guard : PvpButtons.None, -Vector3.UnitX));
                if (step.Cues.Count > 0) deflectStep = step;
            }

            Assert.That(deflectStep, Is.Not.Null);
            Assert.That(deflectStep.Cues.Single().Result, Is.EqualTo(ContactResult.Deflect));
            Assert.That(deflectStep.Snapshot.PlayerOne.Health, Is.EqualTo(100));
            Assert.That(deflectStep.Snapshot.PlayerTwo.Health, Is.EqualTo(100));

            var guard = new PvpCombatMatch();
            PvpStepResult guardStep = null;
            for (int tick = 1; tick <= 12; tick++)
            {
                var step = guard.Advance(Frame(tick, 0, tick == 1 ? PvpButtons.Attack : PvpButtons.None, Vector3.UnitX),
                    Frame(tick, 1, PvpButtons.Guard, -Vector3.UnitX));
                if (step.Cues.Count > 0) guardStep = step;
            }

            Assert.That(guardStep.Cues.Single().Result, Is.EqualTo(ContactResult.Guard));
        }

        [Test]
        public void DodgeAvoidsAContactAndBowProducesAnArrowCue()
        {
            var dodge = new PvpCombatMatch();
            PvpStepResult dodgeStep = null;
            for (int tick = 1; tick <= 12; tick++)
            {
                var step = dodge.Advance(Frame(tick, 0, tick == 1 ? PvpButtons.Attack : PvpButtons.None, Vector3.UnitX),
                    Frame(tick, 1, tick == 5 ? PvpButtons.Dodge : PvpButtons.None, -Vector3.UnitX,
                        tick == 5 ? -Vector3.UnitX : Vector3.Zero));
                if (step.Cues.Count > 0) dodgeStep = step;
            }

            Assert.That(dodgeStep.Cues.Single().Result, Is.EqualTo(ContactResult.Dodge));
            Assert.That(dodgeStep.Snapshot.PlayerTwo.Health, Is.EqualTo(100));

            var bow = new PvpCombatMatch();
            PvpStepResult arrowStep = null;
            for (int tick = 1; tick <= 40; tick++)
            {
                PvpButtons buttons = tick == 1 ? PvpButtons.EquipBow : tick == 2 ? PvpButtons.Attack : PvpButtons.None;
                var step = bow.Advance(Frame(tick, 0, buttons, Vector3.UnitX),
                    Frame(tick, 1, PvpButtons.None, -Vector3.UnitX));
                if (step.Cues.Any(cue => cue.IsArrow)) arrowStep = step;
            }

            Assert.That(arrowStep, Is.Not.Null);
            Assert.That(arrowStep.Cues.Single(cue => cue.IsArrow).Result, Is.EqualTo(ContactResult.Hit));
            Assert.That(arrowStep.Snapshot.PlayerOne.ArrowsRemaining, Is.EqualTo(5));
            Assert.That(arrowStep.Snapshot.PlayerTwo.Health, Is.EqualTo(82));
        }

        [Test]
        public void FinishEndsTheMatchAndRepeatedSettlementDoesNotEmitAnotherResult()
        {
            var match = new PvpCombatMatch(new PvpCombatConfig(100, 15, 6,
                new Vector3(-1.1f, 0, 0), new Vector3(1.1f, 0, 0)));
            PvpStepResult completed = null;
            for (int tick = 1; tick <= 55; tick++)
            {
                PvpButtons buttons = tick == 1 || tick >= 30 ? PvpButtons.Attack : PvpButtons.None;
                if (tick >= 30) buttons = PvpButtons.Finish;
                var step = match.Advance(Frame(tick, 0, buttons, Vector3.UnitX),
                    Frame(tick, 1, PvpButtons.None, -Vector3.UnitX));
                if (step.Result != null) completed = step;
            }

            Assert.That(completed, Is.Not.Null);
            Assert.That(completed.Result.Winner, Is.EqualTo(0));
            Assert.That(completed.Result.Loser, Is.EqualTo(1));
            Assert.That(completed.Snapshot.Status, Is.EqualTo(PvpMatchStatus.Victory));
            Assert.That(completed.Cues.Any(cue => cue.Result == ContactResult.Finish), Is.True);

            var repeated = match.Advance(Frame(56, 0, PvpButtons.None, Vector3.UnitX),
                Frame(56, 1, PvpButtons.None, -Vector3.UnitX));
            Assert.That(repeated.Result, Is.Null);
            Assert.That(repeated.Cues, Is.Empty);
            Assert.That(repeated.Snapshot.Tick, Is.EqualTo(completed.Snapshot.Tick));
            Assert.That(match.Result.Tick, Is.EqualTo(completed.Result.Tick));
        }

        [Test]
        public void MovementStaysInsideTheCourtyard()
        {
            var match = new PvpCombatMatch();
            for (int tick = 1; tick <= 300; tick++)
                match.Advance(new PvpInputFrame(tick, 0, PvpButtons.None, new Vector3(-1, 0, 1), Vector3.Zero),
                    PvpInputFrame.Neutral(tick, 1));
            Assert.That(match.Snapshot.PlayerOne.Position, Is.EqualTo(new Vector3(-8.5f, 0, 10.5f)));
        }

        [Test]
        public void InputFramesMustBeTheTwoPlayersForTheNextFixedTick()
        {
            var match = new PvpCombatMatch();
            Assert.Throws<System.ArgumentException>(() => match.Advance(
                Frame(2, 0, PvpButtons.None, Vector3.UnitX), Frame(2, 1, PvpButtons.None, -Vector3.UnitX)));
            Assert.Throws<System.ArgumentException>(() => match.Advance(
                Frame(1, 1, PvpButtons.None, Vector3.UnitX), Frame(1, 0, PvpButtons.None, -Vector3.UnitX)));
        }

        private static PvpInputFrame Frame(int tick, int playerId, PvpButtons buttons, Vector3 aim) =>
            new PvpInputFrame(tick, playerId, buttons, Vector3.Zero, aim);

        private static PvpInputFrame Frame(int tick, int playerId, PvpButtons buttons, Vector3 aim, Vector3 move) =>
            new PvpInputFrame(tick, playerId, buttons, move, aim);

        private static string CueKey(PvpCombatCue cue) =>
            $"{cue.Sequence}:{cue.Tick}:{cue.Attacker}:{cue.Target}:{(int)cue.Result}:{cue.IsArrow}";
    }
}
