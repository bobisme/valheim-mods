using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The Forsaken-touched: a newcomer secretly marked (after the first boss, one in twelve). Now and then, when no one's watching much,
    // a little mischief: food spoiled in the stores, tools blunted, a hall door left open in a raid, an ugly rumour. Each time those near
    // may notice (the touched one more often, an innocent sometimes); anyone seen twice can be named at the Thing. The tells: they dream
    // of ash, and Hugin warns at dawn.
    internal static class Saboteur
    {
        public const string Mark = "touched";
        public static bool Touched(VillagerState s) => s.Count(Mark) > 0;

        // From a touched villager's needs check (on the game that runs them).
        public static void Tick(Brain b)
        {
            if (!Touched(b.S) || b.Colony == null || !b.Colony.Valid) return;
            if (b.NextMischief == 0) { b.NextMischief = Time.time + Random.Range(400f, 900f); return; }
            if (Time.time < b.NextMischief) return;
            b.NextMischief = Time.time + Random.Range(900f, 1800f); // about once a day or so
            Colony c = b.Colony;
            // (in a raid, a door left open first; otherwise one of the rest at random, and another if that can't be done here)
            var tricks = new List<System.Func<string>> { () => Spoil(c, b), () => Blunt(c, b), () => Rumour(c, b) }.OrderBy(_ => Random.value).ToList();
            if (c.RaidOn) tricks.Insert(0, () => OpenDoor(c, b));
            string what = tricks.Select(t => t()).FirstOrDefault(w => w != null);
            if (what == null) return;
            c.Log("text", "", "", what);
            // Who was near: the touched one is noticed more often than not; sometimes an innocent instead (or as well).
            var near = Villagers.All.Where(o => o != b && o.S.Colony == c.Id && Vector3.Distance(o.Pos, b.Pos) < 25f).ToList();
            if (Random.value < 0.6f) { b.S.Bump("seen"); b.Dirty(); }
            if (near.Count > 0 && Random.value < 0.4f) { Brain other = near[Random.Range(0, near.Count)]; other.S.Bump("seen"); other.Dirty(); }
            b.S.Bump("mischief"); b.Dirty();
        }
        private static string Spoil(Colony c, Brain b)
        {
            var food = Stock.Food(c, b.Pos).Where(f => Vector3.Distance(f.chest.transform.position, b.Pos) < 30f && !ContainerAccess.InUse(f.chest)).ToList();
            if (food.Count == 0) return null;
            var (chest, item) = food[Random.Range(0, food.Count)];
            int n = Mathf.Min(item.m_stack, Random.Range(2, 6));
            ContainerAccess.TakeOver(chest);
            chest.GetInventory().RemoveItem(item, n);
            return $"Food in the stores has gone bad: {n} {Localization.instance.Localize(item.m_shared.m_name).ToLower()} spoiled overnight.";
        }
        private static string Blunt(Colony c, Brain b)
        {
            var tools = Stock.Chests(c, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest) && Vector3.Distance(x.chest.transform.position, b.Pos) < 30f)
                .SelectMany(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_shared.m_useDurability && i.m_durability > i.GetMaxDurability() * 0.3f).Select(i => (x.chest, i))).ToList();
            if (tools.Count == 0) return null;
            var (chest, item) = tools[Random.Range(0, tools.Count)];
            ContainerAccess.TakeOver(chest);
            item.m_durability *= 0.4f;
            chest.GetInventory().m_onChanged?.Invoke(); // (the chest saves its contents)
            string what = Localization.instance.Localize(item.m_shared.m_name).ToLower();
            bool many = what.EndsWith("s") && !what.EndsWith("ss"); // (trousers, greaves: "the", and "were")
            return $"{(many ? "The" : "A")} {what} in the stores {(many ? "were" : "was")} found {(item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet ? "torn and slashed" : "notched and blunted")}.";
        }
        private static string OpenDoor(Colony c, Brain b)
        {
            Room hall = c.Best(RoomType.GreatHall);
            if (hall == null) return null;
            Door door = c.PiecesIn(c.Radius).Select(p => p.GetComponent<Door>()).FirstOrDefault(d => d != null && !Classes.IsShutter(d.gameObject) && Geometry.Distance(hall.Xz, d.transform.position.x, d.transform.position.z) <= 1.5f);
            ZNetView v = door != null ? door.GetComponent<ZNetView>() : null;
            if (v == null || !v.IsValid() || v.GetZDO().GetInt(ZDOVars.s_state) != 0) return null;
            v.InvokeRPC("UseDoor", true);
            return "In the thick of the raid, a door of the great hall was found standing open.";
        }
        private static string Rumour(Colony c, Brain b)
        {
            var others = Villagers.All.Where(o => o != b && o.S.Colony == c.Id).ToList();
            if (others.Count < 2) return null;
            Brain x = others[Random.Range(0, others.Count)], y = others.Where(o => o != x).OrderBy(_ => Random.value).First();
            x.S.Bonds[y.S.Vid] = Mathf.Clamp((x.S.Bonds.TryGetValue(y.S.Vid, out int a) ? a : 0) - 15, -100, 100);
            y.S.Bonds[x.S.Vid] = Mathf.Clamp((y.S.Bonds.TryGetValue(x.S.Vid, out int d) ? d : 0) - 15, -100, 100);
            x.Dirty(); y.Dirty();
            return $"Ugly whispers go round: {x.S.Name} and {y.S.Name} each heard what the other is supposed to have said.";
        }

        // At dawn (on the colony owner): the touched dream of ash, and Hugin sometimes warns.
        public static void Dawn(Colony c)
        {
            var touched = Villagers.All.Where(b => b.S.Colony == c.Id && Touched(b.S)).ToList();
            foreach (Brain b in touched) { Mood.Add(b.S, "ashdreams", b.Now); b.Dirty(); }
            if (touched.Count > 0 && Random.value < 0.3f) c.Tell("Hugin croaks from the pillars: “One of yours smells of ash.”", true);
        }

        // ---- the cleansing rite ----
        // The jarl may ask the gothi to burn the ash out of one of theirs instead: a precious offering from the treasury, spent whatever
        // comes of it; at Odin's altar, the gothi tries. The touched are cleansed if Odin is willing (more often the more he favours the
        // colony); one never touched is found clean, and the suspicion on them lifted.
        public const int CleansePrice = 100; public const string Pending = "cleanse:";
        public static string CleansePriceText() => CleansePrice + " coins from the treasury";
        // Why it can't be asked now ("" when it can).
        public static string CleanseWhy(Colony c, string vid)
        {
            if (!c.R.Living.Any(r => r.Prof == Prof.Gothi)) return "the colony has no gothi (give someone that trade)";
            if (OdinAltar(c) == null) return "there's no altar to Odin (an Odin's shrine or a temple to Odin, with its altar)";
            if (c.R.Flags.ContainsKey(Pending + vid)) return "the gothi is already to hold the rite for them";
            Container t = Stock.Treasury(c);
            if (t == null || Stock.Coins(t) < CleansePrice) return $"the treasury hasn't the {CleansePrice} coins the offering costs";
            return "";
        }
        public static Piece OdinAltar(Colony c)
        {
            var rooms = c.R.Rooms.Where(r => r.Ok && (r.Type == RoomType.OdinShrine || r.Type == RoomType.Temple && r.God == "odin")).ToList();
            return c.PiecesIn(c.Radius).Where(p => p != null && Utils.GetPrefabName(p.gameObject) == Pieces.AltarPrefix + "odin" && rooms.Any(r => c.RoomAt(p.transform.position + Vector3.up * 0.3f) == r)).FirstOrDefault();
        }
        // On the owner (an action): the offering taken, the rite set for the gothi.
        public static void AskCleanse(Colony c, string vid, string who)
        {
            string why = CleanseWhy(c, vid);
            RosterRow row = c.R.Row(vid);
            if (why != "" || row == null) { c.Tell("No cleansing rite: " + (why != "" ? why : "who?") + "."); return; }
            if (!Stock.TakeCoins(Stock.Treasury(c), CleansePrice)) { c.Tell("No cleansing rite: the treasury couldn't pay."); return; }
            c.R.Flags[Pending + vid] = c.Day; c.MarkDirty();
            bool shamed = Villagers.ByVid(vid)?.S.Disgraced == true;
            c.Log("text", row.Name, who, $"{who} gave {CleansePrice} coins to Odin, and asked the gothi to {(shamed ? "make atonement for" : "burn the ash out of")} {row.Name}.");
        }
        public static string NextRite(Colony c) => c.R.Flags.Keys.Where(k => k.StartsWith(Pending)).Select(k => k.Substring(Pending.Length)).FirstOrDefault();
        public static void Rite(Colony c, Brain gothi, Brain them)
        {
            c.R.Flags.Remove(Pending + them.S.Vid); c.MarkDirty();
            string name = them.S.Name;
            bool clean = !Touched(them.S), shamed = them.S.Disgraced;
            Thing.Pending(c).RemoveAll(p => p.Id == "accuse" && p.About == name); // (asked about at the rite: not to be named at the Thing as well)
            // A disgraced soul: the rite is their atonement too, if Odin will hear it.
            if (shamed)
            {
                if (clean) { them.S.Counts.Remove("seen"); them.Dirty(); }
                float atone = Mathf.Clamp(0.55f + c.FavourOf("odin") * 0.02f, 0.35f, 0.85f);
                bool heard = Random.value < atone;
                if (heard)
                {
                    if (!clean) { them.S.Counts.Remove(Mark); them.S.Counts.Remove("seen"); Mood.Remove(them.S, "ashdreams"); }
                    Asks.Wash(them);
                    them.Say(new[] { "Odin heard it. I can stand in the hall again.", "The shame's burned away. I'll not waste this.", "I'm clean of it. Thank you, jarl." }[Random.Range(0, 3)], 6);
                    c.Log("text", gothi.S.Name, name, $"At Odin's altar {gothi.S.Name} the gothi made atonement for {them.S.FullName}: Odin heard it, and their disgrace is washed out{(clean ? "" : ", the Forsaken's ash with it")}.");
                }
                else c.Log("text", gothi.S.Name, name, $"{gothi.S.Name} the gothi made atonement for {them.S.FullName} at Odin's altar, but Odin would not hear it: the disgrace is on them still.");
                return;
            }
            if (clean)
            {
                them.S.Counts.Remove("seen"); them.Dirty();
                c.Log("text", gothi.S.Name, name, $"{gothi.S.Name} the gothi found no ash on {them.S.FullName} at Odin's altar: they were never touched. The suspicion on them is lifted.");
                return;
            }
            float chance = Mathf.Clamp(0.55f + c.FavourOf("odin") * 0.02f, 0.35f, 0.85f);
            if (Random.value < chance)
            {
                them.S.Counts.Remove(Mark); them.S.Counts.Remove("seen"); Mood.Remove(them.S, "ashdreams"); them.Dirty();
                c.R.Deeds += Renown.Deed("exiled");
                c.Log("text", gothi.S.Name, name, $"At Odin's altar {gothi.S.Name} the gothi burned the ash out of {them.S.FullName}: the Forsaken's mark is gone, and the mischief with it.");
            }
            else c.Log("text", gothi.S.Name, name, $"{gothi.S.Name} the gothi held the rite for {them.S.FullName}, but Odin would not hear it: the ash clings to them still.");
        }

        // The Thing has spoken: cast out. Touched or not, they go.
        public static void CastOut(Colony c, string name, string who)
        {
            Brain b = Villagers.All.FirstOrDefault(x => x.S.Colony == c.Id && x.S.Name == name);
            if (b == null) return;
            bool guilty = Touched(b.S);
            if (guilty)
            {
                c.Log("text", b.S.Name, who, $"As {b.S.FullName} was cast out, ash fell from their cloak: the Forsaken's mark was on them. The mischief is over.");
                c.R.Deeds += Renown.Deed("exiled");
            }
            else
            {
                c.Log("text", b.S.Name, who, $"{b.S.FullName} was cast out, and swore to the last they had done nothing. The mischief goes on.");
                foreach (Brain o in Villagers.All.Where(o => o != b && o.S.Colony == c.Id)) { Mood.Add(o.S, "unjust", o.Now); o.S.Opinions[who] = Mathf.Clamp((o.S.Opinions.TryGetValue(who, out int op) ? op : 0) - 5, -100, 100); o.Dirty(); }
            }
            foreach (Brain o in Villagers.All.Where(o => o.S.Colony == c.Id && o.S.Count("seen") > 0)) { o.S.Counts.Remove("seen"); o.Dirty(); } // a fresh start for suspicion
            Villagers.Command(b.S.Vid, "dismiss", "");
        }
    }

    // Inspiration and the colony's jarls, at dawn.
    internal static class Spirit
    {
        public static void Dawn(Colony c)
        {
            var people = Villagers.All.Where(b => b.S.Colony == c.Id).ToList();
            // A villager in high spirits may wake inspired: a day of their best work.
            foreach (Brain b in people)
                if (Mood.Of(b.S, b.Now) >= 60 && !Saboteur.Touched(b.S) && Random.value < 0.15f) { Mood.Add(b.S, "inspired", b.Now); b.S.Bump("inspired"); c.Log("inspired", b.S.FullName, "", ""); b.Dirty(); }
            // Co-jarls: a player most of the colony likes (opinion over 20 for more than 60% of them) is followed gladly by those who like them.
            if (people.Count < 3) return;
            foreach (string jarl in people.SelectMany(b => b.S.Opinions.Keys).Distinct().ToList())
            {
                int fans = people.Count(b => b.S.Opinions.TryGetValue(jarl, out int o) && o > 20);
                if (fans * 10 <= people.Count * 6) continue;
                foreach (Brain b in people.Where(b => b.S.Opinions.TryGetValue(jarl, out int o) && o > 40)) { Mood.Add(b.S, "belovedjarl", b.Now); b.Dirty(); }
            }
        }
        // Who follows whom, for the colony window: player → how many like them (over 20).
        public static System.Collections.Generic.List<(string jarl, int fans, int of)> Followers(Colony c)
        {
            var people = Villagers.All.Where(b => b.S.Colony == c.Id).ToList();
            return people.SelectMany(b => b.S.Opinions.Keys).Distinct()
                .Select(j => (j, people.Count(b => b.S.Opinions.TryGetValue(j, out int o) && o > 20), people.Count)).OrderByDescending(x => x.Item2).ToList();
        }
    }
}
