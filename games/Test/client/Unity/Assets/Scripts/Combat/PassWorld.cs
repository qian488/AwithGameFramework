using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Test.Application.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;
using Object = UnityEngine.Object;

namespace Test.Client.Combat
{
    public sealed partial class PassWorld : IDisposable
    {
        private readonly List<Object> owned = new List<Object>();
        private readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();
        private readonly Shader surface;
        private readonly Random random;
        private readonly Material stone, paver, wood, roof, grass, water, iron, paper, flame;
        private Transform geometry;

        internal PassWorld(Transform parent, Shader surface, int seed, int density, bool preview, CombatMap map = null)
        {
            this.surface = surface != null ? surface : throw new ArgumentNullException(nameof(surface));
            random = new Random(seed);
            Map = map;
            Root = new GameObject(map?.Name ?? (preview ? "Rain Pass Preview" : "Rain Pass Generated"));
            if (preview) Root.hideFlags = HideFlags.DontSave;
            Root.transform.SetParent(parent, false);
            geometry = Group("Pass architecture", Root.transform);
            stone = Material("Basalt", new Color(.35f, .39f, .43f), 12, .27f);
            paver = Material("Wet slate", new Color(.25f, .29f, .32f), 16, .64f);
            wood = Material("Red cedar", new Color(.26f, .095f, .074f), 20, .32f);
            roof = Material("Blue clay", new Color(.16f, .21f, .26f), 21, .39f);
            grass = Material("Black reed", new Color(.2f, .26f, .17f), 10, .19f);
            water = Material("Rainwater", new Color(.11f, .16f, .20f), 0, .95f, new Color(.018f, .033f, .045f), .54f);
            iron = Material("Grille", new Color(.24f, .30f, .32f), 22, .55f, default, .48f);
            paper = Material("Lamp paper", new Color(.9f, .38f, .12f), 8, .35f, new Color(.48f, .11f, .022f));
            flame = Material("Lamp core", Color.white, 0, .3f, new Color(2.8f, .95f, .27f));
            if (map == null) Build(Mathf.Clamp(density, 0, 120));
            else BuildRoute(map);
        }

        private PassWorld(GameObject prefab, Transform parent, Shader surface, int seed)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            this.surface = surface;
            random = new Random(seed);
            Root = UnityEngine.Object.Instantiate(prefab, parent, false);
            Root.name = prefab.name;
            ResolveGateReferences();
            RefreshLayoutDigest();
        }

        internal static PassWorld FromBakedPrefab(GameObject prefab, Transform parent, Shader surface, int seed)
        {
            return new PassWorld(prefab, parent, surface, seed);
        }

        public GameObject Root { get; }
        internal CombatMap Map { get; }
        public string LayoutDigest { get; private set; }
        public int ArchitectureCount { get; private set; }
        internal Transform PracticeGate { get; private set; }
        internal Transform CourtGate { get; private set; }

        private void Build(int density)
        {
            Box("Foundation", new Vector3(0, -.32f, 24), new Vector3(28, .64f, 65), stone, true);
            Box("Left wall", new Vector3(-9.6f, 1.65f, 24), new Vector3(1.05f, 3.3f, 63), stone, true);
            Box("Right wall", new Vector3(9.6f, 1.65f, 24), new Vector3(1.05f, 3.3f, 63), stone, true);
            Box("Entrance wall", new Vector3(0, 1.2f, -4.5f), new Vector3(19, 2.4f, .9f), stone, true);
            Box("End wall", new Vector3(0, 1.8f, 53), new Vector3(19, 3.6f, 1), stone, true);

            for (int row = 0; row < 37; row++)
            {
                for (int lane = -2; lane <= 2; lane++)
                {
                    float offset = row % 2 == 0 ? 0 : .22f;
                    var tile = Box("Slate flag", new Vector3(lane * 1.57f + offset, .019f, row * 1.52f - 3),
                        new Vector3(1.49f, .033f, 1.45f), paver);
                    tile.localRotation = Quaternion.Euler(0, Range(-1.5f, 1.5f), 0);
                }
            }

            for (int section = 0; section < 15; section++)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float depth = section * 4 - 3;
                    Box("Rampart cap", new Vector3(side * 9.57f, 3.43f, depth), new Vector3(1.55f, .32f, 3.95f), roof);
                    Box("Buttress", new Vector3(side * 8.88f, 1.48f, depth), new Vector3(.48f, 2.96f, .55f), stone);
                    Box("Rampart trim", new Vector3(side * 9.03f, 2.74f, depth), new Vector3(.17f, .12f, 3.95f), wood);
                    if (section % 2 == 0) Lantern(new Vector3(side * 6.2f, 0, depth + 1), section % 4 == 0);
                    if (section % 3 == 0) Banner(new Vector3(side * 8.6f, 1.75f, depth + 1), side);
                }
            }

            PracticeGate = Gate(12, "Practice gate", 5.8f);
            CourtGate = Gate(34, "Court gate", 6.2f);
            Gate(51, "Summit gate", 7.3f);
            for (int detail = 0; detail < density; detail++)
            {
                float side = random.Next(0, 2) == 0 ? -1 : 1;
                float depth = Range(-3, 51);
                var position = new Vector3(side * Range(5.25f, 8.6f), .05f, depth);
                var pool = Shape("Water decal", PrimitiveType.Cylinder, position, new Vector3(Range(.35f, 2.1f), .009f, Range(.5f, 2.4f)), water, geometry);
                pool.localRotation = Quaternion.Euler(0, Range(0, 180), 0);
                position.x += side * .1f;
                position.y = .18f;
                var debris = Box("Broken stone", position, new Vector3(Range(.16f, .56f), Range(.16f, .48f), Range(.17f, .58f)), stone);
                debris.localRotation = Quaternion.Euler(Range(-18, 18), Range(0, 180), Range(-22, 22));
                if (detail % 2 != 0) continue;
                for (int blade = 0; blade < 3; blade++)
                {
                    var reed = Box("Reed", position + new Vector3(Range(-.2f, .2f), .12f, Range(-.2f, .2f)),
                        new Vector3(.041f, Range(.32f, .85f), .055f), grass);
                    reed.localRotation = Quaternion.Euler(Range(-25, 25), Range(0, 180), Range(-25, 25));
                }
            }

            for (int ridge = 0; ridge < 14; ridge++)
            {
                float side = ridge % 2 == 0 ? -1 : 1;
                var cliff = Shape("Mountain silhouette", PrimitiveType.Sphere,
                    new Vector3(side * Range(17, 40), -3, ridge * 7 - 12), new Vector3(Range(11, 30), Range(15, 34), Range(9, 19)), roof, geometry);
                cliff.localRotation = Quaternion.Euler(Range(-20, 20), Range(0, 180), Range(-25, 25));
            }

            RefreshLayoutDigest();
        }

        private void ResolveGateReferences()
        {
            var transforms = Root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == "Practice gate") PracticeGate = transforms[index];
                if (transforms[index].name == "Court gate") CourtGate = transforms[index];
            }
        }

        private void RefreshLayoutDigest()
        {
            var layout = new StringBuilder();
            var nodes = Root.GetComponentsInChildren<Transform>(true);
            ArchitectureCount = nodes.Length - 1;
            for (int index = 1; index < nodes.Length; index++)
            {
                var node = nodes[index];
                layout.Append(node.name).Append(node.localPosition.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(node.localRotation.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(node.localScale.ToString("F3", CultureInfo.InvariantCulture)).AppendLine();
            }
            using (var hash = System.Security.Cryptography.SHA256.Create())
                LayoutDigest = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(layout.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private void Lantern(Vector3 position, bool lit)
        {
            Box("Lantern post", position + Vector3.up * 1.12f, new Vector3(.12f, 2.24f, .12f), wood);
            Box("Lantern roof", position + Vector3.up * 2.45f, new Vector3(.49f, .14f, .49f), iron);
            Box("Lantern glow", position + Vector3.up * 2.18f, new Vector3(.33f, .42f, .33f), paper);
            Box("Lantern core", position + Vector3.up * 2.19f, new Vector3(.35f, .09f, .35f), flame);
            if (!lit) return;
            var lamp = Group("Lantern light", geometry);
            lamp.localPosition = position + Vector3.up * 2.18f;
            var light = lamp.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .40f, .14f);
            light.intensity = 2.1f;
            light.range = 9;
            light.renderMode = LightRenderMode.ForceVertex;
        }

        private void Banner(Vector3 position, int side)
        {
            Box("Banner mount", position + Vector3.up * 1.2f, new Vector3(.24f, .15f, 1.1f), iron);
            Box("Cedar cloth", position + Vector3.up * .31f, new Vector3(.08f, 1.72f, .94f), wood);
            var stripe = Box("Banner crest", position + new Vector3(-side * .05f, .58f, 0), new Vector3(.025f, .43f, .46f), paper);
            stripe.localRotation = Quaternion.Euler(45, 0, 0);
        }

        private Transform Gate(float depth, string name, float height)
        {
            foreach (int side in new[] { -1, 1 })
            {
                Box("Gate flank", new Vector3(side * 6.4f, 1.46f, depth), new Vector3(5.8f, 2.92f, 1.02f), stone, true);
                Box("Gate column", new Vector3(side * 3.36f, height * .46f, depth), new Vector3(.75f, height * .92f, .84f), wood, true);
                Box("Gate pedestal", new Vector3(side * 3.36f, .35f, depth), new Vector3(1.02f, .7f, 1.06f), stone);
                Lantern(new Vector3(side * 4.25f, 0, depth - .78f), true);
            }
            Box("Gate lintel", new Vector3(0, height * .77f, depth), new Vector3(7.7f, .32f, .85f), wood);
            for (int span = -8; span <= 8; span++)
            {
                float rise = Mathf.Pow(Mathf.Abs(span) / 8f, 3) * .33f;
                for (int course = -1; course <= 1; course++)
                {
                    var tile = Box("Ceramic eave", new Vector3(span * .53f, height + rise - Mathf.Abs(course) * .22f, depth + course * .46f),
                        new Vector3(.55f, .20f, .61f), roof);
                    tile.localRotation = Quaternion.Euler(course * 20, 0, span * 1.1f);
                }
            }
            var grille = Group(name, Root.transform);
            grille.localPosition = new Vector3(0, 0, depth);
            var collider = grille.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(6.1f, 3.9f, .28f);
            collider.center = Vector3.up * 1.95f;
            for (int bar = -8; bar <= 8; bar++)
                Shape("Iron bar", PrimitiveType.Cube, new Vector3(bar * .36f, 1.88f, 0), new Vector3(.09f, 3.76f, .12f), iron, grille);
            for (int brace = 0; brace < 3; brace++)
                Shape("Iron brace", PrimitiveType.Cube, new Vector3(0, .45f + brace * 1.35f, 0), new Vector3(6.1f, .13f, .16f), iron, grille);
            return grille;
        }

        internal static Transform Group(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private Transform Box(string name, Vector3 position, Vector3 scale, Material material, bool solid = false)
        {
            var shape = Shape(name, PrimitiveType.Cube, position, scale, material, geometry);
            if (solid) shape.gameObject.AddComponent<BoxCollider>();
            return shape;
        }

        internal Transform Shape(string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            if (!meshes.TryGetValue(primitive, out var mesh))
            {
                var template = GameObject.CreatePrimitive(primitive);
                mesh = template.GetComponent<MeshFilter>().sharedMesh;
                template.SetActive(false);
                Release(template);
                meshes.Add(primitive, mesh);
            }
            var shape = Group(name, parent);
            shape.localPosition = position;
            shape.localScale = scale;
            shape.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = shape.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = scale.sqrMagnitude < 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return shape;
        }

        internal Material Material(string name, Color color, int noise, float gloss, Color glow = default, float metal = 0)
        {
            var material = new Material(surface) { name = name, enableInstancing = true, color = color };
            material.SetFloat("_Smoothness", gloss);
            material.SetFloat("_Metallic", metal);
            material.SetColor("_Glow", glow);
            owned.Add(material);
            if (noise > 0)
            {
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name + " generated grain", wrapMode = TextureWrapMode.Repeat };
                var pixels = new Color32[size * size];
                for (int index = 0; index < pixels.Length; index++)
                {
                    int across = index % size;
                    int down = index / size;
                    float grain = Mathf.PerlinNoise(across * .19f + noise, down * .11f) * .56f + Mathf.PerlinNoise(across * .42f, down * .6f + noise) * .32f;
                    byte shade = (byte)(132 + grain * 122 - (down % 16 == 0 ? 20 : 0));
                    pixels[index] = new Color32(shade, shade, shade, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply(true, true);
                material.mainTexture = texture;
                owned.Add(texture);
            }
            return material;
        }

        internal float Range(float minimum, float maximum) => minimum + (float)random.NextDouble() * (maximum - minimum);
        internal void Own(Object value) => owned.Add(value);

        public void Dispose()
        {
            if (Root != null) Root.SetActive(false);
            Release(Root);
            foreach (var item in owned) Release(item);
            owned.Clear();
        }

        internal static void Release(Object value)
        {
            if (value == null) return;
            if (UnityEngine.Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
