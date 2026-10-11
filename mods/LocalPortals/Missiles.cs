using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LocalPortals
{
    // Arrows, spears, bombs and dropped things fly through the glass: whatever reaches a mirror's opening comes out of its
    // partner at the matching spot, moving the matching way. Only the owner of a thing moves it (the others see it move).
    internal static class Missiles
    {
        private static readonly AccessTools.FieldRef<Projectile,ZNetView> ShotView=AccessTools.FieldRefAccess<Projectile,ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Projectile,Vector3> ShotVel=AccessTools.FieldRefAccess<Projectile,Vector3>("m_vel");
        private static readonly AccessTools.FieldRef<Projectile,bool> ShotHit=AccessTools.FieldRefAccess<Projectile,bool>("m_didHit");
        private static readonly AccessTools.FieldRef<Projectile,bool> ShotHasStart=AccessTools.FieldRefAccess<Projectile,bool>("m_haveStartPoint");
        private static readonly AccessTools.FieldRef<Projectile,Character> ShotOwner=AccessTools.FieldRefAccess<Projectile,Character>("m_owner");
        private static readonly AccessTools.FieldRef<ItemDrop,ZNetView> ItemView=AccessTools.FieldRefAccess<ItemDrop,ZNetView>("m_nview");
        private static int _solidMask,_itemMask;
        internal static int Count;              // things put through, this session
        internal static string LastThrough;     // for the lportal command

        // Before the game moves a shot this physics step: if the step takes it into a mirror's glass, it is put through
        // first, so the game's own step carries on from the partner's glass (and its hit test starts there).
        internal static void Fly(Projectile shot)
        {
            if(LocalPortal.Live.Count==0||ShotHit(shot))return;
            ZNetView view=ShotView(shot);
            if(view==null||!view.IsValid()||!view.IsOwner())return;
            float dt=Time.fixedDeltaTime;
            Vector3 vel=ShotVel(shot)+Vector3.down*(shot.m_gravity*dt); // the game's own step
            vel-=vel.normalized*(vel.sqrMagnitude*shot.m_drag*dt);
            Transform t=shot.transform;
            Vector3 from=t.position,to=from+vel*dt;
            float r=Mathf.Max(0,shot.m_rayRadius);
            float reach=vel.magnitude*dt+4;
            Outward(shot,from,vel,r,dt);
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Partner==null||(p.transform.position-from).sqrMagnitude>reach*reach)continue;
                Transform a=p.transform,b=p.Partner.transform;
                double k=Pass(a,from,to,r,r);
                if(k<0)continue;
                Vector3 glass=Vector3.Lerp(from,to,(float)k);
                if(Blocked(from,glass,r))return; // something in front of the glass is hit first: the game's step finds it
                Quaternion turn=Crossing.Turn(a,b);
                Vector3 exit=Crossing.Point(a,b,glass)+b.forward*(2*r+0.01f); // front to the other glass, not through it
                Vector3 onward=turn*vel;
                ShotVel(shot)=turn*ShotVel(shot); // (the turn is about the vertical, so gravity is the same after it)
                shot.m_startPoint=exit;ShotHasStart(shot)=true;
                // the game's step then ends where the rest of it would, and always a little way out: its hit test runs from the glass
                // to there, and a step of nothing has no direction (it could look back into the board)
                t.SetPositionAndRotation(exit+onward.normalized*0.03f-onward*((float)k*dt),turn*t.rotation);
                Through(p,shot.name,shot);
                return;
            }
        }

        // The game's hit test starts half a step behind a shot (if it has no start point of its own): just out of a mirror, that
        // is back through the glass, into the board behind it. There it starts from the shot instead.
        private static void Outward(Projectile shot,Vector3 at,Vector3 vel,float r,float dt)
        {
            if(ShotHasStart(shot))return;
            float back=vel.magnitude*dt*0.5f+r+0.05f;
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||(p.transform.position-at).sqrMagnitude>back*back+16)continue;
                Vector3 l=p.transform.InverseTransformPoint(at);
                if(l.z<0||l.z>back||!Policy.InOpening(l.x,l.y,-0.5))continue;
                shot.m_startPoint=at;ShotHasStart(shot)=true;
                return;
            }
        }

        // A shot loosed by someone in front of a mirror whose hand (or bow) is already past the glass starts beyond it.
        internal static void Loosed(Projectile shot)
        {
            if(LocalPortal.Live.Count==0)return;
            ZNetView view=ShotView(shot);
            Character owner=ShotOwner(shot);
            if(owner==null||view==null||!view.IsValid()||!view.IsOwner())return;
            Transform t=shot.transform;
            Vector3 at=t.position,centre=owner.GetCenterPoint();
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Partner==null||(p.transform.position-at).sqrMagnitude>36)continue;
                Transform a=p.transform,b=p.Partner.transform;
                Vector3 s=a.InverseTransformPoint(at),c=a.InverseTransformPoint(centre);
                double k=Policy.Loosed((c.x,c.y,c.z),(s.x,s.y,s.z));
                if(k<0)continue;
                Vector3 glass=Vector3.Lerp(centre,at,(float)k);
                Quaternion turn=Crossing.Turn(a,b);
                ShotVel(shot)=turn*ShotVel(shot);
                shot.m_startPoint=Crossing.Point(a,b,glass)+b.forward*0.01f;ShotHasStart(shot)=true;
                t.SetPositionAndRotation(Crossing.Point(a,b,at),turn*t.rotation);
                Through(p,shot.name,shot);
                return;
            }
        }

        // Dropped and tossed things (loose items with a body) going into the glass.
        private static readonly Collider[] Near=new Collider[64];
        private static readonly HashSet<Rigidbody> Moved=new HashSet<Rigidbody>();
        internal static void Tick()
        {
            if(LocalPortal.Live.Count==0)return;
            if(_itemMask==0)_itemMask=LayerMask.GetMask("item");
            float dt=Time.fixedDeltaTime;
            Moved.Clear();
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Partner==null)continue;
                Transform a=p.transform,b=p.Partner.transform;
                Vector3 centre=a.TransformPoint(new Vector3(0,(float)Policy.CentreY,0));
                int n=Physics.OverlapSphereNonAlloc(centre,4,Near,_itemMask,QueryTriggerInteraction.Ignore);
                for(int i=0;i<n;i++)
                {
                    Rigidbody body=Near[i].attachedRigidbody;
                    if(body==null||body.isKinematic||Moved.Contains(body))continue;
                    ItemDrop item=body.GetComponent<ItemDrop>();
                    if(item==null||!Policy.MayCarry(Plugin.BlockUnportable.Value,false,!Plugin.BlockUnportable.Value||item.m_itemData.m_shared.m_teleportable||ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll)))continue;
                    ZNetView view=ItemView(item);
                    if(view==null||!view.IsValid()||!view.IsOwner())continue;
                    Vector3 from=body.position,vel=body.linearVelocity,to=from+vel*dt;
                    float r=Mathf.Max(0,Front(Near[i],from,a.forward));
                    double k=Pass(a,from,to,r,0); // (its middle in the opening: the frame stops the rest, if it must)
                    if(k<0)continue;
                    Quaternion turn=Crossing.Turn(a,b);
                    Vector3 glass=Vector3.Lerp(from,to,(float)k);
                    Vector3 exit=Crossing.Point(a,b,glass)+b.forward*(2*r+0.01f);
                    body.position=exit;body.rotation=turn*body.rotation;
                    item.transform.SetPositionAndRotation(exit,body.rotation);
                    body.linearVelocity=turn*vel;body.angularVelocity=turn*body.angularVelocity;
                    Moved.Add(body);
                    Through(p,item.name);
                }
            }
        }

        // How far a thing reaches from its middle toward a mirror (facing `normal`): to the point of its collider nearest a spot
        // far off in the glass's direction (its outermost point that way), or by its box for a shape that cannot say.
        private static float Front(Collider c,Vector3 middle,Vector3 normal)
        {
            bool exact=c is BoxCollider||c is SphereCollider||c is CapsuleCollider||(c is MeshCollider m&&m.convex);
            if(exact)return Vector3.Dot(c.ClosestPoint(middle-normal*100)-middle,-normal);
            Bounds box=c.bounds;
            return Mathf.Abs(normal.x)*box.extents.x+Mathf.Abs(normal.y)*box.extents.y+Mathf.Abs(normal.z)*box.extents.z+Vector3.Dot(box.center-middle,-normal);
        }
        private static double Pass(Transform a,Vector3 from,Vector3 to,float depth,float room)
        {
            Vector3 f=a.InverseTransformPoint(from),e=a.InverseTransformPoint(to);
            return Policy.Pass((f.x,f.y,f.z),(e.x,e.y,e.z),depth,room);
        }
        private static readonly RaycastHit[] Hits=new RaycastHit[8];
        private static bool Blocked(Vector3 from,Vector3 to,float radius)
        {
            if(_solidMask==0)_solidMask=LayerMask.GetMask("Default","static_solid","Default_small","piece","piece_nonsolid","terrain","character","character_net","character_ghost","hitbox","character_noenv","vehicle");
            Vector3 d=to-from;
            if(d.sqrMagnitude<1e-6f)return false;
            int n=radius>0?Physics.SphereCastNonAlloc(from,radius,d.normalized,Hits,d.magnitude,_solidMask,QueryTriggerInteraction.Ignore):Physics.RaycastNonAlloc(from,d.normalized,Hits,d.magnitude,_solidMask,QueryTriggerInteraction.Ignore);
            return n>0;
        }
        internal static void Unstart(Projectile shot)=>ShotHasStart(shot)=false; // (for a test shot fired away from its owner)
        internal static string LastHit;         // for the lportal command: what the last shot to go through hit
        private static readonly HashSet<int> Sent=new HashSet<int>();
        internal static void Hit(Projectile shot,Collider collider,Vector3 point)
        {
            if(!Sent.Remove(shot.GetInstanceID()))return;
            LastHit=(collider!=null?collider.name+" ("+collider.transform.root.name+")":"nothing")+" at "+point.ToString("F2");
        }
        private static void Through(LocalPortal p,string what,Projectile shot=null)
        {
            if(shot!=null){if(Sent.Count>200)Sent.Clear();Sent.Add(shot.GetInstanceID());}
            Count++;
            LastThrough=what.Replace("(Clone)","")+" through "+(p.Name==""?"a mirror":p.Name);
        }
    }

    [HarmonyPatch(typeof(Projectile),"FixedUpdate")]
    internal static class ShotThroughGlass
    {
        private static void Prefix(Projectile __instance)=>Missiles.Fly(__instance);
    }
    [HarmonyPatch(typeof(Projectile),nameof(Projectile.OnHit))]
    internal static class ShotHit
    {
        private static void Prefix(Projectile __instance,Collider collider,Vector3 hitPoint)=>Missiles.Hit(__instance,collider,hitPoint);
    }
    [HarmonyPatch(typeof(Projectile),nameof(Projectile.Setup))]
    internal static class ShotLoosed
    {
        private static void Postfix(Projectile __instance)=>Missiles.Loosed(__instance);
    }
}
