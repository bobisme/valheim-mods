using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // With Quad's SkalTavern: the hall is a tavern. A player's Skål! (its toast key) is answered by the villagers round them, tankards
    // raised; idle villagers in the hall drink its ale, mead and wine from the hall's chests (three a day at most), toast each other, and
    // the merrier ones sway and slur a little. SkalTavern itself is untouched: its toast is watched from outside.
    internal static class Skal
    {
        private static Harmony _harmony; private static BaseUnityPlugin _hooked;
        public static bool Present => Compat.Has("SkalTavern");
        // (once a few seconds: hook the copy of SkalTavern that's loaded now, whichever it is after a reload)
        private static float _nextLook;
        public static void Tick()
        {
            if (Time.time < _nextLook) return;
            _nextLook = Time.time + 5f;
            BaseUnityPlugin skal = Compat.Find(Compat.Known.First(k => k.name == "SkalTavern").guids);
            if (skal == _hooked) return;
            Unhook();
            if (skal == null) return;
            Type toast = skal.GetType().Assembly.GetType("SkalTavern.Toast");
            MethodInfo raise = toast != null ? AccessTools.Method(toast, "Raise") : null, heard = toast != null ? AccessTools.Method(toast, "OnToast") : null;
            if (raise == null) return;
            _harmony = new Harmony("com.bobisme.jarldom.skal");
            _harmony.Patch(raise, postfix: new HarmonyMethod(typeof(Skal), nameof(Raised)));
            if (heard != null) _harmony.Patch(heard, postfix: new HarmonyMethod(typeof(Skal), nameof(Heard)));
            _hooked = skal;
        }
        public static void Unhook() { _harmony?.UnpatchSelf(); _harmony = null; _hooked = null; }
        private static void Raised(Player player) { if (player != null) Plugin.Guard(() => Answer(player.transform.position, player.GetPlayerName())); }
        private static void Heard(Vector3 pos, string name) => Plugin.Guard(() => Answer(pos, name));

        // A toast near them: villagers within 12 m lift their drink and answer.
        private static float _lastAnswer;
        public static void Answer(Vector3 at, string who)
        {
            if (Time.time - _lastAnswer < 2f) return;
            _lastAnswer = Time.time;
            int spoke = 0;
            foreach (Brain b in Villagers.All.Where(b => b.Alive && !b.Fighting && Vector3.Distance(b.Pos, at) < 12f).ToList())
            {
                Body_.Anim(b.Body)?.SetTrigger(b.Body.RightItem != null && Gear.IsTankard(b.Body.RightItem) ? "emote_drink" : "emote_cheer");
                if (spoke++ < 2) b.Say(spoke == 1 ? new[] { $"Skål, {who}!", $"To you, {who}!", $"Skål to {who}!", $"Here's to {who}!", $"Drink to {who}!" }[UnityEngine.Random.Range(0, 5)] : new[] { "Skål!", "Skål, all!", "Another!", "Drink up!", "To the hall!" }[UnityEngine.Random.Range(0, 5)], 3);
                Mood.Add(b.S, "skal", b.Now); Mood.Satisfy(b.S, Need.Social, 6);
                b.Dirty();
            }
        }

        // ---- drinks in the hall ----
        private static readonly Dictionary<string, (int day, int n)> Drunk = new Dictionary<string, (int, int)>();
        public static int Today(Brain b) => Drunk.TryGetValue(b.S.Vid, out var d) && d.day == b.Colony.Day ? d.n : 0;
        // One of SkalTavern's drinks from a chest in the hall, if there is one and they haven't had three today.
        public static bool DrinkFromHall(Brain b)
        {
            if (!Present || b.Colony == null || Today(b) >= 3) return false;
            Room hall = b.Colony.RoomAt(b.Pos, RoomType.GreatHall);
            if (hall == null) return false;
            foreach (var x in Stock.Chests(b.Colony, b.Pos))
            {
                if (x.category == "treasury" || ContainerAccess.InUse(x.chest) || b.Colony.RoomAt(x.chest.transform.position + Vector3.up * 0.3f) != hall) continue;
                ItemDrop.ItemData drink = x.chest.GetInventory().GetAllItems().FirstOrDefault(i => Classes.Prefab(i).StartsWith("dh_drink_"));
                if (drink == null) continue;
                ContainerAccess.TakeOver(x.chest);
                x.chest.GetInventory().RemoveItem(drink, 1);
                Drunk[b.S.Vid] = (b.Colony.Day, Today(b) + 1);
                Mood.Add(b.S, "tavern", b.Now); Mood.Satisfy(b.S, Need.Social, 6);
                b.S.Bump("drinks"); b.Dirty();
                if (Drunk.Count > 200) Drunk.Clear();
                return true;
            }
            return false;
        }
        // The merrier (two drinks or more today): a sway now and then, and a slur.
        public static void Merry(Brain b)
        {
            int n = Today(b);
            if (n < 2 || UnityEngine.Random.value > 0.25f) return;
            b.Body.Stagger(UnityEngine.Random.insideUnitSphere);
            if (UnityEngine.Random.value < 0.4f) b.Say(new[] { "Shkål!", "Who moved the floor?", "Another, and then I'll go.", "I love you all. Even Rognvald.", "The benches are walking away from me.", "Where did the fire go?", "Who put the bench there?", "I can see two of you. Lovely.", "Sing it again, and louder.", "I'm fine. The hall is spinning, that's all." }[UnityEngine.Random.Range(0, 10)], 3);
        }
        // A toast with whoever's near: the others in the hall lift theirs too.
        public static void ToastTogether(Brain b)
        {
            foreach (Brain o in Villagers.All.Where(o => o != b && o.Alive && Vector3.Distance(o.Pos, b.Pos) < 5f).Take(4))
            { Body_.Anim(o.Body)?.SetTrigger(o.Body.RightItem != null && Gear.IsTankard(o.Body.RightItem) ? "emote_drink" : "emote_cheer"); Mood.Satisfy(o.S, Need.Social, 2); }
        }
    }
}
