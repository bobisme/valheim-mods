using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class BoatRide
    {
        private const string ShipKey="bob_gary_ship",SpotKey="bob_gary_deck";
        private sealed class Collision
        {internal Collider Other;internal bool Ignored;}
        private sealed class Passenger
        {
            internal Character Gary;
            internal Ship Ship;
            internal ZDOID ShipID;
            internal Rigidbody Body;
            internal ZSyncTransform Sync;
            internal bool Kinematic,Gravity,ParentSync;
            internal float LastAboard;
            internal readonly List<Collision> Collisions=new List<Collision>();
        }
        private static readonly Dictionary<Character,Passenger> Passengers=new Dictionary<Character,Passenger>();
        private static readonly AccessTools.FieldRef<Character,Rigidbody> GroundBody=AccessTools.FieldRefAccess<Character,Rigidbody>("m_lastGroundBody");
        private static readonly AccessTools.FieldRef<Character,Collider> GroundCollider=AccessTools.FieldRefAccess<Character,Collider>("m_lastGroundCollider");
        private static readonly AccessTools.FieldRef<Character,float> GroundTouch=AccessTools.FieldRefAccess<Character,float>("m_lastGroundTouch");
        private static readonly string[] SpeedParameters={"forward_speed","sideway_speed","turn_speed"};
        private static int SolidMask=>LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","blocker","vehicle");
        private static Ship ShipFrom(ZDOID id)
        {
            GameObject go=ZNetScene.instance!=null?ZNetScene.instance.FindInstance(id):null;
            Ship ship=go!=null?go.GetComponent<Ship>():null;
            ZNetView view=ship!=null?ship.GetComponent<ZNetView>():null;
            return ship!=null&&ship.isActiveAndEnabled&&view!=null&&view.IsValid()?ship:null;
        }
        private static bool KnownShip(ZDOID id)
        {
            ZDO z=ZDOMan.instance!=null?ZDOMan.instance.GetZDO(id):null;
            GameObject prefab=z!=null&&ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(z.GetPrefab()):null;
            return prefab!=null&&prefab.GetComponent<Ship>()!=null;
        }
        private static Ship ShipOf(Player player)
        {
            if(player==null||player.IsDead()||player.IsTeleporting()||player.InInterior())return null;
            Ship ship=player.GetStandingOnShip()??player.GetControlledShip();
            if(ship!=null)return ship;
            // A remote player's controls/ground contact live on its owner. Native parent sync identifies its actual boat.
            ZDO z=Companion.Data(player);
            Ship parent=z!=null?ShipFrom(z.GetConnectionZDOID(ZDOExtraData.ConnectionType.SyncTransform)):null;
            if(parent!=null&&Vector3.Distance(player.transform.position,parent.transform.TransformPoint(z.GetVec3(ZDOVars.s_relPosHash,Vector3.zero)))<=3)return parent;
            // Seated players need not report a ground contact. Match the actual ship trigger, never a nearby boat.
            if(player.IsAttachedToShip())
                foreach(IMonoUpdater updater in Ship.Instances)
                    if(updater is Ship seated&&seated.IsPlayerInBoat(player))return seated;
            return null;
        }
        private static bool DeckSpot(Ship ship,Player master,out Vector3 local)
        {
            local=Vector3.zero;
            Vector3[] offsets={new Vector3(0,0,2.5f),new Vector3(-0.8f,0,1.8f),new Vector3(0.8f,0,1.8f),new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),
                new Vector3(-1.3f,0,0),new Vector3(1.3f,0,0),new Vector3(0,0,-1.3f),new Vector3(0,0,1.3f)};
            foreach(Vector3 offset in offsets)
            {
                Vector3 candidate=master.transform.position+ship.transform.TransformDirection(offset);
                RaycastHit[] hits=Physics.RaycastAll(candidate+Vector3.up*1.5f,Vector3.down,4,SolidMask,QueryTriggerInteraction.Ignore);
                Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
                foreach(RaycastHit hit in hits)
                {
                    if(hit.collider.GetComponentInParent<Ship>()!=ship||hit.normal.y<0.55f||
                        Mathf.Abs(hit.point.y-master.transform.position.y)>1.5f)continue;
                    Vector3 foot=hit.point+Vector3.up*0.12f;
                    if(Physics.CheckCapsule(foot+Vector3.up*0.5f,foot+Vector3.up*1.3f,0.35f,SolidMask,QueryTriggerInteraction.Ignore))continue;
                    local=ship.transform.InverseTransformPoint(foot);return Finite(local);
                }
            }
            return false;
        }
        internal static bool CallSpot(Player player,out Vector3 spot)
        {
            Ship ship=ShipOf(player);
            if(ship==null)return LandingSpot(player,out spot)||Companion.SafeSpot(player.transform.position,out spot);
            if(DeckSpot(ship,player,out Vector3 local)){spot=ship.transform.TransformPoint(local);return true;}
            spot=default;return false;
        }
        private static bool LandingSpot(Player player,out Vector3 spot)
        {
            spot=default;if(player==null||ZoneSystem.instance==null)return false;
            for(int i=0;i<12;i++)
            {
                Vector3 candidate=player.transform.position+Quaternion.Euler(0,i*30,0)*Vector3.forward*1.8f;
                RaycastHit[] hits=Physics.RaycastAll(candidate+Vector3.up*1.5f,Vector3.down,4,SolidMask,QueryTriggerInteraction.Ignore);
                Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
                foreach(RaycastHit hit in hits)
                {
                    // A pier/floor is dry landing space even when its underlying terrain is underwater.
                    if(hit.collider.GetComponentInParent<Ship>()!=null||hit.normal.y<0.7f||
                        hit.point.y<ZoneSystem.instance.m_waterLevel+0.15f||Mathf.Abs(hit.point.y-player.transform.position.y)>2)continue;
                    Vector3 foot=hit.point+Vector3.up*0.12f;
                    if(Physics.CheckCapsule(foot+Vector3.up*0.5f,foot+Vector3.up*1.3f,0.35f,SolidMask,QueryTriggerInteraction.Ignore))continue;
                    spot=foot;return Finite(spot);
                }
            }
            return false;
        }
        private static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&
            !float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
        private static Passenger Capture(Character c,Ship ship,ZDOID id)
        {
            if(Passengers.TryGetValue(c,out Passenger ride))
            {
                if(ride.Ship==ship&&ride.ShipID==id)return ride;
                Restore(ride);Passengers.Remove(c);
            }
            Rigidbody body=c.GetComponent<Rigidbody>();ZSyncTransform sync=c.GetComponent<ZSyncTransform>();
            if(body==null||sync==null)return null;
            ride=new Passenger{Gary=c,Ship=ship,ShipID=id,Body=body,Sync=sync,Kinematic=body.isKinematic,
                Gravity=body.useGravity,ParentSync=sync.m_characterParentSync,LastAboard=Time.time};
            Collider collider=c.GetCollider();
            if(collider!=null)
                foreach(Collider other in ship.GetComponentsInChildren<Collider>(true))
                {
                    if(other.isTrigger)continue;
                    ride.Collisions.Add(new Collision{Other=other,Ignored=Physics.GetIgnoreCollision(collider,other)});
                }
            Passengers.Add(c,ride);return ride;
        }
        private static bool Current(Character c,out Passenger ride)
        {
            ride=null;
            if(c==null||Plugin.Instance==null||!Plugin.Instance.Boats.Value||!Companion.Is(c)){Release(c);return false;}
            ZDO z=Companion.Data(c);ZDOID id=z.GetZDOID(ShipKey);
            Ship ship=ShipFrom(id);
            if(ship==null||!Finite(z.GetVec3(SpotKey,Vector3.zero))){Release(c);return false;}
            ride=Capture(c,ship,id);return ride!=null;
        }
        internal static bool IsRiding(Character c)=>Current(c,out _);
        internal static bool Hold(Character c)
        {
            if(!Current(c,out Passenger ride))return false;
            ZDO z=Companion.Data(c);Vector3 position=ride.Ship.transform.TransformPoint(z.GetVec3(SpotKey,Vector3.zero));
            Quaternion rotation=ride.Ship.transform.rotation*Quaternion.Euler(0,z.GetFloat("bob_gary_deck_yaw",0),0);
            Collider collider=c.GetCollider();
            if(collider!=null)
                foreach(Collision pair in ride.Collisions)if(pair.Other!=null)Physics.IgnoreCollision(collider,pair.Other,true);
            if(!ride.Body.isKinematic){ride.Body.linearVelocity=Vector3.zero;ride.Body.angularVelocity=Vector3.zero;}
            ride.Body.isKinematic=true;ride.Body.useGravity=false;ride.Sync.m_characterParentSync=true;
            c.transform.SetPositionAndRotation(position,rotation);ride.Body.position=position;ride.Body.rotation=rotation;
            ZSyncAnimation anim=c.GetZAnim();
            foreach(string name in SpeedParameters)
                if(anim!=null&&anim.HasParameter(name,AnimatorControllerParameterType.Float))anim.SetFloat(name,0);
            return true;
        }
        internal static bool Client(Character c,float dt)
        {
            if(!Current(c,out Passenger ride))return false;
            // Preserve native fixed-update cadence: sync the boat before its passenger, never from a render-frame hold.
            if(!ride.Ship.IsOwner())ride.Ship.GetComponent<ZSyncTransform>()?.CustomFixedUpdate(dt);
            return Hold(c);
        }
        internal static bool Relative(Character c,out ZDOID parent,out Vector3 position,out Quaternion rotation)
        {
            parent=ZDOID.None;position=Vector3.zero;rotation=Quaternion.identity;
            if(!Current(c,out Passenger ride))return false;
            parent=ride.ShipID;position=Companion.Data(c).GetVec3(SpotKey,Vector3.zero);rotation=Quaternion.Euler(0,Companion.Data(c).GetFloat("bob_gary_deck_yaw",0),0);return true;
        }
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt,bool retreat)
        {
            Character c=st.Body;ZDO z=Companion.Data(c);
            Ship ship=ShipFrom(z.GetZDOID(ShipKey));Ship wanted=ShipOf(master);
            bool following=master!=null&&!master.IsDead()&&!master.IsTeleporting()&&!master.InInterior()&&!z.GetBool(Companion.Waiting,false);
            if(!Plugin.Instance.Boats.Value)
            {if(!z.GetZDOID(ShipKey).IsNone())Forget(z);Release(c);return false;}
            if(ship==null)
            {
                ZDOID saved=z.GetZDOID(ShipKey);
                // World loading can instantiate the passenger before its saved boat. Preserve the link briefly.
                if(!saved.IsNone()&&KnownShip(saved))
                {
                    if(st.BoatMissingUntil==0)st.BoatMissingUntil=Time.time+8;
                    if(Time.time<st.BoatMissingUntil){ai.StopMoving();Brain.Status(st,"waiting for my boat");return true;}
                }
                st.BoatMissingUntil=0;
                if(!saved.IsNone())Forget(z);
                Release(c);
                if(!RidePolicy.CanBoard(following,retreat,c.InAttack(),wanted!=null,
                    master!=null?Vector3.Distance(c.transform.position,master.transform.position):double.PositiveInfinity,
                    master!=null?Mathf.Abs(c.transform.position.y-master.transform.position.y):double.PositiveInfinity)||Time.time<st.NextBoat)return false;
                st.NextBoat=Time.time+1;
                if(!DeckSpot(wanted,master,out Vector3 local))return false;
                ZNetView view=wanted.GetComponent<ZNetView>();if(view==null||!view.IsValid())return false;
                ZDOID id=view.GetZDO().m_uid;
                z.Set(SpotKey,local);z.Set(ShipKey,id);
                // Re-establish the native link even when recalling onto the same ship after clearing it.
                z.SetConnection(ZDOExtraData.ConnectionType.SyncTransform,id);z.Set(ZDOVars.s_attachJointHash,"");
                z.Set(ZDOVars.s_relPosHash,local);z.Set(ZDOVars.s_relRotHash,Quaternion.identity);z.Set(ZDOVars.s_velRelHash,Vector3.zero);
                ship=wanted;
            }
            st.BoatMissingUntil=0;
            if(!Current(c,out Passenger ride)){Forget(z);Release(c);return false;}
            if(wanted==ship)ride.LastAboard=Time.time;
            bool waiting=z.GetBool(Companion.Waiting,false);
            if(wanted==null&&!waiting&&!RidePolicy.Grace(Time.time-ride.LastAboard)&&master!=null)
            {
                bool dry=master.IsOnGround()&&!master.IsSwimming()&&!master.IsTeleporting()&&!master.InInterior();
                if(RidePolicy.CanDisembark(!master.IsDead(),dry,Vector3.Distance(c.transform.position,master.transform.position))&&
                    LandingSpot(master,out Vector3 shore))
                {
                    Forget(z);Release(c);
                    c.transform.SetPositionAndRotation(shore,Quaternion.Euler(0,master.transform.eulerAngles.y,0));
                    Rigidbody body=c.GetComponent<Rigidbody>();body.position=shore;
                    if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
                    c.GetComponent<ZSyncTransform>()?.SyncNow();return false;
                }
            }
            ai.StopMoving();Hold(c);
            if(retreat)c.Heal(c.GetMaxHealth()*0.03f*Mathf.Min(dt,1),false);
            Brain.Status(st,retreat?"resting aboard to heal":wanted==ship?"sailing with my friend":"waiting aboard");return true;
        }
        internal static void Personality(Companion.State st,bool retreat)
        {if(Current(st.Body,out Passenger ride))Activities.Sailing(st,ride.Ship,retreat);}
        internal static void Forget(ZDO z)
        {
            // Called only by the current Gary owner, or by server recall after taking Gary ownership.
            z.Set("bob_gary_deck_yaw",0f);z.Set(ShipKey,ZDOID.None);z.Set(SpotKey,Vector3.zero);
            z.UpdateConnection(ZDOExtraData.ConnectionType.SyncTransform,ZDOID.None);z.Set(ZDOVars.s_attachJointHash,"");
            foreach(Passenger ride in new List<Passenger>(Passengers.Values))
                if(ride.Gary!=null&&Companion.Data(ride.Gary)?.m_uid==z.m_uid)Release(ride.Gary);
        }
        private static void Restore(Passenger ride)
        {
            Collider collider=ride.Gary!=null?ride.Gary.GetCollider():null;
            if(collider!=null)
                foreach(Collision pair in ride.Collisions)if(pair.Other!=null)Physics.IgnoreCollision(collider,pair.Other,pair.Ignored);
            // Locomotion was paused, so its old ground contact may still name the hull after a shore recall.
            if(ride.Gary!=null){GroundBody(ride.Gary)=null;GroundCollider(ride.Gary)=null;GroundTouch(ride.Gary)=1;}
            if(ride.Sync!=null)ride.Sync.m_characterParentSync=ride.ParentSync;
            if(ride.Body!=null){ride.Body.isKinematic=ride.Kinematic;ride.Body.useGravity=ride.Gravity;}
        }
        private static void Release(Character c)
        {if(c!=null&&Passengers.TryGetValue(c,out Passenger ride)){Restore(ride);Passengers.Remove(c);}}
        internal static void LateUpdate()
        {
            foreach(Passenger ride in new List<Passenger>(Passengers.Values))
                if(!Current(ride.Gary,out _))
                {Restore(ride);Passengers.Remove(ride.Gary);}
            foreach(Companion.State st in Companion.States.Values)if(st.Body!=null)Hold(st.Body);
        }
        internal static void Clear()
        {foreach(Passenger ride in Passengers.Values)Restore(ride);Passengers.Clear();}
    }
    [HarmonyPatch(typeof(Character),"UpdateMotion")]
    internal static class GaryPassengerMotion
    {private static bool Prefix(Character __instance)=>!BoatRide.Hold(__instance);}
    [HarmonyPatch(typeof(Character),"IsAttached")]
    internal static class GaryPassengerAttached
    {private static void Postfix(Character __instance,ref bool __result){if(BoatRide.IsRiding(__instance))__result=true;}}
    [HarmonyPatch(typeof(Character),"IsAttachedToShip")]
    internal static class GaryPassengerShip
    {private static void Postfix(Character __instance,ref bool __result){if(BoatRide.IsRiding(__instance))__result=true;}}
    [HarmonyPatch(typeof(Character),"GetRelativePosition")]
    internal static class GaryPassengerRelative
    {
        private static bool Prefix(Character __instance,ref ZDOID parent,ref string attachJoint,ref Vector3 relativePos,
            ref Quaternion relativeRot,ref Vector3 relativeVel,ref bool __result)
        {
            if(!BoatRide.Relative(__instance,out ZDOID ship,out Vector3 local,out Quaternion rotation))return true;
            parent=ship;attachJoint="";relativePos=local;relativeRot=rotation;relativeVel=Vector3.zero;__result=true;return false;
        }
    }
    [HarmonyPatch(typeof(ZSyncTransform),"ClientSync")]
    internal static class GaryPassengerClient
    {private static bool Prefix(ZSyncTransform __instance,float dt)=>!BoatRide.Client(__instance.GetComponent<Character>(),dt);}
    [HarmonyPatch(typeof(ZSyncTransform),"OwnerSync")]
    internal static class GaryPassengerOwner
    {private static void Prefix(ZSyncTransform __instance){BoatRide.Hold(__instance.GetComponent<Character>());}}
}
