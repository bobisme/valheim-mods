using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The jarl living among their people. Sitting in the great hall at dusk with three or more of them for half a minute: hall-fed (Rested
    // five minutes longer, once a day) and those at table think a little better of you. Sleeping in a bed in a colony longhouse: Rested five
    // minutes longer on waking, and those who sleep in that room think a little better of you. Watched on the player's own game; the
    // opinions are the colony owner's (Jarldom_Act "among").
    internal static class Among
    {
        public const float SitFor = 30f, Extra = 300f; public const int AtTable = 3, HallOpinion = 2, RoomOpinion = 1;
        public const string FedKey = "jarldom_hallfed"; // (player custom data: colony id and day of the last hall meal)
        private static float _sat, _next, _wakeAt; private static bool _slept; private static string _bedRoom, _bedColony;

        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) { _sat = 0; return; }
            if (me.IsSleeping() && !_slept) Asleep(me);
            if (!me.IsSleeping() && _slept && me.transform.position.y > -1000) { _slept = false; _wakeAt = Time.time + 3f; }
            if (_wakeAt > 0 && Time.time >= _wakeAt) { _wakeAt = 0; Woke(me); }
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            Hall(me);
        }

        // (jarldom among hall|sleep: what sitting the half minute, or waking in a longhouse, gives, without doing it)
        public static string Test(string what)
        {
            Player me = Player.m_localPlayer; Colony c = me != null ? Colony.At(me.transform.position) : null;
            if (c == null) return "not in a colony";
            float before = me.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested)?.GetRemaningTime() ?? 0;
            if (what == "hall") { Room hall = c.Best(RoomType.GreatHall); var there = hall == null ? new string[0] : Present(c, hall); Rest(me); Net.Act(c, "among", "hall|" + HallOpinion + "|" + string.Join(",", there)); return $"rested {before:0} -> {me.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested)?.GetRemaningTime() ?? 0:0} s; +{HallOpinion} with {there.Length} in the hall"; }
            Room room = c.R.Rooms.FirstOrDefault(r => r.Type == RoomType.Longhouse && r.Ok);
            if (room == null) return "no longhouse";
            _bedColony = c.Id; _bedRoom = room.Id; Woke(me);
            return $"rested {before:0} -> {me.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested)?.GetRemaningTime() ?? 0:0} s; room {room.Id}";
        }

        // ---- at table ----
        private static void Hall(Player me)
        {
            Colony c = Colony.At(me.transform.position);
            Room hall = c?.RoomAt(me.transform.position, RoomType.GreatHall);
            double h = c?.Hour ?? 0;
            if (hall == null || !me.IsAttached() || me.InBed() || h < Schedule.HallAt - 0.5 || h >= Schedule.SleepAt) { _sat = 0; return; }
            var there = Present(c, hall);
            if (there.Length < AtTable) { _sat = 0; return; }
            if (me.m_customData.TryGetValue(FedKey, out string fed) && fed == c.Id + ":" + c.Day) return; // (once a day)
            _sat += 1f;
            if (_sat < SitFor) return;
            _sat = 0;
            me.m_customData[FedKey] = c.Id + ":" + c.Day;
            Rest(me);
            Net.Act(c, "among", "hall|" + HallOpinion + "|" + string.Join(",", there));
            me.Message(MessageHud.MessageType.Center, $"Hall-fed: you ate among your people. Rested lasts {Extra / 60:0} minutes longer, and those at table think the better of you.");
        }
        private static string[] Present(Colony c, Room room) =>
            Character.GetAllCharacters().Where(x => x != null && !x.IsDead() && Jarldom.VillagerPrefab.Is(x) && c.RoomAt(x.transform.position) == room)
                .Select(x => x.GetComponent<ZNetView>()?.GetZDO()?.GetString(Jarldom.Villagers.VidKey, "") ?? "").Where(v => v != "").ToArray();

        // ---- asleep in a longhouse ----
        private static void Asleep(Player me)
        {
            _slept = true; _bedRoom = _bedColony = null;
            Colony c = Colony.At(me.transform.position);
            Room room = c?.RoomAt(me.transform.position, RoomType.Longhouse);
            if (room == null) return;
            _bedRoom = room.Id; _bedColony = c.Id;
        }
        private static void Woke(Player me)
        {
            Colony c = Colony.Of(_bedColony);
            if (c == null || _bedRoom == null) return;
            Rest(me);
            Net.Act(c, "among", "sleep|" + RoomOpinion + "|" + _bedRoom);
            me.Message(MessageHud.MessageType.TopLeft, $"You slept among your people at {c.R.Name}: Rested lasts {Extra / 60:0} minutes longer.");
            _bedRoom = _bedColony = null;
        }

        // Rested five minutes longer (or five minutes of it, if not rested).
        private static void Rest(Player me)
        {
            SEMan se = me.GetSEMan();
            StatusEffect rested = se.GetStatusEffect(SEMan.s_statusEffectRested) ?? se.AddStatusEffect(SEMan.s_statusEffectRested, true);
            if (rested == null) return;
            rested.m_ttl += Extra; // (its time left, longer)
        }

        // On the colony's owner: who thinks the better of the jarl.
        public static void Owned(Colony c, string arg, string who)
        {
            string[] f = arg.Split('|');
            if (f.Length < 3 || string.IsNullOrEmpty(who) || !int.TryParse(f[1], out int by)) return;
            var vids = f[0] == "hall" ? f[2].Split(',').Where(v => v != "").ToList()
                : c.R.Living.Where(r => { Bed b = c.BedById(r.Bed); return b != null && c.RoomAt(b.transform.position)?.Id == f[2]; }).Select(r => r.Vid).ToList();
            foreach (string vid in vids) Jarldom.Villagers.Command(vid, "opinion", who + "|" + by);
        }
    }
}
