using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Words over villagers' heads: said on the owner, carried in the ZDO so every game nearby shows them.
    internal static class Talk
    {
        public const string SayKey = "jarldom_say", SayTimeKey = "jarldom_sayt";
        private static float _next;
        private static readonly Dictionary<ZDOID, long> Shown = new Dictionary<ZDOID, long>();
        // (the same words from the same mouth at most every two minutes: a complaint is heard, not chanted)
        private static readonly Dictionary<string, (string text, float at)> Last = new Dictionary<string, (string, float)>();
        public static void Say(Brain b, string text, float ttl)
        {
            if (b == null || string.IsNullOrEmpty(text)) return;
            string who = b.S?.Vid ?? "";
            if (Last.TryGetValue(who, out var last) && last.text == text && Time.time - last.at < 120f) return;
            Last[who] = (text, Time.time);
            if (Last.Count > 400) Last.Clear();
            ZDO z = b.View.GetZDO();
            z.Set(SayKey, text + "\u001f" + ttl.ToString(System.Globalization.CultureInfo.InvariantCulture));
            z.Set(SayTimeKey, ZNet.instance.GetTime().Ticks);
        }
        public static void Tick()
        {
            if (Time.time < _next || Chat.instance == null || Player.m_localPlayer == null) return;
            _next = Time.time + 0.5f;
            Vector3 me = Player.m_localPlayer.transform.position;
            foreach (Humanoid h in Villagers.Bodies())
            {
                if (Vector3.Distance(h.transform.position, me) > 30) continue;
                ZDO z = h.GetComponent<ZNetView>().GetZDO();
                long at = z.GetLong(SayTimeKey, 0L);
                if (at == 0 || Shown.TryGetValue(z.m_uid, out long seen) && seen == at) continue;
                Shown[z.m_uid] = at;
                if ((ZNet.instance.GetTime().Ticks - at) / TimeSpan.TicksPerSecond > 8) continue;
                string[] f = z.GetString(SayKey, "").Split('\u001f');
                float ttl = f.Length > 1 && float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t) ? t : 5;
                Chat.instance.SetNpcText(h.gameObject, Vector3.up * 2.2f, 20f, ttl, "", f[0], false);
                if (Vector3.Distance(h.transform.position, me) < 20 && f[0].Length > 30) MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"{Villagers.DisplayName(h)}: {f[0]}");
            }
            if (Shown.Count > 500) Shown.Clear();
        }
        // In the hall: a line about something that really happened lately.
        public static void Gossip(Brain b)
        {
            if (b.Colony == null) return;
            var rng = new System.Random(Environment.TickCount ^ b.S.Vid.GetStableHashCode());
            if (rng.Next(3) == 0 && b.S.Has("merry")) { b.Say(Merry[rng.Next(Merry.Length)], 5); foreach (Brain o in Villagers.All.Where(o => o != b && Vector3.Distance(o.Pos, b.Pos) < 10)) Mood.Add(o.S, "merry", o.Now); return; }
            string line = Stories.Gossip(rng, b.S.Name, b.Colony.R.Log, b.Now);
            if (line == null) return;
            b.Say(line.Substring(b.S.Name.Length).TrimStart(), 7);
            // Talking together warms them to each other.
            foreach (Brain o in Villagers.All.Where(o => o != b && o.S.Colony == b.S.Colony && Vector3.Distance(o.Pos, b.Pos) < 8)) Society.Warm(b, o, 1);
        }
        private static readonly string[] Merry = { "Another horn of mead for the jarl!", "Did you hear the one about the troll and the fishing rod?", "Skål!", "Sing, you lot, sing!", "My grandmother could out-drink this whole hall." };
    }

    // How villagers feel about each other and the jarls; breakdowns when they're miserable.
    internal static class Society
    {
        public static void Warm(Brain a, Brain b, int by)
        {
            int ab = a.S.Bonds.TryGetValue(b.S.Vid, out int x) ? x : 0, ba = b.S.Bonds.TryGetValue(a.S.Vid, out int y) ? y : 0;
            a.S.Bonds[b.S.Vid] = Mathf.Clamp(ab + by, -100, 100);
            b.S.Bonds[a.S.Vid] = Mathf.Clamp(ba + by, -100, 100);
            a.Dirty(); b.Dirty();
        }
        public static void Swear(Colony c, string va, string vb)
        {
            Brain a = Villagers.ByVid(va), b = Villagers.ByVid(vb);
            if (a == null || b == null) return;
            int o = a.S.Bonds.TryGetValue(vb, out int x) ? x : 0;
            if (o <= 80) { c.Tell($"{a.S.Name} and {b.S.Name} aren't close enough to swear brotherhood ({o} of 80)."); return; }
            a.S.Sworn = vb; b.S.Sworn = va; a.Dirty(); b.Dirty();
            c.Log("sworn", a.S.Name, b.S.Name);
        }
        // Miserable villagers slack, brawl and steal; after two days below -80 they're disgraced and may desert.
        public static void Breakdowns(Brain b, int mood, double now)
        {
            if (mood >= -60 || b.Colony == null) return;
            var rng = new System.Random((int)now ^ b.S.Vid.GetStableHashCode());
            // A check every five seconds: about 5% an in-game hour.
            if (rng.NextDouble() > 0.05 * 5 / Clock.Hour) return;
            int kind = rng.Next(3);
            if (kind == 0) { b.Say("Why should I lift a finger?", 5); }
            else if (kind == 1)
            {
                Brain other = Villagers.All.Where(o => o != b && o.S.Colony == b.S.Colony && Vector3.Distance(o.Pos, b.Pos) < 15).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (other != null) { b.S.Bump("brawls"); Mood.Add(b.S, "brawl", now); Mood.Add(other.S, "brawl", now); Warm(b, other, -20); b.Colony.Log("brawl", b.S.Name, other.S.Name); b.Say("Say that again!", 4); }
            }
            else
            {
                Container chest = Stock.Chests(b.Colony, b.Pos).Select(x => x.chest).FirstOrDefault(ch => !ContainerAccess.InUse(ch) && ch.GetInventory().NrOfItems() > 0);
                ItemDrop.ItemData item = chest?.GetInventory().GetAllItems().OrderBy(_ => rng.Next()).FirstOrDefault();
                if (item != null && Stock.Take(chest, b.Body.GetInventory(), item, 1) > 0) { b.S.Bump("thefts"); b.Colony.Log("theft", b.S.Name, ""); }
            }
            if (mood < -80 && b.S.LowMoodSince > 0 && now - b.S.LowMoodSince > Clock.DayLength * 2 && !b.S.Disgraced)
            {
                b.S.Disgraced = true; b.S.DisgraceSince = now; Worth.Gain(b.S, "disgrace");
                b.Colony.Log("text", b.S.Name, "", $"{b.S.FullName} is disgraced.");
            }
            if (b.S.Disgraced && Clock.IsNight(now) && rng.NextDouble() < 0.1)
            {
                b.Colony.Log("desert", b.S.FullName, "");
                Net.Act(b.Colony, "gone", b.S.Vid + "|deserted");
                b.Release();
                b.View.Destroy(); // they take their hoard (it was never coins) and what they carry
            }
            b.Dirty();
        }
    }

    // Alerts for the jarl: a profession with no gear, said at most every few minutes per villager.
    internal static class Alerts
    {
        private static readonly Dictionary<string, float> Last = new Dictionary<string, float>();
        public static void NeedsGear(Colony c, Brain b, string what)
        {
            string key = b.S.Vid + what;
            if (Last.TryGetValue(key, out float t) && Time.time - t < 300) return;
            Last[key] = Time.time;
            b.Say($"I can't work as a {Tables.Of(b.S.Prof).Name.ToLower()}: {what}. I'll carry things meanwhile.", 6);
        }
    }
}
