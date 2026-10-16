using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Where Jarldom's own relics are found (Core's Relics): Freyja's golden wildflower now and then as a meadow flower is picked; Frigg's
    // golden spindle and Odin's raven feather in a chest of the world's own (a Black Forest burial chamber's, a Mistlands ruin's) the
    // first time it's opened; Thor's storm stone where the lightning strikes high on a peak in a storm.
    internal static class RelicFinds
    {
        public const float FlowerChance = 1f / 40f, SpindleChance = 0.2f, FeatherChance = 0.15f, StoneChance = 0.25f;
        public const float PeakHeight = 120f; // (over the sea: a mountain's top, not its foot)
        public const string RolledKey = "jarldom_relicroll";
        private static readonly string[] EffectNames = { "fx_Lightning" }; // (the look of it only: lightningAOE is the strike itself, and hurts)

        public static void Drop(string prefab, Vector3 at)
        {
            GameObject p = ObjectDB.instance?.GetItemPrefab(prefab);
            if (p == null) return;
            GameObject go = Object.Instantiate(p, at + Vector3.up * 0.4f, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            go.GetComponent<ItemDrop>()?.SetStack(1);
        }

        // A meadow flower picked: now and then a golden one with it.
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class Flower
        {
            private static void Postfix(Pickable __instance, Humanoid character, bool __result)
            {
                if (!__result || !(character is Player p) || p != Player.m_localPlayer) return;
                if (Utils.GetPrefabName(__instance.gameObject) != "Pickable_Dandelion") return;
                if (WorldGenerator.instance?.GetBiome(__instance.transform.position) != Heightmap.Biome.Meadows || Random.value >= FlowerChance) return;
                Drop(Relics.GoldenFlower, __instance.transform.position);
                p.Message(MessageHud.MessageType.Center, "Among the flowers, one of gold.");
            }
        }

        // A chest of the world's own, opened for the first time: in a Black Forest burial chamber, now and then Frigg's spindle; in the
        // Mistlands, Odin's raven feather.
        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Chests
        {
            private static void Prefix(Container __instance, Humanoid character)
            {
                if (!(character is Player p) || p != Player.m_localPlayer) return;
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !v.IsOwner() || __instance.GetComponent<Piece>() != null && __instance.GetComponent<Piece>().IsPlacedByPlayer()) return;
                if (v.GetZDO().GetBool(RolledKey)) return;
                v.GetZDO().Set(RolledKey, true);
                Vector3 at = __instance.transform.position;
                Heightmap.Biome biome = WorldGenerator.instance?.GetBiome(at) ?? Heightmap.Biome.None;
                bool inside = at.y > 3000f; // (a dungeon's rooms lie high over its door)
                string relic = biome == Heightmap.Biome.Mistlands && Random.value < FeatherChance ? Relics.RavenFeather
                    : biome == Heightmap.Biome.BlackForest && inside && Random.value < SpindleChance ? Relics.Spindle : null;
                GameObject prefab = relic != null ? ObjectDB.instance?.GetItemPrefab(relic) : null;
                if (prefab == null) return;
                if (__instance.GetInventory().CanAddItem(prefab, 1)) __instance.GetInventory().AddItem(prefab, 1);
            }
        }

        // A storm on the heights: once a storm, perhaps, the lightning strikes nearby and leaves a stone fused glassy.
        private static float _nextLook; private static string _storm = ""; private static bool _struck;
        public static void Tick()
        {
            if (Time.time < _nextLook) return;
            _nextLook = Time.time + 20f;
            Player p = Player.m_localPlayer;
            if (p == null || EnvMan.instance == null || ZoneSystem.instance == null) return;
            string env = EnvMan.instance.GetCurrentEnvironment()?.m_name ?? "";
            bool storm = env.IndexOf("thunder", System.StringComparison.OrdinalIgnoreCase) >= 0 || env.IndexOf("storm", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!storm) { _storm = ""; _struck = false; return; }
            if (_storm != env) { _storm = env; _struck = false; }
            if (_struck || p.transform.position.y - ZoneSystem.instance.m_waterLevel < PeakHeight) return;
            if (Random.value >= StoneChance) return;
            _struck = true;
            Vector3 at = p.transform.position + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(4f, 8f);
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            foreach (string e in EffectNames) if (Assets.Find(e) != null) { Assets.Effect(e, at); break; }
            Drop(Relics.StormStone, at);
            p.Message(MessageHud.MessageType.Center, "The lightning strikes close, and leaves something smoking in the snow.");
        }
    }
}
