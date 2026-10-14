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
        // Four recorded crowds (CC0, Freesound; Sounds/SOURCES.txt), each a seamless loop: a few talking, a full room, and for a roar a
        // packed bar and a group's walla on top.
        private static readonly string[] Layers = { "hall_small", "hall_big", "hall_bar", "hall_walla" };
        private sealed class Hall { public GameObject Go; public AudioSource[] Src; public float[] Want = new float[4]; public float Gate = 1, NextGate; public string Room; }
        private static readonly Dictionary<string, Hall> Halls = new Dictionary<string, Hall>();
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static bool _loading;
        private static float _next;
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
                if (Clips.Count < Layers.Length) { Load(); return; }
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
            float v = level.Volume * h.Gate;
            h.Want[0] = v * (1 - level.Full); h.Want[1] = v * Mathf.Max(level.Full, level.Roar ? 1 : 0); // (a few talking, giving way to a full room)
            h.Want[2] = level.Roar ? level.Volume : 0; h.Want[3] = level.Roar ? level.Volume * 0.7f : 0;
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
            var (x, z) = Geometry.Centre(room.Xz);
            float reach = 0; for (int i = 0; i + 1 < room.Xz.Count; i += 2) reach = Mathf.Max(reach, new Vector2(room.Xz[i] - x, room.Xz[i + 1] - z).magnitude);
            var go = new GameObject("JarldomHallSound");
            go.transform.position = new Vector3(x, room.Floor + 1.6f, z);
            var h = new Hall { Go = go, Room = room.Id, Src = Layers.Select(l => Source(go, Clips[l], reach)).ToArray() };
            var verb = go.AddComponent<AudioReverbFilter>(); verb.reverbPreset = AudioReverbPreset.Room;
            return h;
        }
        private static AudioSource Source(GameObject go, AudioClip clip, float reach)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = Mathf.Max(4f, reach * 0.8f); s.maxDistance = reach + 22f; s.dopplerLevel = 0; s.spread = 120f; s.priority = 100;
            if (AudioMan.instance != null) s.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            s.time = Random.Range(0f, clip.length * 0.9f);
            return s;
        }
        // The recordings ship inside the mod as .ogg; the game decodes them from a file, so each is written to BepInEx's cache once.
        private static void Load()
        {
            if (_loading || Plugin.Instance == null) return;
            _loading = true;
            Plugin.Instance.StartCoroutine(LoadAll());
        }
        private static System.Collections.IEnumerator LoadAll()
        {
            string dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, "jarldom-sounds");
            System.IO.Directory.CreateDirectory(dir);
            foreach (string name in Layers)
            {
                string path = System.IO.Path.Combine(dir, name + ".ogg");
                using (var raw = typeof(HallSound).Assembly.GetManifestResourceStream("Jarldom.sound." + name + ".ogg"))
                using (var file = System.IO.File.Create(path)) raw.CopyTo(file);
                using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) { Plugin.Warn("Jarldom: hall sound " + name + ": " + req.error); continue; }
                    AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                    clip.name = "jarldom_" + name;
                    Assets.Owned.Add(clip);
                    Clips[name] = clip;
                }
            }
        }
        private static void Fade(Hall h)
        {
            if (h.Go == null) return;
            for (int i = 0; i < h.Src.Length; i++) Step(h.Src[i], h.Want[i]);
        }
        private static void Step(AudioSource s, float want)
        {
            if (s == null) return;
            s.volume = Mathf.MoveTowards(s.volume, want, Time.deltaTime * 0.3f);
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && want <= 0 && s.isPlaying) s.Pause();
        }
        private static void Drop(string id)
        {
            if (Halls.TryGetValue(id, out Hall h) && h.Go != null) Object.Destroy(h.Go);
            Halls.Remove(id);
        }
        public static string Sources() => string.Join("; ", Halls.Values.Where(h => h.Go != null).Select(h => $"at {h.Go.transform.position:F0}, reach {h.Src[0].minDistance:0}-{h.Src[0].maxDistance:0} m: " +
            string.Join(", ", h.Src.Select((src, i) => $"{Layers[i]} {src.volume:0.00}{(src.isPlaying ? " playing" : "")}")))) + $" (clips {Clips.Count}/{Layers.Length})";
        public static void Forget() { foreach (string id in Halls.Keys.ToList()) Drop(id); }
    }
}
