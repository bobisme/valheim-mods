using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Villagers sitting about move as they sit, with Bob's Animations (its Api, found by reflection: nothing without it). The game that runs
    // a villager picks now and then from what's about them: at a tafl table they move pieces and think; in the hall they drink, toast and
    // talk; beside someone seated they talk or point toward them; alone they think, fold their arms or lean on their knees. When a seated
    // villager says something, they talk with their hands toward whoever sits nearest ("Skål!" raises the tankard). The clips end by
    // themselves when the villager gets up (Bob's Animations sees the seat go).
    internal static class SeatedEmotes
    {
        private static readonly ApiRef Anims = new ApiRef("BobsAnimations.Api", "com.bobisme.animations");
        private static readonly string[] Seats = { "attach_chair", "attach_throne" };
        private sealed class Sitter { public float Next, LoopUntil; public long Said; }
        private static readonly Dictionary<Brain, Sitter> Sitting = new Dictionary<Brain, Sitter>();
        private static float _next;

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (!Anims.Present) { if (Sitting.Count > 0) Sitting.Clear(); return; }
            Func<Character, string, bool> play = Anims.Bind<Func<Character, string, bool>>("Play", 1);
            Func<Character, bool> stop = Anims.Bind<Func<Character, bool>>("Stop", 1);
            Func<Character, string> playing = Anims.Bind<Func<Character, string>>("Playing", 1);
            if (play == null || stop == null || playing == null) return;
            foreach (Brain b in Villagers.All.ToList())
            {
                bool seated = b.Held != null && Seats.Contains(b.Held.Anim) && !(b.Task is EatTask); // (eating has the game's own eat)
                if (!seated) { Sitting.Remove(b); continue; }
                if (!Sitting.TryGetValue(b, out Sitter s))
                {
                    Sitting[b] = s = new Sitter { Next = Time.time + UnityEngine.Random.Range(3f, 8f), Said = Said(b) };
                    continue;
                }
                string now = playing(b.Body);
                // A loop held long enough ends; a new line said starts the hands talking (if nothing else is going).
                if (s.LoopUntil > 0 && Time.time > s.LoopUntil) { s.LoopUntil = 0; stop(b.Body); s.Next = Time.time + UnityEngine.Random.Range(2f, 6f); continue; }
                long said = Said(b);
                if (said != s.Said)
                {
                    s.Said = said;
                    if (string.IsNullOrEmpty(now) || s.LoopUntil > 0)
                    {
                        string text = Tags(b).GetString(Talk.SayKey, "").Split('\u001f')[0];
                        Brain near = Neighbour(b);
                        string clip = text.IndexOf("Skål", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Skal!", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "sit_toast_" + Side(b, near) : "sit_talk_" + Side(b, near);
                        if (s.LoopUntil > 0) { s.LoopUntil = 0; stop(b.Body); }
                        if (play(b.Body, clip)) { s.Next = Time.time + UnityEngine.Random.Range(6f, 12f); continue; }
                    }
                }
                if (!string.IsNullOrEmpty(now) || Time.time < s.Next) continue;
                string pick = Pick(b, out bool loop);
                if (pick != null && play(b.Body, pick))
                {
                    s.LoopUntil = loop ? Time.time + UnityEngine.Random.Range(8f, 16f) : 0;
                    s.Next = Time.time + (b.Task is LeisureTask l && l.Kind == "tafl" ? UnityEngine.Random.Range(4f, 8f) : UnityEngine.Random.Range(8f, 18f));
                }
                else s.Next = Time.time + 5f;
            }
            foreach (Brain gone in Sitting.Keys.Where(k => k == null || !k.Alive).ToList()) Sitting.Remove(gone);
        }

        // What to do now, from what's about them.
        private static string Pick(Brain b, out bool loop)
        {
            loop = false;
            Brain near = Neighbour(b);
            string side = Side(b, near);
            var choices = new List<(string clip, float weight)>();
            if (b.Task is LeisureTask l && l.Kind == "tafl" && l.Partner != null)
                choices.AddRange(new[] { ("sit_board_move", 6f), ("sit_think", 3f), ("sit_elbows", 1f), ("sit_laugh", 0.5f) });
            else if (b.Task is HallTask)
            {
                choices.AddRange(new[] { ("sit_drink", 4f), ("sit_toast_" + side, near != null ? 2f : 0.5f), ("sit_laugh", 1.5f), ("sit_cheer", 0.5f), ("sit_fold_arms", 1f) });
                if (near != null) choices.AddRange(new[] { ("sit_talk_" + side, 3f), ("sit_point_" + side, 0.7f) });
            }
            else if (near != null)
                choices.AddRange(new[] { ("sit_talk_" + side, 4f), ("sit_point_" + side, 1f), ("sit_laugh", 1.5f), ("sit_think", 1f), ("sit_fold_arms", 1.5f), ("sit_elbows", 1f) });
            else
                choices.AddRange(new[] { ("sit_think", 2f), ("sit_fold_arms", 3f), ("sit_elbows", 3f) });
            float total = choices.Sum(c => c.weight), r = UnityEngine.Random.value * total;
            foreach (var (clip, weight) in choices)
            {
                if ((r -= weight) > 0) continue;
                loop = clip == "sit_fold_arms" || clip == "sit_elbows";
                return clip;
            }
            return null;
        }

        // The nearest other villager sitting within 3 m, and which side of them they are.
        private static Brain Neighbour(Brain b) =>
            Villagers.All.Where(o => o != b && o.Held != null && Seats.Contains(o.Held.Anim) && Vector3.Distance(o.Pos, b.Pos) < 3f)
                .OrderBy(o => Vector3.Distance(o.Pos, b.Pos)).FirstOrDefault();
        private static string Side(Brain b, Brain near)
        {
            if (near == null) return UnityEngine.Random.value < 0.5f ? "left" : "right";
            return b.Body.transform.InverseTransformPoint(near.Pos).x >= 0 ? "right" : "left";
        }
        private static ZDO Tags(Brain b) => Villagers.Tags(b.View.GetZDO());
        private static long Said(Brain b) => Tags(b).GetLong(Talk.SayTimeKey, 0L);

        public static void Forget() => Sitting.Clear();
    }
}
