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
        internal static string Id(Vector3 p) => Math.Round(p.x).ToString(CultureInfo.InvariantCulture)+","+Math.Round(p.z).ToString(CultureInfo.InvariantCulture);
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(!Plugin.Instance.Guiding.Value){st.Entrance=null;return false;}
            if(st.Entrance==null && Time.time>=st.NextGuide)
            {
                st.NextGuide=Time.time+5;

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
                        string id=Id(entrance);
                        if(distance<12||distance>=best||RecentlyShown(st,id))continue;
                        // An available live scan can reject old fully emptied dungeons immediately.
                        if(!DungeonLoot.Eligible(master,id,door.m_targetPoint.transform.position))continue;
                        best=distance;st.Entrance=entrance;st.EntranceId=id;st.GuideInterior=door.m_targetPoint.transform.position;
                    }
                }
                if(st.Entrance!=null)
                {
                    st.GuideUntil=Time.time+120;st.StuckTime=0;st.LastPosition=st.Body.transform.position;
                    if(master==Player.m_localPlayer)Plugin.Tell("I smell a cave or crypt nearby. This way!");
                }
            }
            if(st.Entrance==null)return false;
            if(Time.time>=st.NextGuide)
            {
                st.NextGuide=Time.time+5;
                if(!DungeonLoot.Eligible(master,st.EntranceId,st.GuideInterior)){st.Entrance=null;return false;}
            }
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
        private static bool RecentlyShown(Companion.State st,string id)
        {
            // A timed cooldown replaces the old permanent shown-entrance blacklist.
            foreach(string entry in Companion.Data(st.Body).GetString("bob_gary_shown_v2","").Split(';'))
            {
                string[] parts=entry.Split('|');
                if(parts.Length==2&&parts[0]==id&&long.TryParse(parts[1],out long at)&&Companion.Now>=at&&Companion.Now-at<TimeSpan.TicksPerMinute*10)return true;
            }
            return false;
        }
        private static void Remember(Companion.State st)
        {
            ZDO z=Companion.Data(st.Body);var recent=new List<string>();
            foreach(string entry in z.GetString("bob_gary_shown_v2","").Split(';'))
            {
                string[] p=entry.Split('|');
                if(p.Length==2&&LootPolicy.ValidId(p[0])&&p[0]!=st.EntranceId&&long.TryParse(p[1],out long at)&&Companion.Now>=at&&Companion.Now-at<TimeSpan.TicksPerMinute*10)recent.Add(entry);
            }
            recent.Add(st.EntranceId+"|"+Companion.Now.ToString(CultureInfo.InvariantCulture));
            while(recent.Count>32)recent.RemoveAt(0);
            z.Set("bob_gary_shown_v2",string.Join(";",recent));
        }
    }
}
