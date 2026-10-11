using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // A feast in the great hall: called from the colony window when the hall's chests hold enough, eaten at dusk.
    internal static class Feast
    {
        public const int Cooldown = 3;
        public static string Ready(Colony c, out List<(Container chest, ItemDrop.ItemData item)> food, out List<(Container chest, ItemDrop.ItemData item)> mead)
        {
            food = new List<(Container, ItemDrop.ItemData)>(); mead = new List<(Container, ItemDrop.ItemData)>();
            Room hall = c.Best(RoomType.GreatHall);
            if (hall == null || !hall.Ok) return "a feast needs a working great hall";
            if (c.Day - c.R.LastFeast < Cooldown) return $"the last feast was {c.Day - c.R.LastFeast} days ago (wait {Cooldown})";
            int pop = Math.Max(1, c.Population);
            foreach (var x in Stock.Chests(c, c.Centre(hall)).Where(x => Geometry.Inside(hall.Xz, x.chest.transform.position.x, x.chest.transform.position.z)))
                foreach (ItemDrop.ItemData i in x.chest.GetInventory().GetAllItems())
                {
                    if (Stock.Edible(i)) food.Add((x.chest, i));
                    if (i.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("Mead")) mead.Add((x.chest, i));
                }
            int kinds = food.Select(f => f.item.m_shared.m_name).Distinct().Count(), portions = food.Sum(f => f.item.m_stack), meads = mead.Sum(m => m.item.m_stack);
            if (kinds < 3) return $"the hall's chests hold {kinds} kinds of food (a feast wants 3)";
            if (portions < 3 * pop) return $"{portions} portions of food in the hall (a feast for {pop} wants {3 * pop})";
            if (meads < pop) return $"{meads} meads in the hall (a feast for {pop} wants {pop})";
            return "";
        }
        public static void Call(Colony c, string who)
        {
            string why = Ready(c, out _, out _);
            if (why != "") { c.Tell("No feast: " + why + "."); return; }
            c.R.Flags["feastday"] = c.Day;
            c.MarkDirty();
            c.Tell($"{who} calls a feast in the hall tonight!", true);
            if (c.Hour >= Schedule.HallAt) Dusk(c, c.Day); // already evening: now
        }
        public static void Dusk(Colony c, int day)
        {
            if (c.R.Flag("feastday") != day) return;
            if (Ready(c, out var food, out var mead) != "" && c.R.LastFeast != day) { c.Tell("The feast was called off: the hall's chests ran short."); c.R.Flags.Remove("feastday"); return; }
            int pop = Math.Max(1, c.Population);
            Take(food, 3 * pop); Take(mead, pop);
            c.R.LastFeast = day; c.R.Flags.Remove("feastday");
            c.R.Deeds += Renown.Deed("feast");
            string glutton = null;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id))
            {
                Mood.Add(b.S, "feast", b.Now); Mood.Satisfy(b.S, Need.Food, 100); Mood.Satisfy(b.S, Need.Social, 40);
                Worth.Gain(b.S, "feast"); b.S.Bump("feasts");
                if (b.S.Has("glutton")) glutton = b.S.Name;
                if (b.S.Count("feasts") >= 5 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("feaster"); c.Log("byname", b.S.Name, b.S.Byname); }
                b.Dirty();
            }
            c.Log("feast", glutton ?? "", "");
            Compat.SkalToast(c);
        }
        private static void Take(List<(Container chest, ItemDrop.ItemData item)> from, int amount)
        {
            foreach (var (chest, item) in from.OrderBy(x => x.item.m_shared.m_food))
            {
                if (amount <= 0) break;
                if (ContainerAccess.InUse(chest)) continue;
                ContainerAccess.TakeOver(chest); // (may reload the chest: find the item again)
                ItemDrop.ItemData have = chest.GetInventory().GetAllItems().FirstOrDefault(i => i == item) ?? chest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality);
                if (have == null) continue;
                int n = Math.Min(amount, have.m_stack);
                if (chest.GetInventory().RemoveItem(have, n)) amount -= n;
            }
        }
    }

    // The Thing: every five days the colony's petitions, from what really happened; rulings change moods and opinions.
    internal static class Thing
    {
        public static void Call(Colony c, int day)
        {
            c.R.LastThing = day;
            var view = new ColonyView
            {
                Population = c.Population, HasBeautyRoom = c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.GreatHall && Tables.GradeRank(r.Grade) >= 2),
                HasShrine = c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.OdinShrine), MissedPaydays = c.R.MissedPaydays, Beds = c.Beds,
                Hungry = Villagers.All.Count(b => b.S.Colony == c.Id && b.S.Needs[(int)Need.Food] < 30) > c.Population / 2,
            };
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id))
            {
                if (b.S.Count("thefts") > 0 || b.S.Count("brawls") > 0) view.Troublemakers.Add((b.S.Name, b.S.Count("thefts"), b.S.Count("brawls")));
                if (b.S.Count("seen") > 0) view.Suspects.Add((b.S.Name, b.S.Count("seen")));
            }
            foreach (var feud in Villagers.All.Where(b => b.S.Colony == c.Id).SelectMany(b => b.S.Bonds.Where(kv => kv.Value < -50).Select(kv => (b.S.Name, Villagers.ByVid(kv.Key)?.S.Name))).Where(f => f.Item2 != null && string.CompareOrdinal(f.Item1, f.Item2) < 0).Distinct())
                view.Feuds.Add((feud.Item1, feud.Item2));
            var list = Petitions.Raise(new System.Random(c.Id.GetStableHashCode() ^ day), view);
            ByColony[c.Id] = list.ToList();
            c.Log("thing", "", list.Count == 0 ? "nobody spoke" : $"{list.Count} petition{(list.Count == 1 ? "" : "s")}");
            if (list.Count > 0) c.Tell($"The Thing meets: {list.Count} petition{(list.Count == 1 ? "" : "s")} wait at the high seat.", true);
            c.R.Flags["thingday"] = day;
            Save(c);
        }
        private static readonly Dictionary<string, List<Petition>> ByColony = new Dictionary<string, List<Petition>>();
        public static List<Petition> Pending(Colony c)
        {
            var l = Load(c); ByColony[c.Id] = l; // always from the record (ownership may have moved since)
            return l;
        }
        // Kept in the record's log as one text line per petition (so other games see them too).
        private const string Kind = "petition";
        private static void Save(Colony c)
        {
            var keep = ByColony.TryGetValue(c.Id, out var l) ? l : new List<Petition>();
            c.R.Log.RemoveAll(e => e.Kind == Kind);
            foreach (Petition p in keep) c.R.Add(new LogEntry(c.Now, c.Day, Kind, p.Id, p.About, string.Join("\u001f", p.Text, p.Yes, p.No, p.YesMood, p.NoMood, p.YesFavour, p.YesDeed)));
            c.MarkDirty();
        }
        private static List<Petition> Load(Colony c) => c.R.Log.Where(e => e.Kind == Kind).Select(e =>
        {
            string[] f = e.Text.Split('\u001f');
            int Num(int i) => f.Length > i && int.TryParse(f[i], out int v) ? v : 0;
            return new Petition { Id = e.A, About = e.B, Text = f.Length > 0 ? f[0] : "", Yes = f.Length > 1 ? f[1] : "Yes", No = f.Length > 2 ? f[2] : "No", YesMood = Num(3), NoMood = Num(4), YesFavour = Num(5), YesDeed = f.Length > 6 ? f[6] : "" };
        }).ToList();
        public static List<Petition> Shown(Colony c) => Load(c);
        public static void Rule(Colony c, int index, bool yes, string who)
        {
            var list = Pending(c);
            if (index < 0 || index >= list.Count) return;
            ByColony[c.Id] = list;
            Petition p = list[index];
            list.RemoveAt(index);
            int mood = yes ? p.YesMood : p.NoMood;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id))
            {
                Mood.Add(b.S, mood >= 0 ? "thingheard" : "thingignored", b.Now);
                b.S.Opinions[who] = Mathf.Clamp((b.S.Opinions.TryGetValue(who, out int o) ? o : 0) + mood, -100, 100);
                b.Dirty();
            }
            if (yes && p.YesFavour != 0) Faith.Add(c, "odin", p.YesFavour, "the Thing's word");
            if (yes && p.YesDeed != "") c.R.Deeds += Renown.Deed(p.YesDeed);
            if (yes && p.Id == "exile") { Brain b = Villagers.All.FirstOrDefault(x => x.S.Colony == c.Id && x.S.Name == p.About); if (b != null) { c.Log("exile", b.S.FullName, who); Villagers.Command(b.S.Vid, "dismiss", ""); } }
            if (yes && p.Id == "guardpay") c.R.Flags["guardraise"] = 1;
            if (yes && p.Id == "accuse") Saboteur.CastOut(c, p.About, who);
            if (yes && Promises.Contains(p.Id)) c.R.Flags["promise:" + p.Id] = c.Day + 5; // to be kept within five days
            c.Log("text", who, "", $"At the Thing, {who} said {(yes ? p.Yes.ToLower() : p.No.ToLower())}: \"{p.Text}\"");
            Save(c);
        }
        public static void Forget() => ByColony.Clear();

        // Promises made at the Thing, checked each dawn: kept is remembered well, broken (five days gone) badly.
        private static readonly string[] Promises = { "beauty", "shrine", "beds", "food", "wages" };
        public static void CheckPromises(Colony c, int day)
        {
            foreach (string key in c.R.Flags.Keys.Where(k => k.StartsWith("promise:")).ToList())
            {
                string id = key.Substring(8); int due = c.R.Flags[key];
                bool kept = id == "beauty" ? c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.GreatHall && Tables.GradeRank(r.Grade) >= 2)
                    : id == "shrine" ? c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.OdinShrine)
                    : id == "beds" ? c.Beds >= c.Population
                    : id == "food" ? day >= due - 3 && Villagers.All.Any(b => b.S.Colony == c.Id) && Villagers.All.Where(b => b.S.Colony == c.Id).All(b => b.S.Needs[(int)Need.Food] > 40) // (fed for a couple of days, not one morning)
                    : id == "wages" && c.R.MissedPaydays == 0 && c.R.LastPayday > due - 5;
                if (!kept && day < due) continue;
                c.R.Flags.Remove(key);
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, kept ? "promisekept" : "promisebroken", b.Now); b.Dirty(); }
                c.Log("text", "", "", kept ? $"The jarl kept the promise made at the Thing ({id})." : $"The promise made at the Thing ({id}) was broken.");
            }
        }
    }

    // Raids at the colony: the game's own events, found when they start near the pillars; the horn sounds, raids grow with renown.
    internal static class Defense
    {
        private static readonly Dictionary<string, (string name, int deathsBefore)> Running = new Dictionary<string, (string, int)>();
        public static void Tick(Colony c)
        {
            Plugin.Guard(() => Faith.Thunder(c));
            RandomEvent ev = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            bool near = ev != null && Utils.DistanceXZ(ev.m_pos, c.Position) < c.Radius + 80;
            bool siege = Compat.Siege(c.Position, c.Radius, out Vector3 stone);
            if (siege && !near) { near = true; ev = null; }
            if (near && !c.RaidOn)
            {
                c.RaidOn = true; c.RaidAt = ev != null ? ev.m_pos : stone;
                string name = ev == null ? "a siege at the Warstone" : Localization.instance.Localize(ev.GetHudText()) is string t && t.Length > 0 ? t : ev.m_name;
                Running[c.Id] = (name, c.R.Log.Count(e => e.Kind == "death" || e.Kind == "lost"));
                c.Log("raid", "", name);
                c.HornUntil = Time.time + 600;
                Assets.Effect("sfx_horn", c.Position);
            }
            else if (!near && c.RaidOn)
            {
                c.RaidOn = false; c.HornUntil = 0;
                int deaths = c.R.Log.Count(e => e.Kind == "death" || e.Kind == "lost");
                bool bought = c.R.Flags.Remove("tribute:bought"); // (paid off: nothing held, no deed)
                if (c.Population > 0 && !bought)
                {
                    string name = Running.TryGetValue(c.Id, out var r) ? r.name : "the raid"; // (a reload mid-raid forgets which)
                    c.R.Deeds += Renown.Deed("raidheld");
                    c.Log("raidheld", "", name);
                    foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "raidheld", b.Now); b.S.Bump("raids"); if (b.S.Prof == Prof.Guard && b.S.Count("raids") >= 3 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("defender"); c.Log("byname", b.S.Name, b.S.Byname); } b.Dirty(); }
                }
                Running.Remove(c.Id);
                Tribute.RaidOver(c);
            }
        }

        // Bigger raids for a colony Odin has noticed: the spawners a raid near a colony uses, scaled by renown.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetCurrentSpawners))]
        private static class Scale
        {
            private static readonly Dictionary<RandomEvent, List<SpawnSystem.SpawnData>> Made = new Dictionary<RandomEvent, List<SpawnSystem.SpawnData>>();
            private static void Postfix(RandEventSystem __instance, ref List<SpawnSystem.SpawnData> __result)
            {
                if (__result == null || Plugin.Instance == null || !Plugin.Instance.Raids.Value) return;
                RandomEvent ev = __instance.GetActiveEvent();
                if (ev == null) return;
                Colony c = Colony.Nearest(ev.m_pos, 200);
                if (c == null) return;
                float scale = Renown.RaidScale(c.Renown);
                if (scale <= 1.01f) return;
                if (!Made.TryGetValue(ev, out var list))
                {
                    if (Made.Count > 8) Made.Clear();
                    list = __result.Select(s => { var copy = s.Clone(); copy.m_maxSpawned = Mathf.CeilToInt(s.m_maxSpawned * scale); copy.m_groupSizeMax = Mathf.CeilToInt(s.m_groupSizeMax * Mathf.Sqrt(scale)); return copy; }).ToList();
                    Made[ev] = list;
                }
                __result = list;
            }
        }
        public static void Forget() => Running.Clear();
    }

    // A soul lost to Hel rises as one of the dead: a draugr wearing their name, carrying their hoard.
    internal static class Draugr
    {
        public const string NameKey = "jarldom_lost", HoardKey = "jarldom_hoard";
        public static void Raise(Colony c, VillagerState s, Vector3 at)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(s.Worth > 50 ? "Draugr_Elite" : "Draugr");
            if (prefab == null) return;
            Vector3 spot = Colony.Spot(at + UnityEngine.Random.insideUnitSphere * 6f, 4f) ?? at;
            GameObject d = Object.Instantiate(prefab, spot, Quaternion.identity);
            ZDO z = d.GetComponent<ZNetView>().GetZDO();
            z.Set(NameKey, "Draugr " + s.Name);
            z.Set(HoardKey, s.Hoard);
            Assets.Effect("vfx_ghost_death", spot);
        }
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class Named
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (__instance.IsPlayer() || VillagerPrefab.Is(__instance)) return;
                string n = __instance.GetComponent<ZNetView>()?.GetZDO()?.GetString(NameKey, "");
                if (!string.IsNullOrEmpty(n)) __result = n;
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Hoard
        {
            private static void Prefix(Character __instance)
            {
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !v.IsOwner()) return;
                int hoard = v.GetZDO().GetInt(HoardKey, 0);
                if (hoard <= 0) return;
                GameObject coins = ZNetScene.instance.GetPrefab("Coins");
                if (coins == null) return;
                for (int left = hoard; left > 0;)
                {
                    int n = Math.Min(left, coins.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                    GameObject drop = Object.Instantiate(coins, __instance.GetCenterPoint() + UnityEngine.Random.insideUnitSphere * 0.5f, Quaternion.identity);
                    drop.GetComponent<ItemDrop>().SetStack(n);
                    left -= n;
                }
            }
        }
    }
}
