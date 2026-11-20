using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    internal sealed class PassFighter
    {
        internal const int FighterLayer = 2;
        private readonly Transform form, torso, weapon, arm, leftLeg, rightLeg, scarf;
        private readonly Transform bow, upperString, lowerString;
        private static readonly Vector3 UpperTip = new Vector3(0, .64f, -.10f);
        private static readonly Vector3 LowerTip = new Vector3(0, -.64f, -.10f);
        private readonly MeshRenderer signal;
        private readonly EnemyWeapon? enemyWeapon;
        private readonly Transform shield;
        private readonly float bladeLength;
        private bool hasPose;
        public readonly CharacterController Controller;

        internal PassFighter(PassWorld world, Transform parent, int role, float scale, Material cloth, Material plate,
            Material edge, Material accent, Material warning, MapEnemy enemy = null)
        {
            enemyWeapon = enemy?.Weapon;
            Root = PassWorld.Group(enemy?.Name ?? (role == 0 ? "Wayfarer" : role == 2 ? "Gate Warden" : "Watchman"), parent);
            Root.gameObject.layer = FighterLayer;
            Controller = Root.gameObject.AddComponent<CharacterController>();
            Controller.height = 1.85f;
            Controller.radius = .35f;
            Controller.center = Vector3.up * .94f;
            Controller.stepOffset = .29f;
            Controller.minMoveDistance = 0;
            form = PassWorld.Group("Articulated form", Root);
            form.localScale *= scale;
            torso = PassWorld.Group("Spine", form);
            Piece("Jacket", PrimitiveType.Sphere, new Vector3(0, 1.22f, 0), new Vector3(.75f, .84f, .44f), cloth, torso);
            Piece("Waist sash", PrimitiveType.Cube, new Vector3(0, .98f, .016f), new Vector3(.72f, .16f, .47f), accent, torso);
            Piece("Scaled breastplate", PrimitiveType.Cube, new Vector3(0, 1.43f, .12f), new Vector3(.6f, .38f, .36f), plate, torso);
            for (int scaleIndex = 0; scaleIndex < 3; scaleIndex++)
                Piece("Laced plate", PrimitiveType.Cube, new Vector3(0, 1.14f + scaleIndex * .12f, .304f), new Vector3(.52f + scaleIndex * .045f, .082f, .067f), edge, torso);
            Piece("Mask", PrimitiveType.Sphere, new Vector3(0, 1.83f, .02f), new Vector3(.43f, .46f, .39f), plate, torso);
            Piece("Visor slit", PrimitiveType.Cube, new Vector3(0, 1.86f, .214f), new Vector3(.25f, .045f, .04f), accent, torso);
            Piece("Rain hat", PrimitiveType.Cylinder, new Vector3(0, 2.01f, 0), new Vector3(role == 0 ? .79f : .56f, .052f, role == 0 ? .79f : .55f), cloth, torso);
            Piece("Crown", PrimitiveType.Sphere, new Vector3(0, 2.06f, 0), new Vector3(.48f, .25f, .48f), plate, torso);
            if (role == 2)
            {
                for (int horn = -1; horn <= 1; horn += 2)
                {
                    var crest = Piece("Warden crest", PrimitiveType.Cube, new Vector3(horn * .24f, 2.21f, .035f), new Vector3(.1f, .4f, .092f), accent, torso);
                    crest.localRotation = Quaternion.Euler(-18, 0, -horn * 33);
                }
            }
            scarf = PassWorld.Group("Trailing sash", torso);
            scarf.localPosition = new Vector3(0, 1.59f, -.24f);
            var flowing = Piece("Cloth tail", PrimitiveType.Cube, new Vector3(0, -.32f, -.23f), new Vector3(.24f, .59f, .063f), accent, scarf);
            flowing.localRotation = Quaternion.Euler(32, 0, 12);
            leftLeg = Leg(-1);
            rightLeg = Leg(1);
            arm = PassWorld.Group("Sword shoulder", torso);
            arm.localPosition = new Vector3(.39f, 1.51f, 0);
            Piece("Sword sleeve", PrimitiveType.Sphere, new Vector3(.04f, -.27f, .11f), new Vector3(.25f, .69f, .28f), cloth, arm);
            Piece("Pauldron", PrimitiveType.Cube, new Vector3(.03f, -.05f, .015f), new Vector3(.29f, .24f, .39f), plate, arm);
            Piece("Left sleeve", PrimitiveType.Sphere, new Vector3(-.4f, 1.24f, .055f), new Vector3(.26f, .64f, .3f), cloth, torso);
            Piece("Left pauldron", PrimitiveType.Cube, new Vector3(-.43f, 1.47f, .04f), new Vector3(.3f, .19f, .33f), plate, torso);
            weapon = PassWorld.Group("Sword wrist", arm);
            weapon.localPosition = new Vector3(.017f, -.54f, .19f);
            Piece("Wrapped hilt", PrimitiveType.Cube, new Vector3(0, 0, .11f), new Vector3(.105f, .12f, .31f), cloth, weapon);
            Piece("Guard", PrimitiveType.Cube, new Vector3(0, 0, .27f), new Vector3(.22f, .075f, .075f), accent, weapon);
            bladeLength = 1.31f;
            Vector3 signalPosition = new Vector3(0, .029f, .8f);
            Vector3 signalSize = new Vector3(.085f, .027f, 1f);
            if (enemyWeapon == EnemyWeapon.Spear || enemyWeapon == EnemyWeapon.Poleaxe)
            {
                bladeLength = enemyWeapon == EnemyWeapon.Spear ? 1.96f : 2.08f;
                Piece("Pole shaft", PrimitiveType.Cube, Vector3.forward * .78f, new Vector3(.08f, .08f, 1.95f), cloth, weapon);
                var head = Piece("Pole head", PrimitiveType.Cube, Vector3.forward * (bladeLength - .19f),
                    new Vector3(enemyWeapon == EnemyWeapon.Poleaxe ? .55f : .13f, .09f, .46f), edge, weapon);
                head.localRotation = Quaternion.Euler(0, enemyWeapon == EnemyWeapon.Poleaxe ? 19 : 0, 0);
                signalPosition = head.localPosition + Vector3.up * .055f;
                signalSize = new Vector3(.12f, .035f, .46f);
            }
            else if (enemyWeapon == EnemyWeapon.ShieldMace || enemyWeapon == EnemyWeapon.Maul)
            {
                bool maul = enemyWeapon == EnemyWeapon.Maul;
                bladeLength = maul ? 1.49f : .94f;
                Piece("Iron handle", PrimitiveType.Cube, Vector3.forward * (bladeLength / 2), new Vector3(.11f, .11f, bladeLength), plate, weapon);
                Piece(maul ? "Hammer head" : "Mace head", maul ? PrimitiveType.Cube : PrimitiveType.Sphere,
                    Vector3.forward * (bladeLength - .12f), maul ? new Vector3(.75f, .32f, .35f) : new Vector3(.34f, .34f, .36f), edge, weapon);
                signalPosition = new Vector3(0, .17f, bladeLength - .12f);
                signalSize = new Vector3(maul ? .72f : .28f, .065f, .27f);
                if (!maul)
                {
                    shield = PassWorld.Group("Shield arm", torso);
                    shield.localPosition = new Vector3(-.46f, 1.28f, .33f);
                    Piece("Shield", PrimitiveType.Cube, Vector3.zero, new Vector3(.55f, .82f, .12f), plate, shield);
                    Piece("Shield boss", PrimitiveType.Sphere, Vector3.forward * .072f, new Vector3(.23f, .23f, .13f), accent, shield);
                }
            }
            else
            {
                if (enemyWeapon == EnemyWeapon.ShortSword) bladeLength = .96f;
                Sword(weapon, bladeLength, role == 2 ? .14f : .083f);
                float edgeLength = bladeLength - .3f;
                signalPosition.z = .3f + edgeLength / 2;
                signalSize.z = edgeLength;
                if (enemyWeapon == EnemyWeapon.TwinBlades)
                {
                    var second = PassWorld.Group("Offhand blade", torso);
                    second.localPosition = new Vector3(-.46f, 1.06f, .17f);
                    second.localRotation = Quaternion.Euler(-22, -34, 7);
                    Sword(second, 1.1f, .10f);
                }
            }
            signal = Piece("Attack highlight", PrimitiveType.Cube, signalPosition, signalSize, warning, weapon).GetComponent<MeshRenderer>();
            signal.enabled = false;
            if (role == 0)
            {
                bow = PassWorld.Group("Longbow", torso);
                bow.localPosition = new Vector3(-.28f, 1.36f, .64f);
                var upperLimb = new Vector3(0, .32f, .12f);
                var lowerLimb = new Vector3(0, -.32f, .12f);
                BowPart("Upper inner limb", Vector3.zero, upperLimb, .066f, accent);
                BowPart("Upper outer limb", upperLimb, UpperTip, .051f, accent);
                BowPart("Lower inner limb", Vector3.zero, lowerLimb, .066f, accent);
                BowPart("Lower outer limb", lowerLimb, LowerTip, .051f, accent);
                BowPart("Grip", Vector3.down * .10f, Vector3.up * .10f, .088f, cloth);
                upperString = BowPart("Upper string", UpperTip, new Vector3(0, 0, -.10f), .016f, edge);
                lowerString = BowPart("Lower string", LowerTip, new Vector3(0, 0, -.10f), .016f, edge);
                bow.gameObject.SetActive(false);
            }

            Transform Piece(string name, PrimitiveType primitive, Vector3 position, Vector3 size, Material material, Transform parentTransform) =>
                world.Shape(name, primitive, position, size, material, parentTransform);

            void Sword(Transform grip, float length, float width)
            {
                float edgeLength = length - .3f;
                float center = .3f + edgeLength / 2;
                Piece("Sword spine", PrimitiveType.Cube, new Vector3(0, 0, center), new Vector3(width, .044f, edgeLength), plate, grip);
                Piece("Tempered edge", PrimitiveType.Cube, new Vector3(width * .42f, 0, center), new Vector3(.04f, .025f, edgeLength), edge, grip);
                var tip = Piece("Tip", PrimitiveType.Cube, Vector3.forward * length, new Vector3(.058f, .028f, .11f), edge, grip);
                tip.localRotation = Quaternion.Euler(0, 31, 0);
            }

            Transform BowPart(string name, Vector3 start, Vector3 end, float thickness, Material material)
            {
                var part = Piece(name, PrimitiveType.Cube, Vector3.zero, Vector3.one, material, bow);
                SetBowSegment(part, start, end, thickness);
                return part;
            }

            Transform Leg(int side)
            {
                var leg = PassWorld.Group("Hip", form);
                leg.localPosition = new Vector3(side * .19f, .92f, 0);
                Piece("Trousers", PrimitiveType.Sphere, new Vector3(0, -.28f, 0), new Vector3(.31f, .69f, .34f), cloth, leg);
                Piece("Shin guard", PrimitiveType.Cube, new Vector3(0, -.62f, .02f), new Vector3(.20f, .45f, .21f), plate, leg);
                Piece("Boot", PrimitiveType.Cube, new Vector3(0, -.83f, .10f), new Vector3(.23f, .15f, .35f), cloth, leg);
                return leg;
            }
        }

        internal Transform Root { get; }
        internal Vector3 SwordTip => weapon.TransformPoint(Vector3.forward * bladeLength);
        internal Vector3 SwordHeel => weapon.TransformPoint(new Vector3(0, 0, .32f));
        internal Vector3 Focus => Root.position + Vector3.up * 1.28f;

        internal void Pose(FighterView view, float time, float smoothing, bool usingBow = false)
        {
            var facing = new Vector3(view.Facing.X, 0, view.Facing.Z);
            Root.rotation = Quaternion.Slerp(Root.rotation, Quaternion.LookRotation(facing), hasPose ? smoothing : 1);
            var phase = view.Phase;
            float progress = view.PhaseProgress;
            float walk = view.Moving ? Mathf.Sin(time * 12) * 26 : 0;
            float lean = view.Moving ? 10 : 0;
            float swing = 17;
            float lift = -11;
            float twist = 0;
            if (phase == AttackPhase.Windup)
            {
                swing = Mathf.Lerp(21, -72, Mathf.SmoothStep(0, 1, progress));
                lift = Mathf.Lerp(-12, -68, progress);
                twist = Mathf.Lerp(0, -24, progress);
            }
            else if (phase == AttackPhase.Active)
            {
                swing = Mathf.Lerp(-77, 92, progress);
                lift = Mathf.Lerp(-53, 16, progress);
                twist = Mathf.Lerp(-24, 36, progress);
            }
            else if (phase == AttackPhase.Recovery)
            {
                swing = Mathf.Lerp(92, 17, progress);
                lift = Mathf.Lerp(16, -11, progress);
                twist = Mathf.Lerp(36, 0, progress);
            }
            if (view.Segment % 2 != 0 && phase != AttackPhase.Ready) { swing *= -1; twist *= -1; }
            if (phase != AttackPhase.Ready && (enemyWeapon == EnemyWeapon.Spear || enemyWeapon == EnemyWeapon.Poleaxe && view.AttackArc < 65))
            {
                swing = phase == AttackPhase.Windup ? Mathf.Lerp(12, -28, progress) : Mathf.Lerp(-12, 10, progress);
                lift = phase == AttackPhase.Windup ? Mathf.Lerp(-5, 12, progress) : -6;
                twist = phase == AttackPhase.Windup ? -17 * progress : 10;
            }
            if (phase != AttackPhase.Ready && enemyWeapon == EnemyWeapon.Maul)
            {
                swing *= .44f;
                lift = phase == AttackPhase.Windup ? Mathf.Lerp(-16, -121, progress)
                    : phase == AttackPhase.Active ? Mathf.Lerp(-108, 39, progress) : Mathf.Lerp(39, -11, progress);
            }
            if (shield != null)
            {
                shield.localPosition = view.Guarding ? new Vector3(-.29f, 1.36f, .45f) : new Vector3(-.49f, 1.06f, .29f);
                shield.localRotation = Quaternion.Euler(0, view.Guarding ? -8 : -34, view.Guarding ? 0 : -19);
            }
            if (view.Guarding) { swing = -55; lift = -77; twist = -8; }
            if (bow != null)
            {
                bow.gameObject.SetActive(usingBow);
                weapon.gameObject.SetActive(!usingBow);
            }
            if (usingBow)
            {
                float draw = phase == AttackPhase.Windup ? progress : 0;
                var nock = new Vector3(draw * .28f, 0, -.10f - draw * .44f);
                SetBowSegment(upperString, UpperTip, nock, .016f);
                SetBowSegment(lowerString, LowerTip, nock, .016f);
                swing = Mathf.Lerp(-16, -57, draw);
                lift = Mathf.Lerp(-28, -94, draw);
                twist = -12 * draw;
            }
            if (view.Broken) { lean = 44; lift = 12; }
            if (view.Dodging) lean = 44;
            if (!view.Vitality.Alive) lean = 88;
            torso.localRotation = Quaternion.Euler(lean, twist, view.Broken ? 15 : 0);
            arm.localRotation = Quaternion.Euler(lift, swing, view.Guarding ? 28 : -12);
            leftLeg.localRotation = Quaternion.Euler(-walk - (view.Broken ? 24 : 0), 0, 0);
            rightLeg.localRotation = Quaternion.Euler(walk + (view.Broken ? 18 : 0), 0, 0);
            scarf.localRotation = Quaternion.Euler(Mathf.Sin(time * 4.8f) * 10 + (view.Moving ? 28 : 0), 0, Mathf.Sin(time * 5.9f) * 15);
            form.localPosition = Vector3.up * (!view.Vitality.Alive ? -.35f : view.Dodging ? -.45f : view.Broken ? -.2f : Mathf.Abs(walk) * .0013f);
            signal.enabled = view.Vitality.Alive && (phase == AttackPhase.Active || phase == AttackPhase.Windup && progress > .70f);
            hasPose = true;
        }

        private static void SetBowSegment(Transform part, Vector3 start, Vector3 end, float thickness)
        {
            Vector3 direction = end - start;
            part.localPosition = (start + end) * .5f;
            part.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            part.localScale = new Vector3(thickness, direction.magnitude, thickness);
        }
    }
}
