using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // What a piece or an item is, decided one way for the whole mod (rooms, work, the gods, the feast), by what the game itself knows
    // of it rather than by its name where it can: a fire is one that warms as the game reckons warmth, a seat is one the game lets you
    // sit on. Every fire is treated alike, every seat, every table.
    internal static class Classes
    {
        private static readonly Dictionary<string, bool> WarmByPrefab = new Dictionary<string, bool>();
        // A fire that warms (campfire, hearth, bonfire, fire pit, brazier): a Fireplace with the game's heat area. Torches, sconces and
        // candles are lights.
        public static bool Warms(Component c)
        {
            if (c == null || c.GetComponent<Fireplace>() == null) return false;
            string n = Utils.GetPrefabName(c.gameObject);
            if (WarmByPrefab.TryGetValue(n, out bool w)) return w;
            w = c.GetComponentsInChildren<EffectArea>(true).Any(a => (a.m_type & EffectArea.Type.Heat) != 0);
            WarmByPrefab[n] = w;
            return w;
        }
        public static bool IsLight(Component c) => c != null && c.GetComponent<Fireplace>() != null && !Warms(c);
        public static bool Lit(Fireplace f) => f != null && (f.m_infiniteFuel || f.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(ZDOVars.s_fuel) > 0f);
        public static bool IsSeat(Piece p) => p != null && p.GetComponent<Chair>() != null;
        public static bool IsHighSeat(GameObject go) { string n = Utils.GetPrefabName(go).ToLowerInvariant(); return n.Contains("throne") || n == Pieces.GildedSeatName.ToLowerInvariant(); }
        // A table to eat at: not a crafting table, a map table or the food preparation table.
        public static bool IsTable(Piece p) { string n = Utils.GetPrefabName(p.gameObject).ToLowerInvariant(); return n.Contains("table") && p.GetComponent<CraftingStation>() == null && p.GetComponent<MapTable>() == null && !n.Contains("prep"); }
        public static bool IsRug(string n) => n.Contains("rug") || n.Contains("carpet");
        public static bool IsShutter(GameObject go) => Utils.GetPrefabName(go).ToLowerInvariant().Contains("window");
        // The warming fire nearest a point (lit ones first), inside an outline if one is given.
        public static Fireplace NearestFire(Vector3 at, float radius, IList<float> inside = null)
        {
            var list = new List<Piece>(); Piece.GetAllPiecesInRadius(at, radius, list);
            return list.Select(p => p.GetComponent<Fireplace>()).Where(f => f != null && Warms(f) && (inside == null || Geometry.Inside(inside, f.transform.position.x, f.transform.position.z)))
                .OrderBy(f => Lit(f) ? 0 : 1).ThenBy(f => Vector3.Distance(f.transform.position, at)).FirstOrDefault();
        }
        // Mead: what a fermenter makes (vanilla meads and wines, and any mod's), learned from the fermenters themselves.
        private static HashSet<string> _meads;
        public static bool IsMead(ItemDrop.ItemData i)
        {
            if (i?.m_dropPrefab == null) return false;
            if (_meads == null && ZNetScene.instance != null)
                _meads = new HashSet<string>(ZNetScene.instance.m_prefabs.Where(p => p != null && p.GetComponent<Fermenter>() != null)
                    .SelectMany(p => p.GetComponent<Fermenter>().m_conversion).Where(cv => cv.m_to != null).Select(cv => cv.m_to.name));
            return _meads != null && _meads.Contains(i.m_dropPrefab.name) || i.m_dropPrefab.name.StartsWith("dh_drink_");
        }
        public static void Forget() { WarmByPrefab.Clear(); _meads = null; }
        // Equipment by what the game says it is.
        public static bool IsWeapon(ItemDrop.ItemData i) { var t = i.m_shared.m_itemType; return t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.Bow; }
        public static bool IsEquipment(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return IsWeapon(i) || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs
                || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility || t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Ammo
                || t == ItemDrop.ItemData.ItemType.Torch || t == ItemDrop.ItemData.ItemType.Trinket || t == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
        }
    }
}
