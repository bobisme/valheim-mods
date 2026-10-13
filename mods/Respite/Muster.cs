using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Respite
{
    // Raiders gather outside the base before they come: a spot past its farthest building, shown (message, map pin, a churning rift)
    // to everyone near, then the raid comes from there.
    internal static class Muster
    {
        private sealed class Waiting { public float At; public Vector3 Spot; public Action<Vector3> Spawn; }
        private sealed class Shown { public float Until,NextGlow; public Vector3 Spot; public Minimap.PinData Pin; }
        private static readonly List<Waiting> Pending=new List<Waiting>();
        private static readonly List<Shown> Showing=new List<Shown>();
        private static string _gameKey;
        internal static Vector3? GameSpot;   // where the game's raid now under way gathers (its spawns come from there)

        // Where raiders gather for a raid on home: past the base's edge (its farthest building, or clear metres if more) by the margin,
        // on dry ground outside every ward. Seeded, so games that see the same raid with the same buildings agree.
        internal static Vector3 Spot(Vector3 home,float clear,int seed)
        {
            var pieces=new List<Piece>();
            Piece.GetAllPiecesInRadius(home,150,pieces);
            float edge=Policy.Edge(pieces.Where(p=>p!=null&&p.IsPlacedByPlayer()).Select(p=>Utils.DistanceXZ(p.transform.position,home)),clear);
            float far=Policy.Distance(edge,Plugin.Instance.Margin.Value,Plugin.Instance.LeastDistance.Value);
            float water=ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30;
            Vector3 first=Vector3.zero;bool any=false;
            foreach(float far2 in new[]{far,far*1.3f})
                foreach(float a in Policy.Angles(seed,16))
                {
                    Vector3 p=home+Quaternion.Euler(0,a,0)*Vector3.forward*far2;
                    p.y=Ground(p);
                    if(!any){first=p;any=true;}
                    if(p.y<water+0.5f||EffectArea.IsPointInsideArea(p,EffectArea.Type.PlayerBase)!=null)continue;
                    return p;
                }
            return first;
        }
        private static float Ground(Vector3 p)
        {
            if(ZoneSystem.instance!=null&&ZoneSystem.instance.GetSolidHeight(p,out float solid))return solid;
            return WorldGenerator.instance!=null?WorldGenerator.instance.GetHeight(p.x,p.z):p.y;
        }

        // A mod's raid: announced now, spawned at the spot when the warning is up. The spot.
        // From: the side they should come from if the ground there allows (null: any side).
        internal static Vector3 Begin(Vector3 home,float clear,Vector3? from,string text,Action<Vector3> spawn)
        {
            int seed=from.HasValue?Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(from.Value.x-home.x,from.Value.z-home.z)*Mathf.Rad2Deg,360)):UnityEngine.Random.Range(0,360);
            Vector3 spot=Spot(home,clear,seed);
            float seconds=Plugin.Instance.WarningSeconds.Value;
            Ledger.Announce(spot,home,seconds,text);
            if(seconds<=0)Run(spawn,spot);
            else Pending.Add(new Waiting{At=Time.time+seconds,Spot=spot,Spawn=spawn});
            return spot;
        }
        private static void Run(Action<Vector3> spawn,Vector3 spot)
        {
            try{spawn?.Invoke(spot);}
            catch(Exception e){Plugin.Log("Respite: a mustered raid failed to come: "+e);}
        }

        // Shown on every game whose player is near the raided base: where they gather and how long until they come.
        internal static void Show(Vector3 spot,Vector3 home,float seconds,string text)
        {
            Player me=Player.m_localPlayer;
            if(me==null||Utils.DistanceXZ(me.transform.position,home)>400)return;
            string where=$"They gather to the {Policy.Compass(spot.x-home.x,spot.z-home.z)}, {Utils.DistanceXZ(spot,home):0} m out";
            string line=seconds>=1?$"{where}: {seconds:0} seconds.":where+"!";
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,string.IsNullOrEmpty(text)?line:text+"\n"+line);
            Minimap.PinData pin=Minimap.instance?.AddPin(spot,Minimap.PinType.RandomEvent,"Raiders gather",false,false);
            Showing.Add(new Shown{Until=Time.time+Mathf.Max(seconds,0)+60,Spot=spot,Pin=pin});
        }

        internal static void Tick()
        {
            foreach(Waiting w in Pending.Where(w=>Time.time>=w.At).ToList()){Pending.Remove(w);Run(w.Spawn,w.Spot);}
            Player me=Player.m_localPlayer;
            foreach(Shown s in Showing.ToList())
            {
                if(Time.time>=s.Until){Unpin(s);Showing.Remove(s);continue;}
                if(Time.time<s.NextGlow||s.Until-Time.time<60||me==null||Utils.DistanceXZ(me.transform.position,s.Spot)>300)continue; // (it churns only while they gather)
                s.NextGlow=Time.time+4;
                Effect("vfx_prespawn",s.Spot);Effect("sfx_prespawn",s.Spot);
            }
            GameRaid();
        }
        // The game's raid where this player is: its gathering spot worked out here (the same spot on every game near, seeded by the
        // raid's place), shown while its spawns are held.
        private static void GameRaid()
        {
            RandomEvent ev=RandEventSystem.instance!=null?RandEventSystem.instance.GetActiveEvent():null;
            if(!GameRaids.Raidlike(ev)||!Plugin.Instance.GameRaids.Value){_gameKey=null;GameSpot=null;return;}
            string key=ev.m_name+ev.m_pos.ToString("F0");
            if(key==_gameKey)return;
            _gameKey=key;
            GameSpot=Spot(ev.m_pos,0,Policy.Seed(ev.m_pos.x,ev.m_pos.z));
            float left=Plugin.Instance.WarningSeconds.Value-ev.m_time;
            if(left>1)Show(GameSpot.Value,ev.m_pos,left,"");
        }
        private static void Effect(string name,Vector3 at)
        {
            GameObject prefab=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(prefab!=null)UnityEngine.Object.Instantiate(prefab,at,Quaternion.identity);
        }
        private static void Unpin(Shown s){if(s.Pin!=null&&Minimap.instance!=null)Minimap.instance.RemovePin(s.Pin);}
        internal static void Clear(){foreach(Shown s in Showing)Unpin(s);Showing.Clear();Pending.Clear();_gameKey=null;GameSpot=null;}
    }
}
