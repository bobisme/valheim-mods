using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The lost, brought home: a soul Hel took walks as a draugr, marked on the map by name ("Ulf, who was lost"). Killed, it leaves
    // Ulf's remains; laid to rest in the colony's burial ground (use the remains there), their friends' grief ends and Hel is pleased.
    internal static class Lost
    {
        public const string VidKey = "jarldom_lostvid", ColonyKey = "jarldom_lostcolony";
        public const string RemainsKey = "jarldom_remains", RemainsName = "jarldom_remainsname", RemainsColony = "jarldom_remainscolony";
        public const int HelFavour = 15;
        private static float _next;
        private static readonly HashSet<string> Pinned = new HashSet<string>();
        private static readonly Dictionary<string, Vector3> PinAt = new Dictionary<string, Vector3>();

        public static string Who(ZDO z) { string n = z?.GetString(Draugr.NameKey, "") ?? ""; return n.StartsWith("Draugr ") ? n.Substring(7) : ""; }
        public static string Called(string name) => $"{name}, who was lost";

        // Every few seconds on each game: the lost who are loaded, pinned where they walk.
        public static void Tick()
        {
            if (Time.time < _next || Minimap.instance == null) return;
            _next = Time.time + 5f;
            foreach (Character ch in Character.GetAllCharacters())
            {
                if (ch == null || ch.IsDead() || ch.IsPlayer()) continue;
                ZDO z = ch.GetComponent<ZNetView>()?.GetZDO(); string name = Who(z);
                if (name == "") continue;
                string key = "lost:" + (z.GetString(VidKey, "") is string v && v != "" ? v : name);
                Net.PinHere(key, ch.transform.position, Called(name), true);
                Pinned.Add(key);
            }
            // Their remains, lying where they fell: pinned until picked up (a pin whose remains are gone, seen from near, comes off).
            var seen = new HashSet<string>();
            foreach (ItemDrop drop in Drops())
            {
                if (drop == null || !IsRemains(drop.m_itemData) || !(drop.GetComponent<ZNetView>() is ZNetView v && v.IsValid())) continue;
                string key = "remains:" + v.GetZDO().m_uid;
                Net.PinHere(key, drop.transform.position, $"{(drop.m_itemData.m_customData.TryGetValue(RemainsName, out string n) ? n : "The lost")}'s remains", true);
                Pinned.Add(key); seen.Add(key); PinAt[key] = drop.transform.position;
                if (drop.transform.Find("remains_glow") == null) Items.Glow(drop.transform); // (dropped before they glowed)
            }
            Player me = Player.m_localPlayer;
            foreach (string key in Pinned.Where(k => k.StartsWith("remains:") && !seen.Contains(k)).ToList())
                if (me != null && PinAt.TryGetValue(key, out Vector3 at) && Vector3.Distance(me.transform.position, at) < 40f) { Net.PinHere(key, Vector3.zero, "", false); Pinned.Remove(key); PinAt.Remove(key); }
        }
        public static void Unpin(ZDO z)
        {
            string name = Who(z); if (name == "") return;
            string key = "lost:" + (z.GetString(VidKey, "") is string v && v != "" ? v : name);
            Net.PinHere(key, Vector3.zero, "", false); Pinned.Remove(key);
        }
        public static void Forget() { Pinned.Clear(); PinAt.Clear(); }
        // (the game's own list of the items lying in the world: no search of the scene)
        private static readonly AccessTools.FieldRef<List<ItemDrop>> DropList = AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));
        private static IEnumerable<ItemDrop> Drops() => DropList() ?? new List<ItemDrop>();

        // At dusk on the colony's owner: one of the unburied, if none of theirs is about, walks back in at the colony's edge (Unburied).
        public static void Dusk(Colony c, int day)
        {
            var about = new HashSet<string>(Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead()).Select(ch => ch.GetComponent<ZNetView>()?.GetZDO()).Where(z => z != null && Who(z) != "").Select(z => z.GetString(VidKey, "")));
            string vid = Unburied.Walker(c.R.Roster, c.R.Flags, day, about, Random.value);
            RosterRow row = vid != null ? c.R.Row(vid) : null;
            if (row == null) return;
            Walk(c, row, day);
        }
        public static void Walk(Colony c, RosterRow row, int day)
        {
            Unburied.Walked(c.R.Flags, row.Vid, day); c.MarkDirty();
            Vector2 dir = Random.insideUnitCircle.normalized;
            Draugr.Raise(c, new VillagerState { Vid = row.Vid, Name = row.Name, Colony = c.Id, Worth = row.Worth }, c.Position + new Vector3(dir.x, 0, dir.y) * c.Radius);
            c.Tell($"{Called(row.Name)} walks again tonight, out at the colony's edge. Strike them down and lay their remains in the burial ground.", true);
        }

        // On the draugr's death (its owner's game): their remains, with their name and their colony's.
        public static void Remains(Character ch)
        {
            ZDO z = ch.GetComponent<ZNetView>()?.GetZDO(); string name = Who(z);
            if (name == "") return;
            string vid = z.GetString(VidKey, ""), colony = z.GetString(ColonyKey, "");
            if (vid == "") { var row = Colony.All.SelectMany(c => c.R.Roster.Select(r => (c, r))).FirstOrDefault(x => x.r.State == SoulState.Lost && x.r.Name == name); vid = row.r?.Vid ?? ""; colony = row.c?.Id ?? ""; } // (a draugr from before they were marked)
            GameObject prefab = Items.Remains; if (prefab == null) return;
            ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab; item.m_stack = 1;
            item.m_customData[RemainsKey] = vid; item.m_customData[RemainsName] = name; item.m_customData[RemainsColony] = colony;
            ItemDrop.DropItem(item, 1, ch.transform.position + Vector3.up, Quaternion.identity);
            Unpin(z);
        }
        public static bool IsRemains(ItemDrop.ItemData i) => i?.m_customData != null && i.m_customData.ContainsKey(RemainsKey);

        // Using the remains (a hotbar key, or right-click in the bag): in the burial ground, laid to rest.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class Use
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
            {
                if (!(__instance is Player p) || p != Player.m_localPlayer || !IsRemains(item)) return true;
                Plugin.Guard(() => LayToRest(p, item));
                return false;
            }
        }
        private static void LayToRest(Player p, ItemDrop.ItemData item)
        {
            string name = item.m_customData.TryGetValue(RemainsName, out string n) ? n : "the lost";
            Colony c = Colony.At(p.transform.position);
            if (c == null || c.RoomAt(p.transform.position + Vector3.up * 0.3f, RoomType.BurialGround) == null)
            { p.Message(MessageHud.MessageType.Center, $"Bring {name}'s remains to the colony's burial ground, and lay them to rest there."); return; }
            p.GetInventory().RemoveItem(item);
            Assets.Effect("vfx_ghost_death", p.transform.position + p.transform.forward);
            Net.Act(c, "rest", $"{(item.m_customData.TryGetValue(RemainsKey, out string v) ? v : "")}|{name}");
            p.Message(MessageHud.MessageType.Center, $"{name} is laid to rest. Hel keeps them now.");
        }
        // On the colony's owner: the saga, Hel's favour, and the grief of their friends ends (on whichever game owns each).
        public static void Rested(Colony c, string vid, string name, string who)
        {
            if (vid != "" && c.R.Flag("rested:" + vid) != 0) return;
            if (vid != "") c.R.Flags["rested:" + vid] = c.Day;
            Faith.Add(c, "hel", HelFavour, $"{who} laid {name} to rest");
            c.Log("rest", name, who);
            if (vid != "") foreach (RosterRow r in c.R.Living) Villagers.Command(r.Vid, "ungrieve", vid);
            c.MarkDirty();
        }
        public static bool Rests(Colony c, string vid) => c.R.Flag("rested:" + vid) != 0;
    }
}
