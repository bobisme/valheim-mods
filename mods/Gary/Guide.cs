using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Guide
    {
        private static List<Location> Locations() => AccessTools.StaticFieldRefAccess<List<Location>>(typeof(Location),"s_allLocations");
        private static string Id(Vector3 p) => Math.Round(p.x).ToString(CultureInfo.InvariantCulture)+","+Math.Round(p.z).ToString(CultureInfo.InvariantCulture);
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(!Plugin.Instance.Guiding.Value){st.Entrance=null;return false;}
            if(st.Entrance==null && Time.time>=st.NextGuide)
            {
                st.NextGuide=Time.time+5;
                var seen=new HashSet<string>(Companion.Data(st.Body).GetString(Companion.Seen,"").Split(';'));
                float best=Plugin.Instance.GuideRange.Value;
                foreach(Location location in Locations())
                {
                    if(location==null||!location.isActiveAndEnabled||!location.m_hasInterior)continue;
                    string name=location.gameObject.name;
                    if(!name.StartsWith("Crypt",StringComparison.Ordinal)&&!name.StartsWith("SunkenCrypt",StringComparison.Ordinal)&&
                        !name.StartsWith("TrollCave",StringComparison.Ordinal)&&!name.StartsWith("MountainCave",StringComparison.Ordinal))continue;
                    if(Vector3.Distance(location.transform.position,master.transform.position)>best+30)continue;
                    foreach(Teleport door in location.GetComponentsInChildren<Teleport>(true))
                    {
                        if(!door.gameObject.activeInHierarchy||door.m_targetPoint==null||door.m_targetPoint.transform.position.y-door.transform.position.y<1000)continue;
                        Vector3 entrance=door.transform.position;
                        float distance=Vector3.Distance(entrance,master.transform.position);
                        if(distance<12||distance>=best||seen.Contains(Id(entrance)))continue;
                        best=distance;st.Entrance=entrance;st.EntranceId=Id(entrance);
                    }
                }
                if(st.Entrance!=null)
                {
                    st.GuideUntil=Time.time+120;st.StuckTime=0;st.LastPosition=st.Body.transform.position;
                    if(master==Player.m_localPlayer)Plugin.Tell("I smell a cave or crypt nearby. This way!");
                }
            }
            if(st.Entrance==null)return false;
            Vector3 destination=st.Entrance.Value;
            if(Time.time>st.GuideUntil || Vector3.Distance(destination,master.transform.position)>Plugin.Instance.GuideRange.Value+30)
            {st.Entrance=null;st.NextGuide=Time.time+60;return false;}
            if(Vector3.Distance(destination,master.transform.position)<12)
            {
                Remember(st);st.Entrance=null;st.NextGuide=Time.time+60;
                if(master==Player.m_localPlayer)Plugin.Tell("Here it is! I'll stay outside while you explore.");return false;
            }
            Brain.Status(st,"showing you a nearby entrance");
            if(Policy.GuideWait(Vector3.Distance(st.Body.transform.position,master.transform.position)))
            {ai.StopMoving();Brain.Status(st,"waiting for you to catch up");return true;}
            Vector3 toward=destination-master.transform.position;toward.y=0;
            Vector3 waypoint=master.transform.position+Vector3.ClampMagnitude(toward,10);
            if(!ZoneSystem.instance.GetGroundHeight(waypoint,out float y)){st.Entrance=null;st.NextGuide=Time.time+60;return false;}
            waypoint.y=y;
            if(Utils.DistanceXZ(st.Body.transform.position,waypoint)<2)
            {ai.StopMoving();st.StuckTime=0;Brain.Status(st,"waiting for you to catch up");return true;}
            Brain.Move(ai,dt,waypoint,1.5f,false);
            if(Vector3.Distance(st.Body.transform.position,st.LastPosition)<0.1f)st.StuckTime+=dt;
            else {st.StuckTime=0;st.LastPosition=st.Body.transform.position;}
            if(st.StuckTime>15){st.Entrance=null;st.NextGuide=Time.time+60;return false;}
            return true;
        }
        private static void Remember(Companion.State st)
        {
            ZDO z=Companion.Data(st.Body);var seen=new List<string>(z.GetString(Companion.Seen,"").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries));
            if(!seen.Contains(st.EntranceId))seen.Add(st.EntranceId);
            while(seen.Count>32)seen.RemoveAt(0);
            z.Set(Companion.Seen,string.Join(";",seen));
        }
    }
}
