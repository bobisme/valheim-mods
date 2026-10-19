using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Homespun
{
    // Nettle, the cloth plant you have from the start (Core/Chain.cs): found growing wild in meadow clearings and at the Black Forest's
    // edge, where it grows back after picking as the game's dandelions do; and sown with the cultivator from a nettle, in the Meadows and
    // the Black Forest, a field crop as the game's carrots are (picked, it's gone, giving 2 or 3). Greydwarfs now and then carry a bundle
    // too: the game only plants a zone the first time it's visited, so ground already explored has no wild nettle. All three are copies
    // of the game's own (Pickable_Dandelion, Pickable_Carrot, sapling_carrot) with Models/nettleplant in their place.
    internal static class Plants
    {
        internal const string Wild = Names.WildNettle, Crop = Names.NettleCrop, Sapling = Names.NettleSapling;
        internal static readonly string[] All = { Wild, Crop, Sapling };
        internal const float GrowSeconds = 3000, GrowSecondsMax = 4000; // (a weed: sooner than the game's crops, 4000-5000)
        internal const int CropGives = 2, WildGives = 1;                 // (and one more, half the time)
        internal const float WildRegrowMinutes = 300, GreydwarfChance = 0.1f;

        private static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<ZoneSystem.ZoneVegetation> Added = new List<ZoneSystem.ZoneVegetation>();
        private static CharacterDrop.Drop _drop;
        internal static GameObject Get(string name) => Made.TryGetValue(name, out GameObject go) ? go : null;
        internal static PieceTable Cultivator(ZNetScene scene) => scene?.GetPrefab("Cultivator")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        // After the items (the plants give and are sown from the nettle): built once, then (back) in the scene, the cultivator's menu,
        // the world's vegetation and the greydwarf's drops. Safe to repeat.
        internal static void Register(ZNetScene scene)
        {
            GameObject nettle = Things.Get(Names.Nettle);
            if (scene == null || nettle == null) return;
            GameObject wild = Get(Wild) ?? Make(scene, "Pickable_Dandelion", Wild, nettle, wildOne: true);
            GameObject crop = Get(Crop) ?? Make(scene, "Pickable_Carrot", Crop, nettle, wildOne: false);
            GameObject sapling = Get(Sapling) ?? Sow(scene, crop, nettle);
            PieceTable table = Cultivator(scene);
            foreach (GameObject go in new[] { wild, crop }) if (go != null) PieceReload.ReplacePrefab(scene, go, null);
            if (sapling != null) PieceReload.ReplacePrefab(scene, sapling, table);
            AddVegetation();
            AddDrop(scene, nettle);
        }

        // A pickable nettle: the game's own with our plant in its look. Wild, it's hidden when picked and grows back; a crop is removed
        // when picked (nothing to hide and no regrowth: the game's own crops are, where a hidden one would stay in the save for ever).
        private static GameObject Make(ZNetScene scene, string from, string name, GameObject nettle, bool wildOne)
        {
            GameObject src = scene.GetPrefab(from);
            if (src == null) { Plugin.Warn($"No {from} to make the {name} from"); return null; }
            GameObject go = Object.Instantiate(src, Things.Holder, false); go.name = name;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (Renderer r in go.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (MeshFilter m in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(m);
            GameObject look = Looks.Add(go.transform, scene, "nettleplant", "visual");
            // (what E finds, and what a scythe swings at: about the plant, as the game's crops have)
            var hit = go.AddComponent<CapsuleCollider>(); hit.radius = 0.25f; hit.height = 0.8f; hit.center = new Vector3(0, 0.4f, 0);
            Pickable p = go.GetComponent<Pickable>();
            p.m_itemPrefab = nettle;
            p.m_amount = wildOne ? WildGives : CropGives;
            p.m_extraDrops = OneMore(nettle);
            p.m_overrideName = "Nettle";
            p.m_hideWhenPicked = wildOne ? look : null;
            p.m_respawnTimeMinutes = wildOne ? WildRegrowMinutes : 0;
            Made[name] = go;
            return go;
        }
        private static DropTable OneMore(GameObject nettle)
        {
            var t = new DropTable { m_dropMin = 1, m_dropMax = 1, m_dropChance = 0.5f };
            t.m_drops.Add(new DropTable.DropData { m_item = nettle, m_stackMin = 1, m_stackMax = 1, m_weight = 1 });
            return t;
        }

        // The sapling: a nettle set in tilled ground with the cultivator, growing (young, then half grown; yellowed where it can't grow)
        // into the crop.
        private static GameObject Sow(ZNetScene scene, GameObject crop, GameObject nettle)
        {
            GameObject src = scene.GetPrefab("sapling_carrot");
            if (src == null || crop == null) { Plugin.Warn("No sapling_carrot to sow nettle from"); return null; }
            GameObject go = Object.Instantiate(src, Things.Holder, false); go.name = Sapling;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            Stage(scene, go.transform.Find("healthy"), 0.35f, false);
            Stage(scene, go.transform.Find("unhealthy"), 0.35f, true);
            Stage(scene, go.transform.Find("healthy_grown"), 0.7f, false);
            Stage(scene, go.transform.Find("unhealthy_grown"), 0.7f, true);
            Plant plant = go.GetComponent<Plant>();
            plant.m_name = "Nettle";
            plant.m_grownPrefabs = new[] { crop };
            plant.m_growTime = GrowSeconds; plant.m_growTimeMax = GrowSecondsMax;
            plant.m_biome = Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest;
            plant.m_needCultivatedGround = true;
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "Nettle";
            piece.m_description = "Stinging nettle, sown in tilled ground. Grows in the Meadows and the Black Forest, quicker than flax, into a stand that gives two or three.";
            piece.m_resources = new[] { new Piece.Requirement { m_resItem = nettle.GetComponent<ItemDrop>(), m_amount = 1, m_recover = false } };
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_icon = PictureOf(nettle) ?? piece.m_icon;
            Icon.Later(() => { if (go != null && PictureOf(nettle) is Sprite s) piece.m_icon = s; }); // (the item's picture, once it's drawn again)
            Made[Sapling] = go;
            return go;
        }
        private static Sprite PictureOf(GameObject item) => item?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_icons?.FirstOrDefault(s => s != null);
        private static void Stage(ZNetScene scene, Transform at, float scale, bool sick)
        {
            if (at == null) return; // (a game update renamed it: that stage keeps the carrot's look)
            for (int i = at.childCount - 1; i >= 0; i--) Object.DestroyImmediate(at.GetChild(i).gameObject);
            foreach (Renderer r in at.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (MeshFilter m in at.GetComponents<MeshFilter>()) Object.DestroyImmediate(m);
            at.localRotation = Quaternion.identity; at.localScale = Vector3.one;
            GameObject look = Looks.Add(at, scene, "nettleplant", "Model");
            if (look == null) return;
            look.transform.localScale = Vector3.one * scale;
            if (sick) Looks.Tint(look, new Color(1.25f, 1.05f, 0.45f)); // (yellowed)
        }

        // Wild: in meadow clearings (away from the trees) and along the Black Forest's edge, a few small stands to a zone. Only zones not
        // yet made get them (the game plants a zone once, when it's first visited).
        internal static void AddVegetation()
        {
            ZoneSystem zones = ZoneSystem.instance; GameObject wild = Get(Wild);
            if (zones == null || wild == null) return;
            if (zones.m_vegetation.Exists(v => v.m_prefab == wild)) return;
            zones.m_vegetation.RemoveAll(v => v.m_name != null && v.m_name.StartsWith("Homespun_nettle")); // (an earlier copy of the mod's)
            Added.Clear();
            foreach (var (name, biome, area, max, inForest) in new[]
            {
                ("Homespun_nettle_meadows", Heightmap.Biome.Meadows, Heightmap.BiomeArea.Everything, 1.5f, true),
                ("Homespun_nettle_forest_edge", Heightmap.Biome.BlackForest, Heightmap.BiomeArea.Edge, 3f, false), // (more tries: the edge is half other ground and the forest's trees block most spots)
            })
            {
                var v = new ZoneSystem.ZoneVegetation
                {
                    m_name = name, m_prefab = wild, m_enable = true, m_min = 0, m_max = max,
                    m_groupSizeMin = 2, m_groupSizeMax = 4, m_groupRadius = 3,
                    m_scaleMin = 0.8f, m_scaleMax = 1.15f, m_randTilt = 5,
                    m_biome = biome, m_biomeArea = area, m_blockCheck = true,
                    m_minAltitude = 1, m_maxAltitude = 80, m_maxTilt = 25,
                    // (the forest factor: low under trees. Clearings in the Meadows: where it's high. Untuned.)
                    m_inForest = inForest, m_forestTresholdMin = inForest ? 1.0f : 0, m_forestTresholdMax = inForest ? 10f : 1,
                };
                zones.m_vegetation.Add(v); Added.Add(v);
            }
        }

        private static void AddDrop(ZNetScene scene, GameObject nettle)
        {
            CharacterDrop drops = scene.GetPrefab("Greydwarf")?.GetComponent<CharacterDrop>();
            if (drops == null) return;
            drops.m_drops.RemoveAll(d => d == null || d.m_prefab == null || d.m_prefab != nettle && d.m_prefab.name == Names.Nettle); // (an earlier copy's)
            if (drops.m_drops.Any(d => d.m_prefab == nettle)) return;
            _drop = new CharacterDrop.Drop { m_prefab = nettle, m_chance = GreydwarfChance, m_amountMin = 1, m_amountMax = 2, m_levelMultiplier = false }; // (1 or 2: Game.ScaleDrops counts the max in, and scales it by the world's loot setting)
            drops.m_drops.Add(_drop);
        }

        // Unloading: out of the cultivator's menu, the vegetation and the drops; the prefabs stay registered (PieceReload, ItemReload: the
        // next copy replaces them by name).
        internal static void Unload()
        {
            if (Get(Sapling) is GameObject sapling) PieceReload.UnloadPiece(sapling, Cultivator(ZNetScene.instance));
            ZoneSystem.instance?.m_vegetation.RemoveAll(v => Added.Contains(v));
            Added.Clear();
            ZNetScene.instance?.GetPrefab("Greydwarf")?.GetComponent<CharacterDrop>()?.m_drops.Remove(_drop);
            _drop = null;
            Made.Clear();
        }
    }

    // The game builds its list of what grows where when it starts: ours go on the end of it.
    [HarmonyPatch(typeof(ZoneSystem), "Start")]
    internal static class ZoneStart { private static void Postfix() => Plugin.Safe("nettle", Plants.AddVegetation); }

    // A sapling holds what it grows into by reference: after a hot reload, the old copy's. Pointed at this copy's just before it grows.
    [HarmonyPatch(typeof(Plant), "Grow")]
    internal static class NettleGrows
    {
        private static void Prefix(Plant __instance)
        {
            if (__instance != null && Utils.GetPrefabName(__instance.gameObject) == Plants.Sapling && Plants.Get(Plants.Crop) is GameObject crop)
                __instance.m_grownPrefabs = new[] { crop };
        }
    }
}
