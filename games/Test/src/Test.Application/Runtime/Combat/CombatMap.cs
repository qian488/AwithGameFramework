using System;
using System.Collections.Generic;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public enum CombatMapId { CedarGrove, SaltCauseway, EmberFoundry }
    public enum EnemyWeapon { ShortSword, TwinBlades, Spear, Poleaxe, ShieldMace, Maul }

    public sealed class MapEnemy
    {
        internal MapEnemy(string name, EnemyWeapon weapon, int health, int posture, int cooldown, float speed, float lunge,
            bool guards, Strike[] opening, Strike[] followUp = null, Strike[] desperation = null)
        {
            Name = name;
            Weapon = weapon;
            Health = health;
            Posture = posture;
            Cooldown = cooldown;
            Speed = speed;
            Lunge = lunge;
            Guards = guards;
            Opening = opening;
            FollowUp = followUp;
            Desperation = desperation;
        }

        public string Name { get; }
        public EnemyWeapon Weapon { get; }
        internal int Health { get; }
        internal int Posture { get; }
        internal int Cooldown { get; }
        internal float Speed { get; }
        internal float Lunge { get; }
        internal bool Guards { get; }
        private Strike[] Opening { get; }
        private Strike[] FollowUp { get; }
        private Strike[] Desperation { get; }

        internal Strike[] Moves(int attack, int health) => Desperation != null && health * 2 <= Health
            ? Desperation : FollowUp != null && attack % 2 != 0 ? FollowUp : Opening;
    }

    public sealed class CombatMap
    {
        private CombatMap(CombatMapId id, string name, string advice, string bossAdvice, float gateDepth, int guards,
            MapEnemy patrol, MapEnemy boss)
        {
            Id = id;
            Name = name;
            Advice = advice;
            BossAdvice = bossAdvice;
            GateDepth = gateDepth;
            Guards = guards;
            Patrol = patrol;
            Boss = boss;
        }

        public CombatMapId Id { get; }
        public string Name { get; }
        public string Advice { get; }
        public string BossAdvice { get; }
        public float GateDepth { get; }
        public int Guards { get; }
        public MapEnemy Patrol { get; }
        public MapEnemy Boss { get; }

        internal CombatMap WithBoss(MapEnemy boss, string advice) =>
            new CombatMap(Id, Name, Advice, advice, GateDepth, Guards, Patrol, boss);

        public static IReadOnlyList<CombatMap> Routes { get; } = Array.AsReadOnly(new[]
        {
            new CombatMap(CombatMapId.CedarGrove, "Cedar Grove", "Deflect the quick cuts.", "Watch for a third cut at low health.", 16, 3,
                new MapEnemy("Grove Scout", EnemyWeapon.ShortSword, 42, 49, 44, 3.1f, .48f, false,
                    new[] { new Strike(23, 4, 22, 12, 17, 1.95f, 85) }),
                new MapEnemy("Twinblade Keeper", EnemyWeapon.TwinBlades, 116, 110, 55, 3f, .44f, false,
                    new[] { new Strike(42, 4, 34, 25, 25, 2.4f, 108) },
                    new[] { new Strike(26, 4, 5, 15, 17, 2.15f, 95), new Strike(13, 4, 27, 17, 20, 2.35f, 104) },
                    new[] { new Strike(26, 4, 4, 15, 17, 2.15f, 95), new Strike(13, 4, 7, 17, 20, 2.35f, 104),
                        new Strike(21, 5, 36, 24, 24, 2.55f, 120) })),
            new CombatMap(CombatMapId.SaltCauseway, "Salt Causeway", "Sidestep the long spear thrust.", "Step off the thrust line. Deflect the wide sweep.", 19, 2,
                new MapEnemy("Salt Lancer", EnemyWeapon.Spear, 50, 56, 53, 2.4f, .72f, false,
                    new[] { new Strike(41, 4, 28, 18, 20, 3.15f, 42) }),
                new MapEnemy("Breakwater Marshal", EnemyWeapon.Poleaxe, 138, 123, 62, 2.6f, .87f, false,
                    new[] { new Strike(49, 5, 15, 26, 26, 3.45f, 48), new Strike(28, 5, 34, 25, 26, 2.95f, 130) },
                    new[] { new Strike(57, 5, 42, 34, 29, 3.15f, 128) })),
            new CombatMap(CombatMapId.EmberFoundry, "Ember Foundry", "Flank shields or strike after the mace swings.", "Punish the long recovery after a hammer slam.", 21, 2,
                new MapEnemy("Furnace Guard", EnemyWeapon.ShieldMace, 57, 62, 69, 2.1f, .28f, true,
                    new[] { new Strike(38, 5, 32, 21, 21, 2.28f, 106) }),
                new MapEnemy("Cinder Smith", EnemyWeapon.Maul, 155, 135, 58, 2.25f, .55f, false,
                    new[] { new Strike(67, 7, 54, 39, 38, 2.85f, 126) },
                    new[] { new Strike(46, 5, 11, 24, 28, 2.55f, 115), new Strike(42, 7, 48, 36, 36, 2.9f, 130) }))
        });
    }
}
