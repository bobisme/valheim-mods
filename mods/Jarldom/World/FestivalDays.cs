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
        public static void Ceremony(Colony c, Festival f) { if (f == Festival.Midsummer) Plugin.Guard(() => Midsummer(c)); }

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
        // On every game near: the fire itself (the bonfire's log heap, flames and crackle, no colliders, no fuel), dusk to dawn.
        public static void Show(int kind, Vector3 at)
        {
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
