using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    internal static class Zones
    {
        public static Vector3 Centre(Zone z) { var (x, cz) = Geometry.Centre(z.Xz); return new Vector3(x, z.Floor, cz); }
        public static float Reach(Zone z) { var (x, cz) = Geometry.Centre(z.Xz); float r = 0; for (int i = 0; i < z.Corners; i++) r = Mathf.Max(r, Vector2.Distance(new Vector2(x, cz), new Vector2(z.Xz[i * 2], z.Xz[i * 2 + 1]))); return r; }
        public static bool Inside(Zone z, Vector3 p) => Geometry.Inside(z.Xz, p.x, p.z);
        // Work's edge is fuzzy: what a zone's work leaves just outside it (a felled tree rolling out, a stump) is still its work, and a
        // tree or rock a little past the line may be, more often the nearer it is (the same answer every time for the same thing).
        // How far past the line each kind reaches, for what's left lying and for what still stands.
        public static (float left, float standing) Fuzz(ZoneType t) =>
            t == ZoneType.Woodlot ? (20f, 6f) : t == ZoneType.Mine ? (12f, 4f) : t == ZoneType.Foraging ? (0f, 4f) : t == ZoneType.Hunting ? (0f, 10f) : (0f, 0f);
        public static bool Works(Zone z, Vector3 p, bool leftLying, int seed)
        {
            if (Inside(z, p)) return true;
            var (left, standing) = Fuzz(z.Type);
            float reach = leftLying ? left : standing * (0.35f + 0.65f * ((seed & 0x7fffffff) % 1000) / 1000f);
            return reach > 0 && Geometry.Distance(z.Xz, p.x, p.z) <= reach;
        }
        public static float FuzzReach(Zone z) { var (l, s) = Fuzz(z.Type); return Mathf.Max(l, s); }
        // Does the zone reach water (for a fishing dock)? Looked at once a minute a zone.
        private static readonly Dictionary<string, (bool wet, float at)> Wet = new Dictionary<string, (bool, float)>();
        public static bool HasWater(Zone z)
        {
            if (Wet.TryGetValue(z.Id, out var w) && Time.time - w.at < 60f) return w.wet;
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            bool wet = Geometry.Grid(z.Xz, 2f).Any(p => ZoneSystem.instance.GetGroundHeight(new Vector3(p.Item1, 0, p.Item2)) < sea - 0.5f);
            Wet[z.Id] = (wet, Time.time);
            return wet;
        }
    }

    // The colony's work: scanners (zones, the ground, fires and stations) post orders on the board; villagers take them as WorkTasks.
    internal static class Work
    {
        private static readonly Collider[] Hits = new Collider[2048]; private static float _fullWarned;
        private static readonly Dictionary<ZDOID, float> FirstSeen = new Dictionary<ZDOID, float>();
        public static readonly HashSet<string> Crops = new HashSet<string>(); // grown crop prefabs (from the cultivator's saplings)

        internal static string Key(ZNetView v) => v.GetZDO().m_uid.ToString();
        public static GameObject Resolve(string target)
        {
            if (string.IsNullOrEmpty(target) || ZNetScene.instance == null) return null;
            string[] p = target.Split(':');
            if (p.Length != 2 || !long.TryParse(p[0], out long user) || !uint.TryParse(p[1], out uint id)) return null;
            return ZNetScene.instance.FindInstance(new ZDOID(user, id));
        }
        // Whether the target is gone from the world (not just unloaded on this game).
        public static bool Exists(string target)
        {
            if (string.IsNullOrEmpty(target) || ZDOMan.instance == null) return false;
            string[] p = target.Split(':');
            return p.Length == 2 && long.TryParse(p[0], out long user) && uint.TryParse(p[1], out uint id) && ZDOMan.instance.GetZDO(new ZDOID(user, id)) != null;
        }

        // ---- the scanners (colony owner, every few seconds) ----
        public static void Scan(Colony c) { foreach (int _ in Steps(c)) { } }
        // The scan a step at a time, a frame each (a zone, the things lying about, what's to be made, the fires and stations): all in one
        // frame it was a hitch every few seconds in a big colony.
        public static IEnumerable<int> Steps(Colony c)
        {
            double now = c.Now;
            LearnCrops();
            // (stores full enough: no more felling or quarrying; logs already down are still cut up, ore is still dug)
            bool enoughWood = Stock.Enough(c, "wood") || !Room(c, "Wood"), enoughStone = Stock.Enough(c, "stone") || !Room(c, "Stone"); // (or nowhere to put it: no use filling bags)
            if (enoughWood || enoughStone)
                foreach (Order o in c.Board.All.Where(o => o.State == OrderState.Open && (enoughWood && o.Kind == "chop" || enoughStone && o.Kind == "mine")).ToList())
                {
                    GameObject t = Resolve(o.Target);
                    if (t == null || o.Kind == "chop" && t.GetComponent<TreeLog>() == null || o.Kind == "mine" && !IsOre(t)) c.Board.Cancel(o.Id);
                }
            var room = new Dictionary<string, bool>(); // (room in the stores for a crop, asked once a scan)
            bool RoomFor(GameObject item) => item == null || (room.TryGetValue(item.name, out bool r) ? r : room[item.name] = Room(c, item.name));
            yield return 1;
            foreach (Zone z in c.R.Zones.ToList()) // (a copy: a zone may be marked between steps)
            {
                yield return 2;
                if (!c.R.Zones.Contains(z)) continue;
                ZoneInfo info = Tables.Of(z.Type);
                if (info.Worker == null) continue;
                Vector3 centre = Zones.Centre(z); float reach = Zones.Reach(z) + 2, scan = reach + Zones.FuzzReach(z);
                switch (z.Type)
                {
                    case ZoneType.GuardPost: c.Board.Post("guard", z.Id, Prof.Guard, 2, centre.x, centre.y, centre.z, now, z.Id, "post"); continue; // (an old guard post: its corners walked)
                    case ZoneType.FishingDock: if (Zones.HasWater(z)) c.Board.Post("fish", z.Id, Prof.Fisherman, 2, centre.x, centre.y, centre.z, now, z.Id, "dock"); continue; // (a dock must reach the water)
                    case ZoneType.Pasture: Pasture(c, z, centre, reach, now); continue;
                }
                int posted = 0;
                foreach (GameObject go in Things(centre, scan, beasts: z.Type == ZoneType.Hunting))
                {
                    if (posted >= 12) break;
                    Vector3 p = go.transform.position;
                    ZNetView v = go.GetComponent<ZNetView>();
                    if (v == null || !v.IsValid()) continue;
                    bool lying = go.GetComponent<TreeLog>() != null || IsStump(go);
                    if (!Zones.Works(z, p, lying, Mathf.FloorToInt(p.x * 4) * 73856093 ^ Mathf.FloorToInt(p.z * 4) * 19349663)) continue; // (by where it stands: ids change with every load)
                    string kind = null; Prof prof = info.Worker.Value; int prio = 3;
                    switch (z.Type)
                    {
                        case ZoneType.Woodlot:
                            if (go.GetComponent<TreeLog>() != null) { kind = "chop"; prio = 4; }
                            else if (enoughWood) break;
                            else if (go.GetComponent<TreeBase>() != null) kind = "chop";
                            else if (go.GetComponent<Destructible>() != null && IsStump(go)) { kind = "chop"; prio = 2; } // stumps cleared for new trees
                            break;
                        case ZoneType.Mine:
                            if ((go.GetComponent<MineRock5>() != null || go.GetComponent<MineRock>() != null || IsRock(go)) && (!enoughStone || IsOre(go))) kind = "mine";
                            break;
                        case ZoneType.Fields:
                            Pickable crop = go.GetComponent<Pickable>();
                            // (left standing while the stores have no room for it: the field keeps it better than the ground does)
                            if (crop != null && Crops.Contains(Utils.GetPrefabName(go)) && crop.CanBePicked() && RoomFor(crop.m_itemPrefab)) { kind = "harvest"; prio = 4; }
                            break;
                        case ZoneType.Foraging:
                            Pickable wild = go.GetComponent<Pickable>();
                            if (wild != null && !Crops.Contains(Utils.GetPrefabName(go)) && wild.CanBePicked() && wild.m_itemPrefab != null && Forage(wild)) kind = "pick";
                            break;
                        case ZoneType.Hunting:
                            Character beast = go.GetComponent<Character>();
                            if (Prey(beast)) kind = "hunt";
                            break;
                    }
                    if (kind == null) continue;
                    c.Board.Post(kind, Key(v), prof, prio, p.x, p.y, p.z, now, z.Id, z.Type.ToString());
                    posted++;
                }
                if (z.Type == ZoneType.Woodlot) Planting(c, z, centre, reach, now);
                if (z.Type == ZoneType.Fields) Sowing(c, z, centre, reach, now);
            }
            yield return 3;
            // Things lying about in the colony: to the stockpiles (if there are any).
            if (Stock.Chests(c).Any(x => x.category != "" && x.category != "treasury"))
            {
                int hauls = 0;
                foreach (ItemDrop drop in Drops(c.Position, c.Radius))
                {
                    if (hauls >= 15) break;
                    ZNetView v = drop.GetComponent<ZNetView>(); if (v == null || !v.IsValid()) continue;
                    ZDOID id = v.GetZDO().m_uid;
                    if (!FirstSeen.TryGetValue(id, out float seen)) { FirstSeen[id] = Time.time; continue; }
                    if (Time.time - seen < 15f) continue; // something just dropped may be on its way somewhere
                    if (Player.GetAllPlayers().Any(pl => Vector3.Distance(pl.transform.position, drop.transform.position) < 4f)) continue;
                    if (!c.Tends(drop.transform.position)) continue; // someone else's things lying in their own base
                    if (drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && Stock.Destination(c, drop.m_itemData, drop.transform.position) == null) continue;
                    // (only what lies where feet can get to it: not on a roof or a wall top, not up a tree)
                    if (OnTop(drop.transform.position)) continue;
                    c.Board.Post("haul", Key(v), Prof.Hauler, 2, drop.transform.position.x, drop.transform.position.y, drop.transform.position.z, now, "", "ground");
                    hauls++;
                }
                if (FirstSeen.Count > 4000) FirstSeen.Clear();
            }
            yield return 4;
            // Watch posts: a round for each guard (two may walk it at once, from different posts).
            var posts = WatchPost.All(c.Position);
            if (posts.Count > 0)
            {
                int guards = Math.Max(1, Math.Min(3, Villagers.All.Count(v => v.S.Colony == c.Id && v.S.Prof == Prof.Guard)));
                Vector3 p0 = posts[0].transform.position;
                for (int i = 0; i < guards; i++) c.Board.Post("guard", "round:" + i, Prof.Guard, 2, p0.x, p0.y, p0.z, now, "", "watch posts");
            }
            Faith.Bog(c);
            yield return 4;
            foreach (int _ in Crafts.Steps(c, now)) yield return 4;
            yield return 4;
            Weaving.Scan(c, now);
            yield return 5;
            // BuildOrders' planned ghosts in the colony: one standing order for the builders (they take the pieces one by one).
            if (Compat.GhostCount(c.Position, c.Radius) > 0) c.Board.Post("build", "ghosts", Prof.Builder, 3, c.Position.x, c.Position.y, c.Position.z, now, "", "plans");
            // Fires that burn low, and the stations: fuel and feeding.
            yield return 6;
            List<Piece> pieces = c.PiecesIn(c.Radius);
            yield return 7;
            int looked = 0;
            foreach (Piece p in pieces)
            {
                if (++looked % 800 == 0) yield return 8; // (a big colony's pieces over a few frames)
                if (p == null) continue;
                Fireplace fire = p.GetComponent<Fireplace>();
                if (fire != null) FireAvoid.Ensure(fire); // (one lit before Jarldom loaded)
                ZNetView v = p.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                // (every fire the colony tends, in a room or out: a campfire under the cooking station, the torches along the road, a brazier,
                // a hot tub; the haulers' round, and anyone idle helps: what warms or lights is one job whatever burns in it)
                // (not the beacon: it's lit when a jarl sails home, and often stands where no hauler climbs)
                if (fire != null && !fire.m_infiniteFuel && fire.m_fuelItem != null && !Beacon.IsMarked(c, fire) && c.Feeds(p) && v.GetZDO().GetFloat(ZDOVars.s_fuel) < fire.m_maxFuel * 0.5f && HaveInStock(c, fire.m_fuelItem.name) && !c.LeftAloneAt(p.transform.position)) // (none can walk to it: left alone)
                    c.Board.Post("fuel", Key(v), Prof.Hauler, Classes.Warms(fire) ? 4 : 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "fire");
                Smelter smelter = p.GetComponent<Smelter>();
                if (smelter != null)
                {
                    bool needFuel = smelter.m_fuelItem != null && v.GetZDO().GetFloat(ZDOVars.s_fuel) < smelter.m_maxFuel * 0.5f && HaveInStock(c, smelter.m_fuelItem.name);
                    bool needOre = QueueSize(smelter) < smelter.m_maxOre / 2 && smelter.m_conversion.Any(cv => cv.m_from != null && Feedable(c, smelter, cv.m_from.name));
                    if (needFuel || needOre) c.Board.Post("smelt", Key(v), Prof.Smelter, 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "station");
                }
                Fermenter ferm = p.GetComponent<Fermenter>();
                if (ferm != null && Roofed(p.transform.position)) // (a fermenter works only under a roof)
                {
                    string st = FermentStatus(ferm);
                    if (st == "Ready") c.Board.Post("tap", Key(v), Prof.Cook, 5, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "fermenter");
                    else if (st == "Empty" && ferm.m_conversion.Any(cv => cv.m_from != null && HaveInStock(c, cv.m_from.name)))
                        c.Board.Post("ferment", Key(v), Prof.Cook, 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "fermenter");
                }
                CookingStation cook = p.GetComponent<CookingStation>();
                // (an oven burns its own fuel; a cooking station sits over a fire)
                if (cook != null && cook.m_useFuel && cook.m_fuelItem != null && v.GetZDO().GetFloat(ZDOVars.s_fuel) < cook.m_maxFuel * 0.5f && HaveInStock(c, cook.m_fuelItem.name) && cook.m_conversion.Any(cv => cv.m_from != null && HaveInStock(c, cv.m_from.name)))
                    c.Board.Post("fuel", Key(v), Prof.Cook, 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "oven");
                if (cook != null && (Cook(cook, "HaveDoneItem") || CookLit(cook) && CookFree(cook) && cook.m_conversion.Any(cv => cv.m_from != null && HaveInStock(c, cv.m_from.name))))
                    c.Board.Post("cook", Key(v), Prof.Cook, Cook(cook, "HaveDoneItem") ? 5 : 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "station");
            }
        }
        private static readonly System.Reflection.MethodInfo FermentStatusMethod = AccessTools.Method(typeof(Fermenter), "GetStatus");
        public static bool Roofed(Vector3 at) { Cover.GetCoverForPoint(at + Vector3.up * 0.8f, out float _, out bool roof); return roof; }
        public static string FermentStatus(Fermenter f) { try { return FermentStatusMethod.Invoke(f, null).ToString(); } catch { return ""; } }
        public static bool Cook(CookingStation s, string method) { try { return (bool)AccessTools.Method(typeof(CookingStation), method).Invoke(s, null); } catch { return false; } }
        public static bool CookLit(CookingStation s) => s.m_useFuel ? s.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel) > 0f : Cook(s, "IsFireLit");
        // (how many slots stand empty: what one trip from the stores can fill)
        public static int CookFreeCount(CookingStation s) { ZNetView v = s != null ? s.GetComponent<ZNetView>() : null; if (v == null || !v.IsValid() || s.m_slots == null) return 0; int n = 0; for (int i = 0; i < s.m_slots.Length; i++) if (v.GetZDO().GetString("slot" + i) == "") n++; return n; }
        public static bool CookFree(CookingStation s) { try { return (int)AccessTools.Method(typeof(CookingStation), "GetFreeSlot").Invoke(s, null) >= 0; } catch { return false; } }
        private static readonly System.Reflection.MethodInfo QueueMethod = AccessTools.Method(typeof(Smelter), "GetQueueSize");
        public static int QueueSize(Smelter s) { try { return (int)QueueMethod.Invoke(s, null); } catch { return 99; } }
        public static bool HaveInStock(Colony c, string prefab) => Stock.Count(c, prefab, free: true) > 0;
        private static bool Forage(Pickable p)
        {
            // (anything wild that's food, a seed or a crop: berries, mushrooms, wild barley and flax, fiddleheads, vineberries, seed
            // carrots and turnips; and the flint, stone and branches lying about)
            string n = p.m_itemPrefab.name;
            ItemDrop d = p.m_itemPrefab.GetComponent<ItemDrop>();
            if (n == "Flint" || n == "Stone" || n == "Wood" || n == "Thistle" || n == "Dandelion" || n.StartsWith("Mushroom")) return true;
            if (d == null) return false;
            string cat = Stock.Category(Stock.Of(p.m_itemPrefab));
            return Stock.Edible(Stock.Of(p.m_itemPrefab)) || cat == "rawfood" || cat == "seeds";
        }
        // Lying on a roof, a wall top, a pole or a beam (where no one walks up to it), from what's under it.
        private static readonly RaycastHit[] Under = new RaycastHit[8];
        public static bool OnTop(Vector3 at)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 0.3f, Vector3.down, Under, 1.5f, LayerMask.GetMask("piece", "piece_nonsolid", "terrain", "Default", "static_solid"), QueryTriggerInteraction.Ignore);
            RaycastHit first = default; float best = float.MaxValue;
            for (int i = 0; i < n; i++) if (Under[i].distance < best && Under[i].collider.GetComponentInParent<ItemDrop>() == null) { best = Under[i].distance; first = Under[i]; }
            if (first.collider == null) return false;
            Piece p = first.collider.GetComponentInParent<Piece>();
            if (p == null) return false;
            string name = Colony.NameOf(p).ToLowerInvariant();
            return name.Contains("roof") || name.Contains("wall") || name.Contains("pole") || name.Contains("beam") || name.Contains("log");
        }
        private static bool IsRock(GameObject go)
        {
            Destructible d = go.GetComponent<Destructible>();
            if (d == null) return false;
            string n = Utils.GetPrefabName(go).ToLowerInvariant();
            return n.Contains("rock") || IsOre(go);
        }
        // Everything networked near a point (trees, rocks, plants, beasts), each once.
        // Game for a hunter: the game's own animals (deer), and boars, necks and hares, which it counts with the forest's monsters.
        private static readonly HashSet<string> Game = new HashSet<string> { "Boar", "Neck", "Hare", "Deer" };
        public static bool Prey(Character c) => c != null && !c.IsDead() && !c.IsTamed() && !c.IsBoss() && !VillagerPrefab.Is(c) && !c.IsPlayer()
            && (c.GetFaction() == Character.Faction.AnimalsVeg || Game.Contains(Utils.GetPrefabName(c.gameObject)));
        // What stands in a zone: not the buildings (wild pickables lie on the item layer: kept) (a zone by a big base found its 2048 places all taken by walls and floors, and
        // the boar in the hunting grounds never); a hunting ground looks only for creatures.
        private static int _things = -1, _beasts = -1;
        public static IEnumerable<GameObject> Things(Vector3 at, float radius, bool beasts = false)
        {
            if (_things == -1) { _things = ~LayerMask.GetMask("piece", "terrain", "character", "character_net", "character_ghost", "character_noenv", "viewblock", "vehicle", "ui", "effect"); _beasts = LayerMask.GetMask("character", "character_net", "character_noenv"); }
            int n = Physics.OverlapSphereNonAlloc(at, radius, Hits, beasts ? _beasts : _things, QueryTriggerInteraction.Collide);
            if (n == Hits.Length && Time.time > _fullWarned) { _fullWarned = Time.time + 300; Plugin.Warn($"Jarldom: a work zone at {at.x:0},{at.z:0} has more than {Hits.Length} things in reach: some of its work isn't seen"); }
            var seen = new HashSet<GameObject>();
            for (int i = 0; i < n; i++)
            {
                ZNetView v = Hits[i].GetComponentInParent<ZNetView>();
                if (v == null || !seen.Add(v.gameObject)) continue;
                yield return v.gameObject;
            }
        }
        public static IEnumerable<ItemDrop> Drops(Vector3 at, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(at, radius, Hits, LayerMask.GetMask("item"), QueryTriggerInteraction.Collide);
            var seen = new HashSet<ItemDrop>();
            for (int i = 0; i < n; i++) { ItemDrop d = Hits[i].GetComponentInParent<ItemDrop>(); if (d != null && !Lost.IsRemains(d.m_itemData) && seen.Add(d)) yield return d; } // (someone's remains are for the jarl to bring home)
        }
        private static bool _learned;
        // Which grown prefabs are crops, and the sapling and seeds to replant each: from the cultivator's own pieces.
        public static readonly Dictionary<string, (GameObject sapling, string seed, int amount)> Replant = new Dictionary<string, (GameObject, string, int)>();
        // Tree saplings the cultivator plants (beech, pine, fir, birch, oak...), with their seeds.
        public static readonly List<(GameObject sapling, string seed, int amount, Plant plant)> Saplings = new List<(GameObject, string, int, Plant)>();
        private static GameObject _till;
        public static GameObject TillPiece() => _till != null ? _till : _till = ObjectDB.instance?.GetItemPrefab("Cultivator")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces?.m_pieces.FirstOrDefault(p => p != null && p.name == "cultivate_v2");

        // Fields with room: a crop sown on a free spot of a 1.25 m grid (hoed first if it needs it), from the seeds in the stores, a few at a time.
        private static void Sowing(Colony c, Zone z, Vector3 centre, float reach, double now)
        {
            if (Replant.Count == 0) return;
            int open = c.Board.All.Count(o => o.Kind == "sow" && o.Zone == z.Id && (o.State == OrderState.Open || o.State == OrderState.Claimed));
            if (open >= 3) return;
            foreach (var (p, sapling) in SowSpots(c, z, centre, reach, 3 - open))
                c.Board.Post("sow", $"sow:{sapling.name}@{p.x:0.00},{p.z:0.00}", Prof.Farmer, 2, p.x, p.y, p.z, now, z.Id, "Fields");
        }
        // Where the next few crops go, and which: the season's chosen crop while there are seeds for it, else whatever the stores hold.
        public static List<(Vector3 at, GameObject sapling)> SowSpots(Colony c, Zone z, Vector3 centre, float reach, int most)
        {
            var spots = new List<(Vector3, GameObject)>();
            var seeds = Replant.Where(kv => !SeedsPlenty(c, kv.Key)).Select(kv => kv.Value).GroupBy(r => r.seed).Select(g => g.First()).Where(r => HaveInStock(c, r.seed)).ToList();
            if (ChosenNow(c, z) is var chosen && chosen != null && HaveInStock(c, chosen.Value.seed)) seeds = new List<(GameObject, string, int)> { chosen.Value }; // (the jarl's crop for the season, while there are seeds for it; else what there is)
            bool flaxWanted = Villagers.All.Any(v => v.S.Colony == c.Id && v.S.Prof == Prof.Weaver) && InStockCount(c, Weaving.Flax) < 30;
            if (seeds.Count == 0) return spots;
            var taken = Things(centre, reach).Where(go => go.GetComponent<Plant>() != null || go.GetComponent<Pickable>() != null).Select(go => go.transform.position).ToList();
            taken.AddRange(c.Board.All.Where(o => o.Kind == "sow" && o.Zone == z.Id && o.State != OrderState.Done && o.State != OrderState.Cancelled).Select(o => new Vector3(o.X, o.Y, o.Z)));
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < z.Corners; i++) { var (x, zz) = z.Corner(i); minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minZ = Mathf.Min(minZ, zz); maxZ = Mathf.Max(maxZ, zz); }
            for (float x = minX + 0.6f; x <= maxX - 0.4f && spots.Count < most; x += 1.25f)
                for (float zz = minZ + 0.6f; zz <= maxZ - 0.4f && spots.Count < most; zz += 1.25f)
                {
                    Vector3 p = new Vector3(x, 0, zz);
                    if (!Zones.Inside(z, p) || taken.Any(t => Utils.DistanceXZ(t, p) < 0.9f)) continue;
                    p.y = ZoneSystem.instance.GetGroundHeight(p);
                    if (p.y < ZoneSystem.instance.m_waterLevel + 0.3f) continue;
                    Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p) : Heightmap.Biome.Meadows;
                    var fits = seeds.Where(r => r.sapling.GetComponent<Plant>() is Plant pl && ((pl.m_biome & biome) != 0 || Weaving.GrowsHere(r.sapling, p))).OrderBy(r => r.seed == Weaving.Flax && flaxWanted ? 0 : 1).ThenByDescending(r => InStockCount(c, r.seed)).ToList(); // (flax grows in the colony's fields anywhere; first, while a weaver wants it)
                    if (fits.Count == 0) return spots;
                    spots.Add((p, fits[0].sapling));
                    taken.Add(p);
                }
            return spots;
        }
        private static int InStockCount(Colony c, string item) => Stock.Count(c, item);
        // A charcoal kiln burns wood into coal: only while the stores want coal (the Work tab's limit, 100 unless set) and never the last
        // of the wood, which the builders and fires need. (Fed whenever there was wood, a kiln took every log the colony was given.)
        public const int KilnWoodLeft = 50;
        public static bool IsKiln(Smelter s) => s.m_conversion.Count > 0 && s.m_conversion.All(cv => cv.m_to != null && cv.m_to.name == "Coal");
        public static bool Feedable(Colony c, Smelter s, string item)
        {
            if (!IsKiln(s)) return HaveInStock(c, item);
            int keep = Stock.Keep(c, "coal");
            return (keep == 0 || InStockCount(c, "Coal") < keep) && InStockCount(c, item) > KilnWoodLeft;
        }
        // A seed crop (a seed carrot, turnip, onion: grown from the food, for its seeds) isn't worth a carrot while the stores hold plenty
        // of the seeds it makes: the food crop instead. (Replanted as it was, a seed patch went on eating every carrot it was given.)
        public const int PlentySeeds = 50;
        public static bool SeedsPlenty(Colony c, string crop) => FoodCrop(crop) is string food && Replant.TryGetValue(food, out var f) && InStockCount(c, f.seed) >= PlentySeeds;
        public static string FoodCrop(string crop) => crop != null && crop.StartsWith("Pickable_Seed") ? "Pickable_" + crop.Substring("Pickable_Seed".Length) : null;

        // The crops a fields zone can be sown with (those that grow where it lies, and flax anywhere), by name; and the one the jarl chose
        // for it this season (Core/CropChoice.cs), if it's still known.
        public static List<(GameObject sapling, string seed, int amount)> CropsFor(Zone z)
        {
            LearnCrops();
            Vector3 at = Zones.Centre(z); Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(at) : Heightmap.Biome.Meadows;
            return Replant.Values.Where(r => r.sapling != null && r.sapling.GetComponent<Plant>() is Plant pl && ((pl.m_biome & biome) != 0 || Utils.GetPrefabName(r.sapling) == Weaving.FlaxSapling))
                .GroupBy(r => r.sapling.name).Select(g => g.First()).OrderBy(r => CropName(r.sapling.name)).ToList();
        }
        public static string CropName(string sapling)
        {
            Piece p = Replant.Values.FirstOrDefault(r => r.sapling != null && r.sapling.name == sapling).sapling?.GetComponent<Piece>();
            string n = p != null ? Localization.instance.Localize(p.m_name) : sapling;
            return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n;
        }
        public static (GameObject sapling, string seed, int amount)? Chosen(Colony c, Zone z, Season s)
        {
            if (z == null || z.Type != ZoneType.Fields) return null;
            LearnCrops();
            string name = CropChoice.Chosen(c.R.Flags, z.Id, s, Replant.Values.Where(r => r.sapling != null).Select(r => r.sapling.name).Distinct());
            if (name == null) return null;
            return Replant.Values.First(r => r.sapling != null && r.sapling.name == name);
        }
        public static (GameObject sapling, string seed, int amount)? ChosenNow(Colony c, Zone z) => Chosen(c, z, Year.Today().Season);
        // What each season sows, for the Work tab: "" while it's all anything.
        public static string CropLine(Colony c, Zone z)
        {
            var parts = new List<string>();
            foreach (Season s in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
                if (Chosen(c, z, s) is var ch && ch != null) parts.Add($"{Calendar.Name(s)} {CropName(ch.Value.sapling.name).ToLower()}");
            return parts.Count == 0 ? "" : "sows " + string.Join(", ", parts) + (parts.Count < 4 ? ", else anything" : "");
        }

        public static bool IsStump(GameObject go) { string n = Utils.GetPrefabName(go).ToLowerInvariant(); return n.Contains("stub") || n.Contains("stump"); }

        // A woodlot keeps about a tree to every 30 m²: below that, a sapling where there's room, from seeds in the stores.
        private static readonly Collider[] Near = new Collider[64];
        private static int _clearMask = -1;
        private static void Planting(Colony c, Zone z, Vector3 centre, float reach, double now)
        {
            if (Saplings.Count == 0) return;
            if (c.Board.All.Any(o => o.Kind == "plant" && o.Zone == z.Id && (o.State == OrderState.Open || o.State == OrderState.Claimed))) return;
            int trees = Things(centre, reach).Count(go => Zones.Inside(z, go.transform.position) && (go.GetComponent<TreeBase>() != null || go.GetComponent<Plant>() != null));
            if (trees >= Mathf.Max(1, (int)(Geometry.Area(z.Xz) / 30f))) return;
            var stocked = Saplings.Where(s => HaveInStock(c, s.seed)).ToList();
            if (stocked.Count == 0) return;
            if (_clearMask == -1) _clearMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < z.Corners; i++) { var (x, zz) = z.Corner(i); minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minZ = Mathf.Min(minZ, zz); maxZ = Mathf.Max(maxZ, zz); }
            for (int tries = 0; tries < 16; tries++)
            {
                Vector3 p = new Vector3(UnityEngine.Random.Range(minX, maxX), 0, UnityEngine.Random.Range(minZ, maxZ));
                if (!Zones.Inside(z, p)) continue;
                p.y = ZoneSystem.instance.GetGroundHeight(p);
                if (p.y < ZoneSystem.instance.m_waterLevel + 0.4f) continue;
                int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.5f, 3f, Near, _clearMask, QueryTriggerInteraction.Ignore);
                bool clear = true;
                for (int i = 0; i < n && clear; i++) { Collider col = Near[i]; if (col.GetComponentInParent<ZNetView>() != null || col.gameObject.layer == LayerMask.NameToLayer("static_solid")) clear = false; }
                if (!clear) continue;
                Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p) : Heightmap.Biome.Meadows;
                var fits = stocked.Where(s => (s.plant.m_biome & biome) != 0).ToList();
                if (fits.Count == 0) return;
                var pick = fits[UnityEngine.Random.Range(0, fits.Count)];
                c.Board.Post("plant", "plant:" + pick.sapling.name + "@" + z.Id, Prof.Woodcutter, 2, p.x, p.y, p.z, now, z.Id, "Woodlot");
                return;
            }
        }

        // A pasture: tame beasts fed when hungry (with what each eats, from the stores), and the surplus culled for meat when there are
        // more than it holds (one to every 12 m²). Named beasts and young ones are never culled; two of each kind are always kept.
        private static void Pasture(Colony c, Zone z, Vector3 centre, float reach, double now)
        {
            // (tame ones, and wild ones brought in to be tamed: both fed)
            var beasts = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsPlayer() && !VillagerPrefab.Is(ch) && ch.GetComponent<Tameable>() != null && (ch.IsTamed() ? ch.GetComponent<Procreation>() != null : Utils.GetPrefabName(ch.gameObject) == Catching.Wild)
                && ch.m_group != "bob_gary" && Vector3.Distance(ch.transform.position, centre) < reach + 4 && Zones.Inside(z, ch.transform.position)).ToList();
            int fed = 0;
            foreach (Character beast in beasts)
            {
                if (fed >= 6) break;
                Tameable tame = beast.GetComponent<Tameable>(); MonsterAI ai = beast.GetComponent<MonsterAI>();
                ZNetView v = beast.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || ai == null || ai.m_consumeItems == null || !tame.IsHungry()) continue;
                if (!ai.m_consumeItems.Any(f => f != null && HaveInStock(c, f.name))) continue;
                Vector3 p = beast.transform.position;
                c.Board.Post("feed", Key(v), Prof.Herder, 3, p.x, p.y, p.z, now, z.Id, "Pasture");
                fed++;
            }
            int holds = Mathf.Max(2, (int)(Geometry.Area(z.Xz) / 12f));
            if (beasts.Count <= holds || c.Board.All.Any(o => o.Kind == "cull" && o.Zone == z.Id && (o.State == OrderState.Open || o.State == OrderState.Claimed))) return;
            var spare = beasts.Where(ch => ch.IsTamed() && ch.GetComponent<Growup>() == null && string.IsNullOrEmpty(ch.GetComponent<Tameable>().GetText()) && ch.GetComponentInChildren<Sadle>() == null) // (never a mount)
                .GroupBy(ch => Utils.GetPrefabName(ch.gameObject)).Where(g => g.Count() > 2).OrderByDescending(g => g.Count()).FirstOrDefault();
            Character cull = spare?.OrderBy(ch => ch.GetLevel()).ThenBy(ch => ch.GetHealth()).FirstOrDefault();
            if (cull == null) return;
            ZNetView cv = cull.GetComponent<ZNetView>();
            c.Board.Post("cull", Key(cv), Prof.Herder, 2, cull.transform.position.x, cull.transform.position.y, cull.transform.position.z, now, z.Id, "Pasture");
        }

        // Is there a chest in the stores with room for this?
        private static bool Room(Colony c, string prefab)
        {
            GameObject p = ObjectDB.instance?.GetItemPrefab(prefab);
            if (p == null) return true;
            ItemDrop.ItemData item = Stock.Of(p);
            return Stock.Destination(c, item, c.Position) != null || Stock.Chests(c).Any(x => x.category != "treasury" && x.chest.GetInventory().CanAddItem(item, 1)); // (as a hauler would: any chest with room)
        }
        // A rock with metal in it (dug whatever the stone stores hold).
        private static readonly string[] Metals = { "copper", "tin", "silver", "iron", "blackmetal", "flametal", "obsidian", "mudpile", "scrap", "meteor" };
        public static bool IsOre(GameObject go) { string n = Utils.GetPrefabName(go).ToLower(); return Metals.Any(m => n.Contains(m)); }

        internal static void LearnCrops()
        {
            if (_learned || ObjectDB.instance == null) return;
            PieceTable table = ObjectDB.instance.GetItemPrefab("Cultivator")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (table == null) return;
            _learned = true;
            foreach (GameObject sapling in table.m_pieces)
            {
                Plant plant = sapling != null ? sapling.GetComponent<Plant>() : null;
                if (plant == null || plant.m_grownPrefabs == null) continue;
                Piece piece = sapling.GetComponent<Piece>();
                Piece.Requirement req = piece?.m_resources?.FirstOrDefault(r => r.m_resItem != null);
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown == null) continue;
                    if (grown.GetComponent<TreeBase>() != null && req != null && !Saplings.Any(x => x.sapling == sapling)) Saplings.Add((sapling, req.m_resItem.name, req.m_amount, plant));
                    if (grown.GetComponent<Pickable>() == null) continue; // trees from saplings aren't crops
                    Crops.Add(grown.name);
                    if (req != null) Replant[grown.name] = (sapling, req.m_resItem.name, req.m_amount);
                }
            }
        }
        public static void Forget() { FirstSeen.Clear(); _learned = false; Crops.Clear(); Replant.Clear(); Saplings.Clear(); }

        // Why a villager has nothing to do (shown in their panel): their trade's zone missing, or empty, or nothing for them.
        public static string WhyIdle(Brain b)
        {
            Colony c = b.Colony; Prof p = b.S.Prof;
            if (p == Prof.Guard) return WatchPost.All(c.Position).Count == 0 && !c.R.Zones.Any(z => z.Type == ZoneType.GuardPost) ? "no watch posts to walk (hammer, Misc: raise two or more)" : "between watches";
            ZoneInfo zone = Tables.Zones.FirstOrDefault(z => z.Worker == p);
            if (zone != null)
            {
                if (!c.R.Zones.Any(z => z.Type == zone.Id)) return $"no {zone.Name.ToLower()} marked ({zone.Text})";
                switch (zone.Id)
                {
                    case ZoneType.Woodlot: return !Room(c, "Wood") ? "no room for wood in the stockpiles: add chests" : Stock.Enough(c, "wood") ? $"resting: the stores hold {Stock.Keep(c, "wood")} wood, as the Work tab asks" : "no trees left to fell in the woodlot";
                    case ZoneType.Mine: return !Room(c, "Stone") ? "no room for stone in the stockpiles: add chests" : Stock.Enough(c, "stone") ? $"resting: the stores hold {Stock.Keep(c, "stone")} stone, as the Work tab asks" : "nothing left to dig in the quarry";
                    case ZoneType.Fields: return "nothing to harvest or sow in the fields yet (what's ripe waits in the field while the stores have no room for it)";
                    case ZoneType.Foraging: return "nothing ripe in the foraging grounds yet";
                    case ZoneType.Hunting: return "no game in the hunting grounds";
                    case ZoneType.FishingDock: return c.R.Zones.Any(z => z.Type == ZoneType.FishingDock && Zones.HasWater(z)) ? "nothing to fish at the dock" : "no fishing dock reaches the water (mark one out over it)";
                    case ZoneType.GuardPost: return "between watches";
                    case ZoneType.Pasture: return "the pasture's beasts are seen to";
                }
            }
            switch (p)
            {
                case Prof.Smelter: return "no ore or fuel for the smelters";
                case Prof.Miner: return Claims.Of(c).Any(x => x.Ore > 0) ? "waiting to go out to a claim" : "nothing to dig: mark a quarry, or stake an ore claim beside a deposit (hammer → Misc)";
                case Prof.Cook: return "nothing to cook or brew";
                case Prof.Smith: return "nothing to make or mend (commission things in the Work tab; the smith also makes missing tools, arrows and the guards' upgrades when the stores allow)";
                case Prof.Builder: return Gear.Tool(b, "hammer") == null ? "needs a hammer to build, mend and pave" : "nothing to build, mend, rebuild or pave";
                case Prof.Hauler: return "nothing to carry, and every fire and torch is fed";
                case Prof.Skald: return "resting until the evening's songs";
                case Prof.Trader: return c.R.Rank < Rank.Village ? "trade runs wait for village rank" : "nothing to sell in the stores (amber, silver, gems)";
                case Prof.Carter: return "no ore or metal at the outpost to cart";
                case Prof.Weaver: return !c.PiecesIn(c.Radius).Any(x => Colony.NameOf(x) == "piece_workbench") ? "no workbench to weave at" : Crafts.InStock(c, Weaving.Thread) < 4 && Crafts.InStock(c, Weaving.Flax) < 2 ? "no flax or linen thread (farmers sow flax; a trader can buy seed)" : "everyone's dressed";
                default: return "idle";
            }
        }

        // ---- what a villager does next ----
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            Gear.Problem = null;
            if (Time.time > b.NoStoreUntil && (Carrying(b) >= 4 || b.Body.GetInventory().GetEmptySlots() <= 2 && Carrying(b) > 0))
            {
                if (b.Body.GetInventory().GetAllItems().Where(i => Stores(b, i)).Any(i => Stock.Destination(c, i, b.Pos) != null)) return new DepositTask();
                b.NoStoreUntil = Time.time + 30; b.Say(new[] { "Where do I put all this? (no stockpile for it)", "My arms are full. Where does it go? (no stockpile for it)", "Nowhere to stack this. (no stockpile for it)", "The hall has no shelf for all this. (no stockpile for it)", "Too much to carry and nowhere to put it. (no stockpile for it)" }[UnityEngine.Random.Range(0, 5)], 5);
            }
            // (worn gear: to a station to mend it, unless there's a smith to do it, and nothing's quite broken yet)
            if (Time.time > b.NoRepairUntil && RepairTask.Worn(b) is var worn && worn.Count > 0 && (b.S.Prof == Prof.Smith || !Smithing.HasSmith(c) || worn.Any(i => i.m_durability <= 0))) return new RepairTask();
            Task grave = GraveTask.Next(b); if (grave != null) return grave;
            Task outfit = OutfitTask.Next(b); if (outfit != null) return outfit;
            Task corner = CornerTask.Next(b); if (corner != null) return corner;
            Prof prof = b.S.Prof;
            if (prof == Prof.Skald) return null; // skalds rest by day and play at dusk
            if (prof == Prof.Healer) { Task heal = HealTask.Next(b); if (heal != null) return heal; }
            if (prof == Prof.Scout && Time.time > b.NoScoutUntil) return new ScoutTask();
            if (prof == Prof.Gothi) { Task rite = GothiTask.Next(b); if (rite != null) return rite; }
            if (prof == Prof.Carter) { Task cart = Carts.Next(b); if (cart != null) return cart; }
            if (prof == Prof.Hauler) { Task fest = FestHaulTask.Next(b); if (fest != null) return fest; } // (a festival coming: the hall readied)
            if (prof == Prof.Smith && c.IsOwner) { Task mend = MendTask.Next(b); if (mend != null) return mend; } // (everyone's worn gear, before new work)
            if (prof == Prof.Builder && c.IsOwner && Compat.GhostCount(c.Position, c.Radius) > 0 && Gear.Tool(b, "hammer") != null && Time.time > b.NoBuildUntil) return new BuildTask();
            if (prof == Prof.Builder) { Task up = Upkeep.Next(b); if (up != null) return up; } // (nothing planned: mend, rebuild, pave)
            if (prof == Prof.Miner) { Task trip = Claims.Next(b); if (trip != null) return trip; } // (ore claims out in the wilds first)
            if (prof == Prof.Herder || prof == Prof.Hunter) { Task catching = Catching.Next(b); if (catching != null) return catching; } // (an empty pasture: a wild boar for it)
            if (prof == Prof.Trader && c.R.Rank >= Rank.Village && Time.time > b.NoTradeUntil && b.Body.GetInventory().GetEmptySlots() > 1 && Trade.Lots(c) >= 3) return new TradeTask(); // (three lots to sell: the trade chests' goods, the stores' treasure)
            Func<Prof, bool> canDo = p => p == prof || p == Prof.Hauler && (prof == Prof.Hauler || Gear.Lacks(b, prof) != null || NothingFor(c, prof));
            string problem = null;
            Order o = c.IsOwner ? c.Board.Best(b.S.Vid, canDo, b.Pos.x, b.Pos.z, order => { string p = GearFor(b, order); if (p != null) problem = p; return p; }, c.Now) : Net.BestShared(c, b, canDo);
            if (problem != null) { Gear.Problem = "needs gear: " + problem; Alerts.NeedsGear(c, b, problem); }
            if (o == null) return null;
            if (o.Kind == "craft" && (!c.IsOwner || Time.time < b.NoCraftUntil)) return null;
            if (c.IsOwner) { if (!c.Board.Claim(o.Id, b.S.Vid, c.Now, out _)) return null; }
            else Net.Claim(c, b, o);
            return o.Kind == "craft" ? new CraftTask(o) : (Task)new WorkTask(o);
        }
        // At dusk, before the hall: the hauler (or a woodcutter, if there's none) feeds the fires and torches gone low, so the colony
        // is lit for the night.
        public static Task Lamplight(Brain b)
        {
            Colony c = b.Colony;
            if (!c.IsOwner || b.S.Prof != Prof.Hauler && !(b.S.Prof == Prof.Woodcutter && !Villagers.All.Any(v => v.S.Colony == c.Id && v.S.Prof == Prof.Hauler && v.Alive))) return null;
            // (each once an evening: one that can't be reached isn't tried all night)
            Order o = c.Board.All.Where(x => x.State == OrderState.Open && x.Kind == "fuel" && x.Prof == Prof.Hauler && !b.LampsTried.Contains(x.Id)).OrderBy(x => (x.X - b.Pos.x) * (x.X - b.Pos.x) + (x.Z - b.Pos.z) * (x.Z - b.Pos.z)).FirstOrDefault();
            if (o == null) { b.LampsTried.Clear(); return null; }
            b.LampsTried.Add(o.Id);
            if (!c.Board.Claim(o.Id, b.S.Vid, c.Now, out _)) return null;
            return new WorkTask(o);
        }
        private static bool NothingFor(Colony c, Prof p) => !c.Board.All.Any(o => o.State == OrderState.Open && o.Prof == p);
        internal static string GearFor(Brain b, Order o)
        {
            if (o.Prof == Prof.Hauler) return null;
            if (o.Kind == "cull") return Gear.Tool(b, "weapon") == null ? "a weapon to cull with" : null;
            if (o.Kind == "sow" && Gear.Tool(b, "cultivator") == null) { Vector3 at = new Vector3(o.X, o.Y, o.Z); Heightmap hm = Heightmap.FindHeightmap(at); if (hm == null || !hm.IsCultivated(at)) return "a cultivator"; }
            int tier = 0;
            if (o.Kind == "chop" || o.Kind == "mine")
            {
                GameObject t = Resolve(o.Target);
                tier = t == null ? 0 : t.GetComponent<TreeBase>()?.m_minToolTier ?? t.GetComponent<TreeLog>()?.m_minToolTier ?? t.GetComponent<MineRock5>()?.m_minToolTier ?? t.GetComponent<MineRock>()?.m_minToolTier ?? t.GetComponent<Destructible>()?.m_minToolTier ?? 0;
            }
            if (o.Kind == "harvest" || o.Kind == "pick" || o.Kind == "fuel" || o.Kind == "smelt" || o.Kind == "cook" || o.Kind == "ferment" || o.Kind == "tap" || o.Kind == "craft" || o.Kind == "plant" || o.Kind == "sow" || o.Kind == "feed" || o.Kind == "guard") return o.Kind == "guard" ? Gear.Lacks(b, Prof.Guard) : null;
            return Gear.Lacks(b, o.Prof, tier);
        }
        public static int Carrying(Brain b) => b.Body.GetInventory().GetAllItems().Count(i => Stores(b, i));
        // What goes to the stores from this villager's bag: loot, and gear they picked up off the ground for the colony (not theirs).
        // (a weaver keeps their spun thread by them)
        public static bool Stores(Brain b, ItemDrop.ItemData i) => !CornerTask.Keepsake(i) && !(b.S.Prof == Prof.Weaver && Classes.Prefab(i) == Weaving.Thread && i.m_stack <= 20) && Loot(i) || !i.m_equipped && i.m_dropPrefab != null && (b.Hauled.Contains(i.m_dropPrefab.name) || Surplus(b, i) || Spare(b, i));
        // A second of what they already have and use: a tankard past their one, a weaker bow, armour or clothes beaten by what they
        // wear in that slot, a second tool of a kind, a weaker weapon (for the stores, where someone without will find it).
        private static readonly string[] SpareKinds = { "pickaxe", "axe", "hammer", "hoe", "cultivator", "rod" };
        public static bool Spare(Brain b, ItemDrop.ItemData i)
        {
            if (i.m_equipped || CornerTask.Keepsake(i)) return false;
            var bag = b.Body.GetInventory().GetAllItems();
            if (Gear.IsTankard(i)) return bag.FirstOrDefault(t => Gear.IsTankard(t) && t.m_equipped) is ItemDrop.ItemData held ? held != i : bag.First(Gear.IsTankard) != i;
            var t0 = i.m_shared.m_itemType;
            if (t0 == ItemDrop.ItemData.ItemType.Bow) return bag.Any(o => o != i && o.m_shared.m_itemType == t0 && Gear.Usable(o) && o.GetDamage().GetTotalDamage() >= i.GetDamage().GetTotalDamage());
            if (t0 == ItemDrop.ItemData.ItemType.Helmet || t0 == ItemDrop.ItemData.ItemType.Chest || t0 == ItemDrop.ItemData.ItemType.Legs || t0 == ItemDrop.ItemData.ItemType.Shoulder)
                return bag.Any(o => o != i && o.m_equipped && o.m_shared.m_itemType == t0 && Gear.Usable(o) && o.GetArmor() >= i.GetArmor());
            // (a second tool of a kind, past the one they'd use: two pickaxes while a miner has none)
            if (Gear.Is("pickaxe", i) && b.S.Prof != Prof.Miner) return true; // (only miners dig: a woodcutter's pickaxe is a miner's without one)
            foreach (string kind in SpareKinds) if (Gear.Is(kind, i)) return Gear.Tool(b, kind) is ItemDrop.ItemData use && use != i;
            // (a weaker weapon besides the one they fight with)
            if (Gear.IsWeapon(i) && Gear.Weapon(b) is ItemDrop.ItemData arm && arm != i && Smithing.Score(i) <= Smithing.Score(arm)) return true;
            return false;
        }
        // Food beyond what a villager keeps for themselves (a small stack of their best, five or fewer; a bigger one goes in whole and they
        // fetch a meal back when hungry): the colony's.
        private const int KeepFood = 5;
        public static bool Surplus(Brain b, ItemDrop.ItemData i)
        {
            if (!Stock.Edible(i)) return false;
            var food = b.Body.GetInventory().GetAllItems().Where(Stock.Edible).ToList();
            if (food.Sum(f => f.m_stack) <= KeepFood) return false;
            ItemDrop.ItemData keep = food.Where(f => f.m_stack <= KeepFood).OrderByDescending(f => f.m_shared.m_food).FirstOrDefault();
            return i != keep;
        }
        // What a villager carries that belongs in the stockpiles (not its own gear, food for a day, or its seeds for replanting).
        // A villager's own gear: armour, weapons, shields, bows and arrows, tools.
        public static bool OwnGear(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility
                || t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.Bow
                || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.Torch;
        }
        public static bool Loot(ItemDrop.ItemData i)
        {
            if (i.m_equipped || CornerTask.Keepsake(i)) return false; // (theirs to keep: for their corner)
            var t = i.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility) return false;
            if (t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.Torch) return false;
            if (Stock.Edible(i)) return false;
            if (Items.IsDrum(i) || Items.IsRelic(i)) return false; // a skald's drum is theirs; a colony's heart isn't for the stores
            if (i.m_dropPrefab != null && (i.m_dropPrefab.name.StartsWith("FishingBait") || (Replant.Values.Any(r => r.seed == i.m_dropPrefab.name) || Saplings.Any(s => s.seed == i.m_dropPrefab.name)) && i.m_stack <= 10)) return false; // (every bait; seeds for crops and trees alike)
            return true;
        }
    }

    // Take what's in the bag (that isn't theirs to keep) to the stockpiles.
    internal sealed class DepositTask : Task
    {
        private Container _to;
        public override void Start(Brain b)
        {
            Doing = "taking things to the stores";
            _to = b.Body.GetInventory().GetAllItems().Where(i => Work.Stores(b, i)).Select(i => Stock.Destination(b.Colony, i, b.Pos)).FirstOrDefault(d => d != null);
            if (_to == null) { Done = true; b.NoStoreUntil = Time.time + 30; b.Idle("nowhere to put things (no stockpile)"); return; }
            b.Move.To(_to.transform.position, 1.8f);
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_to == null || b.Move.Failed) { Done = true; b.NoStoreUntil = Time.time + 30; return; }
            if (!b.Move.Arrived && Stock.OutOfReach(b.Pos, _to.transform.position, 2.5f)) { if (!b.Move.Moving) b.Move.To(_to.transform.position, 1.8f); return; }
            Inventory bag = b.Body.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData item in bag.GetAllItems().Where(i => Work.Stores(b, i)).ToList())
            {
                Container dest = Stock.Destination(b.Colony, item, b.Pos);
                if (dest != _to) continue;
                string name = Classes.Prefab(item);
                int put = Stock.Put(bag, _to, item);
                if (put > 0 && name != null && !bag.GetAllItems().Any(i => Classes.Prefab(i) == name)) b.Hauled.Remove(name);
                if (put > 0) { b.S.Bump("loads"); moved++; CatchUp.Made(b, item, put); }
            }
            if (moved == 0) b.NoStoreUntil = Time.time + 30;
            if (b.S.Count("loads") >= 1000 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("hauler"); b.Colony.Log("byname", b.S.Name, b.S.Byname); }
            b.Dirty();
            Done = true; // another trip if something else is left for another chest
        }
    }

    // One order from the board, done the game's way: chopping and mining with the tool's own damage, picking with the plant's own
    // pick, feeding stations through their own RPCs, carrying by picking up and putting into chests.
    internal sealed class WorkTask : Task
    {
        public readonly Order O; public string Kind => O.Kind; public Character Target;
        public string Tool { get; private set; } = "";
        private GameObject _target; private Vector3 _at; private int _phase; private float _next, _waitUntil, _renew; private string _crop; private Vector3 _cropAt;
        private readonly List<ItemDrop> _collect = new List<ItemDrop>(); private float _collectUntil;
        public override bool Interruptible => _phase == 0;
        public override string Debug(Brain b) => $"{Kind} #{O.Id} phase {_phase} target {(_target != null ? Utils.GetPrefabName(_target) + " " + _target.transform.position.ToString("F1") : "none")} near {(_target != null ? Near(b, 1.6f) : false)} approach {Approach(b):F1} solid {(_target != null ? Solid(_target, b.Pos)?.name + "@" + Solid(_target, b.Pos)?.bounds.center.ToString("F1") : "")}{(_source != null ? $" source {_source.transform.position:F1} carry {_carry} fetched {_fetched}" : "")} wait {Mathf.Max(0, _waitUntil - Time.time):0.0}";
        public WorkTask(Order o) { O = o; }

        public override void Start(Brain b)
        {
            _target = Work.Resolve(O.Target);
            _at = new Vector3(O.X, O.Y, O.Z);
            Tool = Kind == "chop" ? "axe" : Kind == "mine" ? "pickaxe" : Kind == "hunt" ? "bow" : Kind == "guard" ? "weapon" : Kind == "harvest" ? "" : "";
            if (Kind == "hunt" && Gear.Tool(b, "bow") == null) Tool = "weapon";
            Doing = Describe();
            if (Kind == "plant" || Kind == "sow") { _cropAt = _at; }
            else if (Kind != "guard" && Kind != "fish" && _target == null) { Gone(b); return; } // its target is gone (picked up, felled, destroyed): the job with it
            if (Kind == "hunt") { Target = _target.GetComponent<Character>(); b.Foe = Target; }
            _renew = Time.time + 20f;
            b.Move.To(Approach(b), Kind == "haul" ? 1.0f : 1.6f);
        }
        private string Describe()
        {
            string what = _target != null ? Localization.instance.Localize(_target.GetComponent<Hoverable>()?.GetHoverName() ?? _target.name) : "";
            switch (Kind)
            {
                case "chop": return "felling trees";
                case "mine": return "mining";
                case "harvest": return "harvesting " + what.ToLower();
                case "pick": return "foraging";
                case "haul": return "carrying " + (_target?.GetComponent<ItemDrop>() != null ? Localization.instance.Localize(_target.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLower() : "things");
                case "hunt": return "hunting";
                case "guard": return "on patrol";
                case "fish": return "fishing";
                case "fuel": return "tending the fire";
                case "smelt": return "feeding the smelter";
                case "cook": return "cooking";
                case "plant": return "planting a tree";
                case "sow": return "sowing";
                case "feed": return "feeding " + what.ToLower();
                case "cull": return "culling " + what.ToLower();
                case "ferment": return "filling the fermenter";
                case "tap": return "tapping the mead";
                default: return Kind;
            }
        }
        // The solid part to walk up to and strike: a tree's trunk, not its crown (the crown's collider blocks the view, and is huge).
        private static int _solidMask = -1;
        private static int SolidMask => _solidMask != -1 ? _solidMask : _solidMask = LayerMask.GetMask("Default", "static_solid", "piece", "Default_small", "terrain");
        public static Collider Solid(GameObject target, Vector3 from)
        {
            if (target == null) return null;
            TreeBase tree = target.GetComponent<TreeBase>();
            if (tree != null && tree.m_trunk != null)
            {
                Collider trunk = tree.m_trunk.GetComponentsInChildren<Collider>().Where(c => c != null && !c.isTrigger).OrderBy(c => Vector3.Distance(c.ClosestPointOnBounds(from), from)).FirstOrDefault();
                if (trunk != null) return trunk;
            }
            var all = target.GetComponentsInChildren<Collider>().Where(c => c != null && !c.isTrigger && c.enabled && (SolidMask & (1 << c.gameObject.layer)) != 0).ToList();
            var small = all.Where(c => c.bounds.size.x < 5f && c.bounds.size.z < 5f).ToList();
            return (small.Count > 0 ? small : all).OrderBy(c => Vector3.Distance(c.ClosestPointOnBounds(from), from)).FirstOrDefault();
        }
        // The nearest point of the collider itself (a long log's bounding box reaches far past the log).
        public static Vector3 Closest(Collider c, Vector3 from)
        {
            if (c == null) return from;
            if (c is MeshCollider m && !m.convex)
            {
                // (the game has no closest point on a concave mesh, and its box is far wider than a log lying across it: the real surface
                // where a line from here meets it, towards its middle or its box, whichever is nearer)
                // (and a tree's trunk mesh carries its boughs: towards the foot of the trunk first, which is also where to strike if
                // nothing's met)
                Vector3 root = c.transform.root.position, foot = new Vector3(root.x, Mathf.Clamp(from.y + 1f, c.bounds.min.y, c.bounds.max.y), root.z);
                Vector3 onBox = c.ClosestPointOnBounds(from), found = foot; float best = float.MaxValue;
                foreach (Vector3 to in new[] { foot, c.bounds.center, onBox })
                {
                    Vector3 d = to - from; if (d.sqrMagnitude < 0.0001f) continue;
                    if (c.Raycast(new Ray(from, d.normalized), out RaycastHit h, d.magnitude + 2f) && h.distance < best) { best = h.distance; found = h.point; }
                }
                return found;
            }
            return c.ClosestPoint(from);
        }
        private int _approaches;
        private Vector3 Approach(Brain b)
        {
            if (_target == null) return Colony.Spot(_at, 3f) ?? _at;
            Collider col = Solid(_target, b.Pos);
            Vector3 surface = col != null ? Closest(col, b.Pos) : _target.transform.position;
            Vector3 dir = (b.Pos - surface); dir.y = 0;
            Vector3 stand = surface + (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward) * 0.9f;
            return Nav.Valid(out Vector3 v, stand, 1.5f) ? v : stand;
        }
        private bool Near(Brain b, float dist)
        {
            if (_target == null) return Vector3.Distance(b.Pos, _at) < dist + 1;
            Collider col = Solid(_target, b.Pos);
            Vector3 p = col != null ? Closest(col, b.Pos) : _target.transform.position;
            return Utils.DistanceXZ(b.Pos, p) < dist && Mathf.Abs(b.Pos.y - p.y) < 3f;
        }

        public override void Tick(Brain b)
        {
            if (Done) return;
            if (Time.time > _renew) { _renew = Time.time + 20f; if (!Renew(b)) { Finish(b, false); return; } }
            if (Stalled(b)) return;
            if (Time.time < _waitUntil) return;
            switch (Kind)
            {
                case "chop": case "mine": Strike(b); break; // (reach 2 m: an axe's own)
                case "harvest": case "pick": Pick(b); break;
                case "haul": Haul(b); break;
                case "hunt": Hunt(b); break;
                case "guard": Guard(b); break;
                case "fish": Fish(b); break;
                case "fuel": case "smelt": case "cook": case "ferment": case "tap": Station(b); break;
                case "plant": PlantTree(b); break;
                case "sow": Sow(b); break;
                case "feed": Feed(b); break;
                case "cull": Cull(b); break;
                default: Finish(b, false); break;
            }
        }
        // A watchdog: no change at all in two minutes (the phase, the target, where they stand, what they carry) and the job is let go,
        // and said in the log (a stall nothing else caught). Not for work that's long by nature: felling, mining, fishing, the watch, the hunt.
        private string _sig; private float _sigSince;
        private bool Stalled(Brain b)
        {
            if (Kind == "chop" || Kind == "mine" || Kind == "fish" || Kind == "guard" || Kind == "hunt") return false;
            string sig = $"{_phase}|{(_target != null ? _target.GetInstanceID() : 0)}|{_collect.Count}|{Mathf.RoundToInt(b.Pos.x)},{Mathf.RoundToInt(b.Pos.z)}|{b.Body.GetInventory().NrOfItems()}|{b.Body.GetInventory().GetAllItems().Sum(i => i.m_stack)}";
            if (sig != _sig) { _sig = sig; _sigSince = Time.time; return false; }
            if (Time.time - _sigSince < 120f) return false;
            Plugin.Warn($"Jarldom: {b.S.Name}'s {Kind} job #{O.Id} stalled for 2 min ({Debug(b)}): let go");
            Block(b, "stalled");
            return true;
        }
        private bool Renew(Brain b) => b.Colony.IsOwner ? b.Colony.Board.Renew(O.Id, b.S.Vid, b.Colony.Now) : Net.Renew(b.Colony, b, O);

        private float _closeIn;
        private float _walkSince;
        // No nearer in 45 s (round and round a house it can't get into): it can't be reached.
        private float _bestDist = float.MaxValue, _bestAt; private Vector3 _bestTo;
        private bool NoHeadway(Brain b, Vector3 to)
        {
            float d = Utils.DistanceXZ(b.Pos, to);
            if (d < _bestDist - 2f || _bestAt == 0 || Utils.DistanceXZ(to, _bestTo) > 2f) { _bestDist = d; _bestAt = Time.time; _bestTo = to; return false; } // (somewhere new: counted afresh)
            return Time.time - _bestAt > 45f;
        }
        private bool Walking(Brain b, float near)
        {
            if (Near(b, near)) { b.Move.Stop(); _closeIn = 0; _walkSince = 0; _bestAt = 0; _bestDist = float.MaxValue; return false; }
            if (b.Move.Failed || NoHeadway(b, _target != null ? _target.transform.position : _at)) { Block(b, "can't reach it"); return true; }
            // (pacing at the foot of a wall for something on top of it: after a while, not reachable)
            if (_walkSince == 0) _walkSince = Time.time;
            else if (Time.time - _walkSince > 60f && Utils.DistanceXZ(b.Pos, _target != null ? _target.transform.position : _at) < 6f) { Block(b, "can't reach it"); return true; }
            // The navmesh stops short of trunks and rocks: the last few metres straight at it.
            if (!b.Move.Moving && _target != null && _closeIn < 3f)
            {
                Collider col = Solid(_target, b.Pos);
                Vector3 p = col != null ? Closest(col, b.Pos) : _target.transform.position;
                if (Utils.DistanceXZ(b.Pos, p) < 7f && Steer.SafeSpot(Vector3.MoveTowards(b.Pos, p, 1f)))
                {
                    Vector3 dir = p - b.Pos; dir.y = 0;
                    b.Ai.MoveTowards(dir.normalized, false);
                    _closeIn += b.DecideDt;
                    return true;
                }
            }
            if (!b.Move.Moving)
            {
                _closeIn = 0;
                if (++_approaches > 4) { Block(b, "can't get close enough"); return true; }
                b.Move.To(Approach(b), 1.0f);
            }
            return true;
        }

        // Chopping and mining: the swing's animation, and the tool's own damage on the target, as a player's blow does.
        private void Strike(Brain b)
        {
            if (_target == null)
            {
                // A felled tree becomes logs: chop on at the nearest log, then gather the wood.
                if (Kind == "chop" && _phase == 0) { TreeLog log = NearLog(b.Pos); if (log != null) { _target = log.gameObject; return; } }
                if (Collect(b)) return;
                Finish(b, true); return;
            }
            if (Walking(b, 2.0f)) return;
            ItemDrop.ItemData tool = Gear.Tool(b, Tool);
            if (tool == null) { b.Idle(Gear.Lacks(b, O.Prof) ?? "no tool"); Finish(b, false); return; } // (this one's lack: the job stays open for others)
            if (!b.Body.IsItemEquiped(tool)) { b.Body.EquipItem(tool, false); return; }
            Collider hitCol = Solid(_target, b.Pos);
            Vector3 aim = hitCol != null ? Closest(hitCol, b.Body.GetCenterPoint()) : _target.transform.position + Vector3.up * 0.8f;
            Vector3 flat = aim - b.Pos; flat.y = 0;
            b.Ai.LookTowards(flat.normalized);
            if (Time.time < _next) return;
            float speed = Mathf.Max(0.3f, b.Productivity());
            _next = Time.time + 1.8f / speed;
            // The swing as the game plays it: a combo's first blow is "<animation>0", a random set's "<animation>n".
            Attack swing = tool.m_shared.m_attack;
            string anim = swing.m_attackChainLevels > 1 ? swing.m_attackAnimation + "0" : swing.m_attackRandomAnimations >= 2 ? swing.m_attackAnimation + UnityEngine.Random.Range(0, swing.m_attackRandomAnimations) : swing.m_attackAnimation;
            Body_.Anim(b.Body)?.SetTrigger(anim);
            var hit = new HitData();
            HitData.DamageTypes dmg = tool.GetDamage();
            float skill = Kind == "chop" ? b.SkillLevel(Skills.SkillType.WoodCutting) : b.SkillLevel(Skills.SkillType.Pickaxes);
            float scale = Mathf.Lerp(0.4f, 1f, skill / 100f) * speed * (b.S.Has("strong") ? 1.1f : 1f);
            hit.m_damage.m_chop = dmg.m_chop * scale; hit.m_damage.m_pickaxe = dmg.m_pickaxe * scale;
            hit.m_toolTier = (short)tool.m_shared.m_toolTier; hit.m_itemWorldLevel = (byte)Game.m_worldLevel;
            Collider col = hitCol;
            hit.m_point = aim;
            hit.m_hitCollider = col; hit.m_dir = (hit.m_point - b.Body.GetCenterPoint()).normalized;
            hit.SetAttacker(b.Body);
            hit.m_skill = Kind == "chop" ? Skills.SkillType.WoodCutting : Skills.SkillType.Pickaxes;
            IDestructible target = _target.GetComponent<IDestructible>();
            target?.Damage(hit);
            Gear.Wear(b, tool);
            b.Body.RaiseSkill(hit.m_skill, 1f);
            if (Kind == "chop") b.S.Bump("chops");
        }
        private static TreeLog NearLog(Vector3 at)
        {
            foreach (GameObject go in Work.Things(at, 7f)) { TreeLog l = go.GetComponent<TreeLog>(); if (l != null) return l; }
            return null;
        }
        // Pick up what the work left on the ground nearby (wood, stone, crops).
        private bool Collect(Brain b)
        {
            if (_phase < 2) { _phase = 2; _waitUntil = Time.time + 1.5f; return true; } // drops fall a moment after
            if (_phase == 2) { _collect.Clear(); _collect.AddRange(Work.Drops(b.Pos, 7f).Take(8)); _phase = 3; _collectUntil = Time.time + 60f; }
            _collect.RemoveAll(d => d == null);
            if (_collect.Count == 0 || Time.time > _collectUntil) return false; // (a minute to gather up; what's left lies for the haulers)
            ItemDrop next = _collect.OrderBy(d => Vector3.Distance(d.transform.position, b.Pos)).First();
            if (!InReach(b, next.transform.position)) { if (Under(b, next.transform.position) || b.Move.Failed) { _collect.Remove(next); return true; } if (!b.Move.Moving) b.Move.To(next.transform.position, 0.8f); return true; }
            if (!PickUp(b, next)) _collect.Remove(next);
            return true;
        }
        // An item within a reach of the chest, as a player's (one on a fence or a table is picked up from beside it); and one straight
        // over the head and still out of that reach (on a wall top, a roof): walking to it again only stands under it again.
        public static bool InReach(Brain b, Vector3 item) => Vector3.Distance(b.Pos + Vector3.up, item) <= 2.0f;
        public static bool Under(Brain b, Vector3 item) => !b.Move.Moving && Utils.DistanceXZ(b.Pos, item) < 1.5f && item.y - b.Pos.y > 2.0f;
        public enum Got { Picked, Waiting, Failed }
        public static Got PickUpNow(Brain b, ItemDrop drop)
        {
            if (drop == null) return Got.Failed;
            ZNetView v = drop.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return Got.Failed;
            if (!drop.CanPickup(false)) { drop.RequestOwn(); return Got.Waiting; } // asked: picked up on a later tick
            if (!b.Body.GetInventory().CanAddItem(drop.m_itemData)) return Got.Failed;
            return b.Body.Pickup(drop.gameObject, false, false) ? Got.Picked : Got.Failed;
        }
        public static bool PickUp(Brain b, ItemDrop drop) => PickUpNow(b, drop) != Got.Failed;

        // Picking: the plant's own pick (its drops land beside it), gathered up; crops are replanted.
        private void Pick(Brain b)
        {
            if (_phase == 0)
            {
                if (_target == null) { Gone(b); return; }
                if (Walking(b, 1.4f)) return;
                Pickable p = _target.GetComponent<Pickable>();
                if (p == null || !p.CanBePicked()) { Gone(b); return; }
                _crop = Utils.GetPrefabName(_target); _cropAt = _target.transform.position;
                if (!WorkEmotes.Harvest(b)) Body_.Anim(b.Body)?.SetTrigger("interact"); // (crouched to pull it, with Bob's Animations)
                p.Interact(b.Body, false, false);
                b.Body.RaiseSkill(Skills.SkillType.Farming, 1f);
                _phase = 1; _waitUntil = Time.time + 0.6f;
                b.S.Bump(Kind == "harvest" ? "harvests" : "forage");
                return;
            }
            if (_phase <= 3) { if (Collect(b)) return; _phase = 4; }
            if (_phase == 4 && Kind == "harvest")
            {
                if (!Work.Replant.TryGetValue(_crop ?? "", out var plant)) { Finish(b, true); return; }
                if (Work.SeedsPlenty(b.Colony, _crop) && Work.Replant.TryGetValue(Work.FoodCrop(_crop), out var food)) plant = food; // (seeds enough: food in its place)
                Zone field = b.Colony.R.Zones.FirstOrDefault(z => z.Type == ZoneType.Fields && Zones.Inside(z, _cropAt));
                if (Work.ChosenNow(b.Colony, field) is var chosen && chosen != null && (Work.HaveInStock(b.Colony, chosen.Value.seed) || b.Body.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == chosen.Value.seed))) plant = chosen.Value; // (the season's chosen crop in its place)
                if (!Replanting(b, plant)) Finish(b, true);
                return;
            }
            Finish(b, true);
        }
        private bool _seedsFetched, _tilled; private Container _seedChest; private string _plantFail;
        private bool Replanting(Brain b, (GameObject sapling, string seed, int amount) plant)
        {
            Inventory bag = b.Body.GetInventory();
            int have = bag.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed).Sum(i => i.m_stack);
            if (have < plant.amount)
            {
                if (_seedsFetched) { b.Say(new[] { "No seeds to replant with.", "I've no seeds to plant with.", "The stores have no seeds to plant.", "No seeds left to put in the ground.", "Nothing to plant with: no seeds." }[UnityEngine.Random.Range(0, 5)], 4); _plantFail = "no seeds in the stores"; return false; }
                if (_seedChest == null) _seedChest = Stock.ChestsFor(b).Where(x => x.category != "treasury").Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed));
                if (_seedChest == null) { _seedsFetched = true; return true; }
                Stock.Walk sw = Stock.WalkTo(b, _seedChest);
                if (sw == Stock.Walk.Unreachable) { _seedChest = null; return true; }
                if (sw == Stock.Walk.Walking) return true;
                ItemDrop.ItemData seeds = _seedChest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed);
                Stock.Take(_seedChest, bag, seeds, Mathf.Max(plant.amount, 5));
                _seedsFetched = true; return true;
            }
            Stock.Walk pw = Stock.WalkNear(b, _cropAt, 1.6f, 1.0f);
            if (pw == Stock.Walk.Unreachable) { _plantFail = "can't reach the spot"; return false; }
            if (pw == Stock.Walk.Walking) return true;
            Heightmap hm = Heightmap.FindHeightmap(_cropAt);
            if (plant.sapling.GetComponent<Plant>()?.m_needCultivatedGround != false && (hm == null || !hm.IsCultivated(_cropAt)))
            {
                // Hoe it first, the cultivator's own way (its piece, as a player's click makes it), if they have one.
                ItemDrop.ItemData hoe = Gear.Tool(b, "cultivator");
                GameObject till = Work.TillPiece();
                if (hoe == null || till == null || _tilled) { b.Say(new[] { "The ground here wants hoeing, and I've no cultivator.", "This ground needs hoeing, and I've no cultivator.", "Hoe the ground first. I've no cultivator.", "No cultivator, and the ground wants hoeing.", "The soil wants turning, and I've no cultivator." }[UnityEngine.Random.Range(0, 5)], 4); _plantFail = hoe == null ? "needs a cultivator" : "the ground won't take the hoe"; return false; }
                if (!b.Body.IsItemEquiped(hoe)) { b.Body.EquipItem(hoe, false); return true; }
                Body_.Anim(b.Body)?.SetTrigger(hoe.m_shared.m_attack.m_attackAnimation);
                Object.Instantiate(till, new Vector3(_cropAt.x, ZoneSystem.instance.GetGroundHeight(_cropAt), _cropAt.z), Quaternion.identity);
                Gear.Wear(b, hoe);
                b.Body.RaiseSkill(Skills.SkillType.Farming, 0.5f);
                b.S.Bump("tilled");
                _tilled = true; _waitUntil = Time.time + 1.2f;
                return true;
            }
            for (int left = plant.amount; left > 0;)
            {
                ItemDrop.ItemData stack = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed);
                if (stack == null) break;
                int n = Math.Min(left, stack.m_stack); bag.RemoveItem(stack, n); left -= n;
            }
            Vector3 spot = new Vector3(_cropAt.x, ZoneSystem.instance.GetGroundHeight(_cropAt), _cropAt.z); // on the soil (a crop's own position can sit off it)
            GameObject planted = Object.Instantiate(plant.sapling, spot, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0));
            foreach (IPlaced placed in planted.GetComponentsInChildren<IPlaced>()) placed.OnPlaced();
            planted.GetComponent<Piece>()?.m_placeEffect.Create(_cropAt, Quaternion.identity);
            if (!WorkEmotes.Sow(b)) Body_.Anim(b.Body)?.SetTrigger("interact"); // (crouched to sow it, with Bob's Animations)
            b.S.Bump("replanted");
            if (b.S.Count("harvests") >= 100 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("farmer"); b.Colony.Log("byname", b.S.Name, b.S.Byname); }
            return false;
        }

        // Planting a sapling where the woodlot has room: the seeds from the stores, then the sapling in the ground.
        private void PlantTree(Brain b)
        {
            string t = O.Target; int at = t.IndexOf('@');
            string name = at > 6 ? t.Substring(6, at - 6) : "";
            var s = Work.Saplings.FirstOrDefault(x => x.sapling != null && x.sapling.name == name);
            if (s.sapling == null) { Finish(b, false); return; }
            if (!Replanting(b, (s.sapling, s.seed, s.amount))) { if (_plantFail != null) Block(b, _plantFail); else { b.S.Bump("planted"); Finish(b, true); } }
        }

        private void Sow(Brain b)
        {
            string t = O.Target; int at = t.IndexOf('@');
            string name = at > 4 ? t.Substring(4, at - 4) : "";
            var crop = Work.Replant.Values.FirstOrDefault(r => r.sapling != null && r.sapling.name == name);
            if (crop.sapling == null) { Finish(b, false); return; }
            if (!Replanting(b, crop)) { if (_plantFail != null) Block(b, _plantFail); else { b.S.Bump("sown"); Finish(b, true); } }
        }

        // Feeding a hungry beast: what it eats, from the stores, laid on the ground before it (it eats as tame beasts do).
        private bool _carrying;
        private void Feed(Brain b)
        {
            Character beast = _target != null ? _target.GetComponent<Character>() : null;
            MonsterAI ai = beast != null ? beast.GetComponent<MonsterAI>() : null;
            if (beast == null || beast.IsDead() || ai == null) { Gone(b); return; }
            Tameable tame = beast.GetComponent<Tameable>();
            if (tame != null && !tame.IsHungry() && !_carrying) { Finish(b, true); return; }
            var wants = ai.m_consumeItems.Where(f => f != null).Select(f => f.name).ToList();
            Inventory bag = b.Body.GetInventory();
            ItemDrop.ItemData food = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && wants.Contains(i.m_dropPrefab.name) && !i.m_equipped);
            if (food == null)
            {
                if (_carrying) { Finish(b, true); return; }
                if (_source == null) _source = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).Select(x => x.chest)
                    .FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && wants.Contains(i.m_dropPrefab.name) && !Stock.Kept(b.Colony, ch, i)));
                if (_source == null) { Block(b, "nothing in the stores it eats"); return; }
                Stock.Walk w = Stock.WalkTo(b, _source);
                if (w == Stock.Walk.Unreachable) { _source = null; return; } // (another chest with it, next time round)
                if (w == Stock.Walk.Walking) { if (NoHeadway(b, _source.transform.position)) Block(b, "can't reach the stores"); return; }
                ItemDrop.ItemData have = _source.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && wants.Contains(i.m_dropPrefab.name) && !Stock.Kept(b.Colony, _source, i));
                if (have == null || Stock.Take(_source, bag, have, 3) == 0) { Finish(b, false); return; }
                _carrying = true;
                return;
            }
            _carrying = true;
            if (Walking(b, 2.2f)) return;
            Body_.Anim(b.Body)?.SetTrigger("interact");
            Vector3 drop = beast.transform.position + (b.Pos - beast.transform.position).normalized * 0.8f + Vector3.up * 0.3f;
            ItemDrop.DropItem(food, 1, drop, Quaternion.identity);
            bag.RemoveItem(food, 1);
            b.S.Bump("fed");
            _waitUntil = Time.time + 1f;
            Finish(b, true);
        }

        // Culling a surplus beast for its meat and hide: the herder's weapon, the game's own blows; then gathering what it leaves.
        private void Cull(Brain b)
        {
            Character beast = _target != null ? _target.GetComponent<Character>() : null;
            if (beast == null || beast.IsDead())
            {
                if (_phase == 0) { _phase = 1; _waitUntil = Time.time + 1.5f; return; }
                if (Collect(b)) return;
                Finish(b, true); return;
            }
            ItemDrop.ItemData weapon = Gear.Tool(b, "weapon");
            if (weapon == null) { b.Idle("needs a weapon to cull with"); Finish(b, false); return; }
            if (!b.Body.IsItemEquiped(weapon)) { b.Body.EquipItem(weapon, false); return; }
            if (Walking(b, 1.8f)) return;
            Vector3 flat = beast.transform.position - b.Pos; flat.y = 0;
            if (flat.sqrMagnitude > 0.01f) b.Ai.LookTowards(flat.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.4f / Mathf.Max(0.3f, b.Productivity());
            Attack swing = weapon.m_shared.m_attack;
            Body_.Anim(b.Body)?.SetTrigger(swing.m_attackChainLevels > 1 ? swing.m_attackAnimation + "0" : swing.m_attackAnimation);
            var hit = new HitData { m_damage = weapon.GetDamage(), m_point = beast.GetCenterPoint(), m_dir = (beast.GetCenterPoint() - b.Body.GetCenterPoint()).normalized, m_skill = weapon.m_shared.m_skillType, m_toolTier = (short)weapon.m_shared.m_toolTier };
            hit.SetAttacker(b.Body);
            beast.Damage(hit);
            Gear.Wear(b, weapon);
            if (beast.IsDead() || beast.GetHealth() <= 0) b.S.Bump("culled");
        }

        // Carrying: pick it up, then to the stockpile it belongs in.
        private void Haul(Brain b)
        {
            if (_phase == 0)
            {
                ItemDrop drop = _target != null ? _target.GetComponent<ItemDrop>() : null;
                if (drop == null) { Gone(b); return; }
                if (!InReach(b, drop.transform.position))
                {
                    if (Under(b, drop.transform.position)) { Block(b, "can't reach it"); return; } // (stood under it and it's still up out of reach: on a wall top, a roof)
                    if (!b.Move.Moving) b.Move.To(drop.transform.position, 0.8f); if (b.Move.Failed) Block(b, "can't reach it"); return;
                }
                string gear = !Work.Loot(drop.m_itemData) && drop.m_itemData.m_dropPrefab != null && !Stock.Edible(drop.m_itemData) ? drop.m_itemData.m_dropPrefab.name : null;
                Got got = PickUpNow(b, drop);
                if (got == Got.Failed) { Finish(b, false); return; }
                if (got == Got.Waiting) { _waitUntil = Time.time + 0.3f; return; }
                if (gear != null) b.Hauled.Add(gear); // (a weapon or tool off the ground: the colony's, for the stores)
                _phase = 1;
            }
            Finish(b, true);
            b.Task = new DepositTask(); b.Task.Start(b);
        }

        private void Hunt(Brain b)
        {
            if (Target == null || Target.IsDead()) { if (Collect(b)) return; Finish(b, true); return; }
            // (run off out of the colony's reach, or not brought down in three minutes: let it go a while)
            if (!Follow.Near(b, Target.transform.position, 25)) { b.Foe = null; Block(b, "it ran off out of reach"); return; }
            if (_huntUntil == 0) _huntUntil = Time.time + 180f;
            if (Time.time > _huntUntil) { b.Foe = null; Block(b, "it got away"); return; }
            b.Foe = Target;
            b.SetFoeFor(30);
        }
        private float _guardUntil, _huntUntil;
        // A patrol: the route's waypoints in turn (from the nearest, so two guards go apart), a pause at each to look about.
        private int _wp = -1; private bool _looking;
        private void Guard(Brain b)
        {
            if (_guardUntil == 0) _guardUntil = Time.time + 120f;
            if (Time.time > _guardUntil) { Finish(b, true); return; }
            // The round: the colony's watch posts in order; an old guard post's corners.
            var stops = new List<Vector3>();
            if (O.Target.StartsWith("round:")) stops.AddRange(WatchPost.All(b.Colony.Position).Select(w => w.transform.position + w.transform.forward * 1.2f));
            else { Zone route = b.Colony.R.Zones.FirstOrDefault(z => z.Id == O.Target); if (route != null) for (int i = 0; i < route.Corners; i++) { var (x, z) = route.Corner(i); stops.Add(new Vector3(x, route.Floor, z)); } }
            if (stops.Count == 0) { Finish(b, true); return; }
            Vector3 Point(int i) { Vector3 p = stops[i % stops.Count]; return Colony.Spot(p, 1.5f) ?? p; }
            int count = stops.Count;
            if (_wp < 0) { float best = float.MaxValue; for (int i = 0; i < count; i++) { float d = Utils.DistanceXZ(b.Pos, Point(i)); if (d < best) { best = d; _wp = i; } } }
            if (b.Move.Moving) return;
            Vector3 at = Point(_wp);
            if (Utils.DistanceXZ(b.Pos, at) > 2f && !b.Move.Failed) { _looking = false; b.Move.To(at, 1.2f); return; }
            if (!_looking) { _looking = true; _waitUntil = Time.time + UnityEngine.Random.Range(3f, 6f); b.Ai.LookTowards(Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0) * Vector3.forward); if (WorkEmotes.Watch(b)) _waitUntil = Mathf.Max(_waitUntil, Time.time + 5f); return; } // (a look about at each waypoint)
            _looking = false; _wp = (_wp + 1) % count;
            b.Move.To(Point(_wp), 1.2f);
        }
        // Fishing from the dock: a rod and bait, and patience (the catch comes at the game's pace, not by the minigame).
        private float _fishUntil; private Vector3? _dockAt;
        private void Fish(Brain b)
        {
            Zone dock = b.Colony.R.Zones.FirstOrDefault(z => z.Id == O.Target);
            Vector3 spot = dock != null ? Zones.Centre(dock) : _at;
            if (_dockAt == null) _dockAt = Colony.Spot(spot, 2f) ?? spot; // (one place on the dock, not a new one each step)
            Stock.Walk dw = Stock.WalkNear(b, _dockAt.Value, 3f, 1.5f);
            if (dw == Stock.Walk.Unreachable) { Block(b, "can't reach the dock"); return; }
            if (dw == Stock.Walk.Walking) return;
            ItemDrop.ItemData rod = Gear.Tool(b, "rod");
            ItemDrop.ItemData bait = Gear.Items(b).FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("FishingBait"));
            if (rod == null || bait == null) { Block(b, rod == null ? "no fishing rod" : "no bait"); return; }
            if (_fishUntil == 0) _fishUntil = Time.time + 180f;
            if (Time.time > _fishUntil) { Finish(b, true); return; }
            _waitUntil = Time.time + 45f / Mathf.Max(0.3f, b.Productivity());
            b.SetDoing("fishing");
            bool caught = UnityEngine.Random.value < 0.4f + b.SkillLevel(Skills.SkillType.Fishing) / 250f;
            WorkEmotes.Fishing(b, caught); // (a cast, or the reel-in: with Bob's Animations)
            if (caught)
            {
                GameObject fish = Assets.Find("Fish1");
                b.Body.GetInventory().RemoveItem(bait, 1);
                if (fish != null && b.Body.GetInventory().AddItem(fish, 1)) { b.S.Bump("fish"); b.Say(new[] { "A fish!", "Got one!", "A fine fish!", "Fish on!", "A big one!" }[UnityEngine.Random.Range(0, 5)], 3); }
                b.Body.RaiseSkill(Skills.SkillType.Fishing, 1f);
            }
        }

        // Stations: fetch fuel or what it works on from the stores, then feed it through its own RPCs (so it smelts and cooks as the game does).
        private Container _source; private string _carry; private int _fetched; private bool Kind_Collect;
        private void Station(Brain b)
        {
            if (_target == null) { Gone(b); return; }
            Inventory bag = b.Body.GetInventory();
            if (_phase == 0 && _target.GetComponent<CookingStation>() is CookingStation done && Work.Cook(done, "HaveDoneItem"))
            {
                // Cooked: take it off the fire (the station gives it out, beside us), then gather it.
                if (Walking(b, 3.0f)) return; // (a station is used from a pace or two away: a fire keeps everyone back)
                _target.GetComponent<ZNetView>().InvokeRPC("RPC_RemoveDoneItem", b.Pos, 1);
                _waitUntil = Time.time + 0.5f;
                if (!Work.Cook(done, "HaveDoneItem")) { _phase = 1; Kind_Collect = true; }
                return;
            }
            if (_phase == 0 && Kind == "tap" && _target.GetComponent<Fermenter>() is Fermenter barrel)
            {
                // Ready: tap it (the barrel gives out the mead beside it a moment later), then gather it.
                if (Walking(b, 2.6f)) return;
                if (Work.FermentStatus(barrel) == "Ready") { Body_.Anim(b.Body)?.SetTrigger("interact"); _target.GetComponent<ZNetView>().InvokeRPC("RPC_Tap"); }
                _waitUntil = Time.time + barrel.m_tapDelay + 0.3f; _phase = 1; Kind_Collect = true;
                return;
            }
            if (Kind_Collect) { if (Collect(b)) return; Finish(b, true); return; }
            if (_phase == 0)
            {
                // What to bring.
                ZNetView sv = _target.GetComponent<ZNetView>();
                Fireplace fire = _target.GetComponent<Fireplace>(); Smelter smelter = _target.GetComponent<Smelter>(); CookingStation cook = _target.GetComponent<CookingStation>();
                var wants = new List<string>();
                if (fire != null && fire.m_fuelItem != null) wants.Add(fire.m_fuelItem.name);
                if (cook != null && cook.m_useFuel && cook.m_fuelItem != null && sv.GetZDO().GetFloat(ZDOVars.s_fuel) < cook.m_maxFuel - 1) wants.Add(cook.m_fuelItem.name); // (an oven's wood first)
                if (smelter != null)
                {
                    if (smelter.m_fuelItem != null && sv.GetZDO().GetFloat(ZDOVars.s_fuel) < smelter.m_maxFuel * 0.5f) wants.Add(smelter.m_fuelItem.name);
                    if (Work.QueueSize(smelter) < smelter.m_maxOre / 2) wants.AddRange(smelter.m_conversion.Where(cv => cv.m_from != null && Work.Feedable(b.Colony, smelter, cv.m_from.name)).Select(cv => cv.m_from.name));
                }
                if (cook != null) wants.AddRange(cook.m_conversion.Where(cv => cv.m_from != null).Select(cv => cv.m_from.name));
                Fermenter cask = _target.GetComponent<Fermenter>();
                if (cask != null) wants.AddRange(cask.m_conversion.Where(cv => cv.m_from != null).Select(cv => cv.m_from.name));
                // In the order wanted: fuel before ore (ore in a cold smelter does nothing).
                var chests = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
                foreach (string want in wants)
                {
                    _source = chests.Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want && !Stock.Kept(b.Colony, ch, i)));
                    if (_source != null) { _carry = want; break; }
                }
                // (nothing to bring in any stockpile: set aside a while, not handed straight back for the next to take up and put down, round
                // and round, everyone "tending the fire" with no wood to tend it with)
                if (_source == null) { if (wants.Count > 0) Block(b, $"nothing to bring: no {Localization.instance.Localize(ObjectDB.instance?.GetItemPrefab(wants[0])?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? wants[0]).ToLower()} in the stores"); else Finish(b, false); return; }
                _phase = 1;
            }
            if (_phase == 1)
            {
                Stock.Walk w = Stock.WalkTo(b, _source);
                if (w == Stock.Walk.Unreachable) { _source = null; _phase = 0; return; } // (another chest with it, if there is one)
                if (w == Stock.Walk.Walking) { if (NoHeadway(b, _source.transform.position)) Block(b, "can't reach the stores"); return; }
                ItemDrop.ItemData have = _source.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _carry && !Stock.Kept(b.Colony, _source, i));
                int room = 5;
                ZNetView tv = _target.GetComponent<ZNetView>();
                Fireplace fp = _target.GetComponent<Fireplace>(); Smelter sm = _target.GetComponent<Smelter>(); CookingStation oven = _target.GetComponent<CookingStation>();
                if (fp != null) room = Mathf.Clamp(Mathf.FloorToInt(fp.m_maxFuel - tv.GetZDO().GetFloat(ZDOVars.s_fuel)), 0, 5);
                else if (oven != null && oven.m_useFuel && oven.m_fuelItem != null && oven.m_fuelItem.name == _carry) room = Mathf.Clamp(Mathf.FloorToInt(oven.m_maxFuel - tv.GetZDO().GetFloat(ZDOVars.s_fuel)), 0, 5);
                else if (sm != null && sm.m_fuelItem != null && sm.m_fuelItem.name == _carry) room = Mathf.Clamp(Mathf.FloorToInt(sm.m_maxFuel - tv.GetZDO().GetFloat(ZDOVars.s_fuel)), 0, 5);
                else if (sm != null) room = Mathf.Clamp(sm.m_maxOre - Work.QueueSize(sm), 0, 5);
                else if (oven != null) room = Mathf.Clamp(Work.CookFreeCount(oven), 1, 5); // (a spit's empty slots, filled in one trip)
                else if (_target.GetComponent<Fermenter>() != null) room = 1;
                _fetched = have != null && room > 0 ? Stock.Take(_source, bag, have, room) : 0;
                if (_fetched == 0) { Finish(b, false); return; }
                _phase = 2; b.Move.To(Approach(b), 1.4f);
                return;
            }
            if (_phase == 2)
            {
                if (Walking(b, 3.0f)) return; // (a station is used from a pace or two away: a fire keeps everyone back)
                ZNetView v = _target.GetComponent<ZNetView>();
                ItemDrop.ItemData item = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _carry);
                if (item == null || v == null || !v.IsValid()) { Finish(b, true); return; }
                Body_.Anim(b.Body)?.SetTrigger("interact");
                Smelter smelter = _target.GetComponent<Smelter>(); CookingStation cook = _target.GetComponent<CookingStation>(); Fireplace fire = _target.GetComponent<Fireplace>();
                bool fuel = (smelter != null && smelter.m_fuelItem != null && smelter.m_fuelItem.name == _carry) || fire != null || cook != null && cook.m_useFuel && cook.m_fuelItem != null && cook.m_fuelItem.name == _carry;
                // Only what it will take (a full fire or station ignores the rest, and the item would be gone).
                float max = fire != null ? fire.m_maxFuel : smelter != null ? smelter.m_maxFuel : cook != null ? cook.m_maxFuel : 0;
                bool full = fuel && max > 0 && Mathf.CeilToInt(v.GetZDO().GetFloat(ZDOVars.s_fuel)) >= max
                    || smelter != null && !fuel && Work.QueueSize(smelter) >= smelter.m_maxOre
                    || cook != null && !fuel && (!Work.CookFree(cook) || !Work.CookLit(cook))
                    || _target.GetComponent<Fermenter>() is Fermenter f && Work.FermentStatus(f) != "Empty";
                if (full) { Finish(b, true); return; } // leftovers go back to the stores with the next deposit
                if (_target.GetComponent<Fermenter>() != null) v.InvokeRPC("RPC_AddItem", _carry.GetStableHashCode(), false);
                else if (cook != null && !fuel) v.InvokeRPC("RPC_AddItem", _carry, false);
                else if (smelter != null && !fuel) v.InvokeRPC("RPC_AddOre", _carry, false);
                else v.InvokeRPC("RPC_AddFuel");
                bag.RemoveItem(item, 1);
                _waitUntil = Time.time + 0.6f;
                if (--_fetched <= 0) Finish(b, true);
                return;
            }
        }

        private void Block(Brain b, string why)
        {
            if (b.Colony.IsOwner) b.Colony.Board.Block(O.Id, why); else Net.Report(b.Colony, b, O, "block:" + why);
            Done = true;
            b.Idle(why);
        }
        private void Gone(Brain b)
        {
            if (Work.Exists(O.Target)) { Finish(b, false); return; } // still in the world, only not loaded here: let it go, don't cancel it
            Done = true;
            if (b.Colony == null) return;
            if (b.Colony.IsOwner) b.Colony.Board.Cancel(O.Id); else Net.Report(b.Colony, b, O, "cancel");
        }
        private void Finish(Brain b, bool ok)
        {
            Done = true;
            if (b.Colony == null) return;
            if (b.Colony.IsOwner) { if (ok) Completed(b); else b.Colony.Board.Release(O.Id, b.S.Vid); }
            else Net.Report(b.Colony, b, O, ok ? "done" : "release");
        }
        private void Completed(Brain b)
        {
            if (b.Colony.Board.Complete(O.Id, b.S.Vid))
            {
                b.S.Bump("orders");
                if (b.S.Count("orders") % 100 == 0) Worth.Gain(b.S, "orders100");
                b.Dirty();
            }
        }
        public override void Stop(Brain b)
        {
            if (!Done) Finish(b, false);
            if (Kind == "hunt") b.Foe = null;
            base.Stop(b);
        }
    }
}
