using System.Collections.Generic;
using UnityEngine;

namespace BBads.Infrastructure
{
    public sealed class PrefabPool
    {
        readonly GameObject prefab;
        readonly Transform root;
        readonly Stack<GameObject> inactive = new Stack<GameObject>();
        readonly HashSet<GameObject> active = new HashSet<GameObject>();
        int created;
        int rents;
        int returns;
        int peak;

        public (int Created, int Rents, int Returns, int Active, int Idle, int Peak) Counts =>
            (created, rents, returns, active.Count, inactive.Count, peak);

        public PrefabPool(GameObject prefab, Transform root)
        {
            this.prefab = prefab;
            this.root = root;
        }

        public GameObject Get()
        {
            GameObject instance = null;
            while (inactive.Count > 0 && instance == null)
                instance = inactive.Pop();
            if (instance == null)
            {
                instance = Object.Instantiate(prefab, root);
                created++;
            }
            active.Add(instance);
            rents++;
            peak = Mathf.Max(peak, active.Count);
            instance.SetActive(true);
            return instance;
        }

        public void Release(GameObject instance)
        {
            if (!active.Remove(instance) || instance == null)
                return;
            returns++;
            instance.SetActive(false);
            instance.transform.SetParent(root, false);
            inactive.Push(instance);
        }
    }
}

