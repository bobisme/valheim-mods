using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Odin sends the fallen: at dawn Hugin says who is coming, at dusk a Valkyrie sets them down at the pillars, and the jarl takes them
    // in or turns them away (at the high seat).
    internal static class Migration
    {
        public static string Why(Colony c)
        {
            if (!Plugin.Instance.Migration.Value) return "migration is switched off";
            int need = Renown.Threshold(c.R.Arrivals);
            if (c.R.GuaranteedArrival == 0 && c.Renown < need) return $"Odin waits for renown {need} (now {c.Renown})";
            int free = c.FreeBeds().Count - c.R.Coming.Count;
            if (free <= 0) return "there is no free bed for anyone new";
            if (c.Population >= c.Cap) return c.Beds < Tables.Of(c.R.Rank).Cap ? $"the colony has {c.Beds} bed{(c.Beds == 1 ? "" : "s")} for {c.Population} souls" : $"a {Tables.Of(c.R.Rank).Name.ToLower()} holds no more than {Tables.Of(c.R.Rank).Cap}";
            if (c.RaidOn) return "a raid is on";
            return "";
        }
        public static void Dawn(Colony c, int day, bool force = false)
        {
            ColonyRecord r = c.R;
            if (r.Coming.Count > 0 && !r.ComingLanded && r.ComingDay < day) r.Coming.Clear(); // they didn't come (the colony slept through dusk)
            if (r.Coming.Count > 0) return;
            if (!force && Why(c) != "") return;
            var rng = new System.Random(c.Id.GetStableHashCode() ^ day * 7919);
            bool feasted = r.LastFeast == day - 1;
            float chance = Mathf.Clamp01(Plugin.Instance.ArrivalChance.Value + (Favour.ArrivalChance(c.FavourOf("odin"), feasted) - 0.6f));
            if (!force && r.GuaranteedArrival == 0 && rng.NextDouble() > chance) return;
            int free = Math.Max(1, c.FreeBeds().Count);
            r.Coming = Souls.Group(rng, Math.Min(free, c.Cap - c.Population));
            if (r.Coming.Count == 0) return;
            r.ComingDay = day; r.ComingLanded = false; r.GuaranteedArrival = 0;
            c.MarkDirty();
            string count = r.Coming.Count == 1 ? "One of the fallen comes" : r.Coming.Count == 2 ? "Two fallen come" : "Three fallen come";
            Hugin(c, "Odin has seen your hall", $"{count} at dusk. Odin has seen your hall.");
        }
        public static void Dusk(Colony c, int day, bool force = false)
        {
            ColonyRecord r = c.R;
            if (r.Coming.Count == 0 || r.ComingLanded || r.ComingDay != day && !force) return;
            r.ComingLanded = true;
            c.MarkDirty();
            Net.Valkyrie_(c.Position);
            c.Tell($"A Valkyrie sets {(r.Coming.Count == 1 ? r.Coming[0].Name : r.Coming.Count + " souls")} down at the pillars. Give them your word at the high seat.", true);
        }

        public static void Accept(Colony c, int index, string who)
        {
            ColonyRecord r = c.R;
            if (!r.ComingLanded || index < 0 || index >= r.Coming.Count) return;
            Newcomer n = r.Coming[index];
            string vid = Colony.NewId();
            VillagerState s = Souls.Become(n, vid, c.Id, c.Now);
            // Souls who fell in one battle think well of each other.
            foreach (Brain other in Villagers.All.Where(b => b.S.Colony == c.Id && b.S.Group == n.Group)) { other.S.Bonds[vid] = 50; s.Bonds[other.S.Vid] = 50; other.Dirty(); }
            foreach (Newcomer mate in r.Coming.Where(x => x != n && x.Group == n.Group)) s.Flags_Mate(mate.Name);
            if (c.Rooms_Freyja()) Mood.Add(s, "blessed", c.Now);
            Vector3 at = Colony.Spot(c.Position + c.Seat.transform.forward * 3f, 2.5f) ?? c.Position + Vector3.up;
            Humanoid h = Villagers.Spawn(c, s, at, null, dressNew: true);
            if (h == null) return;
            r.Coming.RemoveAt(index);
            if (r.Coming.Count == 0) r.ComingLanded = false;
            r.Arrivals++;
            r.Roster.Add(new RosterRow { Vid = vid, Name = s.FullName, Female = s.Female, Prof = s.Prof, State = SoulState.Active, X = at.x, Y = at.y, Z = at.z, Seen = c.Now });
            Assets.Effect("vfx_spawn", at);
            c.Log("arrive", s.Name, who);
            // A free bed, if there is one.
            Bed bed = c.FreeBeds().OrderBy(b => Vector3.Distance(b.transform.position, at)).FirstOrDefault();
            if (bed != null) s.Bed = Colony.IdOf(bed.GetComponent<ZNetView>());
            ZDO z = h.GetComponent<ZNetView>().GetZDO();
            z.Set(Villagers.StateKey, s.Save());
            if (bed != null) { ZNetView bv = bed.GetComponent<ZNetView>(); if (!bv.IsOwner()) bv.ClaimOwnership(); bv.GetZDO().Set(Villagers.ClaimKey, vid); bv.GetZDO().Set("dhc_home", Colony.Hash(vid)); bv.GetZDO().Set("dhc_homename", s.FullName); }
            c.Rerank();
        }
        public static void Refuse(Colony c, int index, string who)
        {
            ColonyRecord r = c.R;
            if (index < 0 || index >= r.Coming.Count) return;
            Newcomer n = r.Coming[index];
            r.Coming.RemoveAt(index);
            if (r.Coming.Count == 0) r.ComingLanded = false;
            r.Refused++;
            c.Log("refuse", n.Name, who);
            if (r.ComingLanded || r.Coming.Count == 0) Net.Valkyrie_(c.Position);
        }

        // Hugin's word: the game's raven if it can, and the message in the middle of the screen for everyone near.
        public static void Hugin(Colony c, string topic, string text)
        {
            c.Tell("<color=#ffd27a>Hugin:</color> " + text, true);
            try
            {
                if (Player.m_localPlayer == null || Utils.DistanceXZ(Player.m_localPlayer.transform.position, c.Position) > c.Radius + 60) return;
                if (!Raven.IsInstantiated() && Tutorial.instance != null && Tutorial.instance.m_ravenPrefab != null) Object.Instantiate(Tutorial.instance.m_ravenPrefab, Vector3.zero, Quaternion.identity);
                Raven.AddTempText("jarldom_" + c.Id + "_" + c.Day + "_" + topic.GetStableHashCode(), topic, text, "", false);
            }
            catch (Exception e) { Plugin.Trace("Jarldom: no raven: " + e.Message); }
        }
    }

    // The Valkyrie seen at an arrival: the game's own Valkyrie model and animation, flown by us (the game's Valkyrie carries the local player,
    // so a copy without its script and network parts is made for each game that sees it).
    internal static class Valkyrie
    {
        private static GameObject _flying;
        public static void Fly(Vector3 pillars)
        {
            if (Player.m_localPlayer == null || Vector3.Distance(Player.m_localPlayer.transform.position, pillars) > 300) return;
            GameObject prefab = ZNetScene.instance?.GetPrefab("Valkyrie");
            if (prefab == null) return;
            if (_flying != null) Object.Destroy(_flying);
            var holder = new GameObject("JarldomValkyrieHolder"); holder.SetActive(false);
            GameObject v = Object.Instantiate(prefab, holder.transform);
            foreach (var t in new[] { typeof(global::Valkyrie), typeof(ZSyncAnimation), typeof(ZSyncTransform), typeof(Rigidbody) }) { Component comp = v.GetComponent(t); if (comp != null) Object.DestroyImmediate(comp); }
            ZNetView view = v.GetComponent<ZNetView>(); if (view != null) Object.DestroyImmediate(view);
            foreach (Collider col in v.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            v.name = "JarldomValkyrie";
            v.transform.SetParent(null, false);
            Object.Destroy(holder);
            v.AddComponent<Flight>().Begin(pillars);
            _flying = v;
        }
        // In from far and high, down to just above the pillars, a pause, and away.
        private sealed class Flight : MonoBehaviour
        {
            private Vector3 _from, _over, _away; private float _t; private Animator _anim;
            public void Begin(Vector3 pillars)
            {
                Vector3 dir = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0) * Vector3.forward;
                _over = pillars + Vector3.up * 9f;
                _from = pillars - dir * 220f + Vector3.up * 90f;
                _away = pillars + dir * 260f + Vector3.up * 140f;
                transform.position = _from;
                _anim = GetComponentInChildren<Animator>();
                gameObject.SetActive(true);
            }
            private void Update()
            {
                _t += Time.deltaTime;
                Vector3 from = transform.position, to;
                if (_t < 14f) to = Vector3.Lerp(_from, _over, Smooth(_t / 14f));
                else if (_t < 18f) { to = _over + Vector3.up * Mathf.Sin((_t - 14f) * 2f) * 0.6f; if (_anim != null) _anim.SetBool("dropped", true); }
                else if (_t < 30f) to = Vector3.Lerp(_over, _away, Smooth((_t - 18f) / 12f));
                else { Destroy(gameObject); return; }
                Vector3 d = to - from; d.y *= 0.3f;
                if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), Time.deltaTime * 3f);
                transform.position = to;
            }
            private static float Smooth(float x) => x * x * (3 - 2 * x);
        }
        public static void Clear() { if (_flying != null) Object.Destroy(_flying); _flying = null; }
    }

    internal static class SoulHelpers
    {
        public static void Flags_Mate(this VillagerState s, string name) => s.Counts["mate:" + name] = 1;
        public static bool Rooms_Freyja(this Colony c) => c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "freyja") && c.FavourOf("freyja") > 0;
    }
}
