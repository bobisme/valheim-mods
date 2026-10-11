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
        public const string KeyHead = "jarldom_claim_", ColonyKey = "jarldom_claimcolony", IdKey = "jarldom_claimid";
        private static ZoneSystem Z => ZoneSystem.instance;

        public static IEnumerable<Claim> All()
        {
            if (Z == null) yield break;
            foreach (string line in Z.GetGlobalKeys().Where(l => l.StartsWith(KeyHead)).ToList())
            {
                int space = line.IndexOf(' '); if (space < 0) continue;
                Claim c = Claim.Decode(line.Substring(KeyHead.Length, space - KeyHead.Length), line.Substring(space + 1));
                if (c != null) yield return c;
            }
        }
        public static IEnumerable<Claim> Of(Colony c) => All().Where(x => x.Colony == c.Id.ToLowerInvariant());
        public static Claim Get(string id) => All().FirstOrDefault(x => x.Id == id);
        public static void Save(Claim c) { if (Z != null) Z.SetGlobalKey(KeyHead + c.Id + " " + c.Encode()); }
        public static void Remove(string id) { if (Z != null) Z.RemoveGlobalKey(KeyHead + id); }
        public static Vector3 At(Claim c) => new Vector3(c.X, 0, c.Z);
        public static string Ores(Claim c) => c.Left.Where(kv => kv.Value.n > 0).Any() ? string.Join(", ", c.Left.Where(kv => kv.Value.n > 0).OrderByDescending(kv => kv.Value.n).Select(kv => $"~{kv.Value.n} {Name(kv.Key)}")) : "worked out";
        public static string Name(string prefab) { GameObject p = ObjectDB.instance?.GetItemPrefab(prefab) ?? Carts_Item(prefab); return p != null ? Localization.instance.Localize(p.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLower() : prefab; }
        private static GameObject Carts_Item(string lower) => ObjectDB.instance?.m_items.FirstOrDefault(i => i != null && i.name.ToLowerInvariant() == lower);
        public static string Title(Claim c) => (c.Crypt ? "Crypt claim" : c.Camp ? "Mining camp" : "Ore claim") + (c.Left.Count > 0 ? " (" + string.Join(", ", c.Left.OrderByDescending(kv => kv.Value.n).Take(2).Select(kv => Name(kv.Key).Replace(" ore", "").Replace("scrap ", ""))) + ")" : "");
        public static string Where(Claim c, Vector3 from) => $"{Mathf.RoundToInt(Utils.DistanceXZ(At(c), from))} m to the {ScoutTask.Compass(At(c) - from)}";

        // ---- deposits ----
        private static readonly Dictionary<string, (string ore, float each)> PerArea = new Dictionary<string, (string, float)>();
        private static readonly AccessTools.FieldRef<MineRock5, System.Collections.IList> Areas5 = AccessTools.FieldRefAccess<MineRock5, System.Collections.IList>("m_hitAreas");
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
            else return null;
            if (table == null || areas <= 0) return null;
            string key = Utils.GetPrefabName(go);
            if (!PerArea.TryGetValue(key, out var per)) PerArea[key] = per = Expect(table);
            if (per.ore == null) return null;
            int n = Mathf.RoundToInt(per.each * areas * Mathf.Max(0.1f, Game.m_resourceRate));
            return n > 0 ? (per.ore, n, tier) : ((string, int, int)?)null;
        }
        // The expected ore of one roll of a drop table (its most plentiful ore, if it has any).
        private static (string, float) Expect(DropTable t)
        {
            float rolls = (t.m_dropMin + t.m_dropMax) / 2f * t.m_dropChance, weights = t.m_drops.Sum(x => x.m_weight);
            var best = t.m_drops.Where(x => x.m_item != null && x.m_item.GetComponent<ItemDrop>() is ItemDrop i && Stock.Category(i.m_itemData) == "ore")
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
            var mine = Of(c).Where(x => x.Ore > 0).ToList();
            if (mine.Count == 0) return null;
            Claim best = mine.Where(x => x.Left.Any(kv => kv.Value.n > 0 && kv.Value.tier <= pick) && Out(c, x.Id) < Mining.Room(x) && (!x.Crypt || Z.GetGlobalKey("defeated_gdking")))
                .OrderBy(x => Utils.DistanceXZ(At(x), c.Position)).FirstOrDefault();
            if (best == null)
            {
                Claim want = mine.OrderBy(x => x.Tier).First();
                if (want.Tier > pick) Gear.Problem = $"needs gear: a pickaxe of tier {want.Tier} for the {Title(want).ToLower()}";
                return null;
            }
            return new ClaimTrip(best);
        }
        // Miners out at a claim now.
        public static int Out(Colony c, string claim) => c.R.Fallen.Count(f => Mining.Decode(f.Killer, out string id, out _, out _) && id == claim);

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
            var got = Mining.Take(claim.Left, Mining.Carry(Carts.CartNear(c) != null, claim.Camp), pick);
            Mining.Apply(claim, got);
            Save(claim);
            var told = new List<string>();
            foreach (var kv in got)
            {
                GameObject prefab = Carts_Item(kv.Key); if (prefab == null) continue;
                int left = kv.Value;
                while (left > 0) { int stack = Math.Min(left, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); if (!h.GetInventory().AddItem(prefab, stack)) break; left -= stack; }
                if (left > 0) left -= Carts.Put(c, prefab, left); // (more than a bag holds: straight to the stores, as from the cart)
                told.Add($"{kv.Value} {Name(kv.Key)}");
            }
            if (b != null) { b.S.Bump("orehauls"); if (roll < death + wound) b.S.InjuredUntil = c.Now + Clock.Hour * 6; b.Dirty(); }
            c.Log("text", s.Name, "", told.Count == 0 ? $"{s.Name} came back from {where} {Where(claim, c.Position)} with nothing: it's worked out." : $"{s.Name} came back from {where} {Where(claim, c.Position)} with {string.Join(", ", told)}{(roll < death + wound ? ", wounded" : "")}.{(claim.Ore == 0 ? " It's worked out now." : "")}");
        }
    }

    // Off to a claim: a pickaxe in hand, down the road toward it, and away for the trip (an idle guard goes along where it's dangerous).
    internal sealed class ClaimTrip : Task
    {
        private readonly Claim _claim; private Vector3 _edge;
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
            double secs = Mining.TripSeconds(far, _claim.Camp, Clock.DayLength);
            var (wound, death) = Mining.Risk(_claim.Biome, _claim.Crypt, _claim.Camp, false);
            // (a guard with nothing to do goes along where it's dangerous)
            Brain guard = wound + death >= 0.05f ? Villagers.All.FirstOrDefault(x => x.S.Colony == c.Id && x.S.Prof == Prof.Guard && x.Alive && (x.Task == null || x.Task is LeisureTask) && x.Activity == Activity.Work && Gear.CanFight(x)) : null;
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
        private ZNetView _v; private ZDOID _zdo; private float _next; private string _id = "";
        private ZDO Z => _v != null && _v.IsValid() ? _v.GetZDO() : null;
        public string Id => Z?.GetString(Claims.IdKey, "") ?? "";
        private void Start()
        {
            _v = GetComponent<ZNetView>();
            if (Z == null) return;
            _zdo = Z.m_uid;
            if (_v.IsOwner() && Id == "") { Z.Set(Claims.IdKey, Colony.NewId()); Z.Set(Claims.ColonyKey, HomeOf(transform.position)); }
            _id = Id;
            _next = Time.time + 2f;
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
        private void OnDestroy()
        {
            // (taken down, not just unloaded: its claim goes with it)
            if (_id != "" && _zdo != ZDOID.None && ZDOMan.instance != null && ZDOMan.instance.GetZDO(_zdo) == null) Claims.Remove(_id);
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
            string colony = Z.GetString(Claims.ColonyKey, "");
            if (colony == "") { colony = HomeOf(transform.position); Z.Set(Claims.ColonyKey, colony); }
            if (colony == "") return null;
            Vector3 at = transform.position;
            int beds = CampBeds();
            var inside = Claims.Near(at, beds > 0 ? Mining.CampReach : Mining.CryptReach, true);
            bool crypt = inside.Count > 0;
            var deposits = crypt ? inside : Claims.Near(at, beds > 0 ? Mining.CampReach : Mining.BareReach, false);
            if (!crypt && beds == 0) deposits = deposits.OrderBy(d => Utils.DistanceXZ(d.go.transform.position, at)).Take(1).ToList(); // (a bare stake: the deposit beside it)
            Claim had = Claims.Get(Id);
            var claim = new Claim { Id = Id, Colony = colony, X = at.x, Z = at.z, Crypt = crypt, Beds = beds, Biome = (WorldGenerator.instance?.GetBiome(at) ?? Heightmap.Biome.Meadows).ToString() };
            // What the miners took while it lay unloaded: worked-out deposits go, nearest first.
            var taken = had?.Taken ?? new Dictionary<string, int>();
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
            foreach (var g in deposits.GroupBy(d => d.ore)) claim.Left[g.Key] = (g.Sum(d => d.n), g.Min(d => d.tier));
            string value = claim.Encode();
            if (had == null || had.Encode() != value) Claims.Save(claim);
            return claim;
        }
        public string GetHoverName() => "Ore claim";
        public string GetHoverText()
        {
            Claim c = Claims.Get(Id);
            string home = Z?.GetString(Claims.ColonyKey, "") ?? "";
            Vector3? colony = home != "" ? Outposts.Where(home) : null;
            var t = new System.Text.StringBuilder();
            t.Append(c != null ? Claims.Title(c) : "Ore claim");
            if (home == "" ) t.Append("\n<size=80%><color=#ff8c59>No colony to claim it for: found one first.</color></size>");
            else
            {
                if (colony != null) t.Append($"\n<size=80%>For the colony {Mathf.RoundToInt(Utils.DistanceXZ(colony.Value, transform.position))} m to the {ScoutTask.Compass(colony.Value - transform.position)}</size>");
                if (c == null) t.Append("\n<size=80%>Surveying...</size>");
                else
                {
                    t.Append($"\n<size=80%>{(c.Ore > 0 ? Claims.Ores(c) + $" (a pickaxe of tier {c.Tier})" : c.Crypt ? "no iron left in the crypt" : "<color=#ff8c59>no ore within reach: drive it in right beside a deposit</color>")}</size>");
                    if (c.Crypt && !ZoneSystem.instance.GetGlobalKey("defeated_gdking")) t.Append("\n<size=80%><color=#ff8c59>No one goes into a crypt before the Elder is beaten.</color></size>");
                    t.Append(c.Camp ? $"\n<size=80%>A camp: {c.Beds} bed{(c.Beds == 1 ? "" : "s")}, so {c.Beds} miner{(c.Beds == 1 ? "" : "s")} at once, everything within {Mathf.RoundToInt(Mining.CampReach)} m, a day's digging a trip.</size>"
                        : $"\n<size=80%>A bare claim: one miner, the deposit beside it. A camp (a bed, a chest and a fire within {Mathf.RoundToInt(Mining.CampNear)} m) works everything within {Mathf.RoundToInt(Mining.CampReach)} m.</size>");
                    var (wound, death) = Mining.Risk(c.Biome, c.Crypt, c.Camp, false);
                    if (wound + death > 0) t.Append($"\n<size=80%>Danger: {Mathf.RoundToInt((wound + death) * 100)}% a trip (a guard with nothing to do goes along).</size>");
                }
            }
            t.Append("\n[<color=yellow><b>$KEY_Use</b></color>] Survey now");
            return Localization.instance.Localize(t.ToString());
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Z == null) return false;
            if (!_v.IsOwner()) _v.ClaimOwnership();
            Claim c = Survey();
            user.Message(MessageHud.MessageType.Center, c == null ? "No colony to claim it for." : c.Ore > 0 ? $"{Claims.Title(c)}: {Claims.Ores(c)}." : "No ore within reach of the stake.");
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.2f;
    }
}
