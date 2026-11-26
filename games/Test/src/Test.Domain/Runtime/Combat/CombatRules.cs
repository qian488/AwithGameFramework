using System;

namespace Test.Domain.Combat
{
    public enum AttackPhase { Ready, Windup, Active, Recovery }
    public enum ContactResult { Miss, Hit, Guard, Deflect, Dodge, Break, Fall, Finish }

    public readonly struct Strike
    {
        public Strike(int windup, int active, int recovery, int damage, int pressure, float reach, float arc)
        {
            Windup = windup;
            Active = active;
            Recovery = recovery;
            Damage = damage;
            Pressure = pressure;
            Reach = reach;
            Arc = arc;
        }

        public int Windup { get; }
        public int Active { get; }
        public int Recovery { get; }
        public int Damage { get; }
        public int Pressure { get; }
        public float Reach { get; }
        public float Arc { get; }
        public int Duration => Windup + Active + Recovery;
        public AttackPhase At(int elapsed) => elapsed < 0 || elapsed >= Duration ? AttackPhase.Ready
            : elapsed < Windup ? AttackPhase.Windup : elapsed < Windup + Active ? AttackPhase.Active : AttackPhase.Recovery;
    }

    public readonly struct Vitality
    {
        public Vitality(int health, int posture = 0, int brokenUntil = 0)
        {
            Health = health;
            Posture = posture;
            BrokenUntil = brokenUntil;
        }

        public int Health { get; }
        public int Posture { get; }
        public int BrokenUntil { get; }
        public bool Alive => Health > 0;
        public bool Broken(int tick) => Alive && BrokenUntil > tick;
    }

    public static class CombatRules
    {
        public const int DeflectTicks = 8;
        public const int BreakTicks = 150;
        public const int DodgeTicks = 14;

        public static (Vitality Attacker, Vitality Defender, ContactResult Result) Resolve(Vitality attacker,
            Vitality defender, Strike strike, int tick, int guardPressedTick, bool guarding, bool facing,
            bool dodging, int attackerLimit, int defenderLimit)
        {
            if (!attacker.Alive || !defender.Alive) return (attacker, defender, ContactResult.Miss);
            if (dodging) return (attacker, defender, ContactResult.Dodge);
            if (facing && !defender.Broken(tick) && tick - (long)guardPressedTick >= 0 && tick - (long)guardPressedTick < DeflectTicks)
            {
                var pressured = Pressure(attacker, strike.Pressure * 2, attackerLimit, tick);
                return (pressured, defender, ContactResult.Deflect);
            }

            if (guarding && facing && !defender.Broken(tick))
            {
                var pressured = Pressure(defender, strike.Pressure, defenderLimit, tick);
                return (attacker, pressured, pressured.Broken(tick) ? ContactResult.Break : ContactResult.Guard);
            }

            var hit = new Vitality(Math.Max(0, defender.Health - strike.Damage), defender.Posture, defender.BrokenUntil);
            hit = Pressure(hit, strike.Pressure, defenderLimit, tick);
            return (attacker, hit, !hit.Alive ? ContactResult.Fall : hit.Broken(tick) ? ContactResult.Break : ContactResult.Hit);
        }

        public static Vitality Recover(Vitality state, int tick, bool resting, int limit, int recoveryInterval = 12)
        {
            if (!state.Alive || state.Broken(tick)) return state;
            if (state.BrokenUntil != 0) return new Vitality(state.Health, limit / 3);
            return resting && tick % recoveryInterval == 0 ? new Vitality(state.Health, Math.Max(0, state.Posture - 1)) : state;
        }

        public static Vitality Finish(Vitality target, int tick, float distance, float reach) =>
            target.Broken(tick) && distance <= reach ? new Vitality(0) : target;

        private static Vitality Pressure(Vitality state, int amount, int limit, int tick)
        {
            if (!state.Alive || state.Broken(tick)) return state;
            int pressure = Math.Min(limit, state.Posture + amount);
            return new Vitality(state.Health, pressure, pressure == limit ? tick + BreakTicks : 0);
        }
    }
}
