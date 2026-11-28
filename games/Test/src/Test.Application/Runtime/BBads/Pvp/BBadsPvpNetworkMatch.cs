using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Test.Domain.BBads;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;

namespace Test.Application.Combat.Pvp
{
    public enum BBadsPvpPhase { Preparing, Countdown, Fighting, Finished }
    public enum BBadsPvpItem { Weapon, Fish }

    public sealed class BBadsPvpPlacement
    {
        internal BBadsPvpPlacement(BBadsPvpItem item, GridCell cell)
        {
            Item = item;
            X = cell.X;
            Z = cell.Y;
        }

        public BBadsPvpItem Item { get; }
        public int X { get; }
        public int Z { get; }
    }

    public sealed class BBadsPvpBackpackSnapshot
    {
        internal BBadsPvpBackpackSnapshot(int columns, int rows, bool ready, BBadsPvpItem selectedItem,
            IReadOnlyList<BBadsPvpPlacement> placements)
        {
            Columns = columns;
            Rows = rows;
            Ready = ready;
            SelectedItem = selectedItem;
            Placements = placements;
        }

        public int Columns { get; }
        public int Rows { get; }
        public bool Ready { get; }
        public BBadsPvpItem SelectedItem { get; }
        public IReadOnlyList<BBadsPvpPlacement> Placements { get; }
    }

    public sealed class BBadsPvpNetworkMatch : IPvpNetworkMatch
    {
        public const int CountdownTicks = 3 * CombatRun.StepsPerSecond;
        private const int AttackInterval = 12;
        private const int BaseDamage = 6;
        private const int WeaponBonus = 6;
        private const int AdjacentFishBonus = 4;
        private static readonly BackpackItemDefinition Weapon = new BackpackItemDefinition("starter-weapon");
        private static readonly BackpackItemDefinition Fish = new BackpackItemDefinition("starter-fish");

        private readonly Fighter[] fighters = { new Fighter(-1.1f), new Fighter(1.1f) };
        private int tick;
        private int countdown;
        private int ticksUntilAttack;
        private int cueSequence;
        private BBadsPvpPhase phase;
        private PvpNetworkMatchResult result;

        public BBadsPvpNetworkMatch()
        {
            Snapshot = Capture();
        }

        public PvpNetworkSnapshot Snapshot { get; private set; }

        public PvpNetworkStepResult Advance(PvpInputFrame playerOne, PvpInputFrame playerTwo)
        {
            if (result != null) return new PvpNetworkStepResult(Snapshot, Array.Empty<PvpNetworkCue>());
            if (playerOne.Tick != tick + 1 || playerTwo.Tick != tick + 1 ||
                playerOne.PlayerId != 0 || playerTwo.PlayerId != 1)
                throw new ArgumentException("BBads PvP input frames must contain both players for the next tick.");

            tick++;
            IReadOnlyList<PvpNetworkCue> cues = Array.Empty<PvpNetworkCue>();
            switch (phase)
            {
                case BBadsPvpPhase.Preparing:
                    Prepare(fighters[0], playerOne);
                    Prepare(fighters[1], playerTwo);
                    if (fighters[0].Ready && fighters[1].Ready)
                    {
                        phase = BBadsPvpPhase.Countdown;
                        countdown = CountdownTicks;
                    }
                    break;
                case BBadsPvpPhase.Countdown:
                    if (--countdown == 0)
                    {
                        phase = BBadsPvpPhase.Fighting;
                        ticksUntilAttack = 1;
                        foreach (var fighter in fighters) fighter.Damage = CalculateDamage(fighter);
                    }
                    break;
                case BBadsPvpPhase.Fighting:
                    if (--ticksUntilAttack == 0) cues = AttackRound();
                    break;
            }

            Snapshot = Capture();
            return new PvpNetworkStepResult(Snapshot, cues);
        }

        private static void Prepare(Fighter fighter, PvpInputFrame input)
        {
            if (fighter.Ready) return;
            if ((input.Buttons & PvpButtons.EquipSword) != 0) fighter.Selected = BBadsPvpItem.Weapon;
            else if ((input.Buttons & PvpButtons.EquipBow) != 0) fighter.Selected = BBadsPvpItem.Fish;

            if ((input.Buttons & PvpButtons.AttackPressed) != 0)
                Place(fighter, input.Move);
            fighter.Ready = (input.Buttons & PvpButtons.Finish) != 0;
        }

        private static void Place(Fighter fighter, Vector3 move)
        {
            // Wire moveX/moveY are thousandths of a grid cell on the X/Z plane.
            if (!IsGridCoordinate(move.X, fighter.Grid.Columns) || !IsGridCoordinate(move.Z, fighter.Grid.Rows))
                return;

            var cell = new GridCell((int)move.X, (int)move.Z);
            int index = (int)fighter.Selected;
            var placement = fighter.Items[index];
            if (placement != null)
                fighter.Grid.TryMove(placement.InstanceId, cell);
            else if (fighter.Grid.TryPlaceNew(fighter.Selected == BBadsPvpItem.Weapon ? Weapon : Fish, cell, out placement))
                fighter.Items[index] = placement;
        }

        private static bool IsGridCoordinate(float value, int size) =>
            value >= 0 && value < size && value == MathF.Truncate(value);

        private static int CalculateDamage(Fighter fighter)
        {
            var weapon = fighter.Items[(int)BBadsPvpItem.Weapon];
            if (weapon == null) return BaseDamage;
            var fish = fighter.Items[(int)BBadsPvpItem.Fish];
            foreach (var adjacent in fighter.Grid.GetAdjacentItems(weapon))
                if (adjacent == fish) return BaseDamage + WeaponBonus + AdjacentFishBonus;
            return BaseDamage + WeaponBonus;
        }

        private IReadOnlyList<PvpNetworkCue> AttackRound()
        {
            ticksUntilAttack = AttackInterval;
            var first = Attack(0);
            // Slot 0 has initiative on shared attack ticks, including a lethal tie.
            if (result != null) return Array.AsReadOnly(new[] { first });
            return Array.AsReadOnly(new[] { first, Attack(1) });
        }

        private PvpNetworkCue Attack(int attacker)
        {
            int target = 1 - attacker;
            var damage = BBadsCombatRules.ApplyDamage(fighters[target].Health, fighters[attacker].Damage);
            fighters[target].Health = damage.RemainingHealth;
            if (damage.Defeated)
            {
                result = new PvpNetworkMatchResult(tick, attacker, target);
                phase = BBadsPvpPhase.Finished;
                ticksUntilAttack = 0;
            }
            return new PvpNetworkCue(++cueSequence, tick, attacker, target,
                damage.Defeated ? ContactResult.Finish : ContactResult.Hit);
        }

        private PvpNetworkSnapshot Capture()
        {
            var first = CapturePlayer(0);
            var second = CapturePlayer(1);
            return new PvpNetworkSnapshot(tick, HashState(first, second), first, second, result, phase, countdown);
        }

        private PvpNetworkPlayerState CapturePlayer(int slot)
        {
            var fighter = fighters[slot];
            var placements = new List<BBadsPvpPlacement>(fighter.Items.Length);
            for (int index = 0; index < fighter.Items.Length; index++)
            {
                var item = fighter.Items[index];
                if (item != null) placements.Add(new BBadsPvpPlacement((BBadsPvpItem)index, item.Anchor));
            }
            var backpack = new BBadsPvpBackpackSnapshot(fighter.Grid.Columns, fighter.Grid.Rows,
                fighter.Ready, fighter.Selected, placements.AsReadOnly());
            return new PvpNetworkPlayerState(slot, fighter.Health, 0, 0, ticksUntilAttack > 1 ? 1 : 0,
                fighter.Position, fighter.Health > 0, backpack);
        }

        private string HashState(PvpNetworkPlayerState first, PvpNetworkPlayerState second)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(tick);
            writer.Write((int)phase);
            writer.Write(countdown);
            writer.Write(ticksUntilAttack);
            writer.Write(cueSequence);
            WritePlayer(writer, first, fighters[0].Damage);
            WritePlayer(writer, second, fighters[1].Damage);
            writer.Write(result?.Winner ?? -1);
            writer.Flush();
            stream.Position = 0;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WritePlayer(BinaryWriter writer, PvpNetworkPlayerState player, int damage)
        {
            var backpack = player.Backpack;
            writer.Write(player.Health);
            writer.Write(damage);
            writer.Write(backpack.Columns);
            writer.Write(backpack.Rows);
            writer.Write(backpack.Ready);
            writer.Write((int)backpack.SelectedItem);
            writer.Write(backpack.Placements.Count);
            foreach (var placement in backpack.Placements)
            {
                writer.Write((int)placement.Item);
                writer.Write(placement.X);
                writer.Write(placement.Z);
            }
        }

        private sealed class Fighter
        {
            public Fighter(float x) => Position = new Vector3(x, 0, 0);

            public readonly BackpackGrid Grid = new BackpackGrid(3, 2);
            public readonly BackpackPlacement[] Items = new BackpackPlacement[2];
            public readonly Vector3 Position;
            public float Health = 100;
            public int Damage;
            public bool Ready;
            public BBadsPvpItem Selected;
        }
    }
}
