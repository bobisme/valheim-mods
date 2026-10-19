using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Byre's sheep and goats in a pasture (through Byre.Api, version 2 and on; nothing here without it): the herder shears a sheep in full
    // fleece and milks a goat whose milk is in, with the shears or pail from the stores, and carries the wool and milk to the stores (the
    // wool for the weaver, the milk for the cook and the larder).
    internal static class Herding
    {
        private static readonly ApiRef Byre = new ApiRef("Byre.Api", "cx.bob.byre");
        public const string Shears = "ByreShears", Pail = "ByreMilkPail";
        public static bool Present => Byre.Version >= 2;
        // (the shears and pail are one-handed weapons to the game: never fought with, nor taken for a spare weapon)
        public static bool IsTool(string prefab) => prefab == Shears || prefab == Pail;

        private static Func<GameObject, string> KindFn => Byre.Bind<Func<GameObject, string>>("KindOf", 2);
        private static Func<GameObject, bool> TameFn => Byre.Bind<Func<GameObject, bool>>("IsTame", 1);
        private static Func<GameObject, bool> ReadyFn => Byre.Bind<Func<GameObject, bool>>("IsReady", 1);
        private static Func<string, string> ToolFn => Byre.Bind<Func<string, string>>("Tool", 2);
        private static Func<string, string> ProductFn => Byre.Bind<Func<string, string>>("Product", 2);
        private static Func<GameObject, bool> HarvestFn => Byre.Bind<Func<GameObject, bool>>("Harvest", 2);

        public static string KindOf(GameObject go) => Present && go != null ? KindFn?.Invoke(go) : null;
        // A grown, tame one of Byre's with its wool or milk in (IsReady is false for the young).
        public static bool Ready(GameObject go) => KindOf(go) != null && (TameFn?.Invoke(go) ?? false) && (ReadyFn?.Invoke(go) ?? false);
        public static string Tool(string kind) => kind != null ? ToolFn?.Invoke(kind) : null;
        public static string Product(string kind) => kind != null ? ProductFn?.Invoke(kind) : null;
        public static bool Harvest(GameObject go) => go != null && (HarvestFn?.Invoke(go) ?? false);
        public static string Verb(string kind) => kind == "sheep" ? "shearing" : "milking";

        // The pasture's work for the herder: one order for each animal ready, while the stores or a herder has its tool; a few at a time.
        public static void Post(Colony c, Zone z, IEnumerable<Character> beasts, double now, Func<ZNetView, string> key)
        {
            if (!Present) return;
            int open = c.Board.All.Count(o => o.Kind == "tend" && o.Zone == z.Id && (o.State == OrderState.Open || o.State == OrderState.Claimed));
            foreach (Character beast in beasts)
            {
                if (open >= 4) return;
                ZNetView v = beast.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !Ready(beast.gameObject)) continue;
                string tool = Tool(KindOf(beast.gameObject));
                if (tool == null || !Has(c, tool)) continue;
                Vector3 p = beast.transform.position;
                c.Board.Post("tend", key(v), Prof.Herder, 3, p.x, p.y, p.z, now, z.Id, "Pasture");
                open++;
            }
        }
        // The tool in the stores, or in a herder's bag.
        public static bool Has(Colony c, string tool) => Work.HaveInStock(c, tool) || Villagers.All.Any(v => v.Alive && v.S.Colony == c.Id && v.S.Prof == Prof.Herder && v.Body.GetInventory().GetAllItems().Any(i => Classes.Prefab(i) == tool));
        // What the pasture wants for its animals, for the idle line ("" when nothing's missing).
        public static string Missing(Colony c)
        {
            if (!Present) return "";
            var kinds = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && Utils.DistanceXZ(ch.transform.position, c.Position) < c.Radius && (TameFn?.Invoke(ch.gameObject) ?? false)).Select(ch => KindOf(ch.gameObject)).Where(k => k != null).Distinct().ToList();
            var lack = kinds.Select(Tool).Where(t => t != null && !Has(c, t)).Select(t => t == Shears ? "shears" : "a milk pail").ToList();
            return lack.Count == 0 ? "" : "no " + string.Join(" or ", lack) + " in the stores for the herd (Byre's: shears at the forge, the pail at the workbench)";
        }
    }
}
