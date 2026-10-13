using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Omens
{
    // For other mods (Jarldom reads and moves the gods' favour through it), found by reflection: typeof "Omens.Api" in this assembly.
    // The ledger lives on the host, so only the host's game can answer.
    // Rules for this class: bump Version when a method is added; never change a method's signature (add a new one).
    public static class Api
    {
        public const int Version = 1;
        // The gods' favour (-5..5), or int.MinValue on a game that isn't the host.
        public static int GetFate() => Director.ApiFate();
        // Move the gods' favour by delta steps, for a reason told in the log; false on a game that isn't the host.
        public static bool AddFate(int delta, string why) => Director.ApiAddFate(delta, why);

        // Version 1 ----
        // Whether this creature is a great stag an omen set loose.
        public static bool IsGreatStag(Character c) => Stag.Is(c);
        // Where the great stags run now: every one in the world on the host; on another game only those loaded near its player.
        public static Vector3[] GreatStags()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null)
            {
                var deer = new List<ZDO>(); int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("Deer", deer, ref index)) { }
                return deer.Where(z => z.GetBool(Stag.Key, false)).Select(z => z.GetPosition()).ToArray();
            }
            return Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && Stag.Is(c)).Select(c => c.transform.position).ToArray();
        }
    }
}
