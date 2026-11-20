using System;
using System.Collections.Generic;
using System.Numerics;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public enum Encounter { Practice, Courtyard, Gate, Cleared }
    public enum RunStatus { Fighting, Proceed, Paused, Defeated, Victory }
    public enum PlayerWeapon { Sword, Bow }

    [Flags]
    public enum CombatButtons
    {
        None = 0, Attack = 1, Guard = 2, Dodge = 4, Lock = 8, Finish = 16,
        EquipSword = 32, EquipBow = 64, AttackPressed = 128, AttackReleased = 256
    }

    public interface ICombatSpace
    {
        void Reset(Encounter encounter, int actorCount);
        Vector3 Position(int actor);
        void Move(int actor, Vector3 displacement);
        void Sweep(int actor, Vector3 facing, Strike strike, List<int> contacts);
        bool TraceArrow(Vector3 from, Vector3 to, out int target);
    }

    public readonly struct FighterView
    {
        internal FighterView(Vitality vitality, int healthLimit, int postureLimit, Vector3 facing, AttackPhase phase,
            float phaseProgress, bool guarding, bool dodging, bool moving, int segment, bool broken, float attackArc)
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
            AttackArc = attackArc;
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
        public float AttackArc { get; }
    }

    public readonly struct CombatCue
    {
        public CombatCue(int attacker, int target, ContactResult result)
            : this(attacker, target, result, false)
        {
        }

        internal CombatCue(int attacker, int target, ContactResult result, bool isArrow)
        {
            Attacker = attacker;
            Target = target;
            Result = result;
            IsArrow = isArrow;
        }

        public int Attacker { get; }
        public int Target { get; }
        public ContactResult Result { get; }
        public bool IsArrow { get; }
    }

    public sealed class CombatRun
    {
        public const int StepsPerSecond = 60;
        public const int FinishHoldTicks = 21;
        private const int BufferTicks = 12;
        private const float FinishReach = 2.65f;
        private static readonly Strike[] PlayerMoves = { new Strike(11, 5, 18, 14, 15, 2.25f, 105) };
        private static readonly Strike[] BowMoves = { BowRules.Shot };
        private static readonly Strike[] Quick = { new Strike(32, 4, 27, 18, 18, 2.15f, 90) };
        private Strike[] trialMoves;
        public TrialOpponent TrialOpponent { get; private set; }
        private static readonly Strike[] Heavy = { new Strike(56, 5, 38, 32, 27, 2.55f, 100) };
        private static readonly Strike[] Double = { new Strike(31, 4, 5, 20, 19, 2.35f, 95), new Strike(14, 4, 32, 23, 20, 2.4f, 100) };
        private readonly ICombatSpace space;
        private readonly Fighter[] fighters = new Fighter[4];
        private readonly List<int> contacts = new List<int>(8);
        private readonly List<ArrowFlight> arrows = new List<ArrowFlight>(2);
        private RunStatus beforePause;
        private CombatButtons pending;
        private int expires;
        private int nextAttack;
        private int eliteMove;
        private int nextEnemy = 1;
        private CombatButtons requested;
        private int finishHoldTarget;
        private int finishHoldStarted;

        public CombatRun(ICombatSpace space, CombatMap map = null)
        {
            this.space = space ?? throw new ArgumentNullException(nameof(space));
            Map = map;
            AirborneArrows = arrows.AsReadOnly();
            Reset(map == null ? Encounter.Practice : Encounter.Courtyard);
        }

        public event Action<CombatCue> Cue;
        public event Action Rebuilt;
        public CombatMap Map { get; }
        public Encounter Encounter { get; private set; }
        public RunStatus Status { get; private set; }
        public int Tick { get; private set; }
        public int Count { get; private set; }
        public int Locked { get; private set; }
        public int AttackOwner { get; private set; }
        public PlayerWeapon Weapon { get; private set; }
        public int ArrowsRemaining { get; private set; }
        public IReadOnlyList<ArrowFlight> AirborneArrows { get; }
        public bool CanFinish => CanStartFinish(fighters[0]) && FindFinish() > 0;
        public bool IsHoldingFinish => finishHoldTarget != 0;
        public float FinishHoldProgress => IsHoldingFinish ? (Tick - finishHoldStarted) / (float)FinishHoldTicks : 0;
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
                progress, fighter.Guarding, fighter.DodgeUntil > Tick, fighter.Moving, fighter.Segment, fighter.Life.Broken(Tick),
                fighter.Moves == null ? 0 : fighter.Moves[fighter.Segment].Arc);
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
                finishHoldTarget = 0;
                fighters[0].GuardPressed = int.MinValue;
                fighters[0].Guarding = false;
            }
        }

        public void Retry() => Reset(Encounter == Encounter.Cleared ? Encounter.Gate : Encounter);
        public void Restart()
        {
            TrialOpponent = null;
            trialMoves = null;
            Reset(Map == null ? Encounter.Practice : Encounter.Courtyard);
        }

        public void StartTrial(TrialOpponent opponent)
        {
            if (Map != null) throw new InvalidOperationException("Trials require the original Rain Pass route.");
            TrialOpponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
            trialMoves = new[] { opponent.Strike };
            Reset(Encounter.Practice);
        }

        public void Step(Vector3 move, Vector3 look, bool guard, Vector3? arrowAim = null)
        {
            if (Status != RunStatus.Fighting && Status != RunStatus.Proceed) return;
            Tick++;
            var pressed = requested;
            requested = CombatButtons.None;
            var player = fighters[0];
            if ((pressed & CombatButtons.Lock) != 0) Locked = Locked == 0 ? Closest(14) : 0;
            if (Locked != 0 && !fighters[Locked].Life.Alive) Locked = Closest(14);
            if (player.Moves == null && Tick >= player.DodgeUntil && Tick >= player.ActionUntil && !player.Life.Broken(Tick))
            {
                if ((pressed & CombatButtons.EquipSword) != 0) Weapon = PlayerWeapon.Sword;
                else if ((pressed & CombatButtons.EquipBow) != 0)
                {
                    Weapon = PlayerWeapon.Bow;
                    player.GuardPressed = int.MinValue;
                }
            }
            bool canGuard = Weapon == PlayerWeapon.Sword && player.Moves == null && !player.Life.Broken(Tick);
            if (canGuard && (pressed & CombatButtons.Guard) != 0) player.GuardPressed = Tick;
            player.Guarding = guard && canGuard;
            if (Tick >= expires) pending = CombatButtons.None;
            for (int actor = 0; actor < Count; actor++) Advance(actor);
            pressed = ResolveAttackGesture(pressed, player);
            if ((pressed & (CombatButtons.Attack | CombatButtons.Dodge | CombatButtons.Finish)) != 0)
            {
                pending = pressed & (CombatButtons.Attack | CombatButtons.Dodge | CombatButtons.Finish);
                expires = Tick + BufferTicks;
            }
            UpdatePlayer(move, look, player);
            for (int actor = 1; actor < Count; actor++) UpdateEnemy(actor);
            if (Weapon == PlayerWeapon.Sword) Contact(0);
            else if (Phase(player) == AttackPhase.Active) LooseArrow(player, arrowAim);
            AdvanceArrows();
            for (int actor = 1; actor < Count; actor++) Contact(actor);
            if (!player.Life.Alive)
            {
                Status = RunStatus.Defeated;
                finishHoldTarget = 0;
                ReleaseAttacks();
                arrows.Clear();
                return;
            }
            if (Status == RunStatus.Fighting && Closest(float.PositiveInfinity) == 0)
            {
                if (TrialOpponent != null) Status = RunStatus.Victory;
                else if (Encounter == Encounter.Gate)
                {
                    Status = RunStatus.Victory;
                    Encounter = Encounter.Cleared;
                }
                else Status = RunStatus.Proceed;
                if (Status == RunStatus.Victory) arrows.Clear();
            }
            float advanceDepth = Map != null ? Map.GateDepth + 1.5f : Encounter == Encounter.Practice ? 13.5f : 35.5f;
            if (Status == RunStatus.Proceed && space.Position(0).Z >= advanceDepth)
                Reset(Encounter + 1);
        }

        private void Reset(Encounter encounter)
        {
            Encounter = encounter;
            Status = RunStatus.Fighting;
            Tick = Locked = AttackOwner = expires = eliteMove = nextAttack = 0;
            nextEnemy = 1;
            pending = requested = CombatButtons.None;
            finishHoldTarget = 0;
            Weapon = PlayerWeapon.Sword;
            ArrowsRemaining = BowRules.QuiverCapacity;
            arrows.Clear();
            Count = encounter == Encounter.Courtyard ? (Map?.Guards ?? 3) + 1 : 2;
            fighters[0] = new Fighter(100, 100, Vector3.UnitZ);
            for (int actor = 1; actor < Count; actor++)
            {
                var enemy = Map == null ? null : encounter == Encounter.Gate ? Map.Boss : Map.Patrol;
                int health = TrialOpponent?.Health ?? enemy?.Health ?? (encounter == Encounter.Gate ? 140 : encounter == Encounter.Practice ? 65 : 54);
                int posture = TrialOpponent?.Posture ?? enemy?.Posture ?? (encounter == Encounter.Gate ? 128 : encounter == Encounter.Practice ? 66 : 72);
                fighters[actor] = new Fighter(health, posture, -Vector3.UnitZ) { NextAttack = 28 + actor * 12, Enemy = enemy };
            }
            space.Reset(encounter, Count);
            Rebuilt?.Invoke();
        }

        private CombatButtons ResolveAttackGesture(CombatButtons pressed, Fighter player)
        {
            if ((pressed & CombatButtons.AttackPressed) != 0)
            {
                finishHoldTarget = CanStartFinish(player) ? FindFinish() : 0;
                if (finishHoldTarget == 0) return pressed | CombatButtons.Attack;
                finishHoldStarted = Tick;
                pending &= ~CombatButtons.Attack;
            }

            if (finishHoldTarget == 0) return pressed;
            if (!CanStartFinish(player) || !IsFinishTarget(finishHoldTarget) ||
                ((pressed | pending) & (CombatButtons.Dodge | CombatButtons.Attack | CombatButtons.Finish)) != 0)
            {
                finishHoldTarget = 0;
                return pressed;
            }
            if ((pressed & CombatButtons.AttackReleased) != 0)
            {
                finishHoldTarget = 0;
                return pressed | CombatButtons.Attack;
            }
            if (Tick - finishHoldStarted >= FinishHoldTicks)
            {
                Finish(player, finishHoldTarget);
                finishHoldTarget = 0;
            }
            return pressed;
        }

        private bool CanStartFinish(Fighter player) => Weapon == PlayerWeapon.Sword && player.Moves == null &&
            player.Life.Alive && !player.Life.Broken(Tick) && Tick >= player.DodgeUntil && Tick >= player.ActionUntil;

        private void Finish(Fighter player, int target)
        {
            fighters[target].Life = CombatRules.Finish(fighters[target].Life, Tick, Distance(target), FinishReach);
            Cancel(target);
            player.ActionUntil = Tick + 20;
            Cue?.Invoke(new CombatCue(0, target, ContactResult.Finish));
        }

        private void UpdatePlayer(Vector3 move, Vector3 look, Fighter player)
        {
            player.Moving = false;
            if (player.Life.Broken(Tick)) return;
            var direction = Locked == 0 ? (Weapon == PlayerWeapon.Sword && move.LengthSquared() > .01f ? move : look) : space.Position(Locked) - space.Position(0);
            if ((player.Moves == null || Weapon == PlayerWeapon.Bow && Phase(player) == AttackPhase.Windup) && Tick >= player.DodgeUntil)
                player.Facing = Horizontal(direction, player.Facing);
            if (player.Moves == null && Tick >= player.DodgeUntil)
            {
                if ((pending & CombatButtons.Finish) != 0)
                {
                    int target = CanStartFinish(player) ? FindFinish() : 0;
                    if (target != 0) Finish(player, target);
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
                    if (Weapon == PlayerWeapon.Sword) Start(player, PlayerMoves);
                    else if (ArrowsRemaining > 0) Start(player, BowMoves);
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
            else if (Weapon == PlayerWeapon.Sword && Phase(player) == AttackPhase.Active) space.Move(0, player.Facing * .065f);
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
                if (elapsed == strike.Windup) space.Move(actor, fighter.Facing * (fighter.Enemy?.Lunge ?? .3f));
                return;
            }
            fighter.Facing = Horizontal(toward, fighter.Facing);
            fighter.Guarding = fighter.Enemy?.Guards == true && Tick >= fighter.RecoverGuardAt;
            if (distance > 9f) return;
            if (distance <= EnemyReach(fighter) && AttackOwner == 0 && Tick >= fighter.NextAttack && CanTakeTurn(actor))
            {
                AttackOwner = actor;
                nextEnemy = actor == Count - 1 ? 1 : actor + 1;
                var moves = fighter.Enemy?.Moves(fighter.AttacksTaken++, fighter.Life.Health);
                Start(fighter, trialMoves ?? moves ?? (Encounter == Encounter.Gate ? (eliteMove++ % 2 == 0 ? Heavy : Double) : Quick));
                return;
            }
            float spacing = AttackOwner == 0 ? 1.85f : 2.9f;
            if (distance > spacing || (AttackOwner != 0 && distance < 2.25f))
            {
                fighter.Moving = true;
                space.Move(actor, fighter.Facing * ((distance > spacing ? fighter.Enemy?.Speed ?? 2.5f : -1.35f) / StepsPerSecond));
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
                if (fighter.Life.Alive && !fighter.Life.Broken(Tick) && Tick >= fighter.NextAttack && Distance(candidate) <= EnemyReach(fighter))
                    return candidate == actor;
            }
            return false;
        }

        private static float EnemyReach(Fighter fighter) => fighter.Enemy == null ? 2.55f
            : fighter.Enemy.Moves(fighter.AttacksTaken, fighter.Life.Health)[0].Reach + fighter.Enemy.Lunge * .5f;

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
                ApplyContact(actor, target, strike, space.Position(actor) - space.Position(target));
            }
        }

        private void LooseArrow(Fighter player, Vector3? aim)
        {
            var height = Vector3.UnitY * 1.34f;
            var origin = space.Position(0) + height + player.Facing * .32f;
            var target = Locked > 0 ? space.Position(Locked) + height : aim ?? origin + player.Facing * BowRules.AimDistance;
            arrows.Add(BowRules.Launch(origin, target, player.Facing));
            ArrowsRemaining--;
        }

        private void AdvanceArrows()
        {
            for (int index = 0; index < arrows.Count;)
            {
                var arrow = arrows[index];
                var next = BowRules.Advance(arrow, 1f / StepsPerSecond);
                if (space.TraceArrow(arrow.Position, next.Position, out int target))
                {
                    if (target > 0 && target < Count) ApplyContact(0, target, BowRules.Shot, -arrow.Velocity, true);
                    arrows.RemoveAt(index);
                }
                else if (next.Age >= BowRules.FlightLifetime) arrows.RemoveAt(index);
                else arrows[index++] = next;
            }
        }

        private void ApplyContact(int actor, int target, Strike strike, Vector3 incoming, bool isArrow = false)
        {
            var attacker = fighters[actor];
            var defender = fighters[target];
            if (!attacker.Life.Alive || !defender.Life.Alive) return;
            var relative = Horizontal(incoming, defender.Facing);
            var outcome = CombatRules.Resolve(attacker.Life, defender.Life, strike, Tick, defender.GuardPressed,
                defender.Guarding, Vector3.Dot(relative, defender.Facing) >= .25f, defender.DodgeUntil > Tick,
                attacker.MaxPosture, defender.MaxPosture);
            attacker.Life = outcome.Attacker;
            defender.Life = outcome.Defender;
            attacker.LastContact = defender.LastContact = Tick;
            if (attacker.Life.Broken(Tick) || outcome.Result == ContactResult.Deflect && attacker.Moves?.Length == 1) Cancel(actor);
            if (!defender.Life.Alive || defender.Life.Broken(Tick)) Cancel(target);
            Cue?.Invoke(new CombatCue(actor, target, outcome.Result, isArrow));
        }

        private void Cancel(int actor)
        {
            var fighter = fighters[actor];
            fighter.Moves = null;
            if (AttackOwner == actor)
            {
                AttackOwner = 0;
                fighter.NextAttack = Tick + (TrialOpponent?.AttackIntervalTicks ?? fighter.Enemy?.Cooldown ?? 66);
                fighter.RecoverGuardAt = Tick + 22;
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
            if (Locked > 0 && IsFinishTarget(Locked)) return Locked;
            for (int actor = 1; actor < Count; actor++)
                if (IsFinishTarget(actor)) return actor;
            return 0;
        }

        private bool IsFinishTarget(int actor) => fighters[actor].Life.Broken(Tick) && Distance(actor) <= FinishReach;

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
            public int AttacksTaken, RecoverGuardAt;
            public MapEnemy Enemy;
            public int GuardPressed = int.MinValue;
            public Strike[] Moves;
        }
    }
}
