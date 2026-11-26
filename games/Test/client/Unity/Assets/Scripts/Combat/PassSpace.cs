using System.Collections.Generic;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using Numerics = System.Numerics;

namespace Test.Client.Combat
{
    internal sealed class PassSpace : ICombatSpace
    {
        private const int ArrowLayers = Physics.DefaultRaycastLayers | (1 << PassFighter.FighterLayer);
        private readonly PassWorld world;
        private readonly Transform actors;
        private readonly Material teal, vermilion, charcoal, black, steel, brass, flash, alert, routeCloth;
        private Transform live;
        private readonly PassFighter[] fighters = new PassFighter[4];
        private readonly RaycastHit[] rayHits = new RaycastHit[16];
        private readonly List<Transform> arrowModels = new List<Transform>(2);
        private int count;
        private Encounter encounter;

        private readonly float characterScale;

        internal PassSpace(PassWorld world, float characterScale)
        {
            this.world = world;
            this.characterScale = characterScale;
            actors = PassWorld.Group("Encounter actors", world.Root.transform);
            teal = world.Material("Petrol cloth", new Color(.11f, .32f, .37f), 4, .29f);
            vermilion = world.Material("Oxide cloth", new Color(.50f, .125f, .09f), 6, .26f);
            charcoal = world.Material("Warden lacquer", new Color(.16f, .13f, .17f), 9, .44f);
            black = world.Material("Lacquer", new Color(.16f, .22f, .24f), 3, .46f, default, .26f);
            steel = world.Material("Folded steel", new Color(.66f, .81f, .87f), 0, .65f, new Color(.035f, .055f, .07f), .35f);
            brass = world.Material("Rusted brass", new Color(.75f, .37f, .14f), 5, .5f, new Color(.14f, .042f, .012f));
            flash = world.Material("Cold blade", Color.white, 0, .7f, new Color(.49f, 1.67f, 2.3f));
            alert = world.Material("Hot blade", Color.white, 0, .7f, new Color(3.3f, .45f, .08f));
            if (world.Map != null)
                routeCloth = world.Material("Route uniform", PassWorld.RoutePalette.For(world.Map.Id).Trim, 6, .30f);
        }

        internal PassFighter Fighter(int actor) => fighters[actor];

        public void Reset(Encounter checkpoint, int actorCount)
        {
            if (live != null)
            {
                live.gameObject.SetActive(false);
                live.SetParent(null);
                PassWorld.Release(live.gameObject);
            }
            encounter = checkpoint;
            count = actorCount;
            live = PassWorld.Group(checkpoint.ToString(), actors);
            if (world.Map != null)
            {
                var map = world.Map;
                bool boss = checkpoint == Encounter.Gate;
                Add(0, 0, new Vector3(0, .02f, boss ? map.GateDepth + 2.9f : 1.6f));
                if (boss) Add(1, 2, new Vector3(0, .02f, map.GateDepth + 10.7f), map.Boss);
                else
                {
                    Add(1, 1, new Vector3(0, .02f, 8.4f), map.Patrol);
                    Add(2, 1, new Vector3(map.Guards == 3 ? -2.25f : 1.65f, .02f, 10.2f), map.Patrol);
                    if (count == 4) Add(3, 1, new Vector3(2.25f, .02f, 10.2f), map.Patrol);
                }
                SyncEncounter();
                return;
            }

            float start = checkpoint == Encounter.Practice ? 1.6f : checkpoint == Encounter.Courtyard ? 16.2f : 38f;
            Add(0, 0, new Vector3(0, .02f, start));
            if (checkpoint == Encounter.Practice) Add(1, 1, new Vector3(0, .02f, 8.4f));
            if (checkpoint == Encounter.Courtyard)
            {
                Add(1, 1, new Vector3(0, .02f, 25.5f));
                Add(2, 1, new Vector3(-3.25f, .02f, 27.1f));
                Add(3, 1, new Vector3(3.25f, .02f, 27.1f));
            }
            if (checkpoint == Encounter.Gate) Add(1, 2, new Vector3(0, .02f, 45.4f));
            SyncEncounter();
        }

        private void SyncEncounter()
        {
            Gates(false, 1);
            Physics.SyncTransforms();
            DrawArrows(System.Array.Empty<ArrowFlight>());
        }

        private void Add(int id, int role, Vector3 position, MapEnemy enemy = null)
        {
            var fighter = new PassFighter(world, live, role, characterScale * (role == 2 ? 1.2f : 1),
                role == 0 ? teal : enemy != null ? routeCloth : role == 1 ? vermilion : charcoal,
                black, steel, brass, role == 0 ? flash : alert, enemy);
            fighter.Root.localPosition = position;
            fighter.Root.localRotation = Quaternion.Euler(0, role == 0 ? 0 : 180, 0);
            fighters[id] = fighter;
        }

        public Numerics.Vector3 Position(int actor)
        {
            var position = fighters[actor].Root.localPosition;
            return new Numerics.Vector3(position.x, position.y, position.z);
        }

        internal Numerics.Vector3 AimPoint(Ray ray)
        {
            Vector3 point = ClosestArrowHit(ray, BowRules.AimDistance, out var hit) ? hit.point : ray.GetPoint(BowRules.AimDistance);
            return ToLocal(point);
        }

        public bool TraceArrow(Numerics.Vector3 from, Numerics.Vector3 to, out int target)
        {
            target = 0;
            Vector3 origin = world.Root.transform.TransformPoint(ToUnity(from));
            Vector3 offset = world.Root.transform.TransformPoint(ToUnity(to)) - origin;
            if (!ClosestArrowHit(new Ray(origin, offset), offset.magnitude, out var hit)) return false;
            for (int actor = 1; actor < count; actor++)
            {
                if (hit.collider != fighters[actor].Controller) continue;
                target = actor;
                break;
            }
            return true;
        }

        private bool ClosestArrowHit(Ray ray, float distance, out RaycastHit closest)
        {
            var hits = rayHits;
            int hitCount = Physics.RaycastNonAlloc(ray, hits, distance, ArrowLayers, QueryTriggerInteraction.Ignore);
            if (hitCount == hits.Length)
            {
                hits = Physics.RaycastAll(ray, distance, ArrowLayers, QueryTriggerInteraction.Ignore);
                hitCount = hits.Length;
            }
            float nearest = float.PositiveInfinity;
            closest = default;
            for (int index = 0; index < hitCount; index++)
            {
                var hit = hits[index];
                if (hit.collider == fighters[0].Controller || hit.distance >= nearest) continue;
                nearest = hit.distance;
                closest = hit;
            }
            return closest.collider != null;
        }

        internal void DrawArrows(IReadOnlyList<ArrowFlight> arrows)
        {
            for (int index = 0; index < arrows.Count; index++)
                DrawArrow(index, arrows[index].Position, arrows[index].Velocity);
            HideUnusedArrows(arrows.Count);
        }

        internal void DrawReplayArrows(IReadOnlyList<ReplayArrow> arrows)
        {
            for (int index = 0; index < arrows.Count; index++)
                DrawArrow(index, arrows[index].Position, arrows[index].Velocity);
            HideUnusedArrows(arrows.Count);
        }

        private void DrawArrow(int index, Numerics.Vector3 position, Numerics.Vector3 velocity)
        {
            if (index == arrowModels.Count)
            {
                var model = PassWorld.Group("Flying arrow", world.Root.transform);
                world.Shape("Shaft", PrimitiveType.Cube, new Vector3(0, 0, -.38f), new Vector3(.032f, .032f, .76f), brass, model);
                world.Shape("Head", PrimitiveType.Cube, Vector3.zero, new Vector3(.062f, .045f, .14f), steel, model);
                world.Shape("Fletching", PrimitiveType.Cube, new Vector3(0, 0, -.72f), new Vector3(.095f, .045f, .16f), teal, model);
                arrowModels.Add(model);
            }
            var visual = arrowModels[index];
            visual.localPosition = ToUnity(position);
            visual.localRotation = Quaternion.LookRotation(ToUnity(velocity));
            visual.gameObject.SetActive(true);
        }

        private void HideUnusedArrows(int visibleCount)
        {
            for (int index = visibleCount; index < arrowModels.Count; index++) arrowModels[index].gameObject.SetActive(false);
        }

        private Numerics.Vector3 ToLocal(Vector3 worldPoint)
        {
            var point = world.Root.transform.InverseTransformPoint(worldPoint);
            return new Numerics.Vector3(point.x, point.y, point.z);
        }

        private static Vector3 ToUnity(Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);

        public void Move(int actor, Numerics.Vector3 displacement)
        {
            var fighter = fighters[actor];
            if (!fighter.Controller.enabled) return;
            Vector3 offset = new Vector3(displacement.X, -.012f, displacement.Z);
            var next = fighter.Root.localPosition + offset;
            float lower = world.Map != null ? (encounter == Encounter.Gate ? world.Map.GateDepth + .7f : -4)
                : encounter == Encounter.Practice ? -4 : encounter == Encounter.Courtyard ? 12.65f : 34.65f;
            if (next.z < lower) offset.z = lower - fighter.Root.localPosition.z;
            fighter.Controller.Move(world.Root.transform.TransformDirection(offset));
        }

        public void Sweep(int actor, Numerics.Vector3 facing, Strike strike, List<int> contacts)
        {
            var attacker = fighters[actor];
            var direction = world.Root.transform.TransformDirection(new Vector3(facing.X, 0, facing.Z));
            float threshold = Mathf.Cos(strike.Arc * .5f * Mathf.Deg2Rad);
            for (int target = 0; target < count; target++)
            {
                if (target == actor) continue;
                var defender = fighters[target];
                if (!defender.Controller.enabled) continue;
                Vector3 offset = defender.Root.position - attacker.Root.position;
                offset.y = 0;
                if (offset.sqrMagnitude > strike.Reach * strike.Reach || Vector3.Dot(direction, offset.normalized) < threshold) continue;
                if (Physics.Linecast(attacker.Focus, defender.Focus, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                contacts.Add(target);
            }
        }

        internal void Gates(bool proceeding, float smoothing)
        {
            if (world.PracticeGate != null) Gate(world.PracticeGate, encounter > Encounter.Practice || encounter == Encounter.Practice && proceeding);
            Gate(world.CourtGate, encounter > Encounter.Courtyard || encounter == Encounter.Courtyard && proceeding);

            void Gate(Transform gate, bool open)
            {
                gate.GetComponent<BoxCollider>().enabled = !open;
                var position = gate.localPosition;
                position.y = Mathf.Lerp(position.y, open ? 4.75f : 0, smoothing);
                gate.localPosition = position;
            }
        }
    }
}
