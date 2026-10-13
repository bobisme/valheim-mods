using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Work songs in the world: three or more at the same work within 15 m keep a rhythm (a bit quicker for it), and one of them leads
    // a chant now and then.
    internal static class Songs
    {
        private static readonly Dictionary<string, float> NextLine = new Dictionary<string, float>(); // (by colony and work)
        private static readonly System.Random Rng = new System.Random();
        public static void Tick(Brain b)
        {
            if (!(b.Task is WorkTask w) || b.Colony == null) return;
            var crew = Villagers.All.Where(o => o.S.Colony == b.S.Colony && o.Task is WorkTask ow && ow.Kind == w.Kind && Vector3.Distance(o.Pos, b.Pos) < WorkSongs.Reach).ToList();
            double mood = crew.Average(o => (double)Mood.Of(o.S, o.Now));
            if (!WorkSongs.Sing(crew.Count, mood)) return;
            b.SongUntil = Time.time + 4f; // (in the rhythm: a little quicker)
            string key = b.S.Colony + "|" + w.Kind;
            if (NextLine.TryGetValue(key, out float next) && Time.time < next) return;
            if (crew.Min(o => o.S.Vid) != b.S.Vid) return; // (one leads the chant)
            NextLine[key] = Time.time + WorkSongs.Every + Random.Range(-4f, 6f);
            Brain singer = crew[Rng.Next(crew.Count)];
            singer.Say(Greetings.Show(WorkSongs.Pick(w.Kind, Rng)), 5f);
        }
        public static void Forget() => NextLine.Clear();
    }
}
