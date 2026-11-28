using UnityEngine;

namespace Test.Client.Combat
{
    public sealed partial class CombatShowcase
    {
        private PvpClientShowcase pvp;
        private Transform soloActors;

        internal PvpClientShowcase ActivePvp => pvp != null && pvp.isActiveAndEnabled ? pvp : null;

        private void DrawPvpEntry()
        {
            var matrix = GUI.matrix;
            float scale = Mathf.Min(1.4f, Screen.height / 690f, Screen.width / 730f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float top = (Screen.height / scale - Mathf.Min(Screen.height / scale - 64, 580)) / 2;
            bool selected = GUI.Button(new Rect(Screen.width / scale / 2 - 78, top + 25, 156, 32), "Online PvP");
            GUI.matrix = matrix;
            if (selected) OpenPvp();
        }

        public bool OpenPvp()
        {
            if (!IsReady || !IsTrialSelection || commission || playingReplay != null || trials?.IsSaving == true)
                return false;
            if (pvp == null)
            {
                var node = PassWorld.Group("Online PvP", world.Root.transform);
                node.gameObject.SetActive(false);
                pvp = node.gameObject.AddComponent<PvpClientShowcase>();
                pvp.Initialize(this, world, lens, characterScale);
            }
            soloActors = space.Fighter(0).Root.parent;
            soloActors.gameObject.SetActive(false);
            audio.Sleep();
            pvp.gameObject.SetActive(true);
            return true;
        }

        internal void ClosePvp()
        {
            if (ActivePvp == null) return;
            pvp.gameObject.SetActive(false);
            if (soloActors != null) soloActors.gameObject.SetActive(true);
            soloActors = null;
            ResetPresentation();
        }
    }
}
