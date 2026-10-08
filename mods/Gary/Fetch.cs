using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace Gary
{
    internal static class Fetch
    {
        private const string For="bob_gary_fetch_for",Until="bob_gary_fetch_until",Carrier="bob_gary_fetch_carrier",Master="bob_gary_fetch_master";
        private sealed class Held
        {
            internal ItemDrop Drop;internal Character Gary;internal Rigidbody Body;
            internal bool Gravity,Kinematic,Pickup;internal Collider[] Colliders;internal bool[] Enabled;
        }
        private static readonly Dictionary<ItemDrop,Held> HeldItems=new Dictionary<ItemDrop,Held>();
        private static readonly List<ItemDrop> ActiveDrops=new List<ItemDrop>();
        private static float NextThrow;
        internal static bool Active(ItemDrop drop)
        {
            ZDO z=Companion.Data(drop);if(z==null)return false;
            return !z.GetZDOID(For).IsNone()&&FunPolicy.Recent((z.GetLong(Until,0)-Companion.Now)/(double)TimeSpan.TicksPerSecond,45);
        }
        internal static void Throw(Player p)
        {
            if(!Plugin.Instance.FetchGame.Value||Time.unscaledTime<NextThrow)return;
            Character c=Activities.LocalGary(p);
            if(c==null){Plugin.Tell("Call me nearby first!");return;}
            ZDO g=Companion.Data(c);
            if(!c.GetComponent<ZNetView>().IsOwner()||g.GetBool(Companion.Retreating,false)||g.GetBool(Companion.Waiting,false)||
                BoatRide.IsRiding(c)||p.InInterior()||p.InAttack()||p.InDodge()||Brain.ThreatFor(c,p)!=null)
            {Plugin.Tell("Let's play on clear ground after things calm down.");return;}
            Companion.State st=Companion.Get(c);if(st.FetchDrop!=null){Plugin.Tell("I'm still fetching that one!");return;}
            ItemDrop.ItemData wood=p.GetInventory().GetItem("Wood",-1,true);
            if(wood==null||wood.m_stack<1||wood.m_dropPrefab==null||wood.m_dropPrefab.GetComponent<ItemDrop>()==null)
            {Plugin.Tell("Bring a piece of Wood for me to fetch.");return;}
            Vector3 direction=p.GetLookDir();direction.y=0;if(direction.sqrMagnitude<0.01f)direction=p.transform.forward;direction.Normalize();
            Vector3 target=p.transform.position+direction*9;
            if(!Nature.Ground(c.GetComponent<MonsterAI>(),target,out Vector3 landing)||!Nature.Allowed(landing,p))
            {Plugin.Tell("Aim toward clear, reachable ground about nine metres ahead.");return;}
            int mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","blocker","vehicle");
            Vector3 start=p.GetCenterPoint()+direction*0.8f;
            if(Physics.Linecast(start,landing+Vector3.up*0.8f,mask,QueryTriggerInteraction.Ignore))
            {Plugin.Tell("There's something in the way of that throw.");return;}
            ItemDrop.ItemData copy=wood.Clone();copy.m_stack=1;
            // Inventory is authoritative on this player owner. Debit before the native drop; refund only on creation failure.
            if(!p.GetInventory().RemoveItem(wood,1))return;
            ItemDrop drop=null;
            try
            {
                drop=ItemDrop.DropItem(copy,1,start,Quaternion.identity);ZDO z=Companion.Data(drop);
                if(z==null)throw new InvalidOperationException("Fetch drop has no valid world identity.");
                z.Set(Master,p.GetPlayerID());z.Set(Until,Companion.Now+TimeSpan.TicksPerSecond*35);z.Set(For,c.GetZDOID());
                drop.m_autoPickup=false;ActiveDrops.Add(drop);st.FetchDrop=drop;
                Rigidbody body=drop.GetComponent<Rigidbody>();
                if(body!=null){const float flight=1;body.linearVelocity=(landing-start)/flight-Physics.gravity*flight*0.5f;body.angularVelocity=UnityEngine.Random.insideUnitSphere*4;}
                NextThrow=Time.unscaledTime+5;Plugin.Tell("Fetch! (Your Wood comes back as a normal item.)");
            }
            catch(Exception e)
            {
                // A real, valid drop is never refunded as well as left in the world.
                if(drop==null||Companion.Data(drop)==null)
                {if(drop!=null)UnityEngine.Object.Destroy(drop.gameObject);if(!p.GetInventory().AddItem(copy))ItemDrop.DropItem(copy,1,p.transform.position+Vector3.up,Quaternion.identity);}
                Plugin.Instance.Error(e);
            }
        }
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(!Plugin.Instance.FetchGame.Value){Cancel(st);return false;}
            if(st.FetchDrop==null&&Time.time>=st.NextFetchScan)
            {
                st.NextFetchScan=Time.time+2;
                foreach(ItemDrop item in ActiveDrops)
                {
                    ZDO z=Companion.Data(item);
                    if(Active(item)&&z.GetZDOID(For)==st.Body.GetZDOID()&&z.GetLong(Master,0)==master.GetPlayerID())
                    {st.FetchDrop=item;break;}
                }
            }
            ItemDrop drop=st.FetchDrop;if(drop==null)return false;
            ZDO data=Companion.Data(drop);ZNetView view=drop.GetComponent<ZNetView>();
            if(!Active(drop)||data.GetZDOID(For)!=st.Body.GetZDOID()||data.GetLong(Master,0)!=master.GetPlayerID()||
                !FunPolicy.FetchReach(Vector3.Distance(drop.transform.position,master.transform.position))||drop.InTar()||
                !view.IsOwner()||drop.m_itemData.m_stack!=1||data.GetPrefab()!="Wood".GetStableHashCode())
            {Cancel(st);return false;}
            bool carrying=data.GetZDOID(Carrier)==st.Body.GetZDOID();
            if(carrying)
            {
                if(Vector3.Distance(st.Body.transform.position,master.transform.position)>2.2f)
                {Brain.Move(ai,dt,master.transform.position,1.7f,true);Brain.Status(st,"bringing your stick back");}
                else
                {
                    Release(drop,true);st.FetchDrop=null;ai.StopMoving();st.PoseToFriend=false;Activities.Pose(st,Activities.Proud,10);
                    Personality.Mood(st,Personality.Pet,"I brought it back! A pat?");Brain.Status(st,"proud of my fetch; a pat?");
                }
                return true;
            }
            if(!Nature.HavePath(ai,drop.transform.position)||!Nature.Allowed(drop.transform.position,master)){Cancel(st);return false;}
            st.Entrance=null;st.ForageTarget=null;Personality.CancelVibe(st);
            if(Vector3.Distance(st.Body.transform.position,drop.transform.position)>1.6f)
            {Brain.Move(ai,dt,drop.transform.position,1.3f,true);Brain.Status(st,"chasing your stick");}
            else if(drop.CanPickup())
            {data.Set(Carrier,st.Body.GetZDOID());Hold(drop,st.Body);Brain.Status(st,"found your stick");}
            return true;
        }
        private static void Hold(ItemDrop drop,Character c)
        {
            if(!HeldItems.TryGetValue(drop,out Held held))
            {
                Rigidbody body=drop.GetComponent<Rigidbody>();if(body==null)return;
                Collider[] colliders=drop.GetComponentsInChildren<Collider>(true);var enabled=new bool[colliders.Length];
                for(int i=0;i<colliders.Length;i++){enabled[i]=colliders[i].enabled;colliders[i].enabled=false;}
                held=new Held{Drop=drop,Gary=c,Body=body,Gravity=body.useGravity,Kinematic=body.isKinematic,Pickup=drop.m_autoPickup,Colliders=colliders,Enabled=enabled};HeldItems.Add(drop,held);
                if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
                body.isKinematic=true;body.useGravity=false;drop.m_autoPickup=false;
            }
            Vector3 point=GaryVisuals.Hand(c);drop.transform.SetPositionAndRotation(point,c.transform.rotation);
            held.Body.position=point;held.Body.rotation=c.transform.rotation;
        }
        private static void Restore(ItemDrop drop)
        {
            if(!HeldItems.TryGetValue(drop,out Held held))return;
            if(held.Drop!=null)held.Drop.m_autoPickup=held.Pickup;
            if(held.Body!=null){held.Body.isKinematic=held.Kinematic;held.Body.useGravity=held.Gravity;}
            for(int i=0;i<held.Colliders.Length;i++)if(held.Colliders[i]!=null)held.Colliders[i].enabled=held.Enabled[i];
            HeldItems.Remove(drop);
        }
        private static void Release(ItemDrop drop,bool returned)
        {
            ZDO z=Companion.Data(drop);Character c=HeldItems.TryGetValue(drop,out Held held)?held.Gary:null;
            Restore(drop);
            if(drop==null||z==null||!drop.GetComponent<ZNetView>().IsOwner())return;
            z.Set(Carrier,ZDOID.None);z.Set(For,ZDOID.None);z.Set(Until,0L);drop.m_autoPickup=true;
            if(c!=null)
            {
                Vector3 point=c.transform.position+c.transform.forward*1.3f+Vector3.up*0.7f;
                drop.transform.position=point;Rigidbody body=drop.GetComponent<Rigidbody>();
                if(body!=null&&!body.isKinematic){body.position=point;body.linearVelocity=c.transform.forward*(returned?2:0);body.angularVelocity=Vector3.zero;}
                drop.GetComponent<ZSyncTransform>()?.SyncNow();
            }
        }
        internal static void Cancel(Companion.State st)
        {if(st.FetchDrop!=null)Release(st.FetchDrop,false);st.FetchDrop=null;}
        internal static void Observe(ItemDrop drop)
        {if(Active(drop)&&!ActiveDrops.Contains(drop))ActiveDrops.Add(drop);}
        internal static void Scan()
        {
            ActiveDrops.Clear();
            foreach(ItemDrop drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                ZDO z=Companion.Data(drop);if(z==null||z.GetZDOID(For).IsNone())continue;
                if(Active(drop))ActiveDrops.Add(drop);
                else Release(drop,false);
            }
        }
        internal static void LateUpdate()
        {
            foreach(ItemDrop item in ActiveDrops)
            {
                ZDO z=Companion.Data(item);if(!Active(item)||z==null){Restore(item);continue;}
                ZDOID id=z.GetZDOID(Carrier);GameObject go=!id.IsNone()?ZNetScene.instance.FindInstance(id):null;
                Character c=go!=null?go.GetComponent<Character>():null;
                if(c!=null&&Companion.Is(c)&&z.GetZDOID(For)==id&&Companion.Data(c).GetOwner()==z.GetOwner())Hold(item,c);
                else if(!id.IsNone()&&item.GetComponent<ZNetView>().IsOwner())Release(item,false);
                else Restore(item);
            }
            foreach(ItemDrop item in new List<ItemDrop>(HeldItems.Keys))
                if(item==null||!Active(item)||Companion.Data(item).GetZDOID(Carrier).IsNone())Restore(item);
        }
        internal static bool CanStack(ItemDrop item)
        {
            if(Active(item))return false;
            foreach(ItemDrop other in ActiveDrops)
                if(Active(other)&&Vector3.Distance(item.transform.position,other.transform.position)<=4.5f)return false;
            return true;
        }
        internal static bool IsCarried(ItemDrop item)=>Active(item)&&!Companion.Data(item).GetZDOID(Carrier).IsNone();
        internal static void Clear()
        {
            // Release actual Wood before removing hooks; F6 must never leave an invisible escrow or frozen item.
            foreach(ItemDrop drop in new List<ItemDrop>(ActiveDrops))Release(drop,false);
            foreach(ItemDrop drop in new List<ItemDrop>(HeldItems.Keys))Restore(drop);
            ActiveDrops.Clear();NextThrow=0;
        }
    }
    [HarmonyPatch(typeof(ItemDrop),"Start")]
    internal static class GaryFetchLoaded
    {private static void Postfix(ItemDrop __instance)=>Fetch.Observe(__instance);}
    [HarmonyPatch(typeof(ItemDrop),"AutoStackItems")]
    internal static class GaryFetchStack
    {private static bool Prefix(ItemDrop __instance)=>Fetch.CanStack(__instance);}
    [HarmonyPatch(typeof(ItemDrop),nameof(ItemDrop.CanPickup))]
    internal static class GaryFetchPickup
    {private static bool Prefix(ItemDrop __instance,ref bool __result){if(!Fetch.IsCarried(__instance))return true;__result=false;return false;}}
}
