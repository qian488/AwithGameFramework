using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    internal sealed class PassFighter
    {
        private readonly Transform form, torso, weapon, arm, leftLeg, rightLeg, scarf;
        private readonly MeshRenderer signal;
        private bool hasPose;
        public readonly CharacterController Controller;

        internal PassFighter(PassWorld world, Transform parent, int role, float scale, Material cloth, Material plate,
            Material edge, Material accent, Material warning)
        {
            Root = PassWorld.Group(role == 0 ? "Wayfarer" : role == 2 ? "Gate Warden" : "Watchman", parent);
            Root.gameObject.layer = 2;
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
            Piece("Sword spine", PrimitiveType.Cube, new Vector3(0, 0, .79f), new Vector3(role == 2 ? .14f : .083f, .044f, 1.015f), plate, weapon);
            Piece("Tempered edge", PrimitiveType.Cube, new Vector3(.034f, 0, .81f), new Vector3(.04f, .025f, 1.05f), edge, weapon);
            var tip = Piece("Tip", PrimitiveType.Cube, new Vector3(0, 0, 1.33f), new Vector3(.058f, .028f, .11f), edge, weapon);
            tip.localRotation = Quaternion.Euler(0, 31, 0);
            signal = Piece("Attack highlight", PrimitiveType.Cube, new Vector3(0, .029f, .8f), new Vector3(.085f, .027f, 1.0f), warning, weapon).GetComponent<MeshRenderer>();
            signal.enabled = false;

            Transform Piece(string name, PrimitiveType primitive, Vector3 position, Vector3 size, Material material, Transform parentTransform) =>
                world.Shape(name, primitive, position, size, material, parentTransform);

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
        internal Vector3 SwordTip => weapon.TransformPoint(new Vector3(0, 0, 1.31f));
        internal Vector3 SwordHeel => weapon.TransformPoint(new Vector3(0, 0, .32f));
        internal Vector3 Focus => Root.position + Vector3.up * 1.28f;

        internal void Pose(FighterView view, float time, float smoothing)
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
            if (view.Guarding) { swing = -55; lift = -77; twist = -8; }
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
    }
}
