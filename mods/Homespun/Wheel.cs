using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Homespun
{
    // The game's spinning wheel spins our fibres too: line flax into linen thread, and wool (combed from a lox pelt, or Byre's fleece)
    // into yarn. Raw flax it spins as it always has. And a smelter-like station gives as many as Chain.Yield says for one put in (a
    // line flax two threads, a lox pelt four wool): each one done is queued again as the game queues it, so its stacking and spilling
    // are the game's own.
    internal static class Wheel
    {
        private static readonly List<Smelter.ItemConversion> Added = new List<Smelter.ItemConversion>();
        private static Smelter _wheel; private static float _next;

        // Once a few seconds: the wheel's prefab (and every wheel standing, the first time) knows our fibres.
        internal static void Ensure()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 3;
            Smelter wheel = ZNetScene.instance?.GetPrefab(Names.Wheel)?.GetComponent<Smelter>();
            if (wheel == null || Things.Get(Names.LineFlax) == null) return;
            int can = Chain.At(Names.Wheel).Count(c => (Things.Get(c.From) ?? ZNetScene.instance.GetPrefab(c.From)) != null && (Things.Get(c.To) ?? ZNetScene.instance.GetPrefab(c.To)) != null);
            // (and again once Byre's fleece is there, if it came later, or is a new one: Byre reloaded, the old fleece destroyed)
            if (wheel == _wheel && Added.Count == can && Added.All(c => c.m_from != null && c.m_to != null && wheel.m_conversion.Contains(c))) return;
            _wheel = wheel;
            Added.Clear();
            foreach (Conversion c in Chain.At(Names.Wheel))
            {
                ItemDrop from = Things.Get(c.From)?.GetComponent<ItemDrop>() ?? ZNetScene.instance.GetPrefab(c.From)?.GetComponent<ItemDrop>();
                ItemDrop to = Things.Get(c.To)?.GetComponent<ItemDrop>() ?? ZNetScene.instance.GetPrefab(c.To)?.GetComponent<ItemDrop>();
                if (from == null || to == null) continue; // (Byre's fleece, without Byre)
                Added.Add(new Smelter.ItemConversion { m_from = from, m_to = to });
            }
            // (the wheels standing were made from the prefab before we were here: a hot reload, or a wheel loaded before the items. One
            // search, once per world.)
            foreach (Smelter s in new[] { wheel }.Concat(Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None).Where(s => Utils.GetPrefabName(s.gameObject) == Names.Wheel)))
            {
                s.m_conversion.RemoveAll(c => c == null || c.m_from == null || c.m_to == null || Chain.Of(Names.Wheel, c.m_from.gameObject.name) != null && !Added.Contains(c));
                foreach (var c in Added) if (!s.m_conversion.Contains(c)) s.m_conversion.Add(c);
            }
            Plugin.Log($"The spinning wheel spins {string.Join(", ", Added.Select(c => c.m_from.name))} too");
        }
        // Unloading: the wheel spins only its own again (a hot reload: what's queued of ours spins once the new copy is back; uninstalled, the
        // game spins it to nothing, having no conversion for it).
        internal static void Unload()
        {
            foreach (Smelter s in Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None).Append(_wheel).Where(s => s != null)) s.m_conversion.RemoveAll(c => Added.Contains(c));
            Added.Clear(); _wheel = null;
        }

        // How many one put in gives, at the station it's in (the game's wheel, our brake).
        internal static int Yield(Smelter s, string ore)
        {
            string name = Utils.GetPrefabName(s.gameObject);
            return name == Names.Wheel || Kit.Ours(name) ? Chain.Yield(name, ore) : 1;
        }
    }

    [HarmonyPatch(typeof(Smelter), "QueueProcessed")]
    internal static class MoreThanOne
    {
        private static bool _again;
        private static readonly System.Reflection.MethodInfo Queue = AccessTools.Method(typeof(Smelter), "QueueProcessed");
        private static void Postfix(Smelter __instance, string ore)
        {
            if (_again || __instance == null) return;
            int n = Wheel.Yield(__instance, ore);
            if (n <= 1) return;
            _again = true;
            try { for (int i = 1; i < n; i++) Queue.Invoke(__instance, new object[] { ore }); }
            finally { _again = false; }
        }
    }
}
