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
    }

    // The colony's work: scanners (zones, the ground, fires and stations) post orders on the board; villagers take them as WorkTasks.
    internal static class Work
    {
        private static readonly Collider[] Hits = new Collider[2048];
        private static readonly Dictionary<ZDOID, float> FirstSeen = new Dictionary<ZDOID, float>();
        public static readonly HashSet<string> Crops = new HashSet<string>(); // grown crop prefabs (from the cultivator's saplings)

        private static string Key(ZNetView v) => v.GetZDO().m_uid.ToString();
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
        public static void Scan(Colony c)
        {
            double now = c.Now;
            LearnCrops();
            foreach (Zone z in c.R.Zones)
            {
                ZoneInfo info = Tables.Of(z.Type);
                if (info.Worker == null) continue;
                Vector3 centre = Zones.Centre(z); float reach = Zones.Reach(z) + 2;
                switch (z.Type)
                {
                    case ZoneType.GuardPost: c.Board.Post("guard", z.Id, Prof.Guard, 2, centre.x, centre.y, centre.z, now, z.Id, "post"); continue;
                    case ZoneType.FishingDock: c.Board.Post("fish", z.Id, Prof.Fisherman, 2, centre.x, centre.y, centre.z, now, z.Id, "dock"); continue;
                }
                int posted = 0;
                foreach (GameObject go in Things(centre, reach))
                {
                    if (posted >= 12) break;
                    Vector3 p = go.transform.position;
                    if (!Zones.Inside(z, p)) continue;
                    ZNetView v = go.GetComponent<ZNetView>();
                    if (v == null || !v.IsValid()) continue;
                    string kind = null; Prof prof = info.Worker.Value; int prio = 3;
                    switch (z.Type)
                    {
                        case ZoneType.Woodlot:
                            if (go.GetComponent<TreeLog>() != null) { kind = "chop"; prio = 4; }
                            else if (go.GetComponent<TreeBase>() != null) kind = "chop";
                            break;
                        case ZoneType.Mine:
                            if (go.GetComponent<MineRock5>() != null || go.GetComponent<MineRock>() != null || IsRock(go)) kind = "mine";
                            break;
                        case ZoneType.Fields:
                            Pickable crop = go.GetComponent<Pickable>();
                            if (crop != null && Crops.Contains(Utils.GetPrefabName(go)) && crop.CanBePicked()) { kind = "harvest"; prio = 4; }
                            break;
                        case ZoneType.Foraging:
                            Pickable wild = go.GetComponent<Pickable>();
                            if (wild != null && !Crops.Contains(Utils.GetPrefabName(go)) && wild.CanBePicked() && wild.m_itemPrefab != null && Forage(wild)) kind = "pick";
                            break;
                        case ZoneType.Hunting:
                            Character beast = go.GetComponent<Character>();
                            if (beast != null && !beast.IsDead() && !beast.IsTamed() && beast.GetFaction() == Character.Faction.AnimalsVeg) kind = "hunt";
                            break;
                    }
                    if (kind == null) continue;
                    c.Board.Post(kind, Key(v), prof, prio, p.x, p.y, p.z, now, z.Id, z.Type.ToString());
                    posted++;
                }
            }
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
                    c.Board.Post("haul", Key(v), Prof.Hauler, 2, drop.transform.position.x, drop.transform.position.y, drop.transform.position.z, now, "", "ground");
                    hauls++;
                }
                if (FirstSeen.Count > 4000) FirstSeen.Clear();
            }
            Faith.Bog(c);
            Crafts.Scan(c, now);
            // BuildOrders' planned ghosts in the colony: one standing order for the builders (they take the pieces one by one).
            if (Compat.GhostCount(c.Position, c.Radius) > 0) c.Board.Post("build", "ghosts", Prof.Builder, 3, c.Position.x, c.Position.y, c.Position.z, now, "", "plans");
            // Fires that burn low, and the stations: fuel and feeding.
            foreach (Piece p in c.PiecesIn(c.Radius))
            {
                Fireplace fire = p.GetComponent<Fireplace>();
                ZNetView v = p.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                if (fire != null && !fire.m_infiniteFuel && fire.m_fuelItem != null && c.RoomAt(p.transform.position) != null && v.GetZDO().GetFloat(ZDOVars.s_fuel) < fire.m_maxFuel * 0.5f && HaveInStock(c, fire.m_fuelItem.name))
                    c.Board.Post("fuel", Key(v), Prof.Woodcutter, 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "fire");
                Smelter smelter = p.GetComponent<Smelter>();
                if (smelter != null)
                {
                    bool needFuel = smelter.m_fuelItem != null && v.GetZDO().GetFloat(ZDOVars.s_fuel) < smelter.m_maxFuel * 0.5f && HaveInStock(c, smelter.m_fuelItem.name);
                    bool needOre = QueueSize(smelter) < smelter.m_maxOre / 2 && smelter.m_conversion.Any(cv => cv.m_from != null && HaveInStock(c, cv.m_from.name));
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
                if (cook != null && (Cook(cook, "HaveDoneItem") || Cook(cook, "IsFireLit") && CookFree(cook) && cook.m_conversion.Any(cv => cv.m_from != null && HaveInStock(c, cv.m_from.name))))
                    c.Board.Post("cook", Key(v), Prof.Cook, Cook(cook, "HaveDoneItem") ? 5 : 3, p.transform.position.x, p.transform.position.y, p.transform.position.z, now, "", "station");
            }
        }
        private static readonly System.Reflection.MethodInfo FermentStatusMethod = AccessTools.Method(typeof(Fermenter), "GetStatus");
        public static bool Roofed(Vector3 at) { Cover.GetCoverForPoint(at + Vector3.up * 0.8f, out float _, out bool roof); return roof; }
        public static string FermentStatus(Fermenter f) { try { return FermentStatusMethod.Invoke(f, null).ToString(); } catch { return ""; } }
        public static bool Cook(CookingStation s, string method) { try { return (bool)AccessTools.Method(typeof(CookingStation), method).Invoke(s, null); } catch { return false; } }
        public static bool CookFree(CookingStation s) { try { return (int)AccessTools.Method(typeof(CookingStation), "GetFreeSlot").Invoke(s, null) >= 0; } catch { return false; } }
        private static readonly System.Reflection.MethodInfo QueueMethod = AccessTools.Method(typeof(Smelter), "GetQueueSize");
        public static int QueueSize(Smelter s) { try { return (int)QueueMethod.Invoke(s, null); } catch { return 99; } }
        public static bool HaveInStock(Colony c, string prefab) => Stock.Chests(c).Any(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest) && x.chest.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab));
        private static bool Forage(Pickable p)
        {
            string n = p.m_itemPrefab.name;
            return n == "Raspberry" || n == "Blueberries" || n == "Cloudberry" || n == "Mushroom" || n == "MushroomYellow" || n == "MushroomBlue" || n == "Thistle" || n == "Dandelion" || n == "Flint" || n == "Stone" || n == "Wood" || n.StartsWith("Mushroom");
        }
        private static bool IsRock(GameObject go)
        {
            Destructible d = go.GetComponent<Destructible>();
            if (d == null) return false;
            string n = Utils.GetPrefabName(go).ToLowerInvariant();
            return n.Contains("rock") || n.Contains("copper") || n.Contains("tin") || n.Contains("silver") || n.Contains("obsidian");
        }
        // Everything networked near a point (trees, rocks, plants, beasts), each once.
        public static IEnumerable<GameObject> Things(Vector3 at, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(at, radius, Hits, ~0, QueryTriggerInteraction.Collide);
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
            for (int i = 0; i < n; i++) { ItemDrop d = Hits[i].GetComponentInParent<ItemDrop>(); if (d != null && seen.Add(d)) yield return d; }
        }
        private static bool _learned;
        // Which grown prefabs are crops, and the sapling and seeds to replant each: from the cultivator's own pieces.
        public static readonly Dictionary<string, (GameObject sapling, string seed, int amount)> Replant = new Dictionary<string, (GameObject, string, int)>();
        private static void LearnCrops()
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
                    if (grown.GetComponent<Pickable>() == null) continue; // trees from saplings aren't crops
                    Crops.Add(grown.name);
                    if (req != null) Replant[grown.name] = (sapling, req.m_resItem.name, req.m_amount);
                }
            }
        }
        public static void Forget() { FirstSeen.Clear(); _learned = false; Crops.Clear(); Replant.Clear(); }

        // ---- what a villager does next ----
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            Gear.Problem = null;
            if (Time.time > b.NoStoreUntil && (Carrying(b) >= 4 || b.Body.GetInventory().GetEmptySlots() <= 2 && Carrying(b) > 0))
            {
                if (b.Body.GetInventory().GetAllItems().Where(Loot).Any(i => Stock.Destination(c, i, b.Pos) != null)) return new DepositTask();
                b.NoStoreUntil = Time.time + 30; b.Say("Where do I put all this? (no stockpile for it)", 5);
            }
            if (Time.time > b.NoRepairUntil && RepairTask.Worn(b).Count > 0) return new RepairTask();
            Prof prof = b.S.Prof;
            if (prof == Prof.Skald) return null; // skalds rest by day and play at dusk
            if (prof == Prof.Healer) { Task heal = HealTask.Next(b); if (heal != null) return heal; }
            if (prof == Prof.Scout && Time.time > b.NoScoutUntil) return new ScoutTask();
            if (prof == Prof.Builder && c.IsOwner && Compat.GhostCount(c.Position, c.Radius) > 0 && Gear.Tool(b, "hammer") != null && Time.time > b.NoBuildUntil) return new BuildTask();
            if (prof == Prof.Trader && c.R.Rank >= Rank.Village && Time.time > b.NoTradeUntil && b.Body.GetInventory().GetEmptySlots() > 1 && Stock.Chests(c, b.Pos).Any(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest) && x.chest.GetInventory().GetAllItems().Count(Trade.Sellable) >= 3)) return new TradeTask();
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
        private static bool NothingFor(Colony c, Prof p) => !c.Board.All.Any(o => o.State == OrderState.Open && o.Prof == p);
        internal static string GearFor(Brain b, Order o)
        {
            if (o.Prof == Prof.Hauler) return null;
            int tier = 0;
            if (o.Kind == "chop" || o.Kind == "mine")
            {
                GameObject t = Resolve(o.Target);
                tier = t == null ? 0 : t.GetComponent<TreeBase>()?.m_minToolTier ?? t.GetComponent<TreeLog>()?.m_minToolTier ?? t.GetComponent<MineRock5>()?.m_minToolTier ?? t.GetComponent<MineRock>()?.m_minToolTier ?? t.GetComponent<Destructible>()?.m_minToolTier ?? 0;
            }
            if (o.Kind == "harvest" || o.Kind == "pick" || o.Kind == "fuel" || o.Kind == "smelt" || o.Kind == "cook" || o.Kind == "ferment" || o.Kind == "tap" || o.Kind == "craft" || o.Kind == "guard") return o.Kind == "guard" ? Gear.Lacks(b, Prof.Guard) : null;
            return Gear.Lacks(b, o.Prof, tier);
        }
        public static int Carrying(Brain b) => b.Body.GetInventory().GetAllItems().Count(i => Loot(i));
        // What a villager carries that belongs in the stockpiles (not its own gear, food for a day, or its seeds for replanting).
        public static bool Loot(ItemDrop.ItemData i)
        {
            if (i.m_equipped) return false;
            var t = i.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility) return false;
            if (t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.Torch) return false;
            if (Stock.Edible(i)) return false;
            if (i.m_dropPrefab != null && (i.m_dropPrefab.name == "FishingBait" || Replant.Values.Any(r => r.seed == i.m_dropPrefab.name) && i.m_stack <= 10)) return false;
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
            _to = b.Body.GetInventory().GetAllItems().Where(Work.Loot).Select(i => Stock.Destination(b.Colony, i, b.Pos)).FirstOrDefault(d => d != null);
            if (_to == null) { Done = true; b.NoStoreUntil = Time.time + 30; b.Idle("nowhere to put things (no stockpile)"); return; }
            b.Move.To(_to.transform.position, 1.8f);
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_to == null || b.Move.Failed) { Done = true; b.NoStoreUntil = Time.time + 30; return; }
            if (!b.Move.Arrived && Vector3.Distance(b.Pos, _to.transform.position) > 2.5f) { if (!b.Move.Moving) b.Move.To(_to.transform.position, 1.8f); return; }
            Inventory bag = b.Body.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData item in bag.GetAllItems().Where(Work.Loot).ToList())
            {
                Container dest = Stock.Destination(b.Colony, item, b.Pos);
                if (dest != _to) continue;
                if (Stock.Put(bag, _to, item) > 0) { b.S.Bump("loads"); moved++; }
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
        private readonly List<ItemDrop> _collect = new List<ItemDrop>();
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
            if (Kind != "guard" && Kind != "fish" && _target == null) { Gone(b); return; } // its target is gone (picked up, felled, destroyed): the job with it
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
                case "guard": return "on guard";
                case "fish": return "fishing";
                case "fuel": return "tending the fire";
                case "smelt": return "feeding the smelter";
                case "cook": return "cooking";
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
            if (c is MeshCollider m && !m.convex) return c.ClosestPointOnBounds(from);
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
                default: Finish(b, false); break;
            }
        }
        private bool Renew(Brain b) => b.Colony.IsOwner ? b.Colony.Board.Renew(O.Id, b.S.Vid, b.Colony.Now) : Net.Renew(b.Colony, b, O);

        private float _closeIn;
        private bool Walking(Brain b, float near)
        {
            if (Near(b, near)) { b.Move.Stop(); _closeIn = 0; return false; }
            if (b.Move.Failed) { Block(b, "can't reach it"); return true; }
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
            if (_phase == 2) { _collect.Clear(); _collect.AddRange(Work.Drops(b.Pos, 7f).Take(8)); _phase = 3; }
            _collect.RemoveAll(d => d == null);
            if (_collect.Count == 0) return false;
            ItemDrop next = _collect.OrderBy(d => Vector3.Distance(d.transform.position, b.Pos)).First();
            if (Vector3.Distance(next.transform.position, b.Pos) > 1.6f) { if (!b.Move.Moving) b.Move.To(next.transform.position, 0.8f); if (b.Move.Failed) _collect.Remove(next); return true; }
            if (!PickUp(b, next)) _collect.Remove(next);
            return true;
        }
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
                Body_.Anim(b.Body)?.SetTrigger("interact");
                p.Interact(b.Body, false, false);
                b.Body.RaiseSkill(Skills.SkillType.Farming, 1f);
                _phase = 1; _waitUntil = Time.time + 0.6f;
                b.S.Bump(Kind == "harvest" ? "harvests" : "forage");
                return;
            }
            if (_phase <= 3) { if (Collect(b)) return; _phase = 4; }
            if (_phase == 4 && Kind == "harvest")
            {
                if (!Work.Replant.TryGetValue(_crop ?? "", out var plant)) { b.S.Bump("replant:unknown " + _crop); Finish(b, true); return; }
                if (!Replanting(b, plant)) Finish(b, true);
                return;
            }
            Finish(b, true);
        }
        private bool _seedsFetched; private Container _seedChest;
        private bool Replanting(Brain b, (GameObject sapling, string seed, int amount) plant)
        {
            Inventory bag = b.Body.GetInventory();
            int have = bag.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed).Sum(i => i.m_stack);
            if (have < plant.amount)
            {
                if (_seedsFetched) { b.Say("No seeds to replant with.", 4); b.S.Bump("replant:noseeds"); return false; }
                if (_seedChest == null) _seedChest = Stock.Chests(b.Colony, b.Pos).Where(x => x.category != "treasury").Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed));
                if (_seedChest == null) { _seedsFetched = true; return true; }
                if (Vector3.Distance(b.Pos, _seedChest.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(_seedChest.transform.position, 1.8f); if (b.Move.Failed) _seedsFetched = true; return true; }
                ItemDrop.ItemData seeds = _seedChest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == plant.seed);
                Stock.Take(_seedChest, bag, seeds, Mathf.Max(plant.amount, 5));
                _seedsFetched = true; return true;
            }
            if (Vector3.Distance(b.Pos, _cropAt) > 1.6f) { if (!b.Move.Moving) b.Move.To(_cropAt, 1.0f); if (b.Move.Failed) return false; return true; }
            Heightmap hm = Heightmap.FindHeightmap(_cropAt);
            if (hm == null || !hm.IsCultivated(_cropAt)) { b.Say("The ground here wants hoeing again.", 4); b.S.Bump("replant:notcultivated"); return false; }
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
            Body_.Anim(b.Body)?.SetTrigger("interact");
            b.S.Bump("replanted");
            if (b.S.Count("harvests") >= 100 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("farmer"); b.Colony.Log("byname", b.S.Name, b.S.Byname); }
            return false;
        }

        // Carrying: pick it up, then to the stockpile it belongs in.
        private void Haul(Brain b)
        {
            if (_phase == 0)
            {
                ItemDrop drop = _target != null ? _target.GetComponent<ItemDrop>() : null;
                if (drop == null) { Gone(b); return; }
                if (Vector3.Distance(b.Pos, drop.transform.position) > 1.4f) { if (!b.Move.Moving) b.Move.To(drop.transform.position, 0.8f); if (b.Move.Failed) Block(b, "can't reach it"); return; }
                Got got = PickUpNow(b, drop);
                if (got == Got.Failed) { Finish(b, false); return; }
                if (got == Got.Waiting) { _waitUntil = Time.time + 0.3f; return; }
                _phase = 1;
            }
            Finish(b, true);
            b.Task = new DepositTask(); b.Task.Start(b);
        }

        private void Hunt(Brain b)
        {
            if (Target == null || Target.IsDead()) { if (Collect(b)) return; Finish(b, true); return; }
            b.Foe = Target;
            b.SetFoeFor(30);
        }
        private float _guardUntil;
        private void Guard(Brain b)
        {
            if (_guardUntil == 0) _guardUntil = Time.time + 90f;
            if (Time.time > _guardUntil) { Finish(b, true); return; }
            if (b.Move.Moving) return;
            Zone post = b.Colony.R.Zones.FirstOrDefault(z => z.Id == O.Target);
            Vector3 c = post != null ? Zones.Centre(post) : _at;
            Vector3? to = Colony.Spot(c, post != null ? Mathf.Min(8, Zones.Reach(post)) : 6);
            if (to != null) b.Move.To(to.Value, 1f);
            _waitUntil = Time.time + UnityEngine.Random.Range(4f, 9f);
        }
        // Fishing from the dock: a rod and bait, and patience (the catch comes at the game's pace, not by the minigame).
        private float _fishUntil;
        private void Fish(Brain b)
        {
            Zone dock = b.Colony.R.Zones.FirstOrDefault(z => z.Id == O.Target);
            Vector3 spot = dock != null ? Zones.Centre(dock) : _at;
            if (Vector3.Distance(b.Pos, spot) > 3f) { if (!b.Move.Moving) b.Move.To(Colony.Spot(spot, 2f) ?? spot, 1.5f); if (b.Move.Failed) Block(b, "can't reach the dock"); return; }
            ItemDrop.ItemData rod = Gear.Tool(b, "rod");
            ItemDrop.ItemData bait = Gear.Items(b).FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("FishingBait"));
            if (rod == null || bait == null) { Block(b, rod == null ? "no fishing rod" : "no bait"); return; }
            if (_fishUntil == 0) _fishUntil = Time.time + 180f;
            if (Time.time > _fishUntil) { Finish(b, true); return; }
            _waitUntil = Time.time + 45f / Mathf.Max(0.3f, b.Productivity());
            b.SetDoing("fishing");
            if (UnityEngine.Random.value < 0.4f + b.SkillLevel(Skills.SkillType.Fishing) / 250f)
            {
                GameObject fish = Assets.Find("Fish1");
                b.Body.GetInventory().RemoveItem(bait, 1);
                if (fish != null && b.Body.GetInventory().AddItem(fish, 1)) { b.S.Bump("fish"); b.Say("A fish!", 3); }
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
                if (smelter != null)
                {
                    if (smelter.m_fuelItem != null && sv.GetZDO().GetFloat(ZDOVars.s_fuel) < smelter.m_maxFuel * 0.5f) wants.Add(smelter.m_fuelItem.name);
                    if (Work.QueueSize(smelter) < smelter.m_maxOre / 2) wants.AddRange(smelter.m_conversion.Where(cv => cv.m_from != null).Select(cv => cv.m_from.name));
                }
                if (cook != null) wants.AddRange(cook.m_conversion.Where(cv => cv.m_from != null).Select(cv => cv.m_from.name));
                Fermenter cask = _target.GetComponent<Fermenter>();
                if (cask != null) wants.AddRange(cask.m_conversion.Where(cv => cv.m_from != null).Select(cv => cv.m_from.name));
                // In the order wanted: fuel before ore (ore in a cold smelter does nothing).
                var chests = Stock.Chests(b.Colony, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
                foreach (string want in wants)
                {
                    _source = chests.Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want));
                    if (_source != null) { _carry = want; break; }
                }
                if (_source == null) { Finish(b, false); return; }
                _phase = 1;
            }
            if (_phase == 1)
            {
                if (Vector3.Distance(b.Pos, _source.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(_source.transform.position, 1.8f); if (b.Move.Failed) Block(b, "can't reach the stores"); return; }
                ItemDrop.ItemData have = _source.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _carry);
                int room = 5;
                ZNetView tv = _target.GetComponent<ZNetView>();
                Fireplace fp = _target.GetComponent<Fireplace>(); Smelter sm = _target.GetComponent<Smelter>();
                if (fp != null) room = Mathf.Clamp(Mathf.FloorToInt(fp.m_maxFuel - tv.GetZDO().GetFloat(ZDOVars.s_fuel)), 0, 5);
                else if (sm != null && sm.m_fuelItem != null && sm.m_fuelItem.name == _carry) room = Mathf.Clamp(Mathf.FloorToInt(sm.m_maxFuel - tv.GetZDO().GetFloat(ZDOVars.s_fuel)), 0, 5);
                else if (sm != null) room = Mathf.Clamp(sm.m_maxOre - Work.QueueSize(sm), 0, 5);
                else if (_target.GetComponent<CookingStation>() != null || _target.GetComponent<Fermenter>() != null) room = 1;
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
                bool fuel = (smelter != null && smelter.m_fuelItem != null && smelter.m_fuelItem.name == _carry) || fire != null;
                // Only what it will take (a full fire or station ignores the rest, and the item would be gone).
                float max = fire != null ? fire.m_maxFuel : smelter != null ? smelter.m_maxFuel : 0;
                bool full = fuel && max > 0 && Mathf.CeilToInt(v.GetZDO().GetFloat(ZDOVars.s_fuel)) >= max
                    || smelter != null && !fuel && Work.QueueSize(smelter) >= smelter.m_maxOre
                    || cook != null && !fuel && (!Work.CookFree(cook) || !Work.Cook(cook, "IsFireLit"))
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
