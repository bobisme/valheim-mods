using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Villagers at work move like it, with Bob's Animations (its Api, found by reflection: without it each falls back to what it was).
    // On their own, every half second: a fisherman at the dock holds the rod out between casts; anyone walking with an armful of wood,
    // stone or ore for the stores carries it in both arms. Called from the work itself: a cast or a reel-in at each try for a fish, a
    // crouch to sow or to pull a crop, a hand over the eyes at a watch post, the skald's drumming or reciting. (The clips end by
    // themselves when the villager walks off: Bob's Animations sees them move.)
    internal static class WorkEmotes
    {
        private static readonly ApiRef Anims = new ApiRef("BobsAnimations.Api", "com.bobisme.animations");
        private sealed class Doer { public string Loop; public float LoopUntil; }
        private static readonly Dictionary<Brain, Doer> Doing = new Dictionary<Brain, Doer>();
        private static float _next;

        private static Func<Character, string, bool> PlayFn => Anims.Bind<Func<Character, string, bool>>("Play", 1);
        private static Func<Character, string, string, bool> PlayWithFn => Anims.Bind<Func<Character, string, string, bool>>("PlayWith", 2);
        private static Func<Character, bool> StopFn => Anims.Bind<Func<Character, bool>>("Stop", 1);
        private static Func<Character, string> PlayingFn => Anims.Bind<Func<Character, string>>("Playing", 1);
        private static bool Play(Brain b, string clip) { var f = PlayFn; return f != null && b?.Body != null && f(b.Body, clip); }
        private static string Playing(Brain b) { var f = PlayingFn; return f != null && b?.Body != null ? f(b.Body) : ""; }
        private static void Stop(Brain b) { var f = StopFn; if (f != null && b?.Body != null) f(b.Body); }

        // ---- called from the work: true when a clip plays (else the caller does what it did before) ----
        // A try for a fish: the reel-in when one bit, else a cast; the hold between tries comes from Tick.
        public static bool Fishing(Brain b, bool caught) => Play(b, caught ? "stand_fish_reel" : "stand_fish_cast");
        public static bool Sow(Brain b) => Play(b, "stand_sow");
        public static bool Harvest(Brain b) => Play(b, "stand_harvest");
        // At a watch post, now and then (the caller waits about 5 s for it).
        public static bool Watch(Brain b) => UnityEngine.Random.value < 0.6f && Play(b, "stand_watch");
        // The skald: with a drum, a while of drumming (looped, stopped by Tick); without, a recitation.
        public static bool Sing(Brain b, bool drum)
        {
            if (!Play(b, drum ? "stand_drum" : "stand_recite")) return false;
            if (drum) Get(b).LoopUntil = Time.time + UnityEngine.Random.Range(6f, 10f);
            Get(b).Loop = drum ? "stand_drum" : null;
            return true;
        }

        private static Doer Get(Brain b) { if (!Doing.TryGetValue(b, out Doer d)) Doing[b] = d = new Doer(); return d; }

        // ---- on their own ----
        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (!Anims.Present) { Doing.Clear(); return; }
            foreach (Brain b in Villagers.All.ToList())
            {
                if (b.Held != null) { Doing.Remove(b); continue; } // (sitting: SeatedEmotes)
                Doer d = Get(b);
                string now = Playing(b);
                // A loop held long enough, or one whose reason is gone, ends.
                if (d.Loop != null && now == d.Loop && (d.LoopUntil > 0 && Time.time > d.LoopUntil || !Still(b, d.Loop)))
                {
                    Stop(b); d.Loop = null; d.LoopUntil = 0; continue;
                }
                if (!string.IsNullOrEmpty(now)) continue;
                // The fisherman between tries: the rod held out.
                if (Fishing(b) && !b.Move.Moving) { if (Play(b, "stand_fish")) { d.Loop = "stand_fish"; d.LoopUntil = 0; } continue; }
                // An armful for the stores, carried while walking.
                string load = b.Move.Moving ? Armful(b) : null;
                if (load != null && PlayWithFn != null && PlayWithFn(b.Body, "walk_carry", load)) { d.Loop = "walk_carry"; d.LoopUntil = 0; }
            }
            foreach (Brain gone in Doing.Keys.Where(k => k == null || !k.Alive).ToList()) Doing.Remove(gone);
        }
        // Whether a loop still has its reason.
        private static bool Still(Brain b, string loop) =>
            loop == "stand_fish" ? Fishing(b) && !b.Move.Moving :
            loop == "walk_carry" ? b.Move.Moving && Armful(b) != null :
            true;
        private static bool Fishing(Brain b) => b.Task is WorkTask w && w.Kind == "fish" && b.Doing == "fishing";
        // The bulkiest thing they carry for the stores, if it's something carried in the arms (wood, stone, ore): its prefab.
        private static string Armful(Brain b)
        {
            ItemDrop.ItemData best = null;
            foreach (ItemDrop.ItemData i in b.Body.GetInventory().GetAllItems())
            {
                if (i.m_dropPrefab == null || !Bulky(i.m_dropPrefab.name) || !Work.Stores(b, i)) continue;
                if (best == null || i.m_stack * i.m_shared.m_weight > best.m_stack * best.m_shared.m_weight) best = i;
            }
            return best?.m_dropPrefab.name;
        }
        private static bool Bulky(string prefab) =>
            prefab.EndsWith("Wood", StringComparison.Ordinal) || prefab == "RoundLog" || prefab.Contains("Log") || prefab == "Stone" || prefab.EndsWith("Ore", StringComparison.Ordinal)
            || prefab.EndsWith("Scrap", StringComparison.Ordinal) || prefab == "Flint" || prefab == "Obsidian" || prefab == "Grausten" || prefab == "BlackMarble";

        public static void Forget() => Doing.Clear();
    }
}
