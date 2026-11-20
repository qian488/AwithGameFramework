using Test.Application.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    public sealed partial class PassWorld
    {
        private void BuildRoute(CombatMap map)
        {
            float depth = map.GateDepth;
            float end = depth + 20;
            var palette = RoutePalette.For(map.Id);
            var ground = Material("Route floor", palette.Ground, 18, .24f);
            var structure = Material("Route structure", palette.Structure, 15, .34f);
            var highlight = Material("Route trim", palette.Trim, 9, .39f, palette.Glow);
            Box("Route foundation", new Vector3(0, -.35f, (end - 5) / 2), new Vector3(palette.Width * 2 + 3, .7f, end + 5), ground, true);
            Box("Route entrance", new Vector3(0, .9f, -5), new Vector3(palette.Width * 2 + 1, 1.8f, 1), structure, true);
            Box("Route end", new Vector3(0, 1.6f, end), new Vector3(palette.Width * 2 + 1, 3.2f, 1), structure, true);

            foreach (int side in new[] { -1, 1 })
            {
                Box("Route boundary", new Vector3(side * palette.Width, palette.WallHeight / 2, (end - 5) / 2),
                    new Vector3(.72f, palette.WallHeight, end + 5), structure, true);
                if (map.Id == CombatMapId.SaltCauseway)
                    Box("Sea surface", new Vector3(side * (palette.Width + 11), -.48f, end / 2), new Vector3(21.3f, .14f, end + 14), highlight);
                for (int section = 0; section < 12; section++)
                {
                    float z = section * (end + 3) / 12 - 3;
                    if (map.Id == CombatMapId.CedarGrove)
                    {
                        float height = Range(5, 8);
                        var trunk = new Vector3(side * (palette.Width + Range(.55f, 2f)), height / 2, z);
                        Shape("Cedar trunk", PrimitiveType.Cylinder, trunk, new Vector3(.65f, height / 2, .65f), structure, geometry);
                        for (int crown = 0; crown < 3; crown++)
                            Shape("Cedar crown", PrimitiveType.Sphere, trunk + Vector3.up * (height / 2 - 1.2f + crown),
                                new Vector3(4.6f - crown * .75f, 1.55f, 3.9f - crown * .55f), highlight, geometry);
                    }
                    else if (map.Id == CombatMapId.SaltCauseway)
                    {
                        Box("Breakwater pier", new Vector3(side * (palette.Width + .4f), .5f, z), new Vector3(1.4f, 3, 1.35f), structure);
                    }
                    else
                    {
                        Box("Foundry rib", new Vector3(side * (palette.Width + .15f), 3.15f, z), new Vector3(.87f, 6.3f, .65f), structure);
                        Box("Furnace light", new Vector3(side * (palette.Width - .41f), 1.58f, z), new Vector3(.12f, .4f, 1.32f), highlight);
                    }
                }
            }

            for (int row = 0; row < 19; row++)
            {
                float z = row * end / 19;
                if (map.Id == CombatMapId.CedarGrove)
                {
                    var slab = Box("Forest step", new Vector3(Mathf.Sin(row * .62f) * .55f, .015f, z), new Vector3(2.05f, .03f, 1.12f), structure);
                    slab.localRotation = Quaternion.Euler(0, Range(-14, 14), 0);
                }
                else
                {
                    float x = map.Id == CombatMapId.EmberFoundry ? 2.95f : 0;
                    Box("Lane seam", new Vector3(x, .014f, z), new Vector3(map.Id == CombatMapId.SaltCauseway ? 6.9f : .18f, .025f, .16f), highlight);
                    if (x > 0) Box("Lane seam", new Vector3(-x, .014f, z), new Vector3(.18f, .025f, .16f), highlight);
                }
            }

            float coverX = map.Id == CombatMapId.SaltCauseway ? 2.75f : 4.6f;
            Cover(-coverX, depth * .31f);
            Cover(coverX, depth * .69f);
            Cover(-coverX, depth + 7.5f);
            Cover(coverX, depth + 13);

            const float opening = 2.15f;
            float wing = palette.Width - opening;
            foreach (int side in new[] { -1, 1 })
            {
                Box("Checkpoint wing", new Vector3(side * (opening + wing / 2), 1.65f, depth), new Vector3(wing + .2f, 3.3f, 1.1f), structure, true);
                Box("Checkpoint beacon", new Vector3(side * (opening + .3f), 2.8f, depth - .59f), new Vector3(.34f, .74f, .16f), highlight);
            }
            Box("Checkpoint beam", new Vector3(0, 3.97f, depth), new Vector3(opening * 2 + 1.4f, .52f, .76f), structure);
            CourtGate = Group("Route checkpoint", Root.transform);
            CourtGate.localPosition = Vector3.forward * depth;
            var collider = CourtGate.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.up * 1.95f;
            collider.size = new Vector3(opening * 2 + .12f, 3.9f, .3f);
            for (int bar = -5; bar <= 5; bar++)
                Shape("Checkpoint bar", PrimitiveType.Cube, new Vector3(bar * .41f, 1.92f, 0), new Vector3(.13f, 3.84f, .26f), structure, CourtGate);
            Shape("Checkpoint brace", PrimitiveType.Cube, Vector3.up * 1.82f, new Vector3(opening * 2, .17f, .3f), highlight, CourtGate);
            RefreshLayoutDigest();

            void Cover(float x, float z)
            {
                bool furnace = map.Id == CombatMapId.EmberFoundry;
                float width = furnace ? 1.55f : map.Id == CombatMapId.CedarGrove ? 1.6f : 1.05f;
                var position = new Vector3(x, .97f, z);
                Box(furnace ? "Furnace stack" : "Stone cover", position, new Vector3(width, 1.94f, 1.25f), structure, true);
                Box(furnace ? "Kiln vent" : "Cover cap", position + Vector3.up * 1.02f, new Vector3(width + .2f, .17f, 1.4f), highlight);
            }
        }

        internal readonly struct RoutePalette
        {
            private RoutePalette(float width, float wallHeight, Color ground, Color structure, Color trim, Color glow, Color sky, Color key)
            {
                Width = width;
                WallHeight = wallHeight;
                Ground = ground;
                Structure = structure;
                Trim = trim;
                Glow = glow;
                Sky = sky;
                Key = key;
            }

            internal readonly float Width, WallHeight;
            internal readonly Color Ground, Structure, Trim, Glow, Sky, Key;

            internal static RoutePalette For(CombatMapId map)
            {
                if (map == CombatMapId.CedarGrove) return new RoutePalette(8.2f, 1.12f,
                    new Color(.16f, .23f, .16f), new Color(.29f, .23f, .16f), new Color(.31f, .48f, .19f), Color.black,
                    new Color(.27f, .40f, .34f), new Color(.82f, .97f, .72f));
                if (map == CombatMapId.SaltCauseway) return new RoutePalette(4.45f, .95f,
                    new Color(.52f, .51f, .45f), new Color(.61f, .65f, .64f), new Color(.17f, .38f, .44f), Color.black,
                    new Color(.52f, .68f, .76f), new Color(.89f, .96f, 1f));
                return new RoutePalette(10.4f, 5.15f,
                    new Color(.23f, .18f, .16f), new Color(.26f, .19f, .18f), new Color(.79f, .28f, .105f), new Color(.9f, .13f, .02f),
                    new Color(.27f, .15f, .12f), new Color(1f, .68f, .38f));
            }
        }
    }
}
