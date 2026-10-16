using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Ore claims in the game: the stake that surveys its ground while it's loaded, the global keys that carry each claim to its colony,
    // the miner's trip out (away, as a trader is) and the load they bring home. The rules are in Core/Claims.cs.
    internal static class Claims
    {
        public static bool TestElder; // (test: crypts open before the Elder is beaten)
        public static bool ElderBeaten => TestElder || ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey("defeated_gdking");
        public const string KeyHead = "jarldom_claim_", ColonyKey = "jarldom_claimcolony", IdKey = "jarldom_claimid", HuntKey = "jarldom_claimhunt";
        private static ZoneSystem Z => ZoneSystem.instance;

        public const string TakeHead = "jarldom_claimtake_";
        // Keys this game set or removed a moment ago: a client's change comes back from the server only after a round trip, and
        // reading the old value meanwhile would count a load twice (or take deposits out of the world twice).
        private static readonly Dictionary<string, (string value, float at)> Recent = new Dictionary<string, (string, float)>();
        private const float RecentFor = 15f;
        private static Dictionary<string, string> Keys(string head)
        {
            var keys = new Dictionary<string, string>();
            if (Z == null) return keys;
            foreach (string line in Z.GetGlobalKeys().Where(l => l.StartsWith(head)))
            {
                int space = line.IndexOf(' ');
                keys[space < 0 ? line : line.Substring(0, space)] = space < 0 ? "" : line.Substring(space + 1);
            }
            foreach (var kv in Recent.Where(kv => kv.Key.StartsWith(head) && Time.time - kv.Value.at < RecentFor).ToList())
            {
                if (kv.Value.value == null) keys.Remove(kv.Key); else keys[kv.Key] = kv.Value.value;
            }
            return keys;
        }
        private static void Set(string key, string value) { if (Z == null) return; Recent[key] = (value, Time.time); Z.SetGlobalKey(key + " " + value); }
        internal static void Unset(string key) { if (Z == null) return; Recent[key] = (null, Time.time); Z.RemoveGlobalKey(key); }
        public static void Forget() => Recent.Clear();

        // Every claim, with the loads brought home since its stake last looked (each load a key of its own, so the colony's game and
        // the stake's never write over each other): less left, more to take out of the world.
        public static IEnumerable<Claim> All()
        {
            var takes = Takes();
            foreach (var kv in Keys(KeyHead))
            {
                Claim c = Claim.Decode(kv.Key.Substring(KeyHead.Length), kv.Value);
                if (c == null) continue;
                foreach (var t in takes.Where(t => t.claim == c.Id)) Mining.Apply(c, t.got);
                yield return c;
            }
        }
        public static List<(string key, string claim, Dictionary<string, int> got)> Takes()
        {
            var list = new List<(string, string, Dictionary<string, int>)>();
            foreach (var kv in Keys(TakeHead))
            {
                string rest = kv.Key.Substring(TakeHead.Length); int us = rest.LastIndexOf('_'); if (us <= 0) continue;
                var got = new Dictionary<string, int>();
                foreach (string part in kv.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) { string[] p = part.Split(':'); if (p.Length == 2 && int.TryParse(p[1], out int n)) got[p[0]] = n; }
                list.Add((kv.Key, rest.Substring(0, us), got));
            }
            return list;
        }
        public static IEnumerable<Claim> Of(Colony c) => All().Where(x => x.Colony == c.Id.ToLowerInvariant());
        public static Claim Get(string id) => All().FirstOrDefault(x => x.Id == id);
        public static void Save(Claim c) { if (c.Id != "") Set(KeyHead + c.Id, c.Encode()); }
        // (a claim nobody visits never takes its loads in: past a few, they're folded into one key, so the world's keys don't grow
        // with every trip; only the colony's game writes loads, and a stake taking them in at that very moment counts some twice at worst)
        public static void Took(string claim, Dictionary<string, int> got)
        {
            if (got.Count == 0) return;
            var all = new Dictionary<string, int>(got);
            var waiting = Takes().Where(t => t.claim == claim).ToList();
            if (waiting.Count >= MaxLoads) foreach (var t in waiting) { foreach (var kv in t.got) all[kv.Key] = (all.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value; }
            Set(TakeHead + claim + "_" + Colony.NewId().Substring(0, 6), string.Join(";", all.Select(kv => kv.Key + ":" + kv.Value)));
            if (waiting.Count >= MaxLoads) foreach (var t in waiting) Unset(t.key);
        }
        private const int MaxLoads = 3;
        public static void Remove(string id) { Unset(KeyHead + id); foreach (var t in Takes().Where(t => t.claim == id)) Unset(t.key); }
        // The claim as the stake last wrote it, without loads not yet taken in: for the stake's own survey.
        internal static Claim Stored(string id) => Keys(KeyHead).TryGetValue(KeyHead + id, out string v) ? Claim.Decode(id, v) : null;
        public static Vector3 At(Claim c) => new Vector3(c.X, 0, c.Z);
        // A claim whose ground is loaded with no stake of its on it any more (taken away some other way than the hammer: an admin's
        // tool, another mod): gone too.
        private static float _nextSweep;
        public static void Sweep()
        {
            if (Time.time < _nextSweep || ZNetScene.instance == null || Z == null) return;
            _nextSweep = Time.time + 10f;
            foreach (Claim c in All().ToList())
            {
                Vector3 at = new Vector3(c.X, ZoneSystem.instance.GetGroundHeight(At(c)), c.Z);
                if (!ZNetScene.instance.IsAreaReady(at)) continue;
                var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at, 3f, near);
                if (!near.Any(p => p.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.GetZDO().GetString(IdKey, "") == c.Id)) Remove(c.Id); // (by the id on the stake itself)
            }
        }
        public static string Ores(Claim c) => c.Left.Where(kv => kv.Value.n > 0).Any() ? string.Join(", ", c.Left.Where(kv => kv.Value.n > 0).OrderByDescending(kv => kv.Value.n).Select(kv => $"~{kv.Value.n} {Name(kv.Key)}")) : "worked out";
        public static string Name(string prefab) { GameObject p = ObjectDB.instance?.GetItemPrefab(prefab) ?? Carts_Item(prefab); return p != null ? Localization.instance.Localize(p.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLower() : prefab; }
        private static GameObject Carts_Item(string lower) => ObjectDB.instance?.m_items.FirstOrDefault(i => i != null && i.name.ToLowerInvariant() == lower);
        public static string Title(Claim c) => c.Hunt ? (c.Camp ? "Hunting camp" : "Hunting stake") : (c.Crypt ? "Crypt claim" : c.Camp ? "Mining camp" : "Ore claim") + (c.Left.Count > 0 ? " (" + string.Join(", ", c.Left.OrderByDescending(kv => kv.Value.n).Take(2).Select(kv => Name(kv.Key).Replace(" ore", "").Replace("scrap ", ""))) + ")" : "");
        public static string Where(Claim c, Vector3 from) => $"{Mathf.RoundToInt(Utils.DistanceXZ(At(c), from))} m to the {ScoutTask.Compass(At(c) - from)}";

        // ---- deposits ----
        private static readonly Dictionary<string, (string ore, float each)> PerArea = new Dictionary<string, (string, float)>();
        private static readonly System.Reflection.FieldInfo Areas5Field = AccessTools.Field(typeof(MineRock5), "m_hitAreas");
        private static System.Collections.IList Areas5(MineRock5 m) => Areas5Field?.GetValue(m) as System.Collections.IList;
        private static readonly AccessTools.FieldRef<MineRock, Collider[]> Areas = AccessTools.FieldRefAccess<MineRock, Collider[]>("m_hitAreas");
        // A deposit's ore: which, about how much is still in it (by its drop table and the parts still standing), and the pickaxe tier.
        public static (string ore, int n, int tier)? Deposit(GameObject go)
        {
            if (go == null) return null;
            MineRock5 m5 = go.GetComponent<MineRock5>(); MineRock m = go.GetComponent<MineRock>(); Destructible d = go.GetComponent<Destructible>();
            DropTable table; int tier, areas = 1;
            if (m5 != null)
            {
                table = m5.m_dropItems; tier = m5.m_minToolTier;
                try { var list = Areas5(m5); areas = 0; if (list != null) foreach (object a in list) if (Traverse.Create(a).Field("m_health").GetValue<float>() > 0f) areas++; } catch { areas = 1; }
            }
            else if (m != null)
            {
                table = m.m_dropItems; tier = m.m_minToolTier;
                ZDO z = go.GetComponent<ZNetView>()?.GetZDO(); Collider[] cs = null; try { cs = Areas(m); } catch { }
                areas = cs == null || z == null ? 1 : Enumerable.Range(0, cs.Length).Count(i => z.GetFloat("Health" + i, m.m_health) > 0f);
            }
            else if (d != null && go.GetComponent<DropOnDestroyed>() is DropOnDestroyed dod) { table = dod.m_dropWhenDestroyed; tier = d.m_minToolTier; }
            else if (d != null && d.m_spawnWhenDestroyed != null && Whole(d.m_spawnWhenDestroyed) is var whole && whole.table != null)
            {
                // (an untouched deposit is one stone that breaks into its parts at the first blow: copper, silver, the muddy scrap piles)
                table = whole.table; tier = Math.Max(d.m_minToolTier, whole.tier); areas = whole.areas;
            }
            else return null;
            if (table == null || areas <= 0) return null;
            string key = Utils.GetPrefabName(go);
            if (!PerArea.TryGetValue(key, out var per)) PerArea[key] = per = Expect(table);
            if (per.ore == null) return null;
            int n = Mathf.RoundToInt(per.each * areas * Mathf.Max(0.1f, Game.m_resourceRate));
            return n > 0 ? (per.ore, n, tier) : ((string, int, int)?)null;
        }
        // The broken deposit an untouched one becomes: its drop table, tier and number of parts.
        private static (DropTable table, int tier, int areas) Whole(GameObject frac)
        {
            MineRock5 m5 = frac.GetComponent<MineRock5>(); MineRock m = frac.GetComponent<MineRock>();
            if (m5 != null) return (m5.m_dropItems, m5.m_minToolTier, Math.Max(1, frac.GetComponentsInChildren<Collider>(true).Length));
            if (m != null) return (m.m_dropItems, m.m_minToolTier, Math.Max(1, (m.m_areaRoot != null ? m.m_areaRoot : frac).GetComponentsInChildren<Collider>(true).Length));
            return (null, 0, 0);
        }
        // The expected ore of one roll of a drop table (its most plentiful ore, if it has any).
        private static (string, float) Expect(DropTable t)
        {
            float rolls = (t.m_dropMin + t.m_dropMax) / 2f * t.m_dropChance, weights = t.m_drops.Sum(x => x.m_weight);
            var best = t.m_drops.Where(x => x.m_item != null && x.m_item.GetComponent<ItemDrop>() != null && Stock.Category(Stock.Of(x.m_item)) == "ore") // (a prefab's own item data has no drop prefab: Of gives it one)
                .Select(x => (ore: x.m_item.name.ToLowerInvariant(), each: rolls * (x.m_stackMin + x.m_stackMax) / 2f * (t.m_oneOfEach || weights <= 0 ? 1f : x.m_weight / weights)))
                .OrderByDescending(x => x.each).FirstOrDefault();
            return best.ore == null ? ((string)null, 0f) : best;
        }
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        // Every loaded deposit within reach on the map (a crypt's inside too: it lies high above its door).
        public static List<(GameObject go, string ore, int n, int tier)> Near(Vector3 at, float reach, bool inside)
        {
            var list = new List<(GameObject, string, int, int)>();
            if (ZNetScene.instance == null) return list;
            foreach (ZNetView v in Instances(ZNetScene.instance).Values)
            {
                if (v == null || !v.IsValid()) continue;
                Vector3 p = v.transform.position;
                if (Utils.DistanceXZ(p, at) > reach || (p.y > 3000f) != inside) continue;
                if (inside == false && Mathf.Abs(p.y - at.y) > 40f) continue;
                var dep = Deposit(v.gameObject);
                if (dep != null) list.Add((v.gameObject, dep.Value.ore, dep.Value.n, dep.Value.tier));
            }
            return list;
        }

        // ---- the colony's side ----
        // A miner with nothing nearer to dig: a claim with ore their pickaxe can take and room for another miner.
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (!c.IsOwner || Time.time < b.NoClaimUntil) return null;
            b.NoClaimUntil = Time.time + 20;
            int pick = Gear.Tool(b, "pickaxe")?.m_shared.m_toolTier ?? -1;
            var mine = Of(c).Where(x => !x.Hunt && x.Ore > 0).ToList();
            if (mine.Count == 0) { if (Of(c).Any(x => !x.Hunt)) Gear.Problem = "every ore claim is worked out: drive a claim stake by a new deposit"; return null; }
            // (not off again with last trip's load still on their back, nor for ore the stores have no room for)
            if (b.Body.GetInventory().GetAllItems().Any(Carts.IsCargo)) return null;
            mine = mine.Where(x => x.Left.Keys.Any(ore => Carts_Item(ore) is GameObject p && Stock.Destination(c, Stock.Of(p), c.Position) != null)).ToList();
            if (mine.Count == 0) { Gear.Problem = "no room in the stores for more ore: add a chest for ore"; return null; }
            // (by what the stores lack most, the walk counting against it)
            var open = mine.Where(x => x.Left.Any(kv => kv.Value.n > 0 && kv.Value.tier <= pick) && Out(c, x.Id) < Mining.Room(x) && (!x.Crypt || ElderBeaten)).ToList();
            Claim best = open.Select(x => (x, score: Mining.Score(x.Left.Where(kv => kv.Value.n > 0 && kv.Value.tier <= pick).Select(kv => Need(c, kv.Key)).DefaultIfEmpty(0).Max(), Utils.DistanceXZ(At(x), c.Position))))
                .Where(t => t.score > 0).OrderByDescending(t => t.score).Select(t => t.x).FirstOrDefault();
            if (best == null)
            {
                Claim want = mine.OrderBy(x => x.Tier).First();
                if (want.Tier > pick) Gear.Problem = $"needs gear: a pickaxe of tier {want.Tier} for the {Title(want).ToLower()}";
                else if (mine.All(x => x.Crypt) && !ElderBeaten) Gear.Problem = "the only claim with ore left is a crypt, and miners won't go down into one till the Elder is beaten; or drive a claim stake by a new deposit";
                else if (open.Count > 0) Gear.Problem = "nothing to dig: the stores hold enough of every ore (Work tab: stores to keep)";
                return null;
            }
            return new ClaimTrip(best);
        }
        // A hunter with nothing to hunt nearer and the stores short of meat: a hunting camp with room for another (the nearest).
        public static Task HuntNext(Brain b)
        {
            Colony c = b.Colony;
            if (!c.IsOwner || Time.time < b.NoClaimUntil) return null;
            b.NoClaimUntil = Time.time + 20;
            var camps = Of(c).Where(x => x.Hunt).ToList();
            if (camps.Count == 0) return null;
            if (RangeTask.Meat(c) >= Hunting.Keep(c.Population)) return null;
            if (Gear.Tool(b, "bow") == null && !Gear.CanFight(b)) { Gear.Problem = "needs gear: a bow (or a weapon) to hunt with"; return null; }
            if (Stock.Destination(c, Stock.Of(Assets.Find("DeerMeat")), c.Position) == null) { Gear.Problem = "no room in the stores for meat: add a chest (raw food)"; return null; }
            float near = c.Radius + Hunting.NearHome;
            var far = camps.Where(x => Utils.DistanceXZ(At(x), c.Position) >= near).ToList();
            if (far.Count == 0) { Gear.Problem = $"the hunting camp is too near home: game keeps away from homes (put it {Mathf.RoundToInt(Hunting.NearHome)} m or more past the colony's edge)"; return null; }
            var known = far.Where(x => Hunting.Game.ContainsKey(x.Biome.ToLowerInvariant())).ToList();
            if (known.Count == 0) { Gear.Problem = "no game the hunters know at the hunting camp's ground"; return null; }
            Claim best = known.Where(x => Out(c, x.Id) < Mining.Room(x)).OrderBy(x => Utils.DistanceXZ(At(x), c.Position)).FirstOrDefault();
            return best == null ? null : new ClaimTrip(best);
        }
        // How much the colony wants this ore now (Core's Mining.Need from what the stores hold).
        public static int Held(Colony c, string ore) => Carts_Item(ore) is GameObject p ? Crafts.InStock(c, p.name) : 0;
        public static double Need(Colony c, string ore) => Mining.Need(ore, Held(c, ore), Stock.Keep(c, "ore"), Held(c, "copperore"), Held(c, "tinore"));
        // Miners out at a claim now.
        public static int Out(Colony c, string claim) => c.R.Fallen.Count(f => Mining.Decode(f.Killer, out string id, out _, out _) && id == claim)
            + Villagers.All.Count(b => b.S.Colony == c.Id && b.Task is ClaimTrip t && !t.Done && t.Claim == claim); // (on the road out too)

        // Home again (from Colony.Rise): the load, or a wound, or worse.
        public static void Home(Colony c, Humanoid h, VillagerState s, Fallen f)
        {
            if (f.Killer.StartsWith(Mining.Escort)) { c.Log("text", s.Name, "", $"{s.Name} came back from guarding the miners."); return; }
            if (!Mining.Decode(f.Killer, out string id, out _, out bool guard)) return;
            Claim claim = Get(id);
            if (claim == null) { c.Log("text", s.Name, "", $"{s.Name} came back: the claim was gone."); return; }
            var (wound, death) = Mining.Risk(claim.Biome, claim.Crypt, claim.Camp, guard);
            float roll = UnityEngine.Random.value;
            Brain b = Villagers.BrainFor(h); // (made now, so a wound sticks)
            if (claim.Hunt) { HuntHome(c, h, s, b, claim, roll, wound, death); return; }
            string where = claim.Crypt ? "the crypt" : claim.Camp ? "the mining camp" : "the claim";
            if (roll < death)
            {
                // (fallen out there: home without the load, their pickaxe left behind)
                ItemDrop.ItemData lost = h.GetInventory().GetAllItems().Where(i => i.GetDamage().m_pickaxe > 0).OrderByDescending(i => i.m_shared.m_toolTier).FirstOrDefault();
                if (lost != null) h.GetInventory().RemoveItem(lost);
                if (b != null) { b.S.Deaths++; b.S.InjuredUntil = c.Now + Clock.Hour * 12; b.Dirty(); }
                c.Log("text", s.Name, "", $"{s.Name} fell at {where} {Where(claim, c.Position)} ({(claim.Crypt ? "draugr" : "the wilds")}) and was carried home without the ore{(lost != null ? " or their pickaxe" : "")}.");
                return;
            }
            int pick = h.GetInventory().GetAllItems().Where(i => i.GetDamage().m_pickaxe > 0).Select(i => i.m_shared.m_toolTier).DefaultIfEmpty(-1).Max();
            // (by weight: the heaviest ore there sets the load; a cart only where one can go)
            float weight = claim.Left.Keys.Select(k => Carts_Item(k)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_weight ?? 10f).DefaultIfEmpty(10f).Max();
            var wanted = claim.Left.Where(kv => kv.Value.n > 0 && Need(c, kv.Key) > 0).ToDictionary(kv => kv.Key, kv => kv.Value); // (not what the stores already hold enough of, nor what's run out)
            var got = Mining.Take(wanted.Count > 0 ? wanted : claim.Left, Mining.Carry(weight, Carts.CartNear(c) != null && Mining.CartGoes(claim.Biome, claim.Crypt), claim.Crypt), pick);
            Took(claim.Id, got);
            if (got.Count == 0 && claim.Ore > 0) { c.Log("text", s.Name, "", $"{s.Name} came back from {where} {Where(claim, c.Position)} with nothing: their pickaxe couldn't break what's left there."); return; }
            var told = new List<string>();
            foreach (var kv in got)
            {
                GameObject prefab = Carts_Item(kv.Key); if (prefab == null) continue;
                int left = kv.Value;
                while (left > 0) { int stack = Math.Min(left, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); if (!h.GetInventory().AddItem(prefab, stack)) break; left -= stack; }
                if (left > 0) left -= Carts.Put(c, prefab, left); // (more than a bag holds: straight to the stores, as from the cart)
                for (; left > 0;) { int stack = Math.Min(left, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); ItemDrop.DropItem(prefab.GetComponent<ItemDrop>().m_itemData.Clone(), stack, h.transform.position + Vector3.up, Quaternion.identity); left -= stack; } // (stores full too: set down beside them)
                told.Add($"{kv.Value} {Name(kv.Key)}");
            }
            // (and on the way home, what grows out there that the stores are short of)
            foreach (var (item, n) in Gathering.Along(claim.Biome, it => Crafts.InStock(c, it), () => UnityEngine.Random.value))
            {
                GameObject prefab = Assets.Find(item); if (prefab == null) continue;
                if (!h.GetInventory().AddItem(prefab, n) && Carts.Put(c, prefab, n) < n) continue;
                told.Add($"{n} {Localization.instance.Localize(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLower()} picked on the way");
            }
            if (b != null) { b.S.Bump("orehauls"); if (roll < death + wound) b.S.InjuredUntil = c.Now + Clock.Hour * 6; b.Dirty(); }
            c.Log("text", s.Name, "", told.Count == 0 ? $"{s.Name} came back from {where} {Where(claim, c.Position)} with nothing: it's worked out." : $"{s.Name} came back from {where} {Where(claim, c.Position)} with {string.Join(", ", told)}{(roll < death + wound ? ", wounded" : "")}.{(claim.Ore == 0 ? " It's worked out now." : "")}");
        }
        // Home from a hunting camp: the kill (by the ground and their aim), a few arrows spent; or hurt, or carried home without it.
        private static void HuntHome(Colony c, Humanoid h, VillagerState s, Brain b, Claim claim, float roll, float wound, float death)
        {
            string where = $"the {Title(claim).ToLower()} {Where(claim, c.Position)}";
            if (roll < death)
            {
                if (b != null) { b.S.Deaths++; b.S.InjuredUntil = c.Now + Clock.Hour * 12; b.Dirty(); }
                c.Log("text", s.Name, "", $"{s.Name} fell at {where} and was carried home without the kill.");
                return;
            }
            // (arrows: a few loosed and not found again)
            int spent = 0;
            foreach (ItemDrop.ItemData ammo in h.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo).ToList())
            {
                if (spent >= Hunting.Arrows) break;
                int n = Math.Min(ammo.m_stack, Hunting.Arrows - spent); h.GetInventory().RemoveItem(ammo, n); spent += n;
            }
            var got = Hunting.Yield(claim.Biome, claim.Camp, s.Skill("Bows"), () => UnityEngine.Random.value);
            var told = new List<string>();
            foreach (var kv in got)
            {
                GameObject prefab = Assets.Find(kv.Key); if (prefab == null) continue;
                int left = kv.Value;
                while (left > 0) { int stack = Math.Min(left, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); if (!h.GetInventory().AddItem(prefab, stack)) break; left -= stack; }
                if (left > 0) left -= Carts.Put(c, prefab, left);
                for (; left > 0;) { int stack = Math.Min(left, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); ItemDrop.DropItem(prefab.GetComponent<ItemDrop>().m_itemData.Clone(), stack, h.transform.position + Vector3.up, Quaternion.identity); left -= stack; }
                told.Add($"{kv.Value} {Localization.instance.Localize(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLower()}");
            }
            bool hurt = roll < death + wound;
            if (b != null) { b.S.Bump("hunts"); if (got.Count > 0) b.S.Bump("kills"); if (hurt) b.S.InjuredUntil = c.Now + Clock.Hour * 6; b.Body.RaiseSkill(Skills.SkillType.Bows, 2f); b.Dirty(); }
            c.Log("text", s.Name, "", told.Count == 0 ? $"{s.Name} came back from {where} empty-handed{(hurt ? ", and wounded" : "")}: the game kept away." : $"{s.Name} came back from {where} with {string.Join(", ", told)}{(hurt ? ", wounded" : "")}.");
        }
    }

    // Off to a claim: a pickaxe in hand, down the road toward it, and away for the trip (an idle guard goes along where it's dangerous).
    internal sealed class ClaimTrip : Task
    {
        private readonly Claim _claim; private Vector3 _edge;
        public string Claim => _claim.Id;
        public ClaimTrip(Claim claim) { _claim = claim; }
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            Vector3 dir = Claims.At(_claim) - c.Position; dir.y = 0; if (dir.sqrMagnitude < 1) dir = Vector3.forward;
            _edge = c.Position + dir.normalized * Mathf.Min(c.Radius + 10, 35f);
            _edge = Colony.Spot(_edge, 4f) ?? _edge;
            Doing = $"off to the {Claims.Title(_claim).ToLower()}";
            b.Move.To(_edge, 3f);
        }
        public override void Tick(Brain b)
        {
            if (!b.Move.Failed && !b.Move.Arrived && b.Move.Distance > 6f) { if (!b.Move.Moving) b.Move.To(b.Move.Goal, 3f); return; }
            Colony c = b.Colony;
            float far = Utils.DistanceXZ(Claims.At(_claim), c.Position);
            double secs = Mining.TripSeconds(far, _claim.Camp, Clock.DayLength, _claim.Biome);
            var (wound, death) = Mining.Risk(_claim.Biome, _claim.Crypt, _claim.Camp, false);
            // (a guard with nothing to do goes along where it's dangerous)
            Brain guard = wound + death > 0.045f ? Villagers.All.FirstOrDefault(x => x.S.Colony == c.Id && x.S.Prof == Prof.Guard && x.Alive && (x.Task == null || x.Task is LeisureTask || x.Task is WorkTask wt && wt.Kind == "guard") && x.Activity == Activity.Work && Gear.CanFight(x)) : null; // (idle, or on their rounds: called off them to go along)
            c.Log("text", b.S.Name, "", $"{b.S.Name} set out for the {Claims.Title(_claim).ToLower()} {Claims.Where(_claim, c.Position)}{(guard != null ? $", {guard.S.Name} guarding them" : "")}.");
            b.S.Bump("claimtrips"); b.Dirty();
            if (guard != null) guard.Away(Mining.Escort + Mathf.RoundToInt((float)secs));
            b.Away(Mining.Encode(_claim.Id, secs, guard != null));
            Done = true;
        }
    }
}

namespace Jarldom
{
    // The stake: whose claim it is, and while its ground is loaded (on the game that has it) a survey every little while: what ore is
    // there, whether there's a camp, and the deposits the miners worked out taken away.
    internal sealed class OreClaim : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _v; private float _next; private string _id = "";
        private ZDO Z => _v != null && _v.IsValid() ? _v.GetZDO() : null;
        public string Id => Z?.GetString(Claims.IdKey, "") ?? "";
        private void Start()
        {
            _v = GetComponent<ZNetView>();
            if (Z == null) return;
            if (_v.IsOwner() && Id == "") { Z.Set(Claims.IdKey, Colony.NewId()); Z.Set(Claims.ColonyKey, HomeOf(transform.position)); }
            _id = Id;
            _next = Time.time + 2f;
            WearNTear wear = GetComponent<WearNTear>();
            if (wear != null) wear.m_onDestroyed += () => { if (_id != "") Claims.Remove(_id); }; // (pulled down or broken, not merely unloaded: the claim goes)
        }
        // The colony a new claim is for: the one it stands in, else the placer's own nearest (a co-jarl's: the nearest of any).
        public static string HomeOf(Vector3 at)
        {
            Colony inside = Colony.At(at);
            if (inside != null) return inside.Id.ToLowerInvariant();
            string me = (Player.m_localPlayer?.GetPlayerName() ?? "").Replace(' ', '_').ToLowerInvariant();
            var all = new List<(string id, Vector3 at, bool mine)>();
            foreach (string line in ZoneSystem.instance.GetGlobalKeys().Where(l => l.StartsWith("jarldom_colony_")))
            {
                int space = line.IndexOf(' '); if (space < 0) continue;
                string id = line.Substring("jarldom_colony_".Length, space - "jarldom_colony_".Length);
                Vector3? where = Outposts.Where(id); if (where == null) continue;
                all.Add((id, where.Value, line.EndsWith("," + me)));
            }
            return all.OrderByDescending(x => x.mine).ThenBy(x => Utils.DistanceXZ(x.at, at)).Select(x => x.id).FirstOrDefault() ?? "";
        }
        private void Update()
        {
            if (_id == "" && Z != null) _id = Id;
            if (Time.time < _next || Z == null || !_v.IsOwner()) return;
            _next = Time.time + 15f;
            Plugin.Guard(() => Survey());
        }
        // The camp round the stake: its beds, if it has a chest and a fire too.
        public int CampBeds()
        {
            var near = new List<Piece>(); Piece.GetAllPiecesInRadius(transform.position, Mining.CampNear, near);
            bool chest = near.Any(p => p.GetComponent<Container>() != null && p.GetComponent<OreClaim>() == null), fire = near.Any(p => p.GetComponent<Fireplace>() is Fireplace f && Classes.Warms(f));
            return chest && fire ? near.Count(p => p.GetComponent<Bed>() != null) : 0;
        }
        public Claim Survey()
        {
            if (Id == "") return null;
            string colony = Z.GetString(Claims.ColonyKey, "");
            if (colony == "") { colony = HomeOf(transform.position); Z.Set(Claims.ColonyKey, colony); }
            if (colony == "") return null;
            Vector3 at = transform.position;
            int beds = CampBeds();
            if (Hunt)
            {
                // (a hunting camp: no deposits to survey, only its beds and its ground)
                Claim was = Claims.Stored(Id);
                var hunt = new Claim { Id = Id, Colony = colony, X = at.x, Z = at.z, Hunt = true, Beds = beds, Biome = (WorldGenerator.instance?.GetBiome(at) ?? Heightmap.Biome.Meadows).ToString() };
                if (was == null || was.Encode() != hunt.Encode()) Claims.Save(hunt);
                return hunt;
            }
            float reach = beds > 0 ? Mining.CampReach : Mining.CryptReach;
            // (not until the ground all round has loaded: half a survey would lose what was dug, and under-count what's left)
            float need = beds > 0 ? Mining.CampReach : Mining.BareReach + 2f; // (a crypt's inside lies over its door: found in what's loaded)
            if (ZNetScene.instance == null || !new[] { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.All(d => ZNetScene.instance.IsAreaReady(at + d * need))) return Claims.Get(Id);
            var inside = Claims.Near(at, reach, true);
            bool crypt = inside.Count > 0;
            var deposits = crypt ? inside : Claims.Near(at, beds > 0 ? Mining.CampReach : Mining.BareReach, false);
            if (!crypt && beds == 0) deposits = deposits.OrderBy(d => Utils.DistanceXZ(d.go.transform.position, at)).Take(1).ToList(); // (a bare stake: the deposit beside it)
            Claim had = Claims.Stored(Id);
            var loads = Claims.Takes().Where(t => t.claim == Id).ToList();
            var claim = new Claim { Id = Id, Colony = colony, X = at.x, Z = at.z, Crypt = crypt, Beds = beds, Biome = (WorldGenerator.instance?.GetBiome(at) ?? Heightmap.Biome.Meadows).ToString() };
            // What the miners took while it lay unloaded (the last survey's remainder, and the loads since): worked-out deposits go,
            // nearest first; what's left of a part-dug one comes off what's left.
            var taken = new Dictionary<string, int>(had?.Taken ?? new Dictionary<string, int>());
            foreach (var l in loads) foreach (var kv in l.got) taken[kv.Key] = (taken.TryGetValue(kv.Key, out int t) ? t : 0) + kv.Value;
            foreach (string ore in taken.Keys.ToList())
            {
                int left = taken[ore];
                foreach (var d in deposits.Where(d => d.ore == ore).OrderBy(d => Utils.DistanceXZ(d.go.transform.position, at)).ToList())
                {
                    if (d.n > left) break;
                    left -= d.n; deposits.Remove(d);
                    ZNetView dv = d.go.GetComponent<ZNetView>();
                    if (dv != null && dv.IsValid()) { if (!dv.IsOwner()) dv.ClaimOwnership(); ZNetScene.instance.Destroy(d.go); }
                }
                if (left > 0 && deposits.Any(d => d.ore == ore)) claim.Taken[ore] = left; // (a deposit part dug: the rest when it's worked out)
            }
            foreach (var g in deposits.GroupBy(d => d.ore))
                claim.Left[g.Key] = (Math.Max(0, g.Sum(d => d.n) - (claim.Taken.TryGetValue(g.Key, out int dug) ? dug : 0)), g.Min(d => d.tier));
            if (had == null || had.Encode() != claim.Encode() || loads.Count > 0) Claims.Save(claim);
            foreach (var l in loads) Claims.Unset(l.key); // (taken in)
            return claim;
        }
        public bool Hunt => Z != null && Z.GetBool(Claims.HuntKey);
        public string GetHoverName() => Hunt ? "Hunting camp" : "Ore claim";
        public string GetHoverText()
        {
            Claim c = Claims.Get(Id);
            string home = Z?.GetString(Claims.ColonyKey, "") ?? "";
            Vector3? colony = home != "" ? Outposts.Where(home) : null;
            var t = new System.Text.StringBuilder();
            t.Append(c != null ? Claims.Title(c) : Hunt ? "Hunting camp" : "Ore claim");
            if (home == "" ) t.Append("\n<size=80%><color=#ff8c59>No colony to claim it for: found one first.</color></size>");
            else
            {
                if (colony != null) t.Append($"\n<size=80%>For the colony {Mathf.RoundToInt(Utils.DistanceXZ(colony.Value, transform.position))} m to the {ScoutTask.Compass(colony.Value - transform.position)}</size>");
                if (c == null) t.Append("\n<size=80%>Surveying...</size>");
                else if (c.Hunt)
                {
                    Colony home_ = Colony.All.FirstOrDefault(x => x.Valid && x.Id.ToLowerInvariant() == home);
                    bool tooNear = home_ != null && Utils.DistanceXZ(home_.Position, transform.position) < home_.Radius + Hunting.NearHome;
                    bool known = Hunting.Game.ContainsKey(c.Biome.ToLowerInvariant());
                    t.Append(tooNear ? $"\n<size=80%><color=#ff8c59>Too near home: game keeps away from homes. Put it {Mathf.RoundToInt(Hunting.NearHome)} m or more past the colony's edge.</color></size>"
                        : !known ? "\n<size=80%><color=#ff8c59>No game the hunters know on this ground.</color></size>"
                        : $"\n<size=80%>Game here: {string.Join(", ", Hunting.Game[c.Biome.ToLowerInvariant()].Where(g => !g.item.StartsWith("Trophy")).Select(g => Claims.Name(g.item)))}. The hunters go when the stores are short of meat.</size>");
                    t.Append(c.Camp ? $"\n<size=80%>A camp: {c.Beds} bed{(c.Beds == 1 ? "" : "s")}, so {c.Beds} hunter{(c.Beds == 1 ? "" : "s")} at once, a day's hunting a trip.</size>"
                        : $"\n<size=80%>A bare stake: one hunter, half a day. A camp (a bed, a chest and a fire within {Mathf.RoundToInt(Mining.CampNear)} m) hunts a whole day, a hunter a bed.</size>");
                    var (hw, hd) = Mining.Risk(c.Biome, false, c.Camp, false);
                    if (hw + hd > 0) t.Append($"\n<size=80%>Danger: {Mathf.RoundToInt((hw + hd) * 100)}% a trip (a guard with nothing to do goes along).</size>");
                }
                else
                {
                    t.Append($"\n<size=80%>{(c.Ore > 0 ? Claims.Ores(c) + $" (a pickaxe of tier {c.Tier})" : c.Crypt ? "no iron left in the crypt" : "<color=#ff8c59>no ore within reach: drive it in right beside a deposit</color>")}</size>");
                    if (c.Crypt && !Claims.ElderBeaten) t.Append("\n<size=80%><color=#ff8c59>No one goes into a crypt before the Elder is beaten.</color></size>");
                    t.Append(c.Camp ? $"\n<size=80%>A camp: {c.Beds} bed{(c.Beds == 1 ? "" : "s")}, so {c.Beds} miner{(c.Beds == 1 ? "" : "s")} at once, everything within {Mathf.RoundToInt(Mining.CampReach)} m, a day's digging a trip.</size>"
                        : $"\n<size=80%>A bare claim: one miner, the deposit beside it. A camp (a bed, a chest and a fire within {Mathf.RoundToInt(Mining.CampNear)} m) works everything within {Mathf.RoundToInt(Mining.CampReach)} m.</size>");
                    var (wound, death) = Mining.Risk(c.Biome, c.Crypt, c.Camp, false);
                    if (wound + death > 0) t.Append($"\n<size=80%>Danger: {Mathf.RoundToInt((wound + death) * 100)}% a trip (a guard with nothing to do goes along).</size>");
                }
            }
            t.Append("\n[<color=yellow><b>$KEY_Use</b></color>] Survey now");
            t.Append($"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Make it {(Hunt ? "an ore claim" : "a hunting camp")}");
            return Localization.instance.Localize(t.ToString());
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Z == null) return false;
            if (!_v.IsOwner()) _v.ClaimOwnership();
            if (alt)
            {
                Z.Set(Claims.HuntKey, !Hunt);
                Claim h = Survey();
                // (back to an ore claim: its record says so now, before a survey of ground not yet loaded writes it again)
                if (!Hunt && Claims.Stored(Id) is Claim was && was.Hunt) { was.Hunt = false; Claims.Save(was); }
                user.Message(MessageHud.MessageType.Center, Hunt ? "A hunting camp: the hunters come out here when the stores are short of meat." : "An ore claim again.");
                return true;
            }
            Claim c = Survey();
            user.Message(MessageHud.MessageType.Center, c == null ? "No colony to claim it for." : c.Hunt ? $"{Claims.Title(c)} on {c.Biome.ToLowerInvariant()} ground." : c.Ore > 0 ? $"{Claims.Title(c)}: {Claims.Ores(c)}." : "No ore within reach of the stake.");
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.2f;
    }
}
