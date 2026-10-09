using System;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Nature
    {
        internal static readonly Func<BaseAI,Vector3,bool> HavePath=AccessTools.MethodDelegate<Func<BaseAI,Vector3,bool>>(AccessTools.Method(typeof(BaseAI),"HavePath"));
        private static readonly Func<BaseAI,float,Vector3,bool> Flee=AccessTools.MethodDelegate<Func<BaseAI,float,Vector3,bool>>(AccessTools.Method(typeof(BaseAI),"Flee"));
        private static System.Collections.Generic.List<PrivateArea> Wards() => AccessTools.StaticFieldRefAccess<System.Collections.Generic.List<PrivateArea>>(typeof(PrivateArea),"m_allAreas");
        private static readonly Func<PrivateArea,bool> Enabled=AccessTools.MethodDelegate<Func<PrivateArea,bool>>(AccessTools.Method(typeof(PrivateArea),"IsEnabled"));
        private static readonly Func<PrivateArea,Vector3,float,bool> Inside=AccessTools.MethodDelegate<Func<PrivateArea,Vector3,float,bool>>(AccessTools.Method(typeof(PrivateArea),"IsInside"));
        private static readonly Func<PrivateArea,long,bool> Permitted=AccessTools.MethodDelegate<Func<PrivateArea,long,bool>>(AccessTools.Method(typeof(PrivateArea),"IsPermitted"));
        internal static readonly string[] Gifts={"Raspberry","Blueberries","Mushroom","Feathers"};
        internal const string GiftDrop="bob_gary_gift_drop";
        private static readonly string[] Plants={"RaspberryBush","BlueberryBush","Pickable_Mushroom"};
        private static readonly string[] Pocket={"bob_gary_raspberries","bob_gary_blueberries","bob_gary_mushrooms","bob_gary_feathers"};
        internal static ForestStash Load(ZDO z) => new ForestStash(z.GetInt(Pocket[0],0),z.GetInt(Pocket[1],0),z.GetInt(Pocket[2],0),z.GetInt(Pocket[3],0));
        internal static void Save(ZDO z,ForestStash stash)
        {z.Set(Pocket[0],stash.Berries);z.Set(Pocket[1],stash.Blueberries);z.Set(Pocket[2],stash.Mushrooms);z.Set(Pocket[3],stash.Feathers);}
        // Items on the ground near a point, from the physics "item" layer the game's own auto-pickup uses: a scan of every loaded item
        // in the world costs ~10 ms, this a fraction of one.
        private static readonly Collider[] Hits=new Collider[512];
        private static readonly int ItemLayer=LayerMask.GetMask("item");
        // Any kind of thing near a point (plants, trees, stumps, item stands), through the physics scene instead of every loaded object.
        private static readonly Collider[] Wide=new Collider[4096];
        internal static System.Collections.Generic.List<T> Near<T>(Vector3 center,float radius) where T:Component
        {
            var found=new System.Collections.Generic.HashSet<T>();
            int n=Physics.OverlapSphereNonAlloc(center,radius,Wide,~0,QueryTriggerInteraction.Collide);
            for(int i=0;i<n;i++){T t=Wide[i]!=null?Wide[i].GetComponentInParent<T>():null;if(t!=null)found.Add(t);}
            return new System.Collections.Generic.List<T>(found);
        }
        internal static System.Collections.Generic.List<ItemDrop> ItemsNear(Vector3 center,float radius)
        {
            var found=new System.Collections.Generic.List<ItemDrop>();
            int n=Physics.OverlapSphereNonAlloc(center,radius,Hits,ItemLayer,QueryTriggerInteraction.Collide);
            for(int i=0;i<n;i++)
            {
                ItemDrop drop=Hits[i]!=null?Hits[i].GetComponentInParent<ItemDrop>():null;
                if(drop!=null&&!found.Contains(drop))found.Add(drop);
            }
            return found;
        }
        internal static bool Ground(MonsterAI ai,Vector3 candidate,out Vector3 spot)
        {
            spot=candidate;
            if(!ZoneSystem.instance.GetGroundHeight(candidate,out float y))return false;
            // Existing floors/decks can be above terrain. Sample beneath the candidate's walking height, not the roof above it.
            if(Physics.Raycast(candidate+Vector3.up*1.5f,Vector3.down,out RaycastHit floor,15,LayerMask.GetMask("piece","vehicle"),QueryTriggerInteraction.Ignore)&&floor.normal.y>=0.7f)
                y=Mathf.Max(y,floor.point.y);
            if(y<ZoneSystem.instance.m_waterLevel+0.2f||Mathf.Abs(y-ai.transform.position.y)>12)return false;
            spot.y=y+0.1f;
            int mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","blocker","vehicle");
            if(Physics.CheckCapsule(spot+Vector3.up*0.6f,spot+Vector3.up*1.45f,0.45f,mask,QueryTriggerInteraction.Ignore))return false;
            if(EffectArea.IsPointInsideArea(spot,EffectArea.Type.Burning,0.7f)!=null)return false;
            return HavePath(ai,spot);
        }
        internal static bool Allowed(Vector3 position,Player master)
        {
            bool protectedGround=false;
            foreach(PrivateArea ward in Wards())
            {
                if(ward==null||!Enabled(ward)||!Inside(ward,position,0))continue;
                protectedGround=true;Piece piece=ward.GetComponent<Piece>();
                if((piece!=null&&piece.GetCreator()==master.GetPlayerID())||Permitted(ward,master.GetPlayerID()))return true;
            }
            return !protectedGround;
        }
        private static int Kind(Pickable p)
        {
            ZDO z=Companion.Data(p);
            if(z==null||p.m_itemPrefab==null)return -1;
            for(int i=0;i<Plants.Length;i++)
                if(z.GetPrefab()==Plants[i].GetStableHashCode()&&p.m_itemPrefab.name==Gifts[i])return i;
            return -1;
        }
        private static bool FreeToPick(Pickable p,Player master)
        {
            if(p==null||!p.isActiveAndEnabled||p.GetComponent<Piece>()!=null||p.GetComponent<Plant>()!=null||p.m_hideWhenPicked==null||
                p.m_extraDrops.m_drops.Count!=0||p.m_aggravateRange!=0||p.m_tarPreventsPicking||p.GetPicked()||!p.CanBePicked()||p.GetEnabled!=1||Kind(p)<0)return false;
            ZNetView view=p.GetComponent<ZNetView>();
            if(view==null||!view.IsValid()||!view.IsOwner()||!Allowed(p.transform.position,master))return false;
            foreach(Player player in Player.GetAllPlayers())
                if(player!=null&&!player.IsDead()&&Vector3.Distance(player.transform.position,p.transform.position)<4)return false;
            return true;
        }
        private static bool GatherFeather(Companion.State st,MonsterAI ai,Player master,ZDO z)
        {
            if(!Load(z).TryAdd(3,1,out ForestStash next))return false;
            foreach(ItemDrop drop in ItemsNear(st.Body.transform.position,2.5f))
            {
                if(drop==null||!drop.isActiveAndEnabled||!drop.m_autoPickup||drop.IsPiece()||drop.InTar()||
                    Vector3.Distance(drop.transform.position,st.Body.transform.position)>2||
                    Vector3.Distance(drop.transform.position,master.transform.position)>12||
                    EffectArea.IsPointInsideArea(drop.transform.position,EffectArea.Type.PlayerBase)!=null||!Allowed(drop.transform.position,master))continue;
                ZNetView view=drop.GetComponent<ZNetView>();
                if(view==null||!view.IsValid()||!view.IsOwner()||view.GetZDO().GetPrefab()!=Gifts[3].GetStableHashCode()||
                    view.GetZDO().GetBool(GiftDrop,false)||!drop.CanPickup())continue;
                bool nearPlayer=false;
                foreach(Player player in Player.GetAllPlayers())
                    if(player!=null&&!player.IsDead()&&Vector3.Distance(player.transform.position,drop.transform.position)<4){nearPlayer=true;break;}
                if(nearPlayer)continue;
                drop.Load();
                ItemDrop.ItemData item=drop.m_itemData;
                // Count-only pockets must never erase custom item metadata or quality/world differences.
                if(item==null||item.m_dropPrefab==null||item.m_dropPrefab.name!=Gifts[3]||item.m_stack<=0||item.m_quality!=1||
                    item.m_variant!=0||item.m_worldLevel!=Game.m_worldLevel||item.m_cheated||item.m_customData==null||item.m_customData.Count!=0)continue;
                // Already owned, so RemoveOne cannot request ownership. Deplete the real stack before crediting the stash.
                if(!drop.RemoveOne())continue;
                Save(z,next);st.NextForage=Time.time+30;ai.StopMoving();Brain.Status(st,"gathering a feather");return true;
            }
            return false;
        }
        internal static bool Forage(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ZDO z=Companion.Data(st.Body);
            if(!Plugin.Instance.Gifts.Value||Load(z).Count>=ForestStash.Capacity||Vector3.Distance(st.Body.transform.position,master.transform.position)>12)
            {st.ForageTarget=null;return false;}
            if(st.ForageTarget==null&&Time.time>=st.NextForage)
            {
                st.NextForage=Time.time+8;
                if(GatherFeather(st,ai,master,z))return true;
                float best=10;
                foreach(Pickable p in Near<Pickable>(master.transform.position,12))
                {
                    if(p==null||Vector3.Distance(p.transform.position,master.transform.position)>12)continue;
                    float distance=Vector3.Distance(p.transform.position,st.Body.transform.position);
                    if(distance>=best||p==st.ShownTarget||!FreeToPick(p,master)||!HavePath(ai,p.transform.position))continue; // never eats what he is showing you
                    best=distance;st.ForageTarget=p;
                }
                if(st.ForageTarget!=null){st.ForageUntil=Time.time+12;st.StuckTime=0;st.LastPosition=st.Body.transform.position;}
            }
            Pickable target=st.ForageTarget;if(target==null)return false;
            if(Time.time>st.ForageUntil||!FreeToPick(target,master)||Vector3.Distance(target.transform.position,master.transform.position)>12)
            {st.ForageTarget=null;return false;}
            Brain.Status(st,"gathering a forest snack");
            if(Vector3.Distance(target.transform.position,st.Body.transform.position)>2)
            {
                Brain.Move(ai,dt,target.transform.position,1.5f,false);
                if(Vector3.Distance(st.Body.transform.position,st.LastPosition)<0.1f)st.StuckTime+=dt;
                else {st.StuckTime=0;st.LastPosition=st.Body.transform.position;}
                if(st.StuckTime>4){st.ForageTarget=null;st.NextForage=Time.time+15;}
                return true;
            }
            int kind=Kind(target);
            int amount=target.m_dontScale?target.m_amount:Mathf.Max(target.m_minAmountScaled,Game.instance.ScaleDrops(target.m_itemPrefab,target.m_amount));
            if(Load(z).TryAdd(kind,amount,out ForestStash next))
            {
                // Both objects have this simulator as owner. No awaits/RPCs before the native pick is committed.
                // Commit depletion BEFORE stash credit; a concurrent player pick then sees m_picked and produces no extra food.
                ZNetView view=target.GetComponent<ZNetView>();target.SetPicked(true);Save(z,next);
                view.InvokeRPC(ZNetView.Everybody,"RPC_SetPicked",true);
                st.NextForage=Time.time+30;
            }
            st.ForageTarget=null;ai.StopMoving();return true;
        }
        internal static void Retreat(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            Character c=st.Body;ZDO z=Companion.Data(c);
            Vector3 friend=master!=null?master.transform.position:z.GetVec3("bob_gary_retreat_from",c.transform.position);
            if(master!=null||!z.GetBool("bob_gary_retreat_anchor",false))
            {z.Set("bob_gary_retreat_from",friend);z.Set("bob_gary_retreat_anchor",true);}
            if(Time.time>=st.NextRetreatSpot)
            {
                st.NextRetreatSpot=Time.time+5;
                Vector3? danger=null;float near=25;
                foreach(Character enemy in Character.GetAllCharacters())
                {
                    if(enemy==null||enemy.IsDead()||enemy.IsPlayer()||enemy.IsTamed()||!BaseAI.IsEnemy(c,enemy))continue;
                    float distance=Vector3.Distance(enemy.transform.position,c.transform.position);
                    if(distance>=near||!ai.CanSeeTarget(enemy))continue;
                    near=distance;danger=enemy.transform.position;
                }
                if(danger!=null){z.Set("bob_gary_danger",danger.Value);z.Set("bob_gary_danger_time",Companion.Now);}
                else if(Companion.Now-z.GetLong("bob_gary_danger_time",0)<TimeSpan.TicksPerSecond*30)danger=z.GetVec3("bob_gary_danger",c.transform.position);
                st.RetreatDanger=danger;st.RetreatSpot=null;double best=double.NegativeInfinity;
                for(int i=0;i<16;i++)
                {
                    Vector3 candidate=c.transform.position+Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*42;
                    if(!Ground(ai,candidate,out Vector3 spot))continue;
                    double distance=Utils.DistanceXZ(spot,friend),threat=danger!=null?Utils.DistanceXZ(spot,danger.Value):100;
                    bool cover=danger!=null&&Physics.Linecast(danger.Value+Vector3.up,spot+Vector3.up,LayerMask.GetMask("terrain","piece","static_solid"),QueryTriggerInteraction.Ignore);
                    double score=Policy.RestSpotScore(distance,threat,cover);
                    if(score>best){best=score;st.RetreatSpot=spot;}
                }
            }
            float fromFriend=Utils.DistanceXZ(c.transform.position,friend);
            float fromDanger=st.RetreatDanger!=null?Utils.DistanceXZ(c.transform.position,st.RetreatDanger.Value):100;
            bool safe=Policy.SafeRest(fromFriend,fromDanger);
            if(safe){ai.StopMoving();Brain.Alert(ai,false);Brain.Status(st,"resting in a safe spot; coming back soon");}
            else
            {
                Brain.Alert(ai,true);
                if(st.RetreatSpot!=null)Brain.Move(ai,dt,st.RetreatSpot.Value,1.5f,true);
                else
                {
                    Vector3 away=(c.transform.position-friend).normalized;
                    if(st.RetreatDanger!=null)away+=(c.transform.position-st.RetreatDanger.Value).normalized*2;
                    if(away.sqrMagnitude<0.01f)away=-c.transform.forward;
                    Flee(ai,dt,c.transform.position-away.normalized*10);
                }
                Brain.Status(st,"escaping danger to heal");
            }
            // Keep the original blocked-route trickle so an unreachable refuge never strands him.
            c.Heal(c.GetMaxHealth()*(safe?0.03f:0.005f)*Mathf.Min(dt,1),false);
        }
    }
}
