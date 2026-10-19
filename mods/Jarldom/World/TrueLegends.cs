using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // True legends (Core/TrueLegend.cs): with a jarl in the great hall, now and then a villager tells of a real place out in the world;
    // the third time it's told, the place is pinned on the jarls' maps (kept with their characters). The place is found by the server,
    // which alone knows where the world's crypts and caves lie; the colony's owner keeps the legend in the colony's flags.
    internal static class TrueLegends
    {
        public const string PinnedKey = "jarldom_legendpins"; // (player custom data: the legends already pinned on their map)
        public const float TellEvery = 240f; // (real seconds between tellings, with a jarl in the hall)
        private static readonly Dictionary<string, float> NextTell = new Dictionary<string, float>(), Asked = new Dictionary<string, float>();
        private static float _next;

        public static bool On => Plugin.Instance?.TrueLegendsOn?.Value ?? true;
        public static int Stage() => ZoneSystem.instance == null ? 0 : Requests.BossKeys.Count(k => ZoneSystem.instance.GetGlobalKey(k));

        // ---- the colony's owner ----
        public static void Tick(Colony c)
        {
            if (!c.IsOwner || !On || Time.time < _next) return;
            _next = Time.time + 10f;
            Seen(c);
            if (TrueLegend.Open(c.R.Flags, c.Day) >= TrueLegend.MaxOpen) return; // (enough on the maps: none told till one's found)
            if (c.R.Flag(TrueLegend.Cur) == 0) { Ask(c); return; }
            Room hall = c.Best(RoomType.GreatHall);
            if (hall == null) return;
            if (!Player.GetAllPlayers().Any(p => p != null && c.AllowedName(p.GetPlayerName()) && c.RoomAt(p.transform.position) == hall)) return;
            if (NextTell.TryGetValue(c.Id, out float at) && Time.time < at) { return; }
            if (!NextTell.ContainsKey(c.Id)) { NextTell[c.Id] = Time.time + 30f; return; } // (not the moment they walk in)
            Tell(c, hall);
        }
        public static void Tell(Colony c, Room hall)
        {
            NextTell[c.Id] = Time.time + TellEvery;
            var bodies = c.R.Living.Select(r => (r, body: Villagers.Body(r.Vid))).Where(x => x.body != null && c.RoomAt(x.body.transform.position) == hall).ToList();
            if (bodies.Count == 0) return;
            var teller = bodies.FirstOrDefault(x => x.r.Prof == Prof.Skald);
            if (teller.body == null) teller = bodies[Random.Range(0, bodies.Count)];
            var (x0, z0) = TrueLegend.Unpack(c.R.Flag(TrueLegend.Cur));
            Vector3 place = new Vector3(x0, 0, z0);
            int heard = c.R.Flag(TrueLegend.Heard) + 1;
            c.R.Flags[TrueLegend.Heard] = heard;
            string tale = TrueLegend.Tale(c.R.Flag(TrueLegend.KindKey), Utils.DistanceXZ(place, c.Position), ScoutTask.Compass(place - c.Position), heard, new System.Random(System.Environment.TickCount));
            Villagers.Command(teller.r.Vid, "say", tale);
            if (heard >= TrueLegend.HeardForPin)
            {
                string label = char.ToUpper(TrueLegend.Kinds[c.R.Flag(TrueLegend.KindKey)].name[0]) + TrueLegend.Kinds[c.R.Flag(TrueLegend.KindKey)].name.Substring(1) + " (a legend)";
                Net.LegendPin(c, place, label);
                c.Log("text", teller.r.Name, "", $"{teller.r.Name} told the legend of {TrueLegend.Kinds[c.R.Flag(TrueLegend.KindKey)].name} to the {ScoutTask.Compass(place - c.Position)} a third time: it's marked on the jarls' maps.");
                c.R.Flags[TrueLegend.ToldPrefix + c.R.Flag(TrueLegend.Cur)] = c.Day;
                c.R.Flags.Remove(TrueLegend.Cur); c.R.Flags.Remove(TrueLegend.KindKey); c.R.Flags.Remove(TrueLegend.Heard);
            }
            c.MarkDirty();
        }
        // A jarl at a pinned place: found (a remote one too far off to be loaded here isn't seen, but it goes stale in time).
        private static void Seen(Colony c)
        {
            foreach (string k in c.R.Flags.Keys.Where(k => k.StartsWith(TrueLegend.ToldPrefix) && !c.R.Flags.ContainsKey(TrueLegend.SeenPrefix + k.Substring(TrueLegend.ToldPrefix.Length))).ToList())
            {
                if (!int.TryParse(k.Substring(TrueLegend.ToldPrefix.Length), out int packed)) continue;
                var (x, z) = TrueLegend.Unpack(packed);
                if (!Player.GetAllPlayers().Any(p => p != null && c.AllowedName(p.GetPlayerName()) && Utils.DistanceXZ(p.transform.position, new Vector3(x, 0, z)) < TrueLegend.FoundAt)) continue;
                c.R.Flags[TrueLegend.SeenPrefix + packed] = c.Day; c.MarkDirty();
            }
        }
        // A new legend, from the server (once a minute at most while there's none).
        private static void Ask(Colony c)
        {
            if (Asked.TryGetValue(c.Id, out float at) && Time.time - at < 60f) return;
            Asked[c.Id] = Time.time;
            string told = string.Join(",", c.R.Flags.Keys.Where(k => k.StartsWith(TrueLegend.ToldPrefix)).Select(k => k.Substring(TrueLegend.ToldPrefix.Length)));
            Net.LegendAsk(c, Stage(), told);
        }
        public static void Found(Colony c, string prefab, Vector3 at)
        {
            if (c == null || !c.IsOwner || c.R.Flag(TrueLegend.Cur) != 0) return;
            int kind = TrueLegend.KindOf(prefab);
            if (kind < 0) return;
            c.R.Flags[TrueLegend.Cur] = TrueLegend.Pack(at.x, at.z); c.R.Flags[TrueLegend.KindKey] = kind; c.R.Flags[TrueLegend.Heard] = 0;
            c.MarkDirty();
        }

        // ---- the server: where the world's places are ----
        public static (string prefab, Vector3 at)? Find(Vector3 from, int stage, string toldCsv)
        {
            if (ZoneSystem.instance == null) return null;
            var told = new HashSet<int>(toldCsv.Split(',').Where(s => s != "").Select(s => int.TryParse(s, out int v) ? v : 0));
            var places = ZoneSystem.instance.m_locationInstances.Values.Where(l => l.m_location?.m_prefabName != null && TrueLegend.KindOf(l.m_location.m_prefabName) >= 0)
                .Select(l => (l.m_location.m_prefabName, l.m_position.x, l.m_position.z)).ToList();
            int i = TrueLegend.Pick(places, from.x, from.z, stage, told);
            return i < 0 ? ((string, Vector3)?)null : (places[i].Item1, new Vector3(places[i].Item2, 0, places[i].Item3));
        }

        // ---- each game: the pin, on a jarl's own map (kept with their character) ----
        public static void Pin(string colony, Vector3 at, string label)
        {
            Player me = Player.m_localPlayer; Colony c = Colony.Of(colony);
            if (me == null || Minimap.instance == null || c == null || !c.AllowedName(me.GetPlayerName())) return;
            string packed = TrueLegend.Pack(at.x, at.z).ToString();
            me.m_customData.TryGetValue(PinnedKey, out string pinned);
            if ((pinned ?? "").Split(',').Contains(packed)) return; // (pinned for this jarl already)
            me.m_customData[PinnedKey] = string.IsNullOrEmpty(pinned) ? packed : pinned + "," + packed;
            at.y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            Minimap.instance.AddPin(at, Minimap.PinType.Icon3, label, true, false);
            me.Message(MessageHud.MessageType.TopLeft, $"Marked on your map: {label}.");
            Tidy(me);
        }
        // Each game, now and then: a legend's pin comes off the map once the jarl has stood there, and only the newest few are kept (a map
        // from before the cap is thinned once). Not in a player's first moments: their pins are still loading.
        private static float _tidyAt, _spawned; private static Player _for;
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        public static void MapTick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || Minimap.instance == null) { _for = null; return; }
            if (me != _for) { _for = me; _spawned = Time.time; }
            if (Time.time - _spawned < 15f || Time.time < _tidyAt) return;
            _tidyAt = Time.time + 5f;
            Tidy(me);
        }
        private static void Tidy(Player me)
        {
            if (!me.m_customData.TryGetValue(PinnedKey, out string pinned) || string.IsNullOrEmpty(pinned)) return;
            var list = pinned.Split(',').Where(p => p != "").ToList();
            var drop = list.Where(p => int.TryParse(p, out int v) && Utils.DistanceXZ(me.transform.position, At(v)) < TrueLegend.FoundAt).ToList(); // (found)
            drop.AddRange(TrueLegend.Excess(list.Except(drop).ToList(), TrueLegend.MaxOpen));
            if (drop.Count == 0) return;
            foreach (string p in drop)
            {
                if (!int.TryParse(p, out int v)) continue;
                Vector3 at = At(v);
                Minimap.PinData pin = Pins(Minimap.instance).FirstOrDefault(x => x.m_type == Minimap.PinType.Icon3 && x.m_name.EndsWith("(a legend)") && Utils.DistanceXZ(x.m_pos, at) < 2f);
                if (pin != null) Minimap.instance.RemovePin(pin);
            }
            me.m_customData[PinnedKey] = string.Join(",", list.Except(drop));
        }
        private static Vector3 At(int packed) { var (x, z) = TrueLegend.Unpack(packed); return new Vector3(x, 0, z); }
        public static void Forget() { NextTell.Clear(); Asked.Clear(); }
    }
}
