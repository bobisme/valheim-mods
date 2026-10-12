using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // What a chest holds, chosen at the chest: with the hammer's "Mark rooms and zones" chosen, a colony chest's hover says what it's
    // for, and Shift+E (instead of opening it) asks: the treasury, a kind of stores, or nothing of the colony's.
    internal static class ChestPicker
    {
        private static RectTransform _window; private static Colony _c; private static Container _chest;
        public static bool IsOpen => _window != null;
        public static void Close() { if (_window != null) Object.Destroy(_window.gameObject); _window = null; }
        public static void Tick()
        {
            if (_window == null) return;
            if (Player.m_localPlayer == null || global::Menu.IsVisible() || _c == null || !_c.Valid || _chest == null) Close();
        }
        // The colony this chest is a store of (and its id and what it holds now), or why not.
        public static bool Of(Container chest, out Colony c, out string id, out string category)
        {
            id = category = ""; c = Colony.Nearest(chest.transform.position, Renown.ZoneReach + 20);
            if (c == null) return false;
            foreach (var x in Stock.Chests(c)) if (x.chest == chest) { id = x.id; category = x.category; return true; }
            return false;
        }
        public static string Name(string category) =>
            category == "treasury" ? "the treasury" : category == "any" ? "stores of anything" : category == "" ? "not a store" : category + " stores";
        public static void Open(Container chest)
        {
            if (!GameUi.Ready()) return;
            if (!Of(chest, out Colony c, out string id, out string now))
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, c == null ? "Not in a colony." : chest.m_privacy == Container.PrivacySetting.Private ? "A private chest isn't the colony's." : "That isn't one of the colony's chests.");
                return;
            }
            if (id.Length == 0) id = Colony.IdOf(chest.GetComponent<ZNetView>());
            _c = c; _chest = chest;
            Close();
            const float w = 640, h = 330;
            _window = GameUi.Window("JarldomChestPicker", new Vector2(w, h));
            GameUi.Label(_window, "What is this chest for?", new Vector2(30, -18), new Vector2(w - 60, 44), 30, GameUi.Warm, GameUi.Norse);
            GameUi.Label(_window, "Now: " + Name(now), new Vector2(30, -58), new Vector2(w - 60, 24), 16, GameUi.Dim);
            GameUi.Braid(_window, new Vector2(0, -86), w - 60);
            float x = 30, y = 100;
            void Choice(string text, string tip, bool chosen, System.Action act)
            {
                if (x + 140 > w - 20) { x = 30; y += 46; }
                var b = GameUi.Button(_window, chosen ? "• " + text : text, new Vector2(x, -y), new Vector2(136, 40), () => { act(); Close(); }, true, 15);
                if (tip != null) GameUi.Tip(b, text, tip);
                x += 144;
            }
            string chestId = id;
            Choice("Treasury", "Wages, feasts, ransoms, tribute and hired swords are paid from it; coins brought in go there. One chest: choosing it here moves the treasury.", now == "treasury", () => Net.Act(_c, "treasury", chestId));
            foreach (string cat in Stock.Categories)
            {
                string k = cat;
                Choice(cat == "any" ? "anything" : cat, cat == "any" ? "Whatever has no stockpile of its own kind." : null, now == cat && _c.R.Stockpiles.ContainsKey(chestId), () => Net.Act(_c, "stockpile", chestId + "|" + k));
            }
            Choice("nothing", "Not the colony's stores: villagers leave it alone unless it stands in a stockpile room.", now == "" || now == "any" && !_c.R.Stockpiles.ContainsKey(chestId), () => Net.Act(_c, "stockpile", chestId + "|"));
            GameUi.Button(_window, "Cancel", new Vector2(w - 180, -h + 64), new Vector2(150, 42), Close, true, 16);
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact_Designate
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold || !alt || character != Player.m_localPlayer || !Designate.Active) return true;
            ChestPicker.Open(__instance); __result = true; return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class Container_GetHoverText_Designate
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (!Designate.Active || !ChestPicker.Of(__instance, out _, out _, out string cat)) return;
            __result += Localization.instance.Localize($"\n<color=#ffd27a>Colony: {ChestPicker.Name(cat)}</color>\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Choose what it's for");
        }
    }
}
