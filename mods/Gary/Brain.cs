using System;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Brain
    {
        private static readonly Func<BaseAI,float,bool> BaseUpdate=AccessTools.MethodDelegate<Func<BaseAI,float,bool>>(AccessTools.Method(typeof(BaseAI),nameof(BaseAI.UpdateAI)),virtualCall:false);
        internal static readonly Func<BaseAI,float,Vector3,float,bool,bool> Move=AccessTools.MethodDelegate<Func<BaseAI,float,Vector3,float,bool,bool>>(AccessTools.Method(typeof(BaseAI),"MoveTo"));
        internal static readonly Action<BaseAI,bool> Alert=AccessTools.MethodDelegate<Action<BaseAI,bool>>(AccessTools.Method(typeof(BaseAI),"SetAlerted"));
        private static readonly Action<BaseAI,ZDOID> TargetInfo=AccessTools.MethodDelegate<Action<BaseAI,ZDOID>>(AccessTools.Method(typeof(BaseAI),"SetTargetInfo"));
        private static readonly AccessTools.FieldRef<MonsterAI,Character> Target=AccessTools.FieldRefAccess<MonsterAI,Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<MonsterAI,StaticTarget> Static=AccessTools.FieldRefAccess<MonsterAI,StaticTarget>("m_targetStatic");
        private static readonly AccessTools.FieldRef<MonsterAI,Vector3> LastTarget=AccessTools.FieldRefAccess<MonsterAI,Vector3>("m_lastKnownTargetPos");
        private static readonly AccessTools.FieldRef<MonsterAI,bool> BeenAtTarget=AccessTools.FieldRefAccess<MonsterAI,bool>("m_beenAtLastPos");
        private static readonly AccessTools.FieldRef<MonsterAI,float> SinceSensed=AccessTools.FieldRefAccess<MonsterAI,float>("m_timeSinceSensedTargetCreature");

        // True lets native MonsterAI handle following/combat; false means this tick was handled here.
        internal static bool BeforeUpdate(MonsterAI ai,float dt,ref bool result)
        {
            Character c=ai.GetComponent<Character>();Companion.State st=Companion.Get(c);
            ZNetView view=c.GetComponent<ZNetView>();Personality.Visual(st);if(!view.IsOwner())return true;
            ZDO z=view.GetZDO();Player master=Companion.Owner(c);
            if(z.GetBool(Companion.Retreating,false)&&z.GetLong(Companion.RecoverAt,0)>0&&Companion.Now>=z.GetLong(Companion.RecoverAt,0))
                c.Heal(c.GetMaxHealth(),false);
            bool retreat=Policy.Retreat(z.GetBool(Companion.Retreating,false),c.GetHealthPercentage());
            if(retreat!=z.GetBool(Companion.Retreating,false))
            {z.Set(Companion.Retreating,retreat);if(retreat)z.Set(Companion.RecoverAt,Companion.Now+TimeSpan.TicksPerMinute*3);}
            c.m_regenAllHPTime=retreat?1e9f:180;
            if(BoatRide.Tick(st,ai,master,dt,retreat))
            {
                BaseUpdate(ai,dt);SetTarget(ai,null);ai.SetFollowTarget(null);st.Entrance=null;Personality.Cancel(st);
                if(!st.WasRiding||retreat)Activities.Cancel(st);else {Fetch.Cancel(st);st.ShowStarted=0;}
                st.WasRiding=true;BoatRide.Personality(st,retreat);result=true;return false;
            }
            if(st.WasRiding){Activities.Cancel(st);st.WasRiding=false;}
            if(retreat)
            {
                BaseUpdate(ai,dt);SetTarget(ai,null);ai.SetFollowTarget(null);st.Entrance=null;Personality.Cancel(st);Activities.Cancel(st);
                Nature.Retreat(st,ai,master,dt);
                result=true;return false;
            }
            bool follow=CanFollow(master,z);
            ai.SetFollowTarget(follow?master.gameObject:null);
            Character target=ThreatFor(c,follow?master:null);
            if(target!=null)
            {st.Entrance=null;st.LastEnemy=target;st.LastFight=Time.time;Personality.Cancel(st);Activities.Cancel(st);Status(st,target==st.SelfAttacker?"defending myself":"protecting my friend");return true;}
            SetTarget(ai,null);
            Personality.Observe(st,ai,master,dt);
            if(!follow)
            {
                BaseUpdate(ai,dt);ai.StopMoving();st.Entrance=null;st.ForageTarget=null;Activities.Cancel(st);
                Status(st,master!=null&&master.InInterior()?"waiting outside":"waiting for my friend");result=true;return false;
            }
            Gift(st,master);
            if(Activities.Tick(st,ai,master,dt)){BaseUpdate(ai,dt);result=true;return false;}
            if(Personality.Tick(st,ai,master,dt)){BaseUpdate(ai,dt);result=true;return false;}
            if(Guide.Tick(st,ai,master,dt))
            {Personality.CancelVibe(st);BaseUpdate(ai,dt);result=true;return false;}
            if(Nature.Forage(st,ai,master,dt)){Personality.CancelVibe(st);BaseUpdate(ai,dt);result=true;return false;}
            Personality.CancelVibe(st);Status(st,"following");return true;
        }
        private static bool CanFollow(Player master,ZDO z) =>
            master!=null&&!master.IsDead()&&!master.InInterior()&&!z.GetBool(Companion.Waiting,false);
        internal static Character ThreatFor(Character c,Player master)
        {
            Companion.State st=Companion.Get(c);Character self=st.SelfAttacker;
            // Native OnDamaged chooses an attacker, but our target override must retain that verified threat.
            // Ownership changes and hot reload discard this local combat memory rather than persisting an attacker ID.
            if(self!=null && st.SelfThreatOwner==Companion.Data(c).GetOwner() &&
                Policy.FreshSelfThreat((Companion.Now-st.SelfThreatAt)/(double)TimeSpan.TicksPerSecond,
                    Vector3.Distance(self.transform.position,c.transform.position),Vector3.Distance(self.transform.position,st.SelfThreatOrigin),
                    BaseAI.IsEnemy(c,self),self.IsPlayer()||self.IsTamed(),self.IsDead()))return self;
            st.SelfAttacker=null;
            ZDO player=Companion.Data(master);if(player==null)return null;
            // The player ZDO is ephemeral. Bind its threat ID to this session's owner, never save a ZDOID in a player profile.
            if(player.GetLong(Companion.ThreatOwner,0)!=player.GetOwner())return null;
            ZDOID id=player.GetZDOID(Companion.Threat);
            GameObject go=ZNetScene.instance.FindInstance(id);Character target=go!=null?go.GetComponent<Character>():null;
            if(target==null)return null;
            double age=(Companion.Now-player.GetLong(Companion.ThreatAt,0))/(double)TimeSpan.TicksPerSecond;
            return Policy.FreshThreat(age,Vector3.Distance(target.transform.position,master.transform.position),Vector3.Distance(target.transform.position,c.transform.position),
                BaseAI.IsEnemy(master,target),target.IsPlayer()||target.IsTamed(),target.IsDead())?target:null;
        }
        internal static void UpdateTarget(MonsterAI ai,float dt,out bool hear,out bool see)
        {
            Character c=ai.GetComponent<Character>();Player master=Companion.Owner(c);
            ZDO z=Companion.Data(c);
            Character target=!z.GetBool(Companion.Retreating,false)?ThreatFor(c,CanFollow(master,z)?master:null):null;
            SetTarget(ai,target);hear=target!=null&&ai.CanHearTarget(target);see=target!=null&&ai.CanSeeTarget(target);
            SinceSensed(ai)=hear||see?0:SinceSensed(ai)+dt;
        }
        private static void SetTarget(MonsterAI ai,Character target)
        {
            if(Target(ai)!=target && target!=null){LastTarget(ai)=target.transform.position;BeenAtTarget(ai)=false;}
            Target(ai)=target;Static(ai)=null;TargetInfo(ai,target!=null?target.GetZDOID():ZDOID.None);Alert(ai,target!=null);
        }
        internal static void Status(Companion.State st,string status)
        {st.Status=status;ZDO z=Companion.Data(st.Body);if(z.GetString("bob_gary_status","")!=status)z.Set("bob_gary_status",status);}
        private static void Gift(Companion.State st,Player master)
        {
            if(!Plugin.Instance.Gifts.Value)return;
            Character c=st.Body;ZDO z=Companion.Data(c);
            if(!Policy.CanGift(z.GetBool(Companion.Retreating,false),c.InAttack(),z.GetBool(Companion.Waiting,false),master.InInterior(),
                Vector3.Distance(c.transform.position,master.transform.position),(z.GetLong(Companion.GiftAt,0)-Companion.Now)/(double)TimeSpan.TicksPerSecond))return;
            ForestStash stash=Nature.Load(z);if(stash.Count==0||st.FetchDrop!=null||Activities.Active(st,Activities.Curl))return;
            int kind=st.ShowStarted!=0&&stash.At(st.ShowGiftKind)>0?st.ShowGiftKind:stash.GiftKind(UnityEngine.Random.Range(0,100));if(kind<0)return;
            GameObject prefab=ZNetScene.instance.GetPrefab(Nature.Gifts[kind]);if(prefab==null||prefab.GetComponent<ItemDrop>()==null)return;
            if(!Activities.ShowGift(st,master,kind)||!stash.TryTake(kind,out ForestStash next))return;
            Nature.Save(z,next);Companion.ScheduleGift(z); // spend BEFORE spawning; F6 cannot create another copy
            Vector3 start=c.GetCenterPoint()+c.transform.forward*0.8f;
            Vector3 end=master.transform.position+master.transform.right*0.8f+Vector3.up*0.3f;
            GameObject gift=UnityEngine.Object.Instantiate(prefab,start,Quaternion.identity);
            ItemDrop item=gift.GetComponent<ItemDrop>();if(item!=null){item.SetStack(1);ItemDrop.OnCreateNew(item);}
            ZDO drop=Companion.Data(gift.transform);if(drop!=null)drop.Set(Nature.GiftDrop,true);
            Rigidbody body=gift.GetComponent<Rigidbody>();
            if(body!=null)
            {
                const float flight=0.8f;
                body.linearVelocity=(end-start)/flight-Physics.gravity*flight*0.5f;
                body.angularVelocity=UnityEngine.Random.insideUnitSphere*3;
            }
            if(master==Player.m_localPlayer)Plugin.Tell(kind==3?"Found you a feather!":"Found you a snack!");
        }
    }
}
