using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The scripts on the mod's pieces. A reload swaps the old copy's scripts on standing pieces for the new copy's (Reattach), so E on them
    // runs the new code at once; they keep nothing of their own that isn't in the ZDO.
    internal static class PieceScripts
    {
        private static readonly (string prefix, Type type)[] Kinds =
        {
            (Pieces.PillarsName, typeof(HighSeat)), (Pieces.AltarPrefix, typeof(Altar)), (Pieces.HornName, typeof(WarHorn)), (Pieces.PyreName, typeof(Pyre)), (Pieces.WatchPostName, typeof(WatchPost)), (Pieces.OreClaimName, typeof(OreClaim)), (Pieces.MemorialName, typeof(MemorialStone)),
        };
        public static void AttachAll()
        {
            if (ZNetScene.instance == null) return;
            foreach (ZNetView v in AllViews())
            {
                string name = Utils.GetPrefabName(v.gameObject);
                foreach (var (prefix, type) in Kinds)
                {
                    if (!name.StartsWith(prefix)) continue;
                    foreach (MonoBehaviour old in v.GetComponents<MonoBehaviour>().Where(m => m != null && m.GetType().Name == type.Name && m.GetType() != type).ToList()) UnityEngine.Object.DestroyImmediate(old);
                    if (v.GetComponent(type) == null)
                    {
                        Component c = v.gameObject.AddComponent(type);
                        if (c is Altar a) a.God = name.Substring(Pieces.AltarPrefix.Length);
                    }
                }
            }
        }
        public static void DetachAll()
        {
            foreach (ZNetView v in AllViews())
                foreach (var (_, type) in Kinds)
                {
                    Component c = v.GetComponent(type);
                    if (c != null) UnityEngine.Object.DestroyImmediate(c);
                }
        }
        private static IEnumerable<ZNetView> AllViews()
        {
            foreach (int hash in Pieces.All.Select(p => p.name.GetStableHashCode()).ToList())
                foreach (ZNetView v in Assets.Instances(hash)) yield return v;
        }
    }

    // The High-Seat Pillars: the colony lives on their ZDO. E opens the colony window.
    internal sealed class HighSeat : MonoBehaviour, Hoverable, Interactable
    {
        public Colony Colony;
        private float _retry;
        private void Awake() { if (ZNetScene.instance != null) TryAttach(); }
        private void TryAttach()
        {
            ZNetView v = GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            Colony = Colony.Attach(this);
        }
        private void Update()
        {
            if (Time.time < _retry) return;
            _retry = Time.time + (Colony != null && Colony.Valid ? 1 : 2);
            if (Colony == null || !Colony.Valid) TryAttach();
            if (GetComponent<ZNetView>()?.IsValid() == true) Show(Colony != null && Colony.Valid && Colony.R != null ? Mathf.Clamp((int)Colony.R.Rank, 0, Pieces.Tiers - 1) : 0);
        }
        // The look of the colony's rank (one of the prefab's tiers); raised a rank, the change is seen.
        private int _tier = -1; public static int TestTier = -1; // (test: jarldom tier <n>; -1 the colony's own)
        private void Show(int tier)
        {
            if (TestTier >= 0) tier = Mathf.Clamp(TestTier, 0, Pieces.Tiers - 1);
            if (tier == _tier) return;
            Transform look = Refit();
            if (look == null) return;
            for (int i = 0; i < Pieces.Tiers; i++) look.Find("tier" + i)?.gameObject.SetActive(i == tier);
            if (_tier >= 0 && tier > _tier && Vector3.Distance(GameCamera.instance?.transform.position ?? Vector3.zero, transform.position) < 80)
            {
                // (as if built again where they stand: dust up the shafts, a stone's thud at the feet, the hammer)
                foreach (float x in new[] { -1.3f, 1.3f })
                {
                    foreach (float y in new[] { 0.3f, 1.6f, 2.9f }) Assets.Effect("vfx_Place_wood_pole", transform.TransformPoint(new Vector3(x, y, 0)));
                    Assets.Effect("vfx_Place_stone_wall_2x1", transform.TransformPoint(new Vector3(x, 0, 0)));
                }
                Assets.Effect("sfx_build_hammer_wood", transform.position + Vector3.up);
            }
            _tier = tier;
        }
        // Pillars raised before the looks by rank: their old look swapped for the prefab's (the colliders stay; the way between is added).
        private Transform Refit()
        {
            Transform look = transform.Find("new");
            if (look != null && look.Find(Pieces.LookStamp) != null) return look;
            GameObject prefab = Pieces.Get(Pieces.PillarsName);
            Transform fresh = prefab != null ? prefab.transform.Find("new") : null;
            if (fresh == null) return null;
            if (look != null) Destroy(look.gameObject);
            GameObject made = Instantiate(fresh.gameObject, transform, false); made.name = "new";
            if (transform.Find("use_between") == null && prefab.transform.Find("use_between") is Transform gap) Instantiate(gap.gameObject, transform, false).name = "use_between";
            WearNTear w = GetComponent<WearNTear>();
            if (w != null) { w.m_new = made; w.m_worn = made; w.m_broken = made; }
            return made.transform;
        }
        private void OnDestroy() { Colony?.Detach(); Colony = null; }
        public static void AttachAll() => PieceScripts.AttachAll();

        public string GetHoverName() => Colony?.R?.Name ?? "High-seat pillars";
        public string GetHoverText()
        {
            if (Colony == null || !Colony.Valid) return Localization.instance.Localize("High-seat pillars\n<size=80%>(founding...)</size>");
            ColonyRecord r = Colony.R;
            string coming = r.ComingLanded && r.Coming.Count > 0 ? $"\n<color=#ffd27a>{r.Coming.Count} newcomer{(r.Coming.Count == 1 ? "" : "s")} wait{(r.Coming.Count == 1 ? "s" : "")} for your word</color>" : "";
            string next = Ui.ColonyWindow.NextStep(Colony);
            string hint = next != "" ? $"\n<size=75%><color=#ffd27a>Next: {next}</color> (the hall tab says how)</size>" : "";
            return Localization.instance.Localize($"{r.Name}\n<size=80%>{(Tables.Of(r.Rank).Name.ToLower() is string rk && "aeiou".IndexOf(rk[0]) >= 0 ? "An " : "A ")}{Tables.Of(r.Rank).Name.ToLower()}: {Colony.Population} {(Colony.Population == 1 ? "soul" : "souls")}, renown {Colony.Renown}</size>{coming}{hint}\n[<color=yellow><b>$KEY_Use</b></color>] Hold council");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            if (Colony == null || !Colony.Valid) return false;
            Ui.ColonyWindow.Open(Colony);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.5f;
    }

    // A god's altar: offerings from the player's bag, and the god a temple is for.
    internal sealed class Altar : MonoBehaviour, Hoverable, Interactable
    {
        public string God = "odin";
        public const string RelicKey = "jarldom_relic"; // (on the altar: the prefab of the relic laid on it)
        private ZNetView _v; private GameObject _shown; private string _showing = "";
        private ZDO Z => (_v != null ? _v : _v = GetComponent<ZNetView>()) != null && _v.IsValid() ? _v.GetZDO() : null;
        public string Held => Z?.GetString(RelicKey, "") ?? "";
        public bool HoldsRelic => Relics.Of.TryGetValue(God, out var r) && Held == r.prefab;
        private void Update()
        {
            // (the relic shown lying on top, as it's laid on or taken off on any game)
            string held = Held;
            if (held == _showing) return;
            _showing = held;
            if (_shown != null) Destroy(_shown);
            if (held != "") _shown = Show(held);
        }
        private GameObject Show(string prefab)
        {
            GameObject item = ObjectDB.instance?.GetItemPrefab(prefab);
            if (item == null) return null;
            Transform look = item.transform.Find("attach") ?? item.transform.Cast<Transform>().FirstOrDefault(t => t.GetComponentInChildren<MeshRenderer>(true) != null);
            if (look == null) return null;
            GameObject copy = Instantiate(look.gameObject, transform);
            foreach (Collider col in copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
            foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            // (laid on the altar's own table: its broadest flat top above the ground, not on a post's head)
            copy.transform.position = transform.TransformPoint(Shelf(copy.transform)) + Vector3.up * 0.03f;
            copy.transform.rotation = transform.rotation * Quaternion.Euler(0, 30, 0);
            return copy;
        }
        // Where on the altar a thing is laid (the altar's own space; kept for each god): of its model's upward faces, the height with
        // the most area that isn't the ground at its foot, and the middle of that.
        private static readonly Dictionary<string, Vector3> Shelves = new Dictionary<string, Vector3>();
        private Vector3 Shelf(Transform skip)
        {
            if (Shelves.TryGetValue(God, out Vector3 kept)) return kept;
            var area = new Dictionary<int, (float a, Vector3 sum)>();
            float low = float.MaxValue, high = float.MinValue;
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.transform.IsChildOf(skip) || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Vector3[] v = mf.sharedMesh.vertices; int[] t = mf.sharedMesh.triangles;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 p0 = transform.InverseTransformPoint(mf.transform.TransformPoint(v[t[i]])), p1 = transform.InverseTransformPoint(mf.transform.TransformPoint(v[t[i + 1]])), p2 = transform.InverseTransformPoint(mf.transform.TransformPoint(v[t[i + 2]]));
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0); float twice = n.magnitude;
                    low = Mathf.Min(low, p0.y, Mathf.Min(p1.y, p2.y)); high = Mathf.Max(high, p0.y, Mathf.Max(p1.y, p2.y));
                    if (twice < 1e-6f || n.y / twice < 0.92f) continue; // (facing up)
                    Vector3 mid = (p0 + p1 + p2) / 3f; int key = Mathf.RoundToInt(mid.y / 0.05f);
                    area.TryGetValue(key, out var had); area[key] = (had.a + twice / 2, had.sum + mid * (twice / 2));
                }
            }
            Vector3 at = new Vector3(0, 1f, 0);
            var best = area.Where(kv => kv.Key * 0.05f > low + 0.2f && kv.Key * 0.05f < high - 0.05f).OrderByDescending(kv => kv.Value.a).FirstOrDefault();
            if (best.Value.a > 0) at = best.Value.sum / best.Value.a;
            return Shelves[God] = at;
        }
        public string GetHoverName() => "Altar of " + (Tables.God(God)?.Name ?? God);
        public string GetHoverText()
        {
            GodInfo g = Tables.God(God);
            Colony c = Colony.At(transform.position);
            int f = c == null ? 0 : God == "odin" ? Faith.Odin(c) : c.FavourOf(God);
            string favour = c != null ? $"\n<size=80%>{g?.Name}'s favour: {Faith.FavourWord(f)} ({f})</size>" : "";
            string relic = "";
            if (Relics.Of.TryGetValue(God, out var r))
                relic = HoldsRelic ? $"\n<size=80%>{char.ToUpper(r.name[0]) + r.name.Substring(1)} lies on it.</size>\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Take the relic back"
                    : God == "odin" && c?.RoomAt(transform.position)?.Type == RoomType.OdinShrine ? "" : $"\n<size=80%>A temple to {g?.Name} grades no higher than B without {r.name} on the altar ({r.where}): use it on the altar.</size>";
            return Localization.instance.Localize($"Altar of {g?.Name}{favour}{relic}\n<size=80%>wants {g?.Offerings}</size>\n[<color=yellow><b>$KEY_Use</b></color>] Make an offering   <size=80%>(or use an item on it)</size>");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p)) return false;
            if (alt && Held != "")
            {
                // (the relic back into the bag)
                GameObject prefab = ObjectDB.instance?.GetItemPrefab(Held);
                if (prefab == null || !p.GetInventory().CanAddItem(prefab, 1)) { p.Message(MessageHud.MessageType.Center, "No room in your bag for it."); return true; }
                p.GetInventory().AddItem(prefab, 1);
                if (!_v.IsOwner()) _v.ClaimOwnership();
                Z.Set(RelicKey, "");
                Changed();
                p.Message(MessageHud.MessageType.Center, $"You take the relic from {Tables.God(God)?.Name}'s altar.");
                return true;
            }
            Faith.OfferFromBag(this, p);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!(user is Player p)) return true;
            string prefab = item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : ""; // (a Unity null: ?. passes a destroyed prefab)
            string whose = Relics.GodOf(prefab);
            if (whose != null)
            {
                // A relic: laid on its own god's altar, never offered away.
                if (whose != God) { p.Message(MessageHud.MessageType.Center, $"That's {Tables.God(whose)?.Name}'s, not {Tables.God(God)?.Name}'s."); return true; }
                if (HoldsRelic) { p.Message(MessageHud.MessageType.Center, "One lies on the altar already."); return true; }
                if (Z == null) return true;
                if (!_v.IsOwner()) _v.ClaimOwnership();
                p.GetInventory().RemoveItem(item, 1);
                Z.Set(RelicKey, prefab);
                Assets.Effect("vfx_offering", transform.position + Vector3.up);
                Assets.Effect("sfx_offering", transform.position);
                Changed();
                p.Message(MessageHud.MessageType.Center, $"You lay {Relics.Of[God].name} on {Tables.God(God)?.Name}'s altar.");
                return true;
            }
            Faith.Offer(this, p, item, item.m_stack);
            return true;
        }
        // (the room it stands in judged again now: its grade may change)
        private void Changed() => Colony.At(transform.position)?.SurveySoon();
        public float GetHoverOffset() => 1.2f;
    }

    // The war horn: blow it to call everyone in, again for the all-clear.
    internal sealed class WarHorn : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverName() => "War horn";
        public string GetHoverText()
        {
            Colony c = Colony.At(transform.position);
            bool on = c != null && c.HornUntil > Time.time;
            return Localization.instance.Localize($"War horn{(on ? " <color=#ff8c59>(sounding)</color>" : "")}\n[<color=yellow><b>$KEY_Use</b></color>] {(on ? "Sound the all-clear" : "Blow the horn")}");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player)) return false;
            Colony c = Colony.At(transform.position);
            if (c == null) { user.Message(MessageHud.MessageType.Center, "The horn must stand in a colony."); return true; }
            Net.Act(c, "horn", c.HornUntil > Time.time ? "off" : "on");
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.5f;
    }

    // A watch post: its place in the guards' round (numbered as raised; E sends it to the end of the round).
    internal sealed class WatchPost : MonoBehaviour, Hoverable, Interactable
    {
        public const string OrderKey = "jarldom_round";
        private ZNetView _v;
        private void Start()
        {
            _v = GetComponent<ZNetView>();
            if (_v != null && _v.IsValid() && _v.IsOwner() && _v.GetZDO().GetInt(OrderKey, 0) == 0) _v.GetZDO().Set(OrderKey, Next(transform.position));
        }
        public int Order => _v != null && _v.IsValid() ? _v.GetZDO().GetInt(OrderKey, 0) : 0;
        public static int Next(Vector3 at) => All(at).Select(w => w.Order).DefaultIfEmpty(0).Max() + 1;
        // The colony's watch posts in the order of the round.
        public static List<WatchPost> All(Vector3 at)
        {
            Colony c = Colony.At(at);
            if (c == null) return new List<WatchPost>();
            return c.PiecesIn<WatchPost>(c.Radius + 10).OrderBy(w => w.Order).ToList();
        }
        public string GetHoverName() => "Watch post";
        public string GetHoverText()
        {
            var round = All(transform.position);
            int i = round.IndexOf(this) + 1;
            string where = Colony.At(transform.position) == null ? "\n<size=80%><color=#ff8c59>Outside any colony: no guard will walk to it.</color></size>" : round.Count < 2 ? "\n<size=80%>Raise another: guards walk between them.</size>" : "";
            return Localization.instance.Localize($"Watch post {i} of {round.Count}\n<size=80%>Guards walk the posts in turn.</size>{where}\n[<color=yellow><b>$KEY_Use</b></color>] Make it the last of the round");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _v == null || !_v.IsValid()) return false;
            if (!_v.IsOwner()) _v.ClaimOwnership();
            _v.GetZDO().Set(OrderKey, Next(transform.position));
            user.Message(MessageHud.MessageType.Center, "Now the last post of the round.");
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.2f;
    }

    // The funeral pyre: grave goods in its hold; lit from the colony window for a worthy soul.
    internal sealed class Pyre : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverName() => "Funeral pyre";
        public string GetHoverText()
        {
            Container goods = GetComponent<Container>();
            int n = goods != null ? goods.GetInventory().NrOfItems() : 0;
            bool burning = GetComponent<ZNetView>()?.GetZDO()?.GetLong(Ascension.BurnKey, 0L) > 0;
            return Localization.instance.Localize($"Funeral pyre{(burning ? " <color=#ff8c59>(burning)</color>" : "")}\n<size=80%>{n} grave goods</size>\n[<color=yellow><b>$KEY_Use</b></color>] Grave goods");
        }
        public bool Interact(Humanoid user, bool hold, bool alt) => GetComponent<Container>()?.Interact(user, hold, alt) ?? false;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.5f;
        private void Update() => Ascension.PyreTick(this);
        // Grave goods aren't storage: chest mods (and the colony) leave them be.
        private void Start() { ZNetView v = GetComponent<ZNetView>(); if (v != null && v.IsValid() && v.IsOwner() && !v.GetZDO().GetBool("dh_notstorage")) v.GetZDO().Set("dh_notstorage", true); }
    }
}

namespace Jarldom
{
    // The memorial stone: hover tells how many it remembers; E reads the names.
    internal sealed class MemorialStone : MonoBehaviour, Hoverable, Interactable
    {
        private Colony Home => Colony.Nearest(transform.position, Renown.ZoneReach + 20);
        public string GetHoverName() => "Memorial stone";
        public float GetHoverOffset() => 2.4f;
        public string GetHoverText()
        {
            Colony c = Home;
            if (c == null) return Localization.instance.Localize("Memorial stone\n<size=80%>Raise it in a colony: it remembers that colony's dead.</size>");
            int n = c.R.Roster.Count(r => r.State == SoulState.Ascended || r.State == SoulState.Lost);
            return Localization.instance.Localize($"Memorial stone of {c.R.Name}\n<size=80%>{(n == 0 ? "No names on it yet" : n + (n == 1 ? " name" : " names"))}</size>\n[<color=yellow><b>$KEY_Use</b></color>] Read the names");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player) || Ui.MemorialPanel.JustClosed) return false;
            Colony c = Home; if (c == null) return false;
            Ui.MemorialPanel.Open(c, transform.position);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}

namespace Jarldom
{
    // The funeral ship on the beach: grave goods in its hold.
    internal sealed class FuneralShip : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverName() => "Funeral ship";
        public string GetHoverText()
        {
            Container goods = GetComponent<Container>();
            int n = goods != null ? goods.GetInventory().NrOfItems() : 0;
            return Localization.instance.Localize($"Funeral ship\n<size=80%>{n} grave goods. A soul sent to Valhalla goes by ship while it stands.</size>\n[<color=yellow><b>$KEY_Use</b></color>] Grave goods");
        }
        public bool Interact(Humanoid user, bool hold, bool alt) => GetComponent<Container>()?.Interact(user, hold, alt) ?? false;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 2f;
        private void Start() { ZNetView v = GetComponent<ZNetView>(); if (v != null && v.IsValid() && v.IsOwner() && !v.GetZDO().GetBool("dh_notstorage")) v.GetZDO().Set("dh_notstorage", true); }
    }
}
