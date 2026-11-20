using System;
using System.Collections.Generic;
using System.Numerics;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public enum Encounter { Practice, Courtyard, Gate, Cleared }
    public enum RunStatus { Fighting, Proceed, Paused, Defeated, Victory }

    [Flags]
    public enum CombatButtons { None = 0, Attack = 1, Guard = 2, Dodge = 4, Lock = 8, Finish = 16 }

    public interface ICombatSpace
    {
        void Reset(Encounter encounter, int actorCount);
        Vector3 Position(int actor);
        void Move(int actor, Vector3 displacement);
        void Sweep(int actor, Vector3 facing, Strike strike, List<int> contacts);
    }

    public readonly struct FighterView
    {
        internal FighterView(Vitality vitality, int healthLimit, int postureLimit, Vector3 facing, AttackPhase phase,
            float phaseProgress, bool guarding, bool dodging, bool moving, int segment, bool broken)
        {
            Vitality = vitality;
            HealthLimit = healthLimit;
            PostureLimit = postureLimit;
            Facing = facing;
            Phase = phase;
            PhaseProgress = phaseProgress;
            Guarding = guarding;
            Dodging = dodging;
            Moving = moving;
            Segment = segment;
            Broken = broken;
        }

        public Vitality Vitality { get; }
        public int HealthLimit { get; }
        public int PostureLimit { get; }
        public Vector3 Facing { get; }
        public AttackPhase Phase { get; }
        public float PhaseProgress { get; }
        public bool Guarding { get; }
        public bool Dodging { get; }
        public bool Moving { get; }
        public int Segment { get; }
        public bool Broken { get; }
    }

    public readonly struct CombatCue
    {
        public CombatCue(int attacker, int target, ContactResult result)
        {
            Attacker = attacker;
            Target = target;
            Result = result;
        }

        public int Attacker { get; }
        public int Target { get; }
        public ContactResult Result { get; }
    }

    public sealed class CombatRun
    {
        public const int StepsPerSecond = 60;
        private const int BufferTicks = 12;
        private static readonly Strike[] PlayerMoves = { new Strike(11, 5, 18, 14, 15, 2.25f, 105) };
        private static readonly Strike[] Quick = { new Strike(32, 4, 27, 18, 18, 2.15f, 90) };
        private static readonly Strike[] Heavy = { new Strike(56, 5, 38, 32, 27, 2.55f, 100) };
        private static readonly Strike[] Double = { new Strike(31, 4, 5, 20, 19, 2.35f, 95), new Strike(14, 4, 32, 23, 20, 2.4f, 100) };
        private readonly ICombatSpace space;
        private readonly Fighter[] fighters = new Fighter[4];
        private readonly List<int> contacts = new List<int>(8);
        private RunStatus beforePause;
        private CombatButtons pending;
        private int expires;
        private int nextAttack;
        private int eliteMove;
        private int nextEnemy = 1;
        private CombatButtons requested;

        public CombatRun(ICombatSpace space)
        {
            this.space = space ?? throw new ArgumentNullException(nameof(space));
            Reset(Encounter.Practice);
        }

        public event Action<CombatCue> Cue;
        public event Action Rebuilt;
        public Encounter Encounter { get; private set; }
        public RunStatus Status { get; private set; }
        public int Tick { get; private set; }
        public int Count { get; private set; }
        public int Locked { get; private set; }
        public int AttackOwner { get; private set; }
        public bool CanFinish => FindFinish() > 0;
        public int DeflectRemaining => Math.Max(0, CombatRules.DeflectTicks - (int)Math.Min(int.MaxValue, Tick - (long)fighters[0].GuardPressed));

        public FighterView View(int actor)
        {
            var fighter = fighters[actor];
            var phase = Phase(fighter);
            float progress = 0;
            if (phase != AttackPhase.Ready)
            {
                var strike = fighter.Moves[fighter.Segment];
                int elapsed = Tick - fighter.AttackStart;
                progress = phase == AttackPhase.Windup ? (float)elapsed / strike.Windup
                    : phase == AttackPhase.Active ? (float)(elapsed - strike.Windup) / strike.Active
                    : (float)(elapsed - strike.Windup - strike.Active) / strike.Recovery;
            }

            return new FighterView(fighter.Life, fighter.MaxHealth, fighter.MaxPosture, fighter.Facing, phase,
                progress, fighter.Guarding, fighter.DodgeUntil > Tick, fighter.Moving, fighter.Segment, fighter.Life.Broken(Tick));
        }

        public void Queue(CombatButtons buttons)
        {
            if (Status == RunStatus.Fighting || Status == RunStatus.Proceed) requested |= buttons;
        }

        public void TogglePause()
        {
            if (Status == RunStatus.Paused) Status = beforePause;
            else if (Status == RunStatus.Fighting || Status == RunStatus.Proceed)
            {
                beforePause = Status;
                Status = RunStatus.Paused;
                requested = pending = CombatButtons.None;
                fighters[0].GuardPressed = int.MinValue;
                fighters[0].Guarding = false;
            }
        }

        public void Retry() => Reset(Encounter == Encounter.Cleared ? Encounter.Gate : Encounter);
        public void Restart() => Reset(Encounter.Practice);

        public void Step(Vector3 move, Vector3 look, bool guard)
        {
            if (Status != RunStatus.Fighting && Status != RunStatus.Proceed) return;
            Tick++;
            var pressed = requested;
            requested = CombatButtons.None;
            var player = fighters[0];
            if ((pressed & CombatButtons.Lock) != 0) Locked = Locked == 0 ? Closest(14) : 0;
            if (Locked != 0 && !fighters[Locked].Life.Alive) Locked = Closest(14);
            if ((pressed & CombatButtons.Guard) != 0 && player.Moves == null && !player.Life.Broken(Tick)) player.GuardPressed = Tick;
            player.Guarding = guard && player.Moves == null && !player.Life.Broken(Tick);
            if ((pressed & (CombatButtons.Attack | CombatButtons.Dodge | CombatButtons.Finish)) != 0)
            {
                pending = pressed & (CombatButtons.Attack | CombatButtons.Dodge | CombatButtons.Finish);
                expires = Tick + BufferTicks;
            }
            if (Tick >= expires) pending = CombatButtons.None;

            for (int actor = 0; actor < Count; actor++) Advance(actor);
            UpdatePlayer(move, look, player);
            for (int actor = 1; actor < Count; actor++) UpdateEnemy(actor);
            for (int actor = 0; actor < Count; actor++) Contact(actor);
            if (!player.Life.Alive)
            {
                Status = RunStatus.Defeated;
                ReleaseAttacks();
                return;
            }
            if (Status == RunStatus.Fighting && Closest(float.PositiveInfinity) == 0)
            {
                if (Encounter == Encounter.Gate)
                {
                    Status = RunStatus.Victory;
                    Encounter = Encounter.Cleared;
                }
                else Status = RunStatus.Proceed;
            }
            if (Status == RunStatus.Proceed && space.Position(0).Z >= (Encounter == Encounter.Practice ? 13.5f : 35.5f))
                Reset(Encounter + 1);
        }

        private void Reset(Encounter encounter)
        {
            Encounter = encounter;
            Status = RunStatus.Fighting;
            Tick = Locked = AttackOwner = expires = eliteMove = nextAttack = 0;
            nextEnemy = 1;
            pending = requested = CombatButtons.None;
            Count = encounter == Encounter.Courtyard ? 4 : 2;
            fighters[0] = new Fighter(100, 100, Vector3.UnitZ);
            for (int actor = 1; actor < Count; actor++)
            {
                int health = encounter == Encounter.Gate ? 140 : encounter == Encounter.Practice ? 65 : 54;
                int posture = encounter == Encounter.Gate ? 128 : encounter == Encounter.Practice ? 66 : 72;
                fighters[actor] = new Fighter(health, posture, -Vector3.UnitZ) { NextAttack = 28 + actor * 12 };
            }
            space.Reset(encounter, Count);
            Rebuilt?.Invoke();
        }

        private void UpdatePlayer(Vector3 move, Vector3 look, Fighter player)
        {
            player.Moving = false;
            if (player.Life.Broken(Tick)) return;
            var direction = Locked == 0 ? (move.LengthSquared() > .01f ? move : look) : space.Position(Locked) - space.Position(0);
            if (player.Moves == null && Tick >= player.DodgeUntil) player.Facing = Horizontal(direction, player.Facing);
            if (player.Moves == null && Tick >= player.DodgeUntil)
            {
                if ((pending & CombatButtons.Finish) != 0)
                {
                    int target = FindFinish();
                    if (target != 0)
                    {
                        fighters[target].Life = CombatRules.Finish(fighters[target].Life, Tick, Distance(target), 2.65f);
                        Cancel(target);
                        player.ActionUntil = Tick + 20;
                        Cue?.Invoke(new CombatCue(0, target, ContactResult.Finish));
                    }
                    pending &= ~CombatButtons.Finish;
                }
                if (Tick < player.ActionUntil) return;
                if ((pending & CombatButtons.Dodge) != 0)
                {
                    player.DodgeUntil = Tick + CombatRules.DodgeTicks;
                    player.DodgeDirection = Horizontal(move, -player.Facing);
                    player.GuardPressed = int.MinValue;
                    pending = CombatButtons.None;
                }
                else if ((pending & CombatButtons.Attack) != 0)
                {
                    Start(player, PlayerMoves);
                    pending = CombatButtons.None;
                }
            }
            if (Tick < player.DodgeUntil)
            {
                player.Guarding = false;
                space.Move(0, player.DodgeDirection * (.115f));
            }
            else if (player.Moves == null)
            {
                player.Moving = move.LengthSquared() > .01f;
                space.Move(0, move * ((player.Guarding ? 2.5f : 4.4f) / StepsPerSecond));
            }
            else if (Phase(player) == AttackPhase.Active) space.Move(0, player.Facing * .065f);
        }

        private void UpdateEnemy(int actor)
        {
            var fighter = fighters[actor];
            fighter.Moving = false;
            if (!fighter.Life.Alive || fighter.Life.Broken(Tick)) return;
            var toward = space.Position(0) - space.Position(actor);
            float distance = toward.Length();
            if (fighter.Moves != null)
            {
                var strike = fighter.Moves[fighter.Segment];
                int elapsed = Tick - fighter.AttackStart;
                if (elapsed < strike.Windup - 9) fighter.Facing = Horizontal(toward, fighter.Facing);
                if (elapsed == strike.Windup) space.Move(actor, fighter.Facing * .3f);
                return;
            }
            fighter.Facing = Horizontal(toward, fighter.Facing);
            if (distance > 9f) return;
            if (distance <= 2.55f && AttackOwner == 0 && Tick >= fighter.NextAttack && CanTakeTurn(actor))
            {
                AttackOwner = actor;
                nextEnemy = actor == Count - 1 ? 1 : actor + 1;
                Start(fighter, Encounter == Encounter.Gate ? (eliteMove++ % 2 == 0 ? Heavy : Double) : Quick);
                return;
            }
            float spacing = AttackOwner == 0 ? 1.85f : 2.9f;
            if (distance > spacing || (AttackOwner != 0 && distance < 2.25f))
            {
                fighter.Moving = true;
                space.Move(actor, fighter.Facing * ((distance > spacing ? 2.5f : -1.35f) / StepsPerSecond));
            }
            else if (AttackOwner != 0)
            {
                fighter.Moving = true;
                var lateral = Vector3.Cross(Vector3.UnitY, fighter.Facing);
                space.Move(actor, lateral * ((actor % 2 == 0 ? 1f : -1f) / StepsPerSecond));
            }
        }

        private bool CanTakeTurn(int actor)
        {
            for (int offset = 0; offset < Count - 1; offset++)
            {
                int candidate = 1 + (nextEnemy - 1 + offset) % (Count - 1);
                var fighter = fighters[candidate];
                if (fighter.Life.Alive && !fighter.Life.Broken(Tick) && Tick >= fighter.NextAttack && Distance(candidate) <= 2.55f)
                    return candidate == actor;
            }
            return false;
        }

        private void Start(Fighter fighter, Strike[] moves)
        {
            fighter.Moves = moves;
            fighter.AttackId = ++nextAttack;
            fighter.AttackStart = Tick;
            fighter.Segment = 0;
            fighter.ContactMask = 0;
            fighter.Guarding = false;
            fighter.GuardPressed = int.MinValue;
        }

        private void Advance(int actor)
        {
            var fighter = fighters[actor];
            fighter.Life = CombatRules.Recover(fighter.Life, Tick, Tick - fighter.LastContact > 120, fighter.MaxPosture);
            if (fighter.Moves == null || Phase(fighter) != AttackPhase.Ready) return;
            if (fighter.Segment + 1 == fighter.Moves.Length) Cancel(actor);
            else
            {
                fighter.Segment++;
                fighter.AttackStart = Tick;
                fighter.ContactMask = 0;
            }
        }

        private void Contact(int actor)
        {
            var attacker = fighters[actor];
            if (Phase(attacker) != AttackPhase.Active) return;
            int attackId = attacker.AttackId;
            var strike = attacker.Moves[attacker.Segment];
            contacts.Clear();
            space.Sweep(actor, attacker.Facing, strike, contacts);
            foreach (int target in contacts)
            {
                if (attacker.AttackId != attackId || attacker.Moves == null) break;
                if (target < 0 || target >= Count || (target == 0) == (actor == 0)) continue;
                var defender = fighters[target];
                if (!defender.Life.Alive || (attacker.ContactMask & (1 << target)) != 0) continue;
                attacker.ContactMask |= 1 << target;
                var relative = Horizontal(space.Position(actor) - space.Position(target), defender.Facing);
                var outcome = CombatRules.Resolve(attacker.Life, defender.Life, strike, Tick, defender.GuardPressed,
                    defender.Guarding, Vector3.Dot(relative, defender.Facing) >= .25f, defender.DodgeUntil > Tick,
                    attacker.MaxPosture, defender.MaxPosture);
                attacker.Life = outcome.Attacker;
                defender.Life = outcome.Defender;
                attacker.LastContact = defender.LastContact = Tick;
                if (attacker.Life.Broken(Tick) || outcome.Result == ContactResult.Deflect && attacker.Moves.Length == 1) Cancel(actor);
                if (!defender.Life.Alive || defender.Life.Broken(Tick)) Cancel(target);
                Cue?.Invoke(new CombatCue(actor, target, outcome.Result));
            }
        }

        private void Cancel(int actor)
        {
            var fighter = fighters[actor];
            fighter.Moves = null;
            if (AttackOwner == actor)
            {
                AttackOwner = 0;
                fighter.NextAttack = Tick + 66;
            }
        }

        private void ReleaseAttacks()
        {
            for (int actor = 0; actor < Count; actor++) Cancel(actor);
            requested = pending = CombatButtons.None;
        }

        private AttackPhase Phase(Fighter fighter) => fighter.Moves == null ? AttackPhase.Ready : fighter.Moves[fighter.Segment].At(Tick - fighter.AttackStart);
        private float Distance(int actor) => Vector3.Distance(space.Position(0), space.Position(actor));

        private int Closest(float range)
        {
            int closest = 0;
            for (int actor = 1; actor < Count; actor++)
            {
                if (!fighters[actor].Life.Alive) continue;
                float distance = Distance(actor);
                if (distance <= range) { range = distance; closest = actor; }
            }
            return closest;
        }

        private int FindFinish()
        {
            if (Locked > 0 && fighters[Locked].Life.Broken(Tick) && Distance(Locked) <= 2.65f) return Locked;
            for (int actor = 1; actor < Count; actor++)
                if (fighters[actor].Life.Broken(Tick) && Distance(actor) <= 2.65f) return actor;
            return 0;
        }

        private static Vector3 Horizontal(Vector3 vector, Vector3 fallback)
        {
            vector.Y = 0;
            return vector.LengthSquared() > .0001f ? Vector3.Normalize(vector) : fallback;
        }

        private sealed class Fighter
        {
            public Fighter(int health, int posture, Vector3 facing)
            {
                Life = new Vitality(health);
                MaxHealth = health;
                MaxPosture = posture;
                Facing = facing;
            }

            public readonly int MaxHealth;
            public readonly int MaxPosture;
            public Vitality Life;
            public Vector3 Facing, DodgeDirection;
            public bool Guarding, Moving;
            public int DodgeUntil, ActionUntil, AttackId, AttackStart, Segment, ContactMask, NextAttack, LastContact;
            public int GuardPressed = int.MinValue;
            public Strike[] Moves;
        }
    }
}
