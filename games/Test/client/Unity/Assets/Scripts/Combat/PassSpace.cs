using System.Collections.Generic;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using Numerics = System.Numerics;

namespace Test.Client.Combat
{
    internal sealed class PassSpace : ICombatSpace
    {
        private readonly PassWorld world;
        private readonly Transform actors;
        private readonly Material teal, vermilion, charcoal, black, steel, brass, flash, alert;
        private Transform live;
        private readonly PassFighter[] fighters = new PassFighter[4];
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
            Gates(false, 1);
            Physics.SyncTransforms();
        }

        private void Add(int id, int role, Vector3 position)
        {
            var fighter = new PassFighter(world, live, role, characterScale * (role == 2 ? 1.2f : 1),
                role == 0 ? teal : role == 1 ? vermilion : charcoal, black, steel, brass, role == 0 ? flash : alert);
            fighter.Root.localPosition = position;
            fighter.Root.localRotation = Quaternion.Euler(0, role == 0 ? 0 : 180, 0);
            fighters[id] = fighter;
        }

        public Numerics.Vector3 Position(int actor)
        {
            var position = fighters[actor].Root.localPosition;
            return new Numerics.Vector3(position.x, position.y, position.z);
        }

        public void Move(int actor, Numerics.Vector3 displacement)
        {
            var fighter = fighters[actor];
            if (!fighter.Controller.enabled) return;
            Vector3 offset = new Vector3(displacement.X, -.012f, displacement.Z);
            var next = fighter.Root.localPosition + offset;
            float lower = encounter == Encounter.Practice ? -4 : encounter == Encounter.Courtyard ? 12.65f : 34.65f;
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
            Gate(world.PracticeGate, encounter > Encounter.Practice || encounter == Encounter.Practice && proceeding);
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
