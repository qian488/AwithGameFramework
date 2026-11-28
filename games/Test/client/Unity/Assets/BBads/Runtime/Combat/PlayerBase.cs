using Test.Domain.BBads;
using System;
using BBads.Data;
using UnityEngine;

namespace BBads.Combat
{
    public sealed class PlayerBase : MonoBehaviour, ICombatTarget
    {
        [SerializeField, Min(1f)] float maxHealth = 1000f;
        [SerializeField] BoxCollider2D hitBox;
        [SerializeField] Vector2 attackPointOffset = new Vector2(0.25f, 0f);
        [SerializeField] BaseHealthBarView healthBar;
        [SerializeField] bool alwaysShowHealthBar = true;
        float currentHealth;

        public event Action<float, float> HealthChanged;
        public event Action Destroyed;

        public UnitCamp Camp => UnitCamp.Player;
        public Vector2 Position => transform.TransformPoint(attackPointOffset);
        public bool IsAlive => currentHealth > 0f;
        public float CurrentHealth => currentHealth;

        void Awake()
        {
            hitBox ??= GetComponent<BoxCollider2D>();
            healthBar ??= GetComponentInChildren<BaseHealthBarView>(true);
            if (hitBox == null)
                Debug.LogError("PlayerBase requires a BoxCollider2D hit box.", this);
            if (healthBar == null)
                Debug.LogError("PlayerBase requires a BaseHealthBarView child.", this);
            ResetHealth();
        }

        public void ResetHealth()
        {
            currentHealth = maxHealth;
            HealthChanged?.Invoke(currentHealth, maxHealth);
            healthBar?.SetValue(currentHealth, maxHealth);
            healthBar?.SetVisible(alwaysShowHealthBar);
        }

        public void ReceiveDamage(float amount)
        {
            if (!IsAlive || amount <= 0f)
                return;
            var damage = BBadsCombatRules.ApplyDamage(currentHealth, amount);
            currentHealth = damage.RemainingHealth;
            HealthChanged?.Invoke(currentHealth, maxHealth);
            healthBar?.SetValue(currentHealth, maxHealth);
            if (currentHealth > 0f)
                healthBar?.SetVisible(true);
            if (damage.Defeated)
                Destroyed?.Invoke();
        }

        public Vector2 ClosestPoint(Vector2 worldPosition)
        {
            return hitBox != null ? hitBox.ClosestPoint(worldPosition) : Position;
        }

        public float DistanceFrom(Vector2 worldPosition)
        {
            return Vector2.Distance(worldPosition, ClosestPoint(worldPosition));
        }

        void OnValidate()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            hitBox ??= GetComponent<BoxCollider2D>();
            healthBar ??= GetComponentInChildren<BaseHealthBarView>(true);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 attackPoint = transform.TransformPoint(attackPointOffset);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(attackPoint, 0.08f);
        }
    }
}
