using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Shieldwall
{
    // A Warstone draws raids to itself: a base raid the host starts near one becomes a siege at the stone instead, with a longer
    // warning. Only the host decides raids, so this needs Shieldwall on the host (a vanilla dedicated server raids as usual).
    [HarmonyPatch(typeof(RandEventSystem),"SetRandomEvent")]
    internal static class DrawRaids
    {
        private static readonly List<ZDO> Found=new List<ZDO>();
        private static bool Prefix(RandomEvent ev,Vector3 pos)
        {
            if(ev==null||Plugin.Instance==null||!Plugin.Instance.DrawRaids.Value||ZNet.instance==null||!ZNet.instance.IsServer())return true;
            if(!ev.m_random||!ev.m_nearBaseOnly||ev.m_spawn==null||ev.m_spawn.Count==0)return true; // a base raid, not a storm or a boss
            Found.Clear();int index=0;
            while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Stone.PrefabName,Found,ref index)){}
            ZDO best=null;float bestDistance=ev.m_eventRange+60;
            foreach(ZDO z in Found)
            {
                float d=Utils.DistanceXZ(z.GetPosition(),pos);
                if(d<bestDistance&&z.GetInt(Stone.PhaseKey,0)==(int)Phase.Idle){best=z;bestDistance=d;}
            }
            if(best==null||!Net.CallSaved(best,Cause.Raid))return true;
            Plugin.Log($"Raid {ev.m_name} drawn to the Warstone at {best.GetPosition():F0}");
            return false;
        }
    }
}
