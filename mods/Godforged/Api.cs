using System.Linq;
using UnityEngine;

namespace Godforged
{
    // For other mods (Fimbulwinter: Godforged pieces as Ragnarök and trial rewards), found by reflection: the type "Godforged.Api" in this
    // assembly (see Jarldom's Compat/API-CONVENTION.md). Rules: bump Version when a method is added; never change a method's signature.
    public static class Api
    {
        public const int Version=2;

        // Every relic's prefab name: "GF_Mjolnir", "GF_Jarngreipr", "GF_Gungnir"...
        public static string[] Relics()=>Things.All.Select(d=>d.Prefab).ToArray();
        // The relic an item is ("GF_Mjolnir"...), or "" if it isn't one.
        public static string Relic(ItemDrop.ItemData item)=>Things.Of(item)?.Prefab??"";
        // A relic's item prefab, to give or drop (null before the world's prefabs are made, or for an unknown name).
        public static GameObject Prefab(string relic)=>Things.Find(relic)?.Built;
        // (2) Draupnir dripped for a player (their id) this many coins: Fimbulwinter's Glory.
        public static void OnDrip(System.Action<long,int> handler){if(handler!=null)Draupnir.OnDrip.Add(handler);}
        // (2) Gjallarhorn was blown by a player (their id) at a place, on that player's game: Fimbulwinter's call at Ragnarök.
        public static void OnHorn(System.Action<long,Vector3> handler){if(handler!=null)Gjallarhorn.OnHorn.Add(handler);}
        // Whether relics can be made at the forge. A mod that hands them out itself turns this off; it holds until the game restarts.
        public static void SetCraftable(bool craftable)
        {
            Things.Craftable=craftable;
            foreach(ItemDef d in Things.All)if(d.Recipe!=null)d.Recipe.m_enabled=craftable;
        }
    }
}
