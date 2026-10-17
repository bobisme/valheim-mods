using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
        private sealed class Hall
        {
            public GameObject Go; public AudioSource[] Src; public float[] Want = new float[4]; public float Gate = 1, NextGate; public string Room;
            // The skald's song: its own source (and filter), loud inside, muffled outside; Sung when it should be heard now, at SongAt.
            public Room Of; public float Reach; public AudioSource Song; public AudioLowPassFilter Muffle; public string Sung; public double SongAt; public float SongSince;
        }
        private static readonly Dictionary<string, Hall> Halls = new Dictionary<string, Hall>();
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static bool _loading, _loaded;
        private static float _next;
        public static string Last = ""; // (for jarldom hall sound)
        public static float? Forced; public static bool ForceRoar; public static double? ForcedMood;
        public static int? ForcedSong; public static float ForcedSongAt; public static bool? ForcedFemale; // (jarldom hallsound song <0|1|off> [from s] [m|f]: sung now, whoever's there)
        private static readonly Dictionary<string, bool> Singing = new Dictionary<string, bool>();
        // The skald's singing a song of Bob's in this colony's hall now, as this game hears it (the skald's verses wait for it).
        public static bool SongOn(string colony) => Singing.TryGetValue(colony, out bool on) && on; // (jarldom hallsound <volume> | roar | mood <n>: on this game only)

        // How far the game's own music is hushed for the skald's song (0 not at all, 1 silent): by how loud the song is where this player
        // stands, so it's gone in the hall and comes back as they walk away.
        public static float Hush;
        [HarmonyPatch(typeof(MusicMan), "Update")]
        private static class Quiet
        {
            private static void Postfix(AudioSource ___m_musicSource) { if (Hush > 0.001f && ___m_musicSource != null) ___m_musicSource.volume *= 1f - Hush; }
        }

        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            Hush = 0;
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
                if (!_loaded) { Load(); return; }
                if (Layers.Any(l => !Clips.ContainsKey(l))) return; // (a crowd recording that wouldn't load: no hall sound at all, as before)
                near.Add(c.Id);
                if (!Halls.TryGetValue(c.Id, out Hall h) || h.Go == null || h.Room != room.Id) { Drop(c.Id); h = Halls[c.Id] = Make(c, room); }
                Hear(c, room, h);
            }
            foreach (string id in Halls.Keys.Where(k => !near.Contains(k)).ToList()) Drop(id);
            foreach (string id in Singing.Keys.Where(k => !near.Contains(k)).ToList()) Singing.Remove(id);
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
            // The skald's song: a skald in the hall with folk to hear them, in the evening's hour for it, sung in the skald's own voice
            // (the first skald by vid, if there are two: the same one on every game).
            RosterRow skald = inHall.Select(x => c.R.Roster.FirstOrDefault(r => r.Vid == x.z.GetString(Villagers.VidKey, ""))).Where(r => r != null && r.Prof == Prof.Skald).OrderBy(r => r.Vid, System.StringComparer.Ordinal).FirstOrDefault();
            int song = ForcedSong ?? Soundscape.SongOf(day, feasting);
            string voiced = Soundscape.Voiced(song, ForcedFemale ?? skald?.Female ?? false);
            float length = Clips.TryGetValue(voiced, out AudioClip sc) && sc.loadState == AudioDataLoadState.Loaded ? sc.length : 0;
            double songAt = ForcedSong != null ? Time.time - ForcedSongAt : Soundscape.SongAt(hour < 4 ? hour + 24 : hour, Clock.Hour, length);
            bool sung = songAt >= 0 && songAt < length && h.Song != null && (ForcedSong != null || Soundscape.Sung(skald != null, inHall.Count, mood));
            Song(h, sung ? voiced : null, songAt);
            Singing[c.Id] = sung;
            float v = level.Volume * h.Gate * (sung ? 0.4f : 1f); // (the talk drops while he sings)
            h.Want[0] = v * (1 - level.Full); h.Want[1] = v * Mathf.Max(level.Full, level.Roar ? 1 : 0); // (a few talking, giving way to a full room)
            h.Want[2] = level.Roar ? level.Volume : 0; h.Want[3] = level.Roar ? level.Volume * 0.7f : 0;
            if (inHall.Count > 0 && level.LaughsPerMinute > 0 && Random.value < level.LaughsPerMinute / 30f)
                Laugh(inHall[Random.Range(0, inHall.Count)].body.transform.position + Vector3.up * 1.6f, level.Roar);
            Last = $"{c.R.Name}: {inHall.Count} in the hall of {c.Population}, mood {mood:0}, {(feasting ? "feasting" : night ? "evening" : "day")}: volume {level.Volume:0.00}, sparse {level.Sparse:0.00}, laughs {level.LaughsPerMinute:0.0}/min{(Soundscape.Miserable(mood) ? " (miserable: the hall is quiet)" : "")}; "
                + $"song {voiced} at {songAt:0}s: {(sung ? $"sung by {skald?.Name ?? "(forced)"}" : skald == null ? "no skald in the hall" : inHall.Count < Soundscape.SongMinCrowd ? "too few to hear" : songAt < 0 ? "not its hour" : "not sung")}";
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
            // (the song on its own child: the muffling is its alone, the talk's as it was)
            var sg = new GameObject("JarldomHallSong"); sg.transform.SetParent(go.transform, false);
            h.Of = room; h.Reach = reach;
            h.Song = sg.AddComponent<AudioSource>();
            h.Song.loop = false; h.Song.volume = 0; h.Song.spatialBlend = 0.7f; h.Song.dopplerLevel = 0; h.Song.spread = 160f; h.Song.priority = 60;
            h.Song.rolloffMode = AudioRolloffMode.Custom; h.Song.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1)); h.Song.maxDistance = reach + Soundscape.SongFade + 10; // (how loud is worked out below, inside or out)
            if (AudioMan.instance != null) h.Song.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            h.Muffle = sg.AddComponent<AudioLowPassFilter>(); h.Muffle.cutoffFrequency = 22000f;
            var sverb = sg.AddComponent<AudioReverbFilter>(); sverb.reverbPreset = AudioReverbPreset.Livingroom;
            return h;
        }
        // Which song, and where in it: started (or set right, if it's drifted) there, so every game is at the same line.
        private static void Song(Hall h, string song, double at)
        {
            h.Sung = song;
            if (song == null || h.Song == null) return;
            h.SongAt = at; h.SongSince = Time.time;
            if (!Clips.TryGetValue(song, out AudioClip clip)) return;
            if (h.Song.clip != clip) { h.Song.Stop(); h.Song.clip = clip; }
            if (!h.Song.isPlaying || Mathf.Abs(h.Song.time - (float)at) > 1.5f) { h.Song.time = Mathf.Clamp((float)at, 0, clip.length - 0.1f); if (!h.Song.isPlaying) h.Song.Play(); }
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
            foreach (string name in Layers.Concat(Soundscape.AllVoices))
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
            _loaded = true; // (whatever failed stays failed: not tried every two seconds)
        }
        private static void Fade(Hall h)
        {
            if (h.Go == null) return;
            for (int i = 0; i < h.Src.Length; i++) Step(h.Src[i], h.Want[i]);
            if (h.Song == null) return;
            // (every frame, where this player stands: in the hall, the song; outside, through the walls, quieter and duller with distance)
            float want = 0, cutoff = 22000f;
            if (h.Sung != null && Player.m_localPlayer is Player me && h.Of != null)
            {
                Vector3 p = me.transform.position;
                bool inside = Geometry.Inside(h.Of.Xz, p.x, p.z) && p.y > h.Of.Floor - 1.5f && p.y < h.Of.Floor + h.Of.Height + 1.5f;
                float outside = Mathf.Max(0, Utils.DistanceXZ(p, h.Go.transform.position) - h.Reach);
                (want, cutoff) = Soundscape.SongHeard(inside, outside);
            }
            h.Song.volume = Mathf.MoveTowards(h.Song.volume, want, Time.deltaTime * (want > h.Song.volume ? 0.6f : 0.4f));
            h.Muffle.cutoffFrequency = Mathf.Exp(Mathf.MoveTowards(Mathf.Log(h.Muffle.cutoffFrequency), Mathf.Log(cutoff), Time.deltaTime * 4f)); // (in octaves: a door's step smooth)
            if (h.Sung == null && h.Song.volume <= 0.001f && h.Song.isPlaying) h.Song.Pause();
            if (h.Song.isPlaying) Hush = Mathf.Max(Hush, Mathf.Clamp01(h.Song.volume * 2f)); // (just outside the door, nearly silent too)
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
            string.Join(", ", h.Src.Select((src, i) => $"{Layers[i]} {src.volume:0.00}{(src.isPlaying ? " playing" : "")}"))
            + (h.Song != null ? $"; game music hushed {Hush:0.00}; song {h.Sung ?? "none"} {h.Song.volume:0.00}{(h.Song.isPlaying ? $" playing at {h.Song.time:0}s" : "")}, low-pass {h.Muffle.cutoffFrequency:0} Hz" : ""))) + $" (clips {Clips.Count}/{Layers.Length + Soundscape.AllVoices.Count()})";
        public static void Forget() { foreach (string id in Halls.Keys.ToList()) Drop(id); Hush = 0; }
    }
}
