using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Larder
{
    // Spoilage in the game (the rules are Core/Spoilage.cs), only while the server's setting says so: off by default, and with it off
    // nothing here touches an item.
    //
    // A food's age is kept on the item itself (its custom data: carried into chests, onto the ground and through saves with it), worked
    // out only when someone looks: a chest when it's opened (its owner's game, the one that saves it), the player's own bag once a minute.
    // Never a timer over every chest in the world. The time since it was last worked out counts at the rate where it lies now.
    // Spoiled, a stack becomes as much spoiled food in the same cell (never quietly gone).
    internal static class Spoiling
    {
        private const string Used = "larder.used", At = "larder.at";
        private static float _nextBag;

        internal static bool On => Plugin.Instance != null && Plugin.Instance.Spoilage.Value;
        private static float Speed => Plugin.Instance != null ? Mathf.Max(0f, Plugin.Instance.SpoilSpeed.Value) : 1f;
        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;

        // The game's food: eaten for health or stamina (meads and potions aren't; they're eaten for what they do).
        internal static Kind KindOf(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null || item.m_dropPrefab == null) return Kind.Never;
            bool food = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0);
            return Spoilage.KindOf(item.m_dropPrefab.name, food);
        }

        // Its life used so far, as stored (new to us: fresh, from now).
        private static float UsedOf(ItemDrop.ItemData item, out double at)
        {
            at = -1;
            if (item.m_customData == null || !item.m_customData.TryGetValue(At, out string a) || !double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out at)) return 0;
            return item.m_customData.TryGetValue(Used, out string u) && float.TryParse(u, NumberStyles.Float, CultureInfo.InvariantCulture, out float used) ? used : 0;
        }
        private static void Stamp(ItemDrop.ItemData item, float used, double at)
        {
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            item.m_customData[Used] = used.ToString("0.#", CultureInfo.InvariantCulture);
            item.m_customData[At] = at.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // Ages every food in an inventory by the time since each was last looked at, at this rate; turns the spoiled ones to spoiled
        // food. Returns how many stacks spoiled.
        internal static int Age(Inventory inv, Kept where, bool cold)
        {
            if (!On || inv == null || ZNet.instance == null) return 0;
            double now = Now; float rate = Spoilage.Rate(where, cold) * Speed;
            var spoiled = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                Kind k = KindOf(item);
                if (k == Kind.Never) continue;
                float used = UsedOf(item, out double at);
                if (at >= 0) used = Spoilage.Age(used, now - at, rate);
                Stamp(item, used, now);
                if (Spoilage.IsSpoiled(k, used)) spoiled.Add(item);
            }
            GameObject rot = Things.Get(Spoilage.Spoiled);
            if (rot == null) return 0;
            foreach (ItemDrop.ItemData item in spoiled)
            {
                Vector2i cell = item.m_gridPos; int n = item.m_stack;
                inv.RemoveItem(item);
                if (inv.AddItem(Spoilage.Spoiled, n, 1, 0, 0L, "", cell, false) == null) inv.AddItem(rot, n); // (its cell taken meanwhile: anywhere)
            }
            if (spoiled.Count > 0) Plugin.Log($"{spoiled.Count} stack(s) of food spoiled ({where}{(cold ? ", cold" : "")}).");
            return spoiled.Count;
        }

        // Where a chest stands: a root cellar, under a roof, or out in the open; cold in the Mountains and the Deep North.
        internal static Kept Where(Vector3 at) => Cellar.Is(at) ? Kept.Cellar : Cover.IsUnderRoof(at + Vector3.up * 0.5f) ? Kept.Roofed : Kept.Open;
        internal static bool Cold(Vector3 at)
        {
            if (WorldGenerator.instance == null) return false;
            Heightmap.Biome b = WorldGenerator.instance.GetBiome(at);
            return b == Heightmap.Biome.Mountain || b == Heightmap.Biome.DeepNorth;
        }

        // Once a minute: the player's own bag (it's carried: warm, the full rate).
        internal static void Tick()
        {
            if (!On || Time.time < _nextBag) return;
            _nextBag = Time.time + 60;
            if (Player.m_localPlayer is Player p) Age(p.GetInventory(), Kept.Bag, false);
        }

        // (testing) as if it had lain this much longer: its last look moved back (a first look stamps it now, so a new one is stamped first).
        internal static void BackDate(ItemDrop.ItemData item, double seconds)
        {
            float used = UsedOf(item, out double at);
            Stamp(item, used, (at >= 0 ? at : Now) - seconds);
        }

        internal static float DaysLeft(ItemDrop.ItemData item)
        {
            Kind k = KindOf(item);
            if (k == Kind.Never) return float.PositiveInfinity;
            float used = UsedOf(item, out double at);
            if (at >= 0 && ZNet.instance != null) used = Spoilage.Age(used, Now - at, Speed);
            return Spoilage.DaysLeft(k, used);
        }

        // How fresh it is now, for its tooltip (as stored, plus the time since at the carried rate; not saved).
        internal static string Freshness(ItemDrop.ItemData item)
        {
            Kind k = KindOf(item);
            if (k == Kind.Never) return null;
            float used = UsedOf(item, out double at);
            if (at >= 0 && ZNet.instance != null) used = Spoilage.Age(used, Now - at, Speed);
            return Spoilage.Freshness(k, used);
        }
    }

    // A root cellar where a chest stands (Spoilage.IsCellar): roofed, and the ground three metres out, eight ways round, a metre or more
    // above its floor on most sides.
    internal static class Cellar
    {
        internal static bool Is(Vector3 at)
        {
            if (!Cover.IsUnderRoof(at + Vector3.up * 0.5f)) return false;
            var ground = new float[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = at + Quaternion.Euler(0, i * 45f, 0) * Vector3.forward * 3f;
                ground[i] = Heightmap.GetHeight(p, out float h) ? h : at.y;
            }
            return Spoilage.IsCellar(at.y, ground, true);
        }
    }

    // A chest opened: its food aged, on the game that now owns it (opening takes ownership; the chest saves what changed).
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class SpoilOnOpen
    {
        private static void Postfix(Container container)
        {
            if (!Spoiling.On || container == null || container.GetInventory() == null) return;
            ZNetView view = container.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner()) return; // (not ours to change: its owner ages it when they look)
            Vector3 at = container.transform.position;
            Plugin.Guard(() => Spoiling.Age(container.GetInventory(), Spoiling.Where(at), Spoiling.Cold(at)));
        }
    }

    // The tooltip says how fresh a food is, while spoilage is on.
    [HarmonyPatch]
    internal static class SpoilTooltip
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(ItemDrop.ItemData).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.Name == "GetTooltip" && m.ReturnType == typeof(string) && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(ItemDrop.ItemData));
        private static void Postfix(object[] __args, ref string __result)
        {
            if (!Spoiling.On || !(__args.FirstOrDefault() is ItemDrop.ItemData item)) return;
            string line = Spoiling.Freshness(item);
            if (line != null) __result += $"\n<color=orange>{line}</color>";
        }
    }
}
