using UnityEngine;

namespace BobsAnimations
{
    // For other mods, found by reflection (Jarldom's convention: the type "BobsAnimations.Api"; game and BCL types only). Version goes up
    // when a method is added; a method's signature never changes.
    public static class Api
    {
        public const int Version = 1;

        // Plays a clip on a character from its start. False when there's no such clip, or this game doesn't run the character (its owner
        // starts and stops its clips; every game near it plays them).
        public static bool Play(Character c, string clip) => Anim.Play(c, clip);
        // Lets the clip playing on a character blend out (how a looping clip ends). False when nothing plays or another game runs it.
        public static bool Stop(Character c) => Anim.Stop(c);
        // The clip playing on a character, or "" (any game's view).
        public static string Playing(Character c) => Anim.Playing(c) ?? "";
        // Whether this game has a clip of that name.
        public static bool Has(string clip) => Clip.Get(clip) != null;
        // Its length in seconds, or 0 when there's no such clip.
        public static float Length(string clip) => Clip.Get(clip)?.Length ?? 0f;
    }
}
