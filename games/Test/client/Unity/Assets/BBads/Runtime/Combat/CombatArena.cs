using UnityEngine;

namespace BBads.Combat
{
    public sealed class CombatArena : MonoBehaviour
    {
        [SerializeField] Rect bounds = new Rect(-5f, -4f, 10f, 8f);

        public Rect Bounds => bounds;

        public void SetBounds(Rect value) => bounds = value;

        public Vector2 Clamp(Vector2 position)
        {
            return new Vector2(
                Mathf.Clamp(position.x, bounds.xMin, bounds.xMax),
                Mathf.Clamp(position.y, bounds.yMin, bounds.yMax));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}

