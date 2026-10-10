using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeadowGolf
{
    internal static class CourseLabels
    {
        internal const string Pending="bob_golf_label_pending";
        private const string RequestRpc="bob_golf_autolabel_v1";
        private static ZRoutedRpc Rpc;private static object Handler;
        private static readonly HashSet<ZDOID> Known=new HashSet<ZDOID>();
        private static readonly HashSet<ZDOID> Legacy=new HashSet<ZDOID>();
        private static readonly HashSet<ZDOID> Requests=new HashSet<ZDOID>();
        private static int Generation;
        internal static void Remember(ZDO z,bool indexed=false)
        {if(z!=null&&ZNet.instance!=null&&ZNet.instance.IsServer()){Known.Add(z.m_uid);if(indexed&&z.Persistent&&!z.GetBool(Pending,false)&&z.GetString(GolfWorld.LabelKey,"").Length==0)Legacy.Add(z.m_uid);}}
        internal static void Tick()
        {
            if(Rpc==ZRoutedRpc.instance)return;StopRpc();Rpc=ZRoutedRpc.instance;
            if(Rpc==null)return;Rpc.Register<ZDOID>(RequestRpc,Receive);
            Handler=((IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(Rpc))[RequestRpc.GetStableHashCode()];
        }
        internal static void Clear(){Generation++;Known.Clear();Legacy.Clear();Requests.Clear();}
        internal static void Stop(){Clear();StopRpc();}
        private static void StopRpc()
        {
            if(Rpc!=null){var table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(Rpc);if(ReferenceEquals(table[RequestRpc.GetStableHashCode()],Handler))table.Remove(RequestRpc.GetStableHashCode());}
            Rpc=null;Handler=null;
        }
        internal static void Request(GolfMarker marker)
        {
            Player player=Player.m_localPlayer;ZDO z=marker?.View?.GetZDO();
            if(player==null||z==null||!marker.View.IsOwner()||z.GetLong(ZDOVars.s_creator,0)!=player.GetPlayerID()||z.GetString(GolfWorld.LabelKey,"").Length>0)return;
            z.Set(Pending,true);marker.WatchLabel();Tick();Rpc?.InvokeRoutedRPC(RequestRpc,z.m_uid);
        }
        private static void Receive(long sender,ZDOID id)
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||id==ZDOID.None||Requests.Count>=64||Requests.Contains(id))return;
            if(GolfWorld.Actor(sender)==null)return;
            Requests.Add(id);Plugin.Instance.StartCoroutine(Assign(sender,id,Generation));
        }
        private static IEnumerator Assign(long sender,ZDOID id,int generation)
        {
            try
            {
                float deadline=Time.unscaledTime+20;ZDO z;
                while((!GolfWorld.Ready||(z=ZDOMan.instance?.GetZDO(id))==null)&&generation==Generation&&Time.unscaledTime<deadline)yield return null;
                if(generation!=Generation)yield break;
                z=ZDOMan.instance?.GetZDO(id);ZDO actor=GolfWorld.Actor(sender);
                if(z==null||actor==null||!GolfWorld.Ready)yield break;
                bool cup=z.GetPrefab()==Prefabs.Cup.GetStableHashCode();
                if((!cup&&z.GetPrefab()!=Prefabs.Tee.GetStableHashCode())||z.GetLong(ZDOVars.s_creator,0)!=actor.GetLong(ZDOVars.s_playerID,0)||
                    Vector3.Distance(actor.GetPosition(),z.GetPosition())>15||z.GetString(GolfWorld.LabelKey,"").Length>0)yield break;
                var markers=new List<Rules.CourseMarker>();var dead=new List<ZDOID>();
                foreach(ZDOID key in Known)
                {
                    ZDO other=ZDOMan.instance.GetZDO(key);if(other==null){dead.Add(key);continue;}if(key==id)continue;
                    if(Requests.Contains(key)||other.GetString(GolfWorld.LabelKey,"").Length==0&&(other.GetBool(Pending,false)||!Legacy.Contains(key))||!Rules.Parse(other.GetString(GolfWorld.LabelKey,"Meadow:1:3"),out var hole)||!string.Equals(hole.Course,"Meadow",StringComparison.OrdinalIgnoreCase))continue;
                    Vector3 p=other.GetPosition();markers.Add(new Rules.CourseMarker{Hole=hole.Number,Par=hole.Par,Cup=other.GetPrefab()==Prefabs.Cup.GetStableHashCode(),X=p.x,Y=p.y,Z=p.z});
                }
                foreach(ZDOID key in dead){Known.Remove(key);Legacy.Remove(key);}
                Vector3 position=z.GetPosition();int n=Rules.NextMarker(markers,cup,position.x,position.y,position.z,out int par);
                // Serialize assignments on the host and advance the owner revision before writing.
                z.SetOwner(ZNet.GetUID());z.Set(Pending,false);z.Set(GolfWorld.LabelKey,n>0?$"Meadow:{n}:{par}":"");Known.Add(id);
                ZDOMan.instance.ForceSendZDO(id);if(cup)GolfWorld.RememberCup(z);
                GolfWorld.Reply(sender,n>0?$"{(cup?"Golf cup":"Golf tee")} · Meadow:{n}:{par}":"Meadow already has 18 holes. Shift+E: use another course name and hole number.");
            }
            finally{if(generation==Generation)Requests.Remove(id);}
        }
    }
}
