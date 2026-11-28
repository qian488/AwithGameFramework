using BBads.Data;
using UnityEngine;

namespace BBads.Combat
{
    public interface ICombatTarget
    {
        UnitCamp Camp { get; }
        Vector2 Position { get; }
        bool IsAlive { get; }
        void ReceiveDamage(float amount);
    }
}

