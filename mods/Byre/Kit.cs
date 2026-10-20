using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // Everything Byre adds, made once per world from the game's own prefabs as the scene wakes (the scene's postfix; ObjectDB's register
    // for the items, whichever comes first; a once-a-second look in case both came before this mod), and put where the game finds it by
    // name: the scene (saved animals, items, troughs, racks, stalls), ObjectDB (bags, recipes), the hammer (the pieces), every zone's spawner (herds).
    internal static class Kit
    {
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        internal static Piece TroughPiece, RackPiece, StallPiece;
        internal static IEnumerable<Piece> Pieces => new[] { TroughPiece, RackPiece, StallPiece }.Where(x => x != null);
        private static GameObject _holder;
        private static ZNetScene _scene;
        private static float _next;
        private static bool _broken, _checked;
        private static readonly FieldInfo NamedField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly MethodInfo UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

        internal static IEnumerable<GameObject> ItemPrefabs => Items.All.Select(d => Made.TryGetValue(d.Name, out GameObject g) ? g : null).Where(g => g != null);

        internal static void Build(ZNetScene scene)
        {
            if (_broken || scene == null || Plugin.Instance == null) return;
            if (scene != _scene) { Forget(); _scene = scene; }
            if (Made.Count > 0) return;
            if (scene.GetPrefab("Boar") == null) return; // (a scene not yet holding the game's prefabs)
            try
            {
                if (_holder == null) { _holder = new GameObject("Byre prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
                Transform h = _holder.transform;
                // (items first: the animals eat, drop and are harvested for them by name)
                foreach (Items.Def d in Items.All) Keep(Items.Make(scene, h, d));
                Content.Items(ItemPrefabs);
                foreach (Species s in Species.All)
                {
                    GameObject body = Keep(Animals.Ragdoll(scene, h, s));
                    GameObject adult = Keep(Animals.Make(scene, h, s, false, null, body));
                    GameObject young = Keep(Animals.Make(scene, h, s, true, adult, null));
                    GameObject egg = s.Egg != null && Made.TryGetValue(s.Egg, out GameObject e) ? e : null;
                    Animals.Breed(adult, young, egg);
                    Items.Hatches(egg, young);
                }
                GameObject trough = Keep(Trough.Make(scene, h)), rack = Keep(Trough.MakeRack(scene, h)), stall = Keep(Barn.MakeStall(scene, h));
                TroughPiece = trough?.GetComponent<Piece>(); RackPiece = rack?.GetComponent<Piece>(); StallPiece = stall?.GetComponent<Piece>();
                foreach (Piece p in Pieces) Plugin.Instance.Synced.NeedsServer(p.gameObject);
                foreach (GameObject go in Made.Values) if (go.GetComponent<ItemDrop>() == null) Register(scene, go);
                Items.Recipes(Made, null);
                Animals.Spawns(h, Made);
                foreach (string name in PrefabNames.Shipped.Where(n => !Made.ContainsKey(n))) Plugin.Warn($"{name} wasn't made: anything of it in the world is lost when the area loads.");
                Plugin.Log($"Made {Made.Count} prefabs: {string.Join(", ", Made.Keys)}");
            }
            catch (System.Exception e) { _broken = true; Plugin.Instance.Logs.LogError(e); }
        }
        private static GameObject Keep(GameObject go) { if (go != null) Made[go.name] = go; return go; }
        private static void Register(ZNetScene scene, GameObject prefab)
        {
            scene.m_prefabs.RemoveAll(p => p == null || (p != prefab && p.name == prefab.name));
            if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
            ((Dictionary<int, GameObject>)NamedField.GetValue(scene))[prefab.name.GetStableHashCode()] = prefab;
        }

        // Once a second: made (if the hooks were missed), the trough, hay rack and stall divider in the hammer's menu, the herds on or off.
        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;
            if (Made.Count == 0 || scene != _scene) Build(scene);
            if (!_checked && ObjectDB.instance != null && Made.Count > 0)
            {
                _checked = true;
                if (ItemPrefabs.Any(i => !ObjectDB.instance.m_items.Contains(i))) { Content.Items(ItemPrefabs); Items.Recipes(Made, null); }
            }
            PieceTable hammer = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (hammer != null && Pieces.Any(x => !hammer.m_pieces.Contains(x.gameObject)))
            {
                foreach (Piece piece in Pieces.Where(x => !hammer.m_pieces.Contains(x.gameObject)))
                {
                    hammer.m_pieces.RemoveAll(p => p == null || p.name == piece.name);
                    hammer.m_pieces.Add(piece.gameObject);
                }
                if (Player.m_localPlayer is Player p) UpdateAvailablePieces?.Invoke(p, null);
            }
            if (Plugin.Instance == null) return;
            ServerSettings synced = Plugin.Instance.Synced;
            // (a server without Byre would lose every animal it doesn't know: no herds there)
            Animals.Settle(Plugin.Instance.Spawning.Value && synced.Server != ServerSettings.ServerMod.Missing && synced.Server != ServerSettings.ServerMod.Checking);
        }
        internal static void Broken() => _broken = true;

        private static void Forget()
        {
            Made.Clear(); TroughPiece = RackPiece = StallPiece = null; _checked = false;
            Animals.Forget(); Trough.Forget(); Livestock.ByAI.Clear();
            if (_holder != null) Object.Destroy(_holder); _holder = null;
            Models.Forget();
        }
        internal static void Unload()
        {
            Content.RemoveAll();
            if (ZNetScene.instance is ZNetScene scene)
            {
                var named = (Dictionary<int, GameObject>)NamedField.GetValue(scene);
                foreach (GameObject go in Made.Values.Where(g => g != null))
                {
                    scene.m_prefabs.Remove(go);
                    int hash = go.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject had) && had == go) named.Remove(hash);
                }
            }
            PieceTable hammer = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (hammer != null) foreach (Piece piece in Pieces) hammer.m_pieces.Remove(piece.gameObject);
            foreach (SpawnSystem s in Object.FindObjectsByType<SpawnSystem>(FindObjectsSortMode.None)) s.m_spawnLists.RemoveAll(l => l == null || (_holder != null && l.gameObject == _holder));
            Forget(); _scene = null;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class SceneWakes { private static void Postfix(ZNetScene __instance) => Plugin.Guard(() => Build(__instance)); }

        // (ObjectDB rebuilds its lookups here, at its Awake and CopyOtherDB: our items in its list first, or a bag holding wool loads empty)
        [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
        private static class Registers
        {
            private static void Prefix(ObjectDB __instance) => Plugin.Guard(() =>
            {
                if (Made.Count == 0) return;
                foreach (GameObject item in ItemPrefabs)
                {
                    __instance.m_items.RemoveAll(i => i == null || (i != item && i.name == item.name));
                    if (!__instance.m_items.Contains(item)) __instance.m_items.Add(item);
                }
                Content.ItemExtras(ItemPrefabs, __instance);
                Items.Recipes(Made, __instance);
            });
        }
    }
}
