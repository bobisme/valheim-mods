using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // On each game, for the colony near this player: the great hall murmurs with talk as it fills (Soundscape.HallLevel), quieter and
    // broken up when they're low, a roar with laughter on a feast or festival night, silent when the colony is miserable. Worked out here
    // from what every game already sees (the villagers' bodies, their mood on their ZDOs, the record); nothing sent.
    internal static class HallSound
    {
        private sealed class Hall { public GameObject Go; public AudioSource Talk, Roar; public float Gate = 1, NextGate, Want, WantRoar; public string Room; }
        private static readonly Dictionary<string, Hall> Halls = new Dictionary<string, Hall>();
        private static AudioClip _talk, _roar;
        private static float _next;
        public static bool Muted = true;
        public static string Last = ""; // (for jarldom hall sound)
        public static float? Forced; public static bool ForceRoar; public static double? ForcedMood; // (jarldom hallsound <volume> | roar | mood <n>: on this game only)

        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            foreach (Hall h in Halls.Values) Fade(h); // (every frame: smooth)
            if (me == null || Time.time < _next) return;
            _next = Time.time + 2f;
            Vector3 at = me.transform.position;
            var near = new HashSet<string>();
            foreach (Colony c in Colony.All.ToList())
            {
                if (Utils.DistanceXZ(c.Position, at) > c.Radius + 60) continue;
                Room room = c.Best(RoomType.GreatHall);
                if (room == null || !room.Ok) continue;
                near.Add(c.Id);
                if (!Halls.TryGetValue(c.Id, out Hall h) || h.Go == null || h.Room != room.Id) { Drop(c.Id); h = Halls[c.Id] = Make(c, room); }
                Hear(c, room, h);
            }
            foreach (string id in Halls.Keys.Where(k => !near.Contains(k)).ToList()) Drop(id);
        }

        private static void Hear(Colony c, Room room, Hall h)
        {
            var mine = Character.GetAllCharacters().Where(x => x != null && !x.IsDead() && VillagerPrefab.Is(x)).Select(x => (body: x, z: x.GetComponent<ZNetView>()?.GetZDO()))
                .Where(x => x.z != null && x.z.GetString(Villagers.ColonyKey, "") == c.Id).ToList();
            var inHall = mine.Where(x => c.RoomAt(x.body.transform.position, RoomType.GreatHall) == room).ToList();
            double mood = ForcedMood ?? (mine.Count == 0 ? 0 : mine.Average(x => (double)x.z.GetInt(Villagers.MoodKey, 0)));
            double hour = c.Hour; int day = hour < 4 ? c.Day - 1 : c.Day; // (the night after a feast runs past midnight)
            bool night = hour >= Schedule.HallAt || hour < 4;
            bool feasting = night && (c.R.LastFeast == day || c.R.Flag("fest:day") == day && c.R.Flag("fest:power") > 0) || ForceRoar;
            var level = Soundscape.HallLevel(inHall.Count, c.Population, mood, feasting);
            if (Forced is float f) { level.Volume = f; level.Sparse = 0; }
            // Broken up when they're low: now and then the talk dies down for a while.
            if (Time.time >= h.NextGate) { h.Gate = Random.value < level.Sparse * 0.6f ? 0.2f : 1f; h.NextGate = Time.time + Random.Range(1.5f, 4f); }
            h.Want = Muted ? 0 : level.Volume * h.Gate; h.WantRoar = Muted || !level.Roar ? 0 : level.Volume; // (muted: the made-up babble sounded wrong; real recordings coming)
            if (inHall.Count > 0 && level.LaughsPerMinute > 0 && Random.value < level.LaughsPerMinute / 30f)
                Laugh(inHall[Random.Range(0, inHall.Count)].body.transform.position + Vector3.up * 1.6f, level.Roar);
            Last = $"{c.R.Name}: {inHall.Count} in the hall of {c.Population}, mood {mood:0}, {(feasting ? "feasting" : night ? "evening" : "day")}: volume {level.Volume:0.00}, sparse {level.Sparse:0.00}, laughs {level.LaughsPerMinute:0.0}/min{(Soundscape.Miserable(mood) ? " (miserable: the hall is quiet)" : "")}";
        }

        // A laugh in the hall: Haldor's, pitched to sound like someone else's, softer.
        private static void Laugh(Vector3 at, bool roar)
        {
            GameObject sfx = Assets.Find("sfx_haldor_laugh");
            if (sfx == null) return;
            GameObject go = Object.Instantiate(sfx, at, Quaternion.identity);
            ZSFX z = go.GetComponent<ZSFX>();
            if (z != null) { z.m_minPitch = 0.85f; z.m_maxPitch = 1.25f; z.m_minVol = z.m_maxVol = roar ? 0.55f : 0.35f; }
        }

        private static Hall Make(Colony c, Room room)
        {
            if (_talk == null) _talk = Clip("jarldom_hall_talk", 1);
            if (_roar == null) _roar = Clip("jarldom_hall_roar", 2);
            var (x, z) = Geometry.Centre(room.Xz);
            float reach = 0; for (int i = 0; i + 1 < room.Xz.Count; i += 2) reach = Mathf.Max(reach, new Vector2(room.Xz[i] - x, room.Xz[i + 1] - z).magnitude);
            var go = new GameObject("JarldomHallSound");
            go.transform.position = new Vector3(x, room.Floor + 1.6f, z);
            var h = new Hall { Go = go, Room = room.Id, Talk = Source(go, _talk, reach, 1f), Roar = Source(go, _roar, reach, 1.04f) };
            var verb = go.AddComponent<AudioReverbFilter>(); verb.reverbPreset = AudioReverbPreset.Room;
            return h;
        }
        private static AudioSource Source(GameObject go, AudioClip clip, float reach, float pitch)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0; s.pitch = pitch; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = Mathf.Max(4f, reach * 0.8f); s.maxDistance = reach + 22f; s.dopplerLevel = 0; s.spread = 120f; s.priority = 100;
            if (AudioMan.instance != null) s.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            s.time = Random.Range(0f, clip.length * 0.9f);
            return s;
        }
        private static AudioClip Clip(string name, int seed)
        {
            float[] data = Soundscape.Babble(seed);
            int read = 0; // (filled through the reader: SetData's span overload can't be called from this framework)
            var clip = AudioClip.Create(name, data.Length, 1, 22050, false, buffer =>
            {
                int n = Mathf.Min(buffer.Length, data.Length - read);
                System.Array.Copy(data, read, buffer, 0, n);
                if (n < buffer.Length) System.Array.Clear(buffer, n, buffer.Length - n);
                read += n;
            });
            Assets.Owned.Add(clip);
            return clip;
        }
        private static void Fade(Hall h)
        {
            if (h.Go == null) return;
            Step(h.Talk, h.Want); Step(h.Roar, h.WantRoar);
        }
        private static void Step(AudioSource s, float want)
        {
            if (s == null) return;
            s.volume = Mathf.MoveTowards(s.volume, want, Time.deltaTime * 0.4f);
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && want <= 0 && s.isPlaying) s.Pause();
        }
        private static void Drop(string id)
        {
            if (Halls.TryGetValue(id, out Hall h) && h.Go != null) Object.Destroy(h.Go);
            Halls.Remove(id);
        }
        public static string Sources() => string.Join("; ", Halls.Values.Where(h => h.Go != null).Select(h => $"at {h.Go.transform.position:F0}: talk {h.Talk.volume:0.00}{(h.Talk.isPlaying ? " playing" : "")}, roar {h.Roar.volume:0.00}{(h.Roar.isPlaying ? " playing" : "")}, reach {h.Talk.minDistance:0}-{h.Talk.maxDistance:0} m"));
        public static void Forget() { foreach (string id in Halls.Keys.ToList()) Drop(id); }
    }
}
