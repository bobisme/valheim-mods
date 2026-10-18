using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // The feeding trough: a small container piece (a half-log on trestles, hay in it). Fill it with what the animals eat; a hungry tame
    // animal of any kind near it (ours or the game's: a boar, a wolf, a lox, each taking only its own food) walks over and eats one, as it
    // would from the ground. Marked "dh_notstorage" so other mods' craft-from-chests and auto-feeders leave it alone.
    internal sealed class Trough : MonoBehaviour
    {
        internal Container Box;
        private void Awake()
        {
            Box = GetComponent<Container>();
            ZNetView v = GetComponent<ZNetView>();
            if (v == null || v.GetZDO() == null) return;
            if (v.IsOwner() && !v.GetZDO().GetBool("dh_notstorage", false)) v.GetZDO().Set("dh_notstorage", true);
            Live<Trough>.Add(this);
        }
        private void OnDestroy() => Live<Trough>.Remove(this);

        // The trough's own item an animal eats (the first one it has that the animal eats), or null.
        internal ItemDrop.ItemData Food(List<ItemDrop> eats)
        {
            Inventory inv = Box != null ? Box.GetInventory() : null;
            if (inv == null || eats == null) return null;
            foreach (ItemDrop.ItemData i in inv.GetAllItems())
                foreach (ItemDrop e in eats) if (e != null && e.m_itemData.m_shared.m_name == i.m_shared.m_name) return i;
            return null;
        }

        internal static GameObject Make(ZNetScene scene, Transform holder)
        {
            GameObject from = scene.GetPrefab("piece_chest_wood");
            if (from == null) { Plugin.Warn("No piece_chest_wood to make the trough from."); return null; }
            Material wood = Wood(scene);
            GameObject go = Object.Instantiate(from, holder, false); go.name = "ByreTrough";
            foreach (Collider old in go.GetComponentsInChildren<Collider>(true).Where(x => !x.isTrigger)) Object.DestroyImmediate(old);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && (t.name.StartsWith("_snappoint") || t.CompareTag("snappoint"))).ToList()) Object.DestroyImmediate(t.gameObject);
            GameObject look = Models.Replace(go, "trough", wood);
            if (look == null) { Object.DestroyImmediate(go); return null; }
            int layer = LayerMask.NameToLayer("piece");
            Solid(go, layer, new Vector3(0, 0.22f, 0), new Vector3(1.85f, 0.44f, 0.5f)); // (the log on its trestles, knee high, as one block)
            Container c = go.GetComponent<Container>();
            c.m_name = "Trough"; c.m_width = 4; c.m_height = 1; c.m_open = null; c.m_closed = null; c.m_autoDestroyEmpty = false;
            if (go.GetComponent<WearNTear>() is WearNTear w) { w.m_new = look; w.m_worn = look; w.m_broken = look; w.m_fragmentRoots = new GameObject[0]; w.m_materialType = WearNTear.MaterialType.Wood; }
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "Feeding trough";
            piece.m_description = "A hollowed log on trestles. Put food in it: hungry tame animals near (twelve paces) come and eat, one at a time, each what it eats.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = Costs.FindStation("piece_workbench", out _);
            piece.m_resources = Costs.Parse((string)Plugin.Instance.TroughCost.DefaultValue, null, forRecipe: false) ?? new Piece.Requirement[0];
            Costs.Apply(piece, Plugin.Instance.TroughCost.Value, Plugin.Instance.Logs);
            piece.m_enabled = true;
            go.AddComponent<Trough>();
            Sprite icon = Icon.Make(go);
            if (icon != null) { piece.m_icon = icon; Models.Owned.Add(icon); Models.Owned.Add(icon.texture); }
            Icon.Later(() => { if (go != null && Icon.Make(go) is Sprite again) { piece.m_icon = again; Models.Owned.Add(again); Models.Owned.Add(again.texture); } });
            return go;
        }
        // wood_pole's piece material: what the trough and the goods draw with, wearing their own bake.
        internal static Material Wood(ZNetScene scene) => scene?.GetPrefab("wood_pole")?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
        private static void Solid(GameObject go, int layer, Vector3 at, Vector3 size)
        {
            var o = new GameObject("Collider") { layer = layer }; o.transform.SetParent(go.transform, false); o.transform.localPosition = at;
            o.AddComponent<BoxCollider>().size = size;
        }

        // ---- the animals' side: in the game's own "go and eat something" step, a trough as well as food on the ground ----
        private sealed class Seek { public float Next; public Trough To; }
        private static readonly Dictionary<MonsterAI, Seek> Seeking = new Dictionary<MonsterAI, Seek>();
        private static readonly AccessTools.FieldRef<MonsterAI, ItemDrop> ConsumeTarget = AccessTools.FieldRefAccess<MonsterAI, ItemDrop>("m_consumeTarget");
        private static readonly AccessTools.FieldRef<MonsterAI, Tameable> Tamable = AccessTools.FieldRefAccess<MonsterAI, Tameable>("m_tamable");
        private static readonly AccessTools.FieldRef<BaseAI, Character> Owner = AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
        private static readonly AccessTools.FieldRef<BaseAI, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<BaseAI, ZSyncAnimation>("m_animator");
        private static readonly Func<BaseAI, float, Vector3, float, bool, bool> MoveTo = AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(AccessTools.Method(typeof(BaseAI), "MoveTo"));
        private static readonly Action<BaseAI, Vector3> LookAt = AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookAt"));
        private static readonly List<Trough> Near = new List<Trough>();

        internal static void Forget() => Seeking.Clear();

        [HarmonyPatch(typeof(MonsterAI), "UpdateConsumeItem")]
        private static class Eat
        {
            private static bool Prefix(MonsterAI __instance, Humanoid humanoid, float dt, ref bool __result)
            {
                if (__instance.m_consumeItems == null || __instance.m_consumeItems.Count == 0 || ConsumeTarget(__instance) != null) return true;
                Character body = Owner(__instance); Tameable tame = Tamable(__instance);
                if (body == null || tame == null || !body.IsTamed()) return true;
                if (!Seeking.TryGetValue(__instance, out Seek s))
                {
                    if (Seeking.Count > 256) foreach (MonsterAI gone in Seeking.Keys.Where(k => k == null).ToList()) Seeking.Remove(gone); // (tames unloaded or dead)
                    Seeking[__instance] = s = new Seek { Next = Time.time + UnityEngine.Random.Range(0f, 10f) };
                }
                bool handled = false;
                Plugin.Guard(() => handled = Step(__instance, humanoid, body, tame, s, dt));
                if (!handled) return true;
                __result = true; return false;
            }
        }
        private static bool Step(MonsterAI ai, Humanoid humanoid, Character body, Tameable tame, Seek s, float dt)
        {
            if (s.To == null || s.To.Box == null)
            {
                s.To = null;
                if (Time.time < s.Next) return false;
                s.Next = Time.time + 10f; // (the game looks for food on the ground as often)
                if (!tame.IsHungry() || Live<Trough>.All.Count == 0) return false;
                Live<Trough>.Near(body.transform.position, Husbandry.TroughReach, Near);
                s.To = Near.Where(t => t.Food(ai.m_consumeItems) != null && Husbandry.SeeksTrough(true, true, Vector3.Distance(t.transform.position, body.transform.position)))
                    .OrderBy(t => Vector3.Distance(t.transform.position, body.transform.position)).FirstOrDefault();
                if (s.To == null) return false;
            }
            if (!tame.IsHungry()) { s.To = null; return false; }
            Vector3 at = s.To.transform.position;
            if (!MoveTo(ai, dt, at, Husbandry.TroughEatRange, false)) return true;
            LookAt(ai, at);
            if (!ai.IsLookingAt(at, 20f)) return true;
            Trough trough = s.To; Container box = trough.Box; s.To = null;
            if (ContainerAccess.InUse(box)) return true; // (someone has it open: not now, the next look)
            ContainerAccess.TakeOver(box);
            ItemDrop.ItemData food = trough.Food(ai.m_consumeItems); // (from what the owner had: it may be gone)
            if (food == null || !box.GetInventory().RemoveOneItem(food)) return true;
            ItemDrop eaten = food.m_dropPrefab != null ? food.m_dropPrefab.GetComponent<ItemDrop>() : ai.m_consumeItems.FirstOrDefault();
            ai.m_onConsumedItem?.Invoke(eaten);
            humanoid?.m_consumeItemEffects.Create(body.transform.position, Quaternion.identity);
            Anim(ai)?.SetTrigger("consume");
            return true;
        }
    }
}
