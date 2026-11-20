namespace Test.Domain.Combat
{
    public sealed class TrialOpponent
    {
        public TrialOpponent(int id, string name, string description, int health, int posture, int attackIntervalTicks, Strike strike)
        {
            Id = id;
            Name = name;
            Description = description;
            Health = health;
            Posture = posture;
            AttackIntervalTicks = attackIntervalTicks;
            Strike = strike;
        }

        public int Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int Health { get; }
        public int Posture { get; }
        public int AttackIntervalTicks { get; }
        public Strike Strike { get; }
    }
}
