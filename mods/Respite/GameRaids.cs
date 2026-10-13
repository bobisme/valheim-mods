using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Respite
{
    // The game's own raids (and the mods' raids that start one: Omens, Jarldom's Fulings) on the same clock, and mustered.
    internal static class GameRaids
    {
        // A base raid with creatures to send, not a storm or a boss's weather.
        internal static bool Raidlike(RandomEvent ev)=>ev!=null&&ev.m_random&&ev.m_nearBaseOnly&&ev.m_spawn!=null&&ev.m_spawn.Count>0;
        private static readonly AccessTools.FieldRef<RandEventSystem,RandomEvent> Current=AccessTools.FieldRefAccess<RandEventSystem,RandomEvent>("m_randomEvent");

        // Every raid that starts is noted (one a Warstone draws to itself too: Shieldwall turns it into a siege, which is a raid all the
        // same). It lasts the muster longer, so the warning doesn't eat the fight.
        [HarmonyPatch(typeof(RandEventSystem),"SetRandomEvent")]
        private static class Started
        {
            private static void Postfix(RandEventSystem __instance,RandomEvent ev,Vector3 pos)
            {
                if(Plugin.Instance==null||!Raidlike(ev)||ZNet.instance==null||!ZNet.instance.IsServer())return;
                Ledger.Note(pos,ev.m_name);
                RandomEvent now=Current(__instance);
                if(Plugin.Instance.GameRaids.Value&&now!=null&&now.m_name==ev.m_name&&now.m_pos==pos)now.m_duration+=Plugin.Instance.WarningSeconds.Value;
            }
        }

        // The game's timer picks no base still resting from its last raid.
        [HarmonyPatch(typeof(RandEventSystem),"GetValidEventPoints")]
        private static class Resting
        {
            private static float _nextLog;
            private static void Postfix(RandomEvent ev,List<Vector3> __result)
            {
                if(Plugin.Instance==null||!Raidlike(ev)||__result==null)return;
                int before=__result.Count;
                string why="";
                __result.RemoveAll(p=>{string w=Ledger.Blocked(p);if(w!="")why=w;return w!="";});
                if(__result.Count<before&&Time.time>=_nextLog){_nextLog=Time.time+60;Plugin.Log($"Respite: {ev.m_name} kept from {before-__result.Count} place(s): {why}");}
            }
        }

        // While they gather the raid sends no one; after, its creatures come from the gathering spot (or at least from well outside).
        private static readonly Dictionary<List<SpawnSystem.SpawnData>,List<SpawnSystem.SpawnData>> Made=new Dictionary<List<SpawnSystem.SpawnData>,List<SpawnSystem.SpawnData>>();
        private static readonly HashSet<SpawnSystem.SpawnData> Ours=new HashSet<SpawnSystem.SpawnData>();
        [HarmonyPatch(typeof(RandEventSystem),nameof(RandEventSystem.GetCurrentSpawners))]
        [HarmonyPriority(Priority.Last)] // (after Jarldom makes a colony's raid bigger)
        private static class Held
        {
            private static void Postfix(RandEventSystem __instance,ref List<SpawnSystem.SpawnData> __result)
            {
                if(__result==null||Plugin.Instance==null||!Plugin.Instance.GameRaids.Value)return;
                RandomEvent ev=__instance.GetActiveEvent();
                if(!Raidlike(ev))return;
                if(ev.m_time<Plugin.Instance.WarningSeconds.Value){__result=null;return;}
                if(!Made.TryGetValue(__result,out var list))
                {
                    if(Made.Count>8){Made.Clear();Ours.Clear();}
                    list=__result.Select(s=>{var c=s.Clone();c.m_spawnRadiusMin=Mathf.Max(s.m_spawnRadiusMin,70);c.m_spawnRadiusMax=Mathf.Max(s.m_spawnRadiusMax,110);return c;}).ToList();
                    Made[__result]=list;foreach(var c in list)Ours.Add(c);
                }
                __result=list;
            }
        }
        private delegate bool Good(SpawnSystem system,SpawnSystem.SpawnData spawn,ref Vector3 point);
        private static readonly Good IsGood=AccessTools.MethodDelegate<Good>(AccessTools.Method(typeof(SpawnSystem),"IsSpawnPointGood"));
        [HarmonyPatch(typeof(SpawnSystem),"FindBaseSpawnPoint")]
        private static class FromTheSpot
        {
            private static bool Prefix(SpawnSystem __instance,SpawnSystem.SpawnData spawn,List<Player> allPlayers,ref Vector3 spawnCenter,ref Player targetPlayer,ref bool __result)
            {
                if(Muster.GameSpot==null||!Ours.Contains(spawn)||allPlayers==null||allPlayers.Count==0)return true;
                Vector3 spot=Muster.GameSpot.Value;
                for(int i=0;i<10;i++)
                {
                    Vector2 r=Random.insideUnitCircle*12;
                    Vector3 p=new Vector3(spot.x+r.x,spot.y,spot.z+r.y);
                    if(!IsGood(__instance,spawn,ref p))continue;
                    spawnCenter=p;targetPlayer=allPlayers.OrderBy(pl=>Vector3.Distance(pl.transform.position,p)).First();__result=true;
                    return false;
                }
                return true; // (no ground there fits this creature: the game's own way, from well outside)
            }
        }
    }
}
