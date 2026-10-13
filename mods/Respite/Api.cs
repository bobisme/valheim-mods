using System;
using UnityEngine;

namespace Respite
{
    // For other mods (Omens, Jarldom, Shieldwall), found by reflection: the type "Respite.Api" in this assembly. The game's own raids
    // need nothing: Respite notes, spaces out and musters them itself.
    public static class Api
    {
        public const int Version=1;
        // Why no raid may come near a place now: the land still rests from the last one, or the area has had its fill this week.
        // "" if one may. Asks the host's clock (a copy of it on a player's game).
        public static string Blocked(Vector3 at)=>Plugin.Instance==null?"":Ledger.Blocked(at);
        // A raid that doesn't go through the game's raid system came (or is coming) near a place: it starts the clock there.
        public static void Raided(Vector3 at,string source){if(Plugin.Instance!=null)Ledger.Note(at,source);}
        // Raiders for home gather outside it (past its farthest building, or clear metres if more): told to every player near, with
        // where and how long, then spawn(spot) is called on this game when the warning is up. Returns the spot.
        public static Vector3 Muster(Vector3 home,float clear,string text,Action<Vector3> spawn)=>Respite.Muster.Begin(home,clear,null,text,spawn);
        // The same, gathering on the side of from if the ground there allows (a barrow's warband from the barrow's side).
        public static Vector3 MusterFrom(Vector3 home,float clear,Vector3 from,string text,Action<Vector3> spawn)=>Respite.Muster.Begin(home,clear,from,text,spawn);
        // How long a muster waits, in seconds (0: at once).
        public static float WarningSeconds()=>Plugin.Instance!=null?Plugin.Instance.WarningSeconds.Value:0;
    }
}
