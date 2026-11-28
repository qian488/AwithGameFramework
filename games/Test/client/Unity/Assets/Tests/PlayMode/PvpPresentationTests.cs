using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Test.Application.Protocol;
using Test.Client.Combat;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WirePlayer = Test.Application.Protocol.PvpPlayerSnapshot;
using Numerics = System.Numerics;

namespace Test.Client.Tests
{
    public sealed class PvpPresentationTests
    {
        private CombatShowcase showcase;

        [UnitySetUp]
        public IEnumerator LoadSelection()
        {
            yield return SceneManager.LoadSceneAsync("CombatShowcase");
            showcase = Object.FindObjectOfType<CombatShowcase>();
            Assert.That(showcase, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 15;
            while (!showcase.IsReady && showcase.Failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(showcase.Failure, Is.Null);
            Assert.That(showcase.IsTrialSelection, Is.True);
        }

        [UnityTearDown]
        public IEnumerator UnloadSelection()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
        }

        [Test]
        public void EntryCreatesTwoWayfarersAndRestoresSelectionOnExit()
        {
            var originalActors = showcase.Space.Fighter(0).Root.parent;
            int originalTick = showcase.SimulationTick;
            Assert.That(showcase.OpenPvp(), Is.True);
            var pvp = showcase.ActivePvp;
            Assert.That(showcase.IsTrialSelection, Is.False);
            Assert.That(originalActors.gameObject.activeInHierarchy, Is.False);
            Assert.That(pvp.Fighter(0).Root.parent, Is.EqualTo(pvp.Fighter(1).Root.parent));
            for (int slot = 0; slot < 2; slot++)
            {
                var fighter = pvp.Fighter(slot);
                Assert.That(fighter.Controller.enabled, Is.False);
                Assert.That(fighter.Root.GetComponentsInChildren<MeshRenderer>().Length, Is.GreaterThan(10));
                Assert.That(Bow(fighter).activeSelf, Is.False);
            }

            showcase.ShowTrialSelection();
            Assert.That(showcase.ActivePvp, Is.Null);
            Assert.That(showcase.IsTrialSelection, Is.True);
            Assert.That(originalActors.gameObject.activeInHierarchy, Is.True);
            Assert.That(showcase.SimulationTick, Is.EqualTo(originalTick));
            Assert.That(showcase.OpenPvp(), Is.True);
            Assert.That(showcase.ActivePvp, Is.SameAs(pvp));
        }

        [Test]
        public void SnapshotUpdatesBothFightersAndArrowsAcrossSessionEpochs()
        {
            var pvp = OpenJoined();
            pvp.ReceiveSnapshot(Header(1, epoch: 33));
            var first = Player(0, 1, epoch: 33);
            first.PositionX = -3;
            first.FacingY = 1;
            first.Weapon = (int)PvpWeapon.Bow;
            first.ActionState = (int)AttackPhase.Windup;
            first.PhaseProgress = .85;
            pvp.ReceivePlayer(first);
            pvp.PoseFighters();
            Assert.That(pvp.Fighter(0).Root.localPosition.x, Is.EqualTo(-1.1f).Within(.001f));

            var peer = Player(1, 1, epoch: 33);
            peer.PositionX = 3;
            peer.Alive = false;
            peer.Health = 0;
            pvp.ReceivePlayer(peer);
            pvp.PoseFighters();
            Assert.That(pvp.Fighter(0).Root.localPosition.x, Is.EqualTo(-3));
            Assert.That(Vector3.Dot(pvp.Fighter(0).Root.forward, Vector3.forward), Is.GreaterThan(.99f));
            Assert.That(Bow(pvp.Fighter(0)).activeSelf, Is.True);
            Assert.That(pvp.Fighter(1).Root.localPosition.x, Is.EqualTo(3));
            var fallenSpine = pvp.Fighter(1).Root.Find("Articulated form/Spine");
            Assert.That(Quaternion.Angle(fallenSpine.localRotation, Quaternion.identity), Is.GreaterThan(80));

            pvp.ReceiveSnapshot(Header(2, epoch: 33, match: "old-match"));
            pvp.ReceivePlayer(Player(0, 2, epoch: 33));
            pvp.ReceivePlayer(Player(1, 2, epoch: 33));
            pvp.PoseFighters();
            Assert.That(pvp.Fighter(0).Root.localPosition.x, Is.EqualTo(-3));

            pvp.ReceiveArrows(new[] { new Numerics.Vector3(1, 2, 3) });
            var arrow = pvp.Fighter(0).Root.parent.Find("PvP flying arrow");
            Assert.That(arrow.gameObject.activeInHierarchy, Is.True);
            Assert.That(arrow.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
            pvp.ReceiveArrows(System.Array.Empty<Numerics.Vector3>());
            Assert.That(arrow.gameObject.activeSelf, Is.False);

            var final = Header(3, epoch: 33);
            final.RoomState = "Finished";
            final.Winner = 0;
            pvp.ReceiveSnapshot(final);
            pvp.ReceivePlayer(Player(0, 3, epoch: 33));
            pvp.ReceivePlayer(Player(1, 3, epoch: 33));
            Assert.That(pvp.IsFinished, Is.True);
        }

        [Test]
        public void ExistingRainPassBindingsDriveInputAndEscapeClearsIt()
        {
            var pvp = OpenJoined();
            pvp.ReceiveSnapshot(Header(1));
            pvp.ReceivePlayer(Player(0, 1));
            pvp.ReceivePlayer(Player(1, 1));
            var input = showcase.BattleInput;
            input.SetFocused(true).ThrowIfFailed();
            input.ProcessFrame(new Dictionary<string, float>
            {
                ["keyboard/w"] = 1,
                ["keyboard/space"] = 1,
                ["mouse/left"] = 1,
                ["mouse/right"] = 1,
                ["mouse/middle"] = 1
            }).ThrowIfFailed();
            var frame = pvp.SampleInput(true);
            Assert.That(frame.MoveY, Is.EqualTo(1000));
            Assert.That(frame.SimulationTick, Is.GreaterThan(60));
            Assert.That((PvpButtons)frame.Buttons,
                Is.EqualTo(PvpButtons.Dodge | PvpButtons.AttackPressed | PvpButtons.Guard | PvpButtons.Lock));

            input.ProcessFrame(new Dictionary<string, float> { ["keyboard/escape"] = 1 }).ThrowIfFailed();
            frame = pvp.SampleInput(true);
            Assert.That(frame.MoveY, Is.Zero);
            Assert.That(frame.Buttons, Is.Zero);
            Assert.That(showcase.ActivePvp, Is.SameAs(pvp));
        }

        [Test]
        public void BBadsUsesTheSameEntryAndRematchesUseADeterministicRoom()
        {
            Assert.That(showcase.OpenPvp(), Is.True);
            var pvp = showcase.ActivePvp;
            pvp.SelectMode(PvpClientShowcase.BBadsMode);
            Assert.That(pvp.Mode, Is.EqualTo("bbads"));
            Assert.That(pvp.RoomId, Is.EqualTo("bbads-test"));
            Assert.That(pvp.Fighter(0).Root.gameObject.activeInHierarchy, Is.False);
            pvp.SelectMode(PvpClientShowcase.RainPassMode);
            Assert.That(pvp.RoomId, Is.EqualTo("rain-pass-test"));
            Assert.That(pvp.Fighter(0).Root.gameObject.activeInHierarchy, Is.True);
            Assert.That(PvpClientShowcase.RoomForRound("rain-pass-test", 1), Is.EqualTo("rain-pass-test-r1"));
            Assert.That(PvpClientShowcase.RoomForRound(new string('x', 48), 2).Length, Is.EqualTo(48));
        }

        private PvpClientShowcase OpenJoined()
        {
            Assert.That(showcase.OpenPvp(), Is.True);
            var pvp = showcase.ActivePvp;
            Assert.That(pvp.AcceptJoin(new PvpJoinResponse
            {
                Accepted = true, RoomId = "room", MatchId = "match", SessionEpoch = 1, PlayerSlot = 0, RoomState = "Running"
            }), Is.True);
            return pvp;
        }

        private static PvpSnapshotMessage Header(long sequence, long epoch = 1, string match = "match") => new PvpSnapshotMessage
        {
            RoomId = "room", MatchId = match, SessionEpoch = epoch, SnapshotSequence = sequence,
            ServerTick = 60, PlayerCount = 2, RoomState = "Running", PeerConnected = true
        };

        private static WirePlayer Player(int slot, long sequence, long epoch = 1) => new WirePlayer
        {
            RoomId = "room", MatchId = "match", SessionEpoch = epoch, SnapshotSequence = sequence,
            PlayerId = slot == 0 ? "alpha" : "beta", PlayerSlot = slot, Alive = true, Health = 100
        };

        private static GameObject Bow(PassFighter fighter) =>
            fighter.Root.GetComponentsInChildren<Transform>(true).Single(part => part.name == "Longbow").gameObject;
    }
}
