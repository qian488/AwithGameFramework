using System;

namespace Test.Domain.BBads
{
    public static class BBadsCombatRules
    {
        public static (float RemainingHealth, bool Defeated) ApplyDamage(float health, float amount)
        {
            if (health <= 0f || amount <= 0f)
                return (health, false);

            float remaining = Math.Max(0f, health - amount);
            return (remaining, remaining <= 0f);
        }
    }
}