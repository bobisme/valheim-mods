using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The year's festivals in the colony (rules in Core/Festivals.cs, dates in Core/Calendar.cs). Two dawns before, the colony is told
    // what to bring to the great hall's chests; at dusk on the day the festival is held (the goods used, a blessing as strong as the
    // readying, the colony glad of it) or let slip. Midsummer: a bonfire on open ground by the hall and the colony dancing round it
    // till late. The others, for now, in the hall.
    internal static class FestivalDays
    {
        public const string MidsummerMusic = "location_deepnorth_timberhallvillage";
        private const float BonfireSeconds = 540f; // (dusk to dawn)

        // What the great hall's chests hold of each kind.
        private static IEnumerable<(Container chest, ItemDrop.ItemData item)> HallGoods(Colony c)
        {
            Room hall = c.Best(RoomType.GreatHall);
            if (hall == null) yield break;
            foreach (var x in Stock.Chests(c, c.Centre(hall)).Where(x => Geometry.Inside(hall.Xz, x.chest.transform.position.x, x.chest.transform.position.z)))
                foreach (ItemDrop.ItemData i in x.chest.GetInventory().GetAllItems()) yield return (x.chest, i);
        }
        public static bool Is(string kind, ItemDrop.ItemData i)
        {
            string p = i.m_dropPrefab != null ? i.m_dropPrefab.name : "";
            switch (kind)
            {
                case "wood": return p == "Wood" || p == "FineWood" || p == "RoundLog" || p == "YggdrasilWood" || p == "ElderBark";
                case "dandelion": return p == "Dandelion";
                case "food": return Stock.Edible(i) && !Classes.IsMead(i);
                case "mead": return Classes.IsMead(i);
                case "sacrifice": return i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy || p.EndsWith("Meat") && !p.StartsWith("Cooked") || p == "NeckTail";
                default: return false;
            }
        }
        public static int Have(Colony c, string kind) => HallGoods(c).Where(x => Is(kind, x.item)).Sum(x => x.item.m_stack);
        public static float Readied(Colony c, Festival f) => c.Best(RoomType.GreatHall) == null ? 0 : Festivals.Readied(Festivals.Needs(f, c.Population), k => Have(c, k));
        public static string Wants(Colony c, Festival f) => string.Join(", ", Festivals.Needs(f, c.Population).Select(n => $"{n.amount} {n.label} ({Math.Min(n.amount, Have(c, n.kind))} there)"));

        // At dawn (the colony's owner): two days before and on the day, the word.
        public static void Dawn(Colony c, int day)
        {
            Date d = Year.Today();
            var (f, until) = Calendar.Next(d);
            if (until > Calendar.PrepDays) { Blessing(c); return; }
            if (c.Best(RoomType.GreatHall) == null) { c.Tell($"{Calendar.Name(f)} {(until == 0 ? "is tonight" : $"in {until} {(until == 1 ? "day" : "days")}")}, but there's no great hall to keep it in.", true); return; }
            c.Tell(until == 0
                ? $"{Calendar.Name(f)} is tonight, at dusk. Readied {Mathf.RoundToInt(Readied(c, f) * 100)}%: {Wants(c, f)}."
                : $"{Calendar.Name(f)} in {until} {(until == 1 ? "day" : "days")}. Bring to the great hall's chests: {Wants(c, f)}.", true);
            Blessing(c);
        }
        // While a festival's blessing lasts, a little cheer each day.
        private static void Blessing(Colony c)
        {
            var (f, power) = Festivals.Active(c.R.Flags);
            if (f == null) return;
            foreach (RosterRow r in c.R.Living) Villagers.Command(r.Vid, "mood", "festblessed");
        }

        // At dusk on the day (the colony's owner): held, or let slip.
        public static void Dusk(Colony c, int day)
        {
            Date d = Year.Today();
            Festival? today = Calendar.All.Cast<Festival?>().FirstOrDefault(x => Calendar.IsDay(d, x.Value));
            if (today == null || Festivals.Done(c.R.Flags, day)) return;
            Hold(c, today.Value, day);
        }
        public static void Hold(Colony c, Festival f, int day)
        {
            float readied = Readied(c, f);
            string god = Festivals.God(f);
            if (readied < Festivals.Held)
            {
                Festivals.Neglect(c.R.Flags, f, day);
                Faith.Add(c, god, -3, Calendar.Name(f) + " let slip");
                foreach (RosterRow r in c.R.Living) Villagers.Command(r.Vid, "mood", "festslip");
                c.Tell($"{Calendar.Name(f)} was let slip: the great hall wasn't readied for it. The colony is downcast.", true);
                c.Log("text", "", "", $"{Calendar.Name(f)} came, and the colony let it slip.");
                c.MarkDirty(); return;
            }
            // (the goods: what each want asked for, no more)
            var goods = HallGoods(c).ToList();
            foreach (var n in Festivals.Needs(f, c.Population))
            {
                int left = n.amount;
                foreach (var (chest, item) in goods.Where(x => Is(n.kind, x.item)).ToList())
                {
                    if (left <= 0) break;
                    int take = Math.Min(left, item.m_stack);
                    ContainerAccess.TakeOver(chest);
                    chest.GetInventory().RemoveItem(item, take); left -= take;
                }
            }
            Festivals.Hold(c.R.Flags, f, readied, day);
            Faith.Add(c, god, 3 + Mathf.RoundToInt(readied * 7), "kept " + Calendar.Name(f));
            c.R.Deeds += Renown.Deed("festival");
            string mood = f == Festival.Sigrblot ? "sigrblot" : f == Festival.Midsummer ? "midsummer" : f == Festival.WinterNights ? "winternights" : "yule";
            foreach (RosterRow r in c.R.Living) Villagers.Command(r.Vid, "mood", mood);
            int pct = Mathf.RoundToInt(readied * 100);
            c.Tell($"{Calendar.Name(f)} is kept tonight{(pct < 100 ? $" ({pct}% readied)" : "")}! Until the next festival, {Festivals.Blessing(f)}.", true);
            c.Log("text", "", "", $"The colony kept {Calendar.Name(f)}{(pct >= 100 ? " in full" : pct >= 60 ? "" : ", though thinly")}.");
            c.MarkDirty();
            Ceremony(c, f);
        }
        public static void Ceremony(Colony c, Festival f)
        {
            if (f == Festival.Midsummer) Plugin.Guard(() => Midsummer(c));
            else if (f == Festival.Sigrblot) Plugin.Guard(() => Sigrblot(c));
        }

        // ---- Sigrblot: the sacrifice for victory ----
        // At Odin's altar (or before the pillars, with none): the fighters in a line facing it, the rest behind; the gothi (or the
        // skald, or the worthiest) calls on the Allfather and burns the sacrifice, blesses each fighter in turn (each answers with a
        // war cry), and the colony shouts for victory; Odin answers with thunder. Timed from a start on the world's clock, so it plays
        // in step on every game that runs one of them.
        public const float SigrCall = 22f, SigrFire = 29f, SigrFirst = 36f, SigrEach = 5f;
        public static float SigrEnd(int fighters) => SigrFirst + fighters * SigrEach + 3f;
        private static void Sigrblot(Colony c)
        {
            Piece altar = Saboteur.OdinAltar(c);
            Vector3 at = altar != null ? altar.transform.position : c.Position;
            Vector3 fwd = altar != null ? altar.transform.forward : c.Seat.transform.forward; fwd.y = 0; fwd.Normalize();
            var living = c.R.Living.ToList();
            RosterRow priest = living.FirstOrDefault(r => r.Prof == Prof.Gothi) ?? living.FirstOrDefault(r => r.Prof == Prof.Skald) ?? living.OrderByDescending(r => r.Worth).FirstOrDefault();
            var fighters = living.Where(r => r != priest && (r.Prof == Prof.Guard || r.Prof == Prof.Hunter)).Take(8).ToList();
            var rest = living.Where(r => r != priest && !fighters.Contains(r)).ToList();
            long start = ZNet.instance.GetTime().Ticks;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string Head(string role, int i, int n) => string.Join("|", start.ToString(inv), at.x.ToString(inv), at.y.ToString(inv), at.z.ToString(inv), fwd.x.ToString(inv), fwd.z.ToString(inv), role, i.ToString(inv), n.ToString(inv), fighters.Count.ToString(inv));
            if (priest != null) Villagers.Command(priest.Vid, "sigr", Head("gothi", 0, 1) + "|" + string.Join(",", fighters.Select(r => r.Name)));
            for (int i = 0; i < fighters.Count; i++) Villagers.Command(fighters[i].Vid, "sigr", Head("fighter", i, fighters.Count) + "|" + fighters[i].Name);
            for (int i = 0; i < rest.Count; i++) Villagers.Command(rest[i].Vid, "sigr", Head("watch", i, rest.Count) + "|");
            c.Tell($"Sigrblot: the colony gathers at {(altar != null ? "Odin's altar" : "the pillars")} to sacrifice for victory.", true);
        }
        // Where each stands: fighters in a line 3 m out from the altar, facing it; the rest in a half-ring behind them.
        public static Vector3 SigrSpot(Vector3 altar, Vector3 fwd, string role, int i, int n)
        {
            Vector3 side = new Vector3(fwd.z, 0, -fwd.x);
            if (role == "gothi") return altar + fwd * 1.1f;
            if (role == "fighter") return altar + fwd * 3.2f + side * ((i - (n - 1) / 2f) * 1.1f);
            float a = Mathf.PI * (0.15f + 0.7f * (n <= 1 ? 0.5f : i / (float)(n - 1)));
            return altar + fwd * 6f + (side * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * 2.5f;
        }

        // ---- Midsummer: the bonfire ----
        private static void Midsummer(Colony c)
        {
            Vector3? spot = Open(c);
            if (spot == null) { c.Tell("No open ground by the hall for the Midsummer fire: they keep it in the hall instead."); return; }
            Net.Fest_((int)Festival.Midsummer, spot.Value);
            int i = 0; var living = c.R.Living.ToList();
            foreach (RosterRow r in living)
                Villagers.Command(r.Vid, "fest", $"{spot.Value.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{spot.Value.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{i++}|{living.Count}");
        }
        // Open ground near the pillars: off every room, nothing overhead, room for a ring of dancers.
        private static Vector3? Open(Colony c)
        {
            for (int i = 0; i < 60; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(8f, 30f);
                Vector3? spot = Colony.Spot(c.Position + new Vector3(r.x, 0, r.y), 2f);
                if (spot == null || c.RoomAt(spot.Value) != null || Utils.DistanceXZ(spot.Value, c.Position) < 6f) continue;
                Cover.GetCoverForPoint(spot.Value + Vector3.up, out float cover, out bool roof);
                if (roof) continue;
                if (Physics.CheckSphere(spot.Value + Vector3.up * 2.5f, 6f, LayerMask.GetMask("piece"), QueryTriggerInteraction.Ignore) || Physics.CheckSphere(spot.Value + Vector3.up * 1.5f, 3.5f, LayerMask.GetMask("static_solid", "Default"), QueryTriggerInteraction.Ignore)) continue; // (well clear of buildings, room for the ring)
                return spot;
            }
            return null;
        }
        // Sigrblot's sacrifice burning on the altar, and Odin's answer: on every game near.
        public static void SigrShow(int what, Vector3 at)
        {
            if (Player.m_localPlayer == null || Vector3.Distance(Player.m_localPlayer.transform.position, at) > 200) return;
            if (what == 1) { Assets.Effect("fx_Lightning", at); return; }
            GameObject bonfire = Assets.Find("bonfire"); if (bonfire == null) return;
            var go = new GameObject("JarldomSigrFire"); go.transform.position = at + Vector3.up * 0.9f;
            GameObject p = bonfire.transform.Find("_enabled_high/fx_BonfireFlames")?.gameObject;
            if (p != null) { GameObject f = UnityEngine.Object.Instantiate(p, go.transform, false); f.SetActive(true); f.transform.localScale = Vector3.one * 0.35f; }
            Light glow = new GameObject("glow").AddComponent<Light>(); glow.transform.SetParent(go.transform, false); glow.transform.localPosition = Vector3.up * 0.8f;
            glow.type = LightType.Point; glow.color = new Color(1f, 0.5f, 0.2f); glow.range = 9f; glow.intensity = 2f;
            Assets.Effect("vfx_FireAddFuel", at + Vector3.up);
            UnityEngine.Object.Destroy(go, 70f);
        }
        // On every game near: the fire itself (the bonfire's log heap, flames and crackle, no colliders, no fuel), dusk to dawn.
        public static void Show(int kind, Vector3 at)
        {
            if (kind >= 100) { SigrShow(kind - 100, at); return; }
            if ((Festival)kind != Festival.Midsummer || Player.m_localPlayer == null || Vector3.Distance(Player.m_localPlayer.transform.position, at) > 300) return;
            GameObject bonfire = Assets.Find("bonfire");
            if (bonfire == null) return;
            var go = new GameObject("JarldomMidsummerFire"); go.transform.position = at;
            Assets.Model(bonfire, go.transform, Vector3.zero, Quaternion.identity, 1.3f);
            Transform src = bonfire.transform;
            foreach (string part in new[] { "_enabled_high/fx_BonfireFlames", "_enabled_high/SFX" })
            {
                GameObject p = src.Find(part)?.gameObject; if (p == null) continue;
                GameObject f = UnityEngine.Object.Instantiate(p, go.transform, false); f.SetActive(true); f.transform.localPosition = Vector3.up * 0.3f; f.transform.localScale = Vector3.one * 1.4f;
            }
            Light glow = new GameObject("glow").AddComponent<Light>(); glow.transform.SetParent(go.transform, false); glow.transform.localPosition = Vector3.up * 2.5f;
            glow.type = LightType.Point; glow.color = new Color(1f, 0.6f, 0.25f); glow.range = 22f; glow.intensity = 2.2f;
            Assets.Effect("vfx_FireAddFuel", at + Vector3.up);
            if (Vector3.Distance(Player.m_localPlayer.transform.position, at) < 120) MusicMan.instance?.TriggerMusic(MidsummerMusic);
            UnityEngine.Object.Destroy(go, BonfireSeconds);
        }
    }

    // Round the Midsummer fire: each to their place in the ring, facing it; then dancing, cheering and calling out till late.
    internal sealed class FestTask : Task
    {
        private readonly Vector3 _fire; private readonly int _place, _of; private float _until, _next; private Vector3 _spot;
        private static readonly string[] Calls = { "Glaðir sumarnætr! / A merry midsummer!", "Dansið, allir! / Dance, everyone!", "Bjart er í nótt! / Bright is the night!", "Skál fyrir sumrinu! / Skål to the summer!", "Hærra, eldr! / Higher, fire!", "Freyja, gef oss góða uppskeru! / Freyja, give us a good harvest!" };
        public FestTask(Vector3 fire, int place, int of) { _fire = fire; _place = place; _of = Math.Max(1, of); Doing = "going to the Midsummer fire"; }
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            _until = Time.time + 330f; // (till about the time they'd go home)
            float a = 2 * Mathf.PI * _place / _of, r = 4.5f + (_of > 12 ? (_place % 2) * 1.5f : 0);
            _spot = _fire + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
            _spot = Colony.Spot(_spot, 1.5f) ?? _spot;
            b.Move.To(_spot, 0.8f);
        }
        public override void Tick(Brain b)
        {
            if (Time.time > _until || b.Colony == null || b.Colony.RaidOn) { Done = true; return; }
            if (b.Move.Moving && !b.Move.Failed) return;
            Vector3 d = _fire - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Doing != "dancing round the Midsummer fire") { Doing = "dancing round the Midsummer fire"; _next = Time.time + UnityEngine.Random.Range(1f, 5f); }
            if (Time.time < _next) return;
            _next = Time.time + UnityEngine.Random.Range(7f, 12f);
            float roll = UnityEngine.Random.value;
            Body_.Anim(b.Body)?.SetTrigger(roll < 0.6f ? "emote_dance" : roll < 0.8f ? "emote_cheer" : "emote_toast");
            if (UnityEngine.Random.value < 0.25f) { string[] l = Calls[UnityEngine.Random.Range(0, Calls.Length)].Split(new[] { " / " }, StringSplitOptions.None); b.Say(Greetings.Show((l[0], l[1])), 5); }
        }
        public override void Stop(Brain b) { if (b.View != null && b.View.IsValid()) Body_.Anim(b.Body)?.SetTrigger("emote_stop"); }
    }
}

namespace Jarldom
{
    // One part in Sigrblot: the gothi, a fighter, or one watching (FestivalDays.Sigrblot sets it; the beats go by the world's clock).
    internal sealed class SigrTask : Task
    {
        private readonly long _start; private readonly Vector3 _altar, _fwd; private readonly string _role, _name; private readonly int _i, _n, _fighters; private readonly string[] _blessed;
        private int _beat = -1; private Vector3 _spot;
        private static readonly string[] Calls = { "Allföðr, Sigföðr! Heyr oss! / Allfather, Victory-father! Hear us!", "Óðinn, gef oss sigr! / Odin, give us victory!", "Hrafnaguð, sjá þitt fólk! / Raven-god, see your people!" };
        private static readonly string[] Burns = { "Tak þessa fórn! / Take this sacrifice!", "Blóð ok eldr fyrir þik! / Blood and fire for you!", "Þiggðu, Óðinn! / Accept it, Odin!" };
        private static readonly string[] Answers = { "Til sigrs! / To victory!", "Fyrir Óðin! / For Odin!", "Ek skal eigi flýja! / I shall not flee!", "Sverð mitt er þitt! / My sword is yours!", "Sigr eða Valhöll! / Victory or Valhalla!" };
        public SigrTask(long start, Vector3 altar, Vector3 fwd, string role, int i, int n, int fighters, string extra)
        {
            _start = start; _altar = altar; _fwd = fwd; _role = role; _i = i; _n = n; _fighters = fighters;
            _blessed = role == "gothi" ? extra.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0]; _name = extra;
            Doing = role == "gothi" ? "leading the Sigrblot" : role == "fighter" ? "standing for the blessing at Sigrblot" : "watching the Sigrblot";
        }
        public override bool Interruptible => false;
        private float T => (float)((ZNet.instance.GetTime().Ticks - _start) / (double)TimeSpan.TicksPerSecond);
        public override void Start(Brain b) { _spot = Colony.Spot(FestivalDays.SigrSpot(_altar, _fwd, _role, _i, _n), 1.2f) ?? FestivalDays.SigrSpot(_altar, _fwd, _role, _i, _n); b.Move.To(_spot, 0.6f); }
        private static void Say(Brain b, string[] lines) { string[] l = lines[UnityEngine.Random.Range(0, lines.Length)].Split(new[] { " / " }, StringSplitOptions.None); b.Say(Greetings.Show((l[0], l[1])), 6); }
        private void Face(Brain b, Vector3 to) { Vector3 d = to - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized); }
        public override void Tick(Brain b)
        {
            float t = T, end = FestivalDays.SigrEnd(_fighters);
            if (t > end + 10f || b.Colony == null || b.Colony.RaidOn) { Done = true; return; }
            if (b.Move.Moving && !b.Move.Failed) return;
            if (_role == "gothi") { Gothi(b, t, end); return; }
            Face(b, _altar);
            if (_role == "fighter")
            {
                float mine = FestivalDays.SigrFirst + _i * FestivalDays.SigrEach + 1.5f;
                if (_beat < 0 && t >= mine) { _beat = 0; Body_.Anim(b.Body)?.SetTrigger("emote_challenge"); Say(b, Answers); }
            }
            if (_beat < 1 && t >= end) { _beat = 1; Body_.Anim(b.Body)?.SetTrigger(UnityEngine.Random.value < 0.5f ? "emote_cheer" : "emote_roar"); if (UnityEngine.Random.value < 0.4f) b.Say(Greetings.Show(("Sigr!", "Victory!")), 4); }
        }
        private int _blessing = -1; private bool _point; // (the gothi: which fighter they're at, and the touch still to give)
        private void Gothi(Brain b, float t, float end)
        {
            if (t < FestivalDays.SigrCall) { Face(b, _altar); return; }
            if (_beat < 0) { _beat = 0; Face(b, _altar); Body_.Anim(b.Body)?.SetTrigger("emote_point"); Say(b, Calls); return; }
            if (_beat == 0 && t >= FestivalDays.SigrFire) { _beat = 1; Face(b, _altar); Body_.Anim(b.Body)?.SetTrigger("emote_point"); Say(b, Burns); Net.Fest_(100, _altar); return; }
            if (_beat < 1) return;
            int k = Mathf.FloorToInt((t - FestivalDays.SigrFirst) / FestivalDays.SigrEach);
            if (t >= FestivalDays.SigrFirst && k < _blessed.Length && k > _blessing)
            {
                // (to the next fighter, before them, with a word)
                _blessing = k; _point = true;
                b.Move.To(FestivalDays.SigrSpot(_altar, _fwd, "fighter", k, _fighters) - _fwd * 1.0f, 0.4f);
                b.Say(Greetings.Show(($"Sigr fylgi þér, {_blessed[k]}!", $"Victory go with you, {_blessed[k]}!")), 5);
                return;
            }
            if (_point && !b.Move.Moving) { _point = false; Face(b, FestivalDays.SigrSpot(_altar, _fwd, "fighter", _blessing, _fighters)); Body_.Anim(b.Body)?.SetTrigger("emote_point"); return; }
            if (t >= end && _beat != 2)
            {
                _beat = 2; b.Move.To(_spot, 0.6f); Face(b, _altar);
                Body_.Anim(b.Body)?.SetTrigger("emote_challenge");
                b.Say(Greetings.Show(("Óðinn sá oss! Sigr!", "Odin has seen us! Victory!")), 6);
                Net.Fest_(101, _altar + _fwd * 14f + new Vector3(_fwd.z, 0, -_fwd.x) * 6f); // (and he answers)
            }
        }
    }
}
