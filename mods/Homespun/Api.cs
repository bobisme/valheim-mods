using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Homespun
{
    // For other mods (Jarldom's weaver, Byre, Shipyard), found by reflection: the type "Homespun.Api" in this assembly. Small in 0.1.0:
    // the chain's names and what each step takes and gives; the stations' own state is the game's (a smelter's queue, a crafting
    // station's recipes), so another mod works them as a player does.
    public static class Api
    {
        public const int Version = 3; // (2: dyeing; 3: what clothes do, sails, the stations near a place, queueing at one)
        // The prefab names of Homespun's items and stations.
        public static string[] Items => Things.AllItems.ToArray();
        public static string[] Stations => Kit.All.Select(d => d.Name).ToArray();
        public static string[] Garments => Chain.Garments.Concat(Things.DyedGarments).Select(g => g.Prefab).ToArray(); // (2: and every dyed colour of them)
        // Can this item be spun into yarn (combed wool, or Byre's fleece)?
        public static bool IsWool(string prefab) => prefab == Names.Wool || prefab == Names.Fleece;
        // What one of `from` put in at `station` gives (prefab and how many), or null if that station doesn't take it.
        public static (string to, int n)? Gives(string station, string from) => Chain.Of(station, from) is Conversion c ? (c.To, c.Yield) : ((string, int)?)null;
        // Dyeing (2): the colours ("Blue", "Red", "Yellow", "Purple"); a thing's undyed prefab and its colour (null: undyed), or null if it
        // can't be dyed; what it becomes dyed a colour (null: it can't be, or is already); the dyestuff for a colour and how many a measure.
        public static string[] Dyes => Dye.Dyes.Select(d => d.Name).ToArray();
        public static (string undyed, string dye)? Dyed(string prefab) => Dye.Read(prefab) is var r && r != null ? (r.Value.undyed, r.Value.dye?.Name) : ((string, string)?)null;
        public static string DyedAs(string prefab, string dye) => Dye.Into(prefab, Dye.Of(dye));
        public static (string stuff, int perMeasure)? Dyestuff(string dye) => Dye.Of(dye) is Dyestuff d ? (d.Stuff, d.PerMeasure) : ((string, int)?)null;
        public static int Measures(string prefab) => Dye.Measures(prefab);
        // What a thing comes to in what's gathered (flax, fleece or lox pelts, berries...).
        public static Dictionary<string, double> Raw(string item, bool fleece = true) => Chain.Raw(item, fleece);

        // (3) A thing's colour ("Blue"...; null: undyed or not dyeable). (What it becomes dyed a colour: DyedAs.)
        public static string ColourOf(string prefab) => Dye.Read(prefab)?.dye?.Name;
        // (3) A dye's colour as it looks on wool (red, green, blue, 0..1); null for an unknown dye.
        public static (float r, float g, float b)? Rgb(string dye) => Dye.Of(dye)?.Colour;
        // (3) What clothes do: warm (keeps out the cold, the mountains' too: frost resistant), hooded (keeps off the rain), mittens (keep
        // off a chill, not freezing), fine (a dyed garment of ours: two worn give comfort). Wool: a garment of wool, any colour.
        public static bool IsWarm(string prefab) => Wear.Warm(prefab);
        public static bool IsHooded(string prefab) => Wear.Hooded(prefab);
        public static bool IsMittens(string prefab) => Wear.Mittens(prefab);
        public static bool IsFine(string prefab) => Wear.Fine(prefab);
        public static bool IsWoolGarment(string prefab) => Wear.Of(prefab) is Garment g && g.Prefab != Names.Undertunic;
        // (3) Sails: the undyed wool sail and every colour of it; is this one; its colour (null: undyed).
        public static string Sail => Names.Sail;
        public static string[] Sails => new[] { Names.Sail }.Concat(Dye.Dyes.Select(d => Dye.Variant(Names.Sail, d))).ToArray();
        public static bool IsSail(string prefab) => Wear.Sail(prefab);
        // (3) Homespun's stations (and the game's spinning wheel) within reach of a place: each one's prefab and where it stands.
        public static (string prefab, Vector3 at)[] StationsNear(Vector3 at, float radius)
        {
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(at, radius, pieces);
            return pieces.Where(p => p != null && (Kit.Ours(Utils.GetPrefabName(p.gameObject)) || Utils.GetPrefabName(p.gameObject) == Names.Wheel))
                .Select(p => (Utils.GetPrefabName(p.gameObject), p.transform.position)).ToArray();
        }
        // (3) Queueing at a station that works through a queue (the pool, the brake, the wheel), as a player putting things in does: of
        // `n` of `item`, how many it took (up to what it holds; 0 if it doesn't take that, or isn't one within a metre of `stationAt`).
        // The caller takes that many out of wherever they were.
        public static int Queue(Vector3 stationAt, string item, int n)
        {
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(stationAt, 1f, pieces);
            Smelter smelter = pieces.Select(p => p != null ? p.GetComponent<Smelter>() : null).FirstOrDefault(x => x != null);
            ZNetView view = smelter != null ? smelter.GetComponent<ZNetView>() : null;
            if (smelter == null || view == null || !view.IsValid() || !smelter.m_conversion.Any(c => c.m_from != null && c.m_from.gameObject.name == item)) return 0;
            int queued = (int)AccessTools.Method(typeof(Smelter), "GetQueueSize").Invoke(smelter, null);
            int take = Mathf.Clamp(smelter.m_maxOre - queued, 0, n);
            for (int i = 0; i < take; i++) view.InvokeRPC("RPC_AddOre", item, false); // (name, cheated)
            return take;
        }
    }
}
