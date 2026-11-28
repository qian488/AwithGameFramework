using System;
using System.Collections.Generic;
using System.Numerics;

namespace Test.Domain.Combat.Pvp
{
    [Flags]
    public enum PvpButtons
    {
        None = 0,
        Attack = 1,
        Guard = 2,
        Dodge = 4,
        Lock = 8,
        Finish = 16,
        EquipSword = 32,
        EquipBow = 64,
        AttackPressed = 128,
        AttackReleased = 256
    }

    public enum PvpMatchStatus
    {
        Fighting,
        Victory
    }

    public enum PvpWeapon
    {
        Sword,
        Bow
    }

    public readonly struct PvpInputFrame
    {
        public PvpInputFrame(int tick, int playerId, PvpButtons buttons, Vector3 move, Vector3 aim)
        {
            if (tick < 1) throw new ArgumentOutOfRangeException(nameof(tick));
            if (playerId < 0 || playerId > 1) throw new ArgumentOutOfRangeException(nameof(playerId));
            Tick = tick;
            PlayerId = playerId;
            Buttons = buttons;
            Move = move;
            Aim = aim;
        }

        public int Tick { get; }
        public int PlayerId { get; }
        public PvpButtons Buttons { get; }
        public Vector3 Move { get; }
        public Vector3 Aim { get; }

        public static PvpInputFrame Neutral(int tick, int playerId) =>
            new PvpInputFrame(tick, playerId, PvpButtons.None, Vector3.Zero, Vector3.Zero);
    }

    public sealed class PvpCombatConfig
    {
        public PvpCombatConfig()
            : this(100, 100, 6, new Vector3(-1.1f, 0, 0), new Vector3(1.1f, 0, 0))
        {
        }

        public PvpCombatConfig(int playerHealth, int playerPosture, int arrows, Vector3 playerOnePosition,
            Vector3 playerTwoPosition)
        {
            if (playerHealth <= 0) throw new ArgumentOutOfRangeException(nameof(playerHealth));
            if (playerPosture <= 0) throw new ArgumentOutOfRangeException(nameof(playerPosture));
            if (arrows < 0) throw new ArgumentOutOfRangeException(nameof(arrows));
            PlayerHealth = playerHealth;
            PlayerPosture = playerPosture;
            Arrows = arrows;
            PlayerOnePosition = playerOnePosition;
            PlayerTwoPosition = playerTwoPosition;
        }

        public int PlayerHealth { get; }
        public int PlayerPosture { get; }
        public int Arrows { get; }
        public Vector3 PlayerOnePosition { get; }
        public Vector3 PlayerTwoPosition { get; }

        public static Strike SwordStrike => new Strike(11, 5, 18, 14, 15, 2.25f, 105);
        public static Strike BowStrike => BowRules.Shot;
        public const int StepsPerSecond = 60;
        public const int FinishHoldTicks = 21;
        public const float FinishReach = 2.65f;
    }

    public readonly struct PvpPlayerState
    {
        internal PvpPlayerState(int playerId, Vitality vitality, int healthLimit, int postureLimit, Vector3 position,
            Vector3 facing, PvpWeapon weapon, int arrowsRemaining, AttackPhase phase, float phaseProgress,
            bool guarding, bool dodging, bool moving, bool broken, int lockedTarget, int attackStartTick,
            int attackId, int segment, int guardPressedTick, int dodgeUntilTick, int actionUntilTick,
            int finishHoldTarget, int finishHoldStarted, int contactMask, int lastContactTick,
            bool wasGuarding, bool projectileLaunched, Vector3 dodgeDirection, Vector3 aim)
        {
            PlayerId = playerId;
            Vitality = vitality;
            HealthLimit = healthLimit;
            PostureLimit = postureLimit;
            Position = position;
            Facing = facing;
            Weapon = weapon;
            ArrowsRemaining = arrowsRemaining;
            Phase = phase;
            PhaseProgress = phaseProgress;
            Guarding = guarding;
            Dodging = dodging;
            Moving = moving;
            Broken = broken;
            LockedTarget = lockedTarget;
            AttackStartTick = attackStartTick;
            AttackId = attackId;
            Segment = segment;
            GuardPressedTick = guardPressedTick;
            DodgeUntilTick = dodgeUntilTick;
            ActionUntilTick = actionUntilTick;
            FinishHoldTarget = finishHoldTarget;
            FinishHoldStarted = finishHoldStarted;
            ContactMask = contactMask;
            LastContactTick = lastContactTick;
            WasGuarding = wasGuarding;
            ProjectileLaunched = projectileLaunched;
            DodgeDirection = dodgeDirection;
            Aim = aim;
        }

        public int PlayerId { get; }
        public Vitality Vitality { get; }
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
        public int AttackStartTick { get; }
        public int AttackId { get; }
        public int Segment { get; }
        public int GuardPressedTick { get; }
        public int DodgeUntilTick { get; }
        public int ActionUntilTick { get; }
        public int FinishHoldTarget { get; }
        public int FinishHoldStarted { get; }
        public int ContactMask { get; }
        public int LastContactTick { get; }
        public bool WasGuarding { get; }
        public bool ProjectileLaunched { get; }
        public Vector3 DodgeDirection { get; }
        public Vector3 Aim { get; }
    }

    public readonly struct PvpArrowState
    {
        internal PvpArrowState(int owner, Vector3 position, Vector3 velocity, float age)
        {
            Owner = owner;
            Position = position;
            Velocity = velocity;
            Age = age;
        }

        public int Owner { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float Age { get; }
    }

    public sealed class PvpState
    {
        internal PvpState(int tick, PvpMatchStatus status, PvpPlayerState playerOne, PvpPlayerState playerTwo,
            IReadOnlyList<PvpArrowState> arrows, PvpMatchResult result)
        {
            Tick = tick;
            Status = status;
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            Arrows = arrows;
            Result = result;
        }

        public int Tick { get; }
        public PvpMatchStatus Status { get; }
        public PvpPlayerState PlayerOne { get; }
        public PvpPlayerState PlayerTwo { get; }
        public IReadOnlyList<PvpArrowState> Arrows { get; }
        public PvpMatchResult Result { get; }
    }

    public readonly struct PvpCue
    {
        internal PvpCue(int sequence, int tick, int attacker, int target, ContactResult result, bool isArrow)
        {
            Sequence = sequence;
            Tick = tick;
            Attacker = attacker;
            Target = target;
            Result = result;
            IsArrow = isArrow;
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
        internal PvpMatchResult(int tick, int winner, int loser)
        {
            Tick = tick;
            Winner = winner;
            Loser = loser;
        }

        public int Tick { get; }
        public int Winner { get; }
        public int Loser { get; }
    }

    public sealed class PvpSimulationStep
    {
        internal PvpSimulationStep(PvpState state, IReadOnlyList<PvpCue> cues, PvpMatchResult result)
        {
            State = state;
            Cues = cues;
            Result = result;
        }

        public PvpState State { get; }
        public IReadOnlyList<PvpCue> Cues { get; }
        public PvpMatchResult Result { get; }
    }

    public sealed class PvpCombatSimulation
    {
        private readonly PvpCombatConfig config;
        private readonly Fighter[] fighters = new Fighter[2];
        private readonly List<Arrow> arrows = new List<Arrow>(4);
        private readonly List<PvpCue> cues = new List<PvpCue>(4);
        private PvpState state;
        private PvpMatchResult result;
        private int tick;
        private int nextAttackId;
        private int nextCueSequence;

        public PvpCombatSimulation(PvpCombatConfig config = null)
        {
            this.config = config ?? new PvpCombatConfig();
            fighters[0] = new Fighter(0, this.config.PlayerHealth, this.config.PlayerPosture,
                this.config.PlayerOnePosition, Vector3.UnitX, this.config.Arrows);
            fighters[1] = new Fighter(1, this.config.PlayerHealth, this.config.PlayerPosture,
                this.config.PlayerTwoPosition, -Vector3.UnitX, this.config.Arrows);
            state = CaptureState();
        }

        public PvpState State => state;
        public PvpMatchResult Result => result;

        public PvpSimulationStep Step(PvpInputFrame playerOne, PvpInputFrame playerTwo)
        {
            if (result != null) return new PvpSimulationStep(state, Array.Empty<PvpCue>(), null);
            ValidateFrame(playerOne, 0);
            ValidateFrame(playerTwo, 1);

            tick++;
            cues.Clear();
            Prepare(fighters[0], playerOne);
            Prepare(fighters[1], playerTwo);
            Contact(0);
            Contact(1);
            AdvanceArrows();
            CompleteIfNeeded();
            state = CaptureState();
            return new PvpSimulationStep(state, Array.AsReadOnly(cues.ToArray()), result);
        }

        private void ValidateFrame(PvpInputFrame frame, int playerId)
        {
            if (frame.PlayerId != playerId) throw new ArgumentException("The input frame has the wrong player ID.");
            if (frame.Tick != tick + 1) throw new ArgumentException("Input frames must advance one tick at a time.");
        }

        private void Prepare(Fighter fighter, PvpInputFrame input)
        {
            fighter.Moving = false;
            fighter.Life = CombatRules.Recover(fighter.Life, tick, tick - fighter.LastContact > 120,
                fighter.PostureLimit);
            if (fighter.Moves != null && Phase(fighter) == AttackPhase.Ready) Cancel(fighter);
            if (!fighter.Life.Alive) return;

            if (fighter.Moves == null && tick >= fighter.DodgeUntil && tick >= fighter.ActionUntil &&
                !fighter.Life.Broken(tick))
            {
                if ((input.Buttons & PvpButtons.EquipSword) != 0) fighter.Weapon = PvpWeapon.Sword;
                else if ((input.Buttons & PvpButtons.EquipBow) != 0) fighter.Weapon = PvpWeapon.Bow;
            }

            bool canGuard = fighter.Weapon == PvpWeapon.Sword && fighter.Moves == null &&
                !fighter.Life.Broken(tick) && tick >= fighter.DodgeUntil;
            bool guarding = canGuard && (input.Buttons & PvpButtons.Guard) != 0;
            if (guarding && !fighter.WasGuarding) fighter.GuardPressedTick = tick;
            if (!guarding) fighter.GuardPressedTick = int.MinValue;
            fighter.Guarding = guarding;
            fighter.WasGuarding = guarding;
            fighter.Aim = input.Aim;

            if ((input.Buttons & PvpButtons.Lock) != 0)
                fighter.LockedTarget = fighter.LockedTarget < 0 ? Other(fighter.Id) : -1;
            if (fighter.LockedTarget >= 0 && !fighters[fighter.LockedTarget].Life.Alive)
                fighter.LockedTarget = -1;

            UpdateFacing(fighter, input);
            if (fighter.Moves == null && tick >= fighter.DodgeUntil && !fighter.Life.Broken(tick))
            {
                if ((input.Buttons & PvpButtons.Finish) != 0)
                    UpdateFinish(fighter);
                else
                    fighter.FinishHoldTarget = -1;

                if (fighter.FinishHoldTarget < 0 && tick >= fighter.ActionUntil)
                {
                    if ((input.Buttons & PvpButtons.Dodge) != 0)
                        StartDodge(fighter, input.Move);
                    else if ((input.Buttons & (PvpButtons.Attack | PvpButtons.AttackPressed)) != 0)
                        StartAttack(fighter);
                }
            }

            Move(fighter, input.Move);
        }

        private void UpdateFacing(Fighter fighter, PvpInputFrame input)
        {
            Vector3 direction;
            if (fighter.LockedTarget >= 0)
                direction = fighters[fighter.LockedTarget].Position - fighter.Position;
            else if (CombatVectorMath.LengthSquared(input.Move) > .01f)
                direction = input.Move;
            else
                direction = input.Aim;
            fighter.Facing = Horizontal(direction, fighter.Facing);
        }

        private void UpdateFinish(Fighter fighter)
        {
            if (fighter.Weapon != PvpWeapon.Sword || fighter.Moves != null || tick < fighter.DodgeUntil ||
                tick < fighter.ActionUntil)
            {
                fighter.FinishHoldTarget = -1;
                return;
            }

            int target = FindFinish(fighter);
            if (target < 0)
            {
                fighter.FinishHoldTarget = -1;
                return;
            }
            if (fighter.FinishHoldTarget != target)
            {
                fighter.FinishHoldTarget = target;
                fighter.FinishHoldStarted = tick;
            }
            if (tick - fighter.FinishHoldStarted + 1 >= PvpCombatConfig.FinishHoldTicks)
            {
                fighters[target].Life = CombatRules.Finish(fighters[target].Life, tick,
                    Distance(fighter.Id, target), PvpCombatConfig.FinishReach);
                Cancel(fighters[target]);
                fighter.ActionUntil = tick + 20;
                Emit(fighter.Id, target, ContactResult.Finish, false);
                fighter.FinishHoldTarget = -1;
            }
        }

        private int FindFinish(Fighter fighter)
        {
            if (fighter.LockedTarget >= 0 && IsFinishTarget(fighter.LockedTarget)) return fighter.LockedTarget;
            int other = Other(fighter.Id);
            return IsFinishTarget(other) ? other : -1;
        }

        private bool IsFinishTarget(int target) => fighters[target].Life.Broken(tick) &&
            Distance(target == 0 ? 1 : 0, target) <= PvpCombatConfig.FinishReach;

        private void StartDodge(Fighter fighter, Vector3 direction)
        {
            fighter.DodgeUntil = tick + CombatRules.DodgeTicks;
            fighter.DodgeDirection = Horizontal(direction, -fighter.Facing);
            fighter.GuardPressedTick = int.MinValue;
            fighter.Guarding = false;
            fighter.WasGuarding = false;
        }

        private void StartAttack(Fighter fighter)
        {
            fighter.Moves = fighter.Weapon == PvpWeapon.Sword ? new[] { PvpCombatConfig.SwordStrike } :
                new[] { PvpCombatConfig.BowStrike };
            if (fighter.Weapon == PvpWeapon.Bow && fighter.ArrowsRemaining == 0)
            {
                fighter.Moves = null;
                return;
            }
            fighter.AttackId = ++nextAttackId;
            fighter.AttackStartTick = tick;
            fighter.Segment = 0;
            fighter.ContactMask = 0;
            fighter.ProjectileLaunched = false;
            fighter.Guarding = false;
            fighter.WasGuarding = false;
            fighter.GuardPressedTick = int.MinValue;
        }

        private void Move(Fighter fighter, Vector3 input)
        {
            if (fighter.Life.Broken(tick)) return;
            input.Y = 0;
            if (CombatVectorMath.LengthSquared(input) > 1) input = CombatVectorMath.Normalize(input);
            if (tick < fighter.DodgeUntil)
            {
                fighter.Guarding = false;
                fighter.Moving = true;
                fighter.Position += fighter.DodgeDirection * .115f;
            }
            else if (fighter.Moves == null && tick >= fighter.ActionUntil)
            {
                fighter.Moving = CombatVectorMath.LengthSquared(input) > .01f;
                if (fighter.Moving)
                    fighter.Position += input * ((fighter.Guarding ? 2.5f : 4.4f) / PvpCombatConfig.StepsPerSecond);
            }
            else if (fighter.Weapon == PvpWeapon.Sword && Phase(fighter) == AttackPhase.Active)
            {
                fighter.Position += fighter.Facing * .065f;
            }
            // The Rain Pass courtyard anchors the PvP origin at world Z 23.
            fighter.Position = Vector3.Clamp(fighter.Position,
                new Vector3(-8.5f, 0, -10.5f), new Vector3(8.5f, 0, 10.5f));
        }

        private void Contact(int actor)
        {
            var attacker = fighters[actor];
            if (attacker.Moves == null || Phase(attacker) != AttackPhase.Active) return;
            if (attacker.Weapon == PvpWeapon.Bow)
            {
                if (!attacker.ProjectileLaunched)
                {
                    Vector3 origin = attacker.Position + Vector3.UnitY * 1.34f + attacker.Facing * .32f;
                    Vector3 target = attacker.LockedTarget >= 0
                        ? fighters[attacker.LockedTarget].Position + Vector3.UnitY * 1.34f
                        : origin + (CombatVectorMath.LengthSquared(attacker.Aim) > .01f ? Horizontal(attacker.Aim, attacker.Facing) : attacker.Facing) * BowRules.AimDistance;
                    arrows.Add(new Arrow(actor, BowRules.Launch(origin, target, attacker.Facing)));
                    attacker.ArrowsRemaining--;
                    attacker.ProjectileLaunched = true;
                }
                return;
            }

            int targetId = Other(actor);
            var defender = fighters[targetId];
            if (!defender.Life.Alive || (attacker.ContactMask & (1 << targetId)) != 0) return;
            if (Distance(actor, targetId) > PvpCombatConfig.SwordStrike.Reach) return;
            Vector3 toTarget = Horizontal(defender.Position - attacker.Position, attacker.Facing);
            if (CombatVectorMath.Dot(toTarget, attacker.Facing) < .25f) return;
            attacker.ContactMask |= 1 << targetId;
            ApplyContact(actor, targetId, PvpCombatConfig.SwordStrike, attacker.Position - defender.Position, false);
        }

        private void AdvanceArrows()
        {
            for (int index = 0; index < arrows.Count;)
            {
                Arrow arrow = arrows[index];
                ArrowFlight next = BowRules.Advance(arrow.Flight, 1f / PvpCombatConfig.StepsPerSecond);
                int target = Other(arrow.Owner);
                if (fighters[target].Life.Alive && DistanceToSegment(fighters[target].Position + Vector3.UnitY * 1.34f,
                        arrow.Flight.Position, next.Position) <= .65f)
                {
                    ApplyContact(arrow.Owner, target, PvpCombatConfig.BowStrike, -next.Velocity, true);
                    arrows.RemoveAt(index);
                }
                else if (next.Age >= BowRules.FlightLifetime)
                    arrows.RemoveAt(index);
                else
                {
                    arrows[index] = new Arrow(arrow.Owner, next);
                    index++;
                }
            }
        }

        private void ApplyContact(int actor, int target, Strike strike, Vector3 incoming, bool isArrow)
        {
            var attacker = fighters[actor];
            var defender = fighters[target];
            if (!attacker.Life.Alive || !defender.Life.Alive) return;
            Vector3 relative = Horizontal(incoming, defender.Facing);
            var outcome = CombatRules.Resolve(attacker.Life, defender.Life, strike, tick, defender.GuardPressedTick,
                defender.Guarding, CombatVectorMath.Dot(relative, defender.Facing) >= .25f, defender.DodgeUntil > tick,
                attacker.PostureLimit, defender.PostureLimit);
            attacker.Life = outcome.Attacker;
            defender.Life = outcome.Defender;
            attacker.LastContact = defender.LastContact = tick;
            if (attacker.Life.Broken(tick) || outcome.Result == ContactResult.Deflect && attacker.Moves?.Length == 1)
                Cancel(attacker);
            if (!defender.Life.Alive || defender.Life.Broken(tick)) Cancel(defender);
            Emit(actor, target, outcome.Result, isArrow);
        }

        private void CompleteIfNeeded()
        {
            if (result != null) return;
            if (!fighters[0].Life.Alive)
                Complete(1, 0);
            else if (!fighters[1].Life.Alive)
                Complete(0, 1);
        }

        private void Complete(int winner, int loser)
        {
            result = new PvpMatchResult(tick, winner, loser);
            arrows.Clear();
            Cancel(fighters[0]);
            Cancel(fighters[1]);
        }

        private void Emit(int attacker, int target, ContactResult contactResult, bool isArrow) =>
            cues.Add(new PvpCue(++nextCueSequence, tick, attacker, target, contactResult, isArrow));

        private void Cancel(Fighter fighter)
        {
            fighter.Moves = null;
            fighter.ContactMask = 0;
            fighter.ProjectileLaunched = false;
        }

        private AttackPhase Phase(Fighter fighter)
        {
            if (fighter.Moves == null) return AttackPhase.Ready;
            return fighter.Moves[fighter.Segment].At(tick - fighter.AttackStartTick);
        }

        private float PhaseProgress(Fighter fighter)
        {
            if (fighter.Moves == null) return 0;
            Strike strike = fighter.Moves[fighter.Segment];
            int elapsed = tick - fighter.AttackStartTick;
            AttackPhase phase = Phase(fighter);
            if (phase == AttackPhase.Windup) return (float)elapsed / strike.Windup;
            if (phase == AttackPhase.Active) return (float)(elapsed - strike.Windup) / strike.Active;
            return (float)(elapsed - strike.Windup - strike.Active) / strike.Recovery;
        }

        private PvpState CaptureState()
        {
            var arrowStates = new PvpArrowState[arrows.Count];
            for (int index = 0; index < arrowStates.Length; index++)
                arrowStates[index] = new PvpArrowState(arrows[index].Owner, arrows[index].Flight.Position,
                    arrows[index].Flight.Velocity, arrows[index].Flight.Age);
            return new PvpState(tick, result == null ? PvpMatchStatus.Fighting : PvpMatchStatus.Victory,
                CapturePlayer(fighters[0]), CapturePlayer(fighters[1]), Array.AsReadOnly(arrowStates), result);
        }

        private PvpPlayerState CapturePlayer(Fighter fighter) => new PvpPlayerState(fighter.Id, fighter.Life,
            fighter.HealthLimit, fighter.PostureLimit, fighter.Position, fighter.Facing, fighter.Weapon,
            fighter.ArrowsRemaining, Phase(fighter), PhaseProgress(fighter), fighter.Guarding,
            fighter.DodgeUntil > tick, fighter.Moving, fighter.Life.Broken(tick), fighter.LockedTarget,
            fighter.AttackStartTick, fighter.AttackId, fighter.Segment, fighter.GuardPressedTick,
            fighter.DodgeUntil, fighter.ActionUntil, fighter.FinishHoldTarget, fighter.FinishHoldStarted,
            fighter.ContactMask, fighter.LastContact, fighter.WasGuarding, fighter.ProjectileLaunched,
            fighter.DodgeDirection, fighter.Aim);

        private int Other(int playerId) => playerId == 0 ? 1 : 0;
        private float Distance(int actor, int target) => CombatVectorMath.Distance(fighters[actor].Position, fighters[target].Position);

        private static Vector3 Horizontal(Vector3 vector, Vector3 fallback)
        {
            vector.Y = 0;
            return CombatVectorMath.LengthSquared(vector) > .0001f ? CombatVectorMath.Normalize(vector) : fallback;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            double lengthSquared = CombatVectorMath.LengthSquared(segment);
            if (lengthSquared < .0001f) return CombatVectorMath.Distance(point, start);
            double projection = CombatVectorMath.Dot(point - start, segment) / lengthSquared;
            return CombatVectorMath.Distance(point, start + segment * (float)Math.Max(0, Math.Min(1, projection)));
        }

        private sealed class Fighter
        {
            public Fighter(int id, int health, int posture, Vector3 position, Vector3 facing, int arrows)
            {
                Id = id;
                HealthLimit = health;
                PostureLimit = posture;
                Life = new Vitality(health);
                Position = position;
                Facing = facing;
                Weapon = PvpWeapon.Sword;
                ArrowsRemaining = arrows;
                LockedTarget = -1;
                FinishHoldTarget = -1;
                GuardPressedTick = int.MinValue;
            }

            public readonly int Id;
            public readonly int HealthLimit;
            public readonly int PostureLimit;
            public Vitality Life;
            public Vector3 Position;
            public Vector3 Facing;
            public Vector3 DodgeDirection;
            public Vector3 Aim;
            public PvpWeapon Weapon;
            public int ArrowsRemaining;
            public int LockedTarget;
            public int AttackStartTick;
            public int AttackId;
            public int Segment;
            public int GuardPressedTick;
            public int DodgeUntil;
            public int ActionUntil;
            public int FinishHoldTarget;
            public int FinishHoldStarted;
            public int ContactMask;
            public int LastContact;
            public bool Guarding;
            public bool WasGuarding;
            public bool Moving;
            public bool ProjectileLaunched;
            public Strike[] Moves;
        }

        private readonly struct Arrow
        {
            public Arrow(int owner, ArrowFlight flight)
            {
                Owner = owner;
                Flight = flight;
            }

            public int Owner { get; }
            public ArrowFlight Flight { get; }
        }
    }
}
