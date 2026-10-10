using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LocalPortals
{
    // Creatures through the glass. Any creature that fits the opening walks through a mirror as a player does; and a creature
    // going somewhere a mirror makes much nearer (a follower after its player, a hunter after what it chases) walks to the
    // mirror and in. Someone stepping through is not lost by what hunts them: their hunters know where they went.
    // Only the owner of a creature moves it (as the game does).
    internal static class Creatures
    {
        private static readonly AccessTools.FieldRef<BaseAI,Character> AiBody=AccessTools.FieldRefAccess<BaseAI,Character>("m_character");
        private static readonly AccessTools.FieldRef<BaseAI,float> PathTime=AccessTools.FieldRefAccess<BaseAI,float>("m_lastFindPathTime");
        private static readonly AccessTools.FieldRef<BaseAI,List<Vector3>> PathPoints=AccessTools.FieldRefAccess<BaseAI,List<Vector3>>("m_path");
        private static readonly AccessTools.FieldRef<MonsterAI,Character> Hunted=AccessTools.FieldRefAccess<MonsterAI,Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<MonsterAI,Vector3> LastSeen=AccessTools.FieldRefAccess<MonsterAI,Vector3>("m_lastKnownTargetPos");
        private static readonly AccessTools.FieldRef<MonsterAI,bool> BeenThere=AccessTools.FieldRefAccess<MonsterAI,bool>("m_beenAtLastPos");
        private static readonly AccessTools.FieldRef<Character,Vector3> WalkVel=AccessTools.FieldRefAccess<Character,Vector3>("m_currentVel");
        private static readonly AccessTools.FieldRef<Character,Vector3> WalkDir=AccessTools.FieldRefAccess<Character,Vector3>("m_moveDir");
        private static readonly AccessTools.FieldRef<Character,float> MaxAirAltitude=AccessTools.FieldRefAccess<Character,float>("m_maxAirAltitude");
        internal static int Count;              // creatures through, this session
        internal static string LastThrough;     // for the lportal command
        internal static int Routed=>Routes.Count;

        // Small enough for the opening, and walking (fliers and swimmers stay out).
        internal static bool Fits(Character c)
        {
            if(c==null||c.IsPlayer()||c.m_flying)return false;
            var (r,tall)=Size(c);
            return r<=Policy.HalfWidth-0.015&&tall<=Policy.Top-0.1; // (a wolf is 0.585 round: lined up, the frame's edges give way to it)
        }
        internal static (float radius,float tall) Size(Character c)
        {
            CapsuleCollider body=c!=null?c.GetCollider():null;
            if(body==null)return (99,99);
            Vector3 s=body.transform.lossyScale;
            return (body.radius*Mathf.Max(s.x,s.z),body.direction==1?Mathf.Max(body.height,body.radius*2)*s.y:body.radius*2*s.y);
        }
        // The point of a body that has to pass the glass for it to be through.
        internal static Vector3 Middle(Character c)=>c.IsPlayer()?c.transform.position+Vector3.up*(float)Policy.BodyHeight:c.GetCenterPoint();

        // Walking through: like the player's crossing (Crossing), for every creature this game owns.
        private static readonly Dictionary<(Character,LocalPortal),Vector3> Last=new Dictionary<(Character,LocalPortal),Vector3>();
        private static readonly HashSet<(Collider,Collider)> Wanted=new HashSet<(Collider,Collider)>(),Ignored=new HashSet<(Collider,Collider)>();
        private static readonly Dictionary<LocalPortal,List<Collider>> Walls=new Dictionary<LocalPortal,List<Collider>>();
        private static readonly List<(Collider,Collider)> Swap=new List<(Collider,Collider)>();
        private static readonly List<(Character,LocalPortal)> Stale=new List<(Character,LocalPortal)>();
        internal static void Tick()
        {
            Wanted.Clear();
            foreach(List<Collider> list in Walls.Values)list.Clear();
            if(LocalPortal.Live.Count>0)
            {
                foreach(Character c in Character.GetAllCharacters())
                {
                    if(c==null||c.IsDead()||!c.IsOwner()||!Fits(c))continue;
                    Vector3 mid=Middle(c);
                    LocalPortal through=null;
                    foreach(LocalPortal p in LocalPortal.Live)
                    {
                        if(p==null||p.Partner==null||(p.transform.position-mid).sqrMagnitude>36){Last.Remove((c,p));continue;}
                        Vector3 now=p.transform.InverseTransformPoint(mid);
                        if(now.z>0&&now.z<1.5f&&Mathf.Abs(now.x)<Policy.HalfWidth+0.4)Unblock(c,p,Mathf.Abs(now.x)<Policy.HalfWidth);
                        if(through==null&&Last.TryGetValue((c,p),out Vector3 before)&&Policy.Crossed((before.x,before.y,before.z),(now.x,now.y,now.z)))through=p;
                        Last[(c,p)]=now;
                    }
                    if(through!=null)Go(c,through,through.Partner);
                }
            }
            if(Last.Count>64)
            {
                Stale.Clear();
                foreach(var key in Last.Keys)if(key.Item1==null||key.Item2==null)Stale.Add(key);
                foreach(var key in Stale)Last.Remove(key);
            }
            Unwall();
        }
        // A creature in front of a mirror: the board behind the glass, the walls behind it and (lined up) the frame's edges
        // give way to it, as they do to the player.
        private static void Unblock(Character c,LocalPortal p,bool lined)
        {
            Collider body=c.GetCollider();
            if(body==null)return;
            if(!Walls.TryGetValue(p,out List<Collider> walls))Walls[p]=walls=new List<Collider>();
            if(walls.Count==0){Crossing.WallsBehind(p,false,walls);walls.Add(null);} // (null: worked out this frame, even if empty)
            foreach(Collider w in walls)if(w!=null)Wanted.Add((body,w));
            if(p.Back!=null)Wanted.Add((body,p.Back));
            if(lined)foreach(Collider w in p.Solid)if(w!=null)Wanted.Add((body,w));
        }
        private static void Unwall()
        {
            Swap.Clear();
            foreach(var pair in Ignored)if(!Wanted.Contains(pair))Swap.Add(pair);
            foreach(var pair in Swap){Ignored.Remove(pair);if(pair.Item1!=null&&pair.Item2!=null)Physics.IgnoreCollision(pair.Item1,pair.Item2,false);}
            foreach(var pair in Wanted)if(Ignored.Add(pair))Physics.IgnoreCollision(pair.Item1,pair.Item2,true);
        }

        internal static void Go(Character c,LocalPortal from,LocalPortal to)
        {
            Transform a=from.transform,b=to.transform;
            Quaternion turn=Crossing.Turn(a,b);
            Vector3 was=c.transform.position,position=Crossing.Point(a,b,was);
            // out a little way, its middle clear of the glass (it could shuffle straight back in), and the board and walls behind
            // the exit give way to it before the next physics step, or they would push its back half out of them, perhaps backwards
            float clear=0.12f-b.InverseTransformPoint(Crossing.Point(a,b,Middle(c))).z;
            if(clear>0)position+=b.forward*clear;
            Unblock(c,to,true);
            Quaternion rotation=turn*c.transform.rotation;
            Vector3 look=turn*c.GetLookDir();
            var body=c.GetComponent<Rigidbody>();
            Vector3 velocity=body!=null?turn*body.linearVelocity:Vector3.zero;
            float fallen=MaxAirAltitude(c)-was.y;
            c.transform.SetPositionAndRotation(position,rotation);
            if(body!=null){body.position=position;body.rotation=rotation;body.linearVelocity=velocity;}
            MaxAirAltitude(c)=position.y+fallen;
            c.SetLookDir(look);
            WalkVel(c)=turn*WalkVel(c);WalkDir(c)=turn*WalkDir(c); // the game's own walking speed and heading, or it walks straight back in
            Physics.SyncTransforms();
            BaseAI ai=c.GetBaseAI();
            if(ai!=null){PathTime(ai)=-1000;PathPoints(ai)?.Clear();Routes.Remove(ai);} // its path was in the other world
            foreach(LocalPortal p in LocalPortal.Live)Last.Remove((c,p));
            Count++;
            LastThrough=c.m_name+" through "+(from.Name==""?"a mirror":from.Name);
            Gone(c,from);
        }

        // Someone went through a mirror: whatever was hunting them knows where they are now, and follows.
        internal static void Gone(Character who,LocalPortal from)
        {
            if(who==null||from==null)return;
            foreach(BaseAI ai in BaseAI.BaseAIInstances)
            {
                if(!(ai is MonsterAI hunter)||Hunted(hunter)!=who)continue;
                Character body=AiBody(ai);
                if(body==null||!body.IsOwner()||(body.transform.position-from.transform.position).sqrMagnitude>60*60)continue;
                LastSeen(hunter)=who.transform.position;
                BeenThere(hunter)=false;
            }
        }

        // Where a creature walks: toward a point a mirror makes much nearer, it walks to the front of that mirror instead, then
        // straight in. Returns false when it has done the walking itself (the game's own MoveTo is skipped).
        private static readonly Dictionary<BaseAI,LocalPortal> Routes=new Dictionary<BaseAI,LocalPortal>();
        internal static bool Route(BaseAI ai,ref Vector3 point,ref float dist,bool run,ref bool result)
        {
            if(LocalPortal.Live.Count==0){if(Routes.Count>0)Routes.Clear();return true;}
            Character c=AiBody(ai);
            if(c==null||!Fits(c))return true;
            Vector3 at=c.transform.position;
            LocalPortal p=Choose(ai,at,point);
            if(p==null)return true;
            Transform a=p.transform;
            Vector3 l=a.InverseTransformPoint(at);
            if(l.z>-0.05f&&l.z<3.2f&&Mathf.Abs(l.x)<1.0f)
            {
                Vector3 aim=a.TransformPoint(new Vector3(0,l.y,-1.5f))-at; // through the middle of the glass
                aim.y=0;
                ai.MoveTowards(aim.normalized,run);
                result=false;
                return false;
            }
            point=a.TransformPoint(new Vector3(0,0,2.2f)); // the game's path finding takes it to the front of the mirror
            dist=0;
            return true;
        }
        private static LocalPortal Choose(BaseAI ai,Vector3 at,Vector3 goal)
        {
            Routes.TryGetValue(ai,out LocalPortal kept);
            float direct=Vector3.Distance(at,goal);
            LocalPortal best=null;
            float bestCost=float.MaxValue;
            if(direct>5)
                foreach(LocalPortal p in LocalPortal.Live)
                {
                    if(p==null||p.Partner==null||(p.transform.position-at).sqrMagnitude>45*45)continue;
                    float cost=(float)Policy.Detour(Vector3.Distance(at,Front(p)),Vector3.Distance(Front(p.Partner),goal));
                    if(cost<bestCost){best=p;bestCost=cost;}
                }
            if(best!=null&&Policy.TakeMirror(direct,bestCost,best==kept)){Routes[ai]=best;return best;}
            if(kept!=null||Routes.Count>0&&Routes.ContainsKey(ai))Routes.Remove(ai);
            if(Routes.Count>200)Routes.Clear();
            return null;
        }
        private static Vector3 Front(LocalPortal p)=>p.transform.TransformPoint(new Vector3(0,0,1.5f));

        internal static void Stop()
        {
            Wanted.Clear();Unwall();
            Last.Clear();Routes.Clear();Walls.Clear();
        }
    }

    [HarmonyPatch(typeof(BaseAI),"MoveTo")]
    internal static class CreatureRoute
    {
        private static bool Prefix(BaseAI __instance,ref Vector3 point,ref float dist,bool run,ref bool __result)=>Creatures.Route(__instance,ref point,ref dist,run,ref __result);
    }
}
