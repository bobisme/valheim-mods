using System;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Brain
    {
        private static readonly Func<BaseAI,float,bool> BaseUpdate=AccessTools.MethodDelegate<Func<BaseAI,float,bool>>(AccessTools.Method(typeof(BaseAI),nameof(BaseAI.UpdateAI)),virtualCall:false);
        internal static readonly Func<BaseAI,float,Vector3,float,bool,bool> Move=AccessTools.MethodDelegate<Func<BaseAI,float,Vector3,float,bool,bool>>(AccessTools.Method(typeof(BaseAI),"MoveTo"));
        private static readonly Func<BaseAI,float,Vector3,bool> Flee=AccessTools.MethodDelegate<Func<BaseAI,float,Vector3,bool>>(AccessTools.Method(typeof(BaseAI),"Flee"));
        internal static readonly Action<BaseAI,bool> Alert=AccessTools.MethodDelegate<Action<BaseAI,bool>>(AccessTools.Method(typeof(BaseAI),"SetAlerted"));
        private static readonly Action<BaseAI,ZDOID> TargetInfo=AccessTools.MethodDelegate<Action<BaseAI,ZDOID>>(AccessTools.Method(typeof(BaseAI),"SetTargetInfo"));
        private static readonly AccessTools.FieldRef<MonsterAI,Character> Target=AccessTools.FieldRefAccess<MonsterAI,Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<MonsterAI,StaticTarget> Static=AccessTools.FieldRefAccess<MonsterAI,StaticTarget>("m_targetStatic");
        private static readonly AccessTools.FieldRef<MonsterAI,Vector3> LastTarget=AccessTools.FieldRefAccess<MonsterAI,Vector3>("m_lastKnownTargetPos");
        private static readonly AccessTools.FieldRef<MonsterAI,bool> BeenAtTarget=AccessTools.FieldRefAccess<MonsterAI,bool>("m_beenAtLastPos");
        private static readonly AccessTools.FieldRef<MonsterAI,float> SinceSensed=AccessTools.FieldRefAccess<MonsterAI,float>("m_timeSinceSensedTargetCreature");
        private static readonly string[] Foods={"Raspberry","Blueberries","Mushroom"};

        // True lets native MonsterAI handle following/combat; false means this tick was handled here.
        internal static bool BeforeUpdate(MonsterAI ai,float dt,ref bool result)
        {
            Character c=ai.GetComponent<Character>();Companion.State st=Companion.Get(c);
            ZNetView view=c.GetComponent<ZNetView>();if(!view.IsOwner())return true;
            ZDO z=view.GetZDO();Player master=Companion.Owner(c);
            if(z.GetBool(Companion.Retreating,false)&&z.GetLong(Companion.RecoverAt,0)>0&&Companion.Now>=z.GetLong(Companion.RecoverAt,0))
                c.Heal(c.GetMaxHealth(),false);
            bool retreat=Policy.Retreat(z.GetBool(Companion.Retreating,false),c.GetHealthPercentage());
            if(retreat!=z.GetBool(Companion.Retreating,false))
            {z.Set(Companion.Retreating,retreat);if(retreat)z.Set(Companion.RecoverAt,Companion.Now+TimeSpan.TicksPerMinute*3);}
            c.m_regenAllHPTime=retreat?1e9f:180;
            if(retreat)
            {
                BaseUpdate(ai,dt);SetTarget(ai,null);st.Entrance=null;
                Vector3 from=master!=null?master.transform.position:z.GetVec3("bob_gary_retreat_from",c.transform.position-c.transform.forward);
                if(master!=null)z.Set("bob_gary_retreat_from",from);
                float distance=Utils.DistanceXZ(c.transform.position,from);
                if(distance<Policy.RetreatDistance)
                {Alert(ai,true);if(distance<0.5f)from-=c.transform.forward;Flee(ai,dt,from);Status(st,"retreating to heal");}
                else {ai.StopMoving();Alert(ai,false);Status(st,"resting; coming back soon");}
                // A small trickle while fleeing prevents being stranded forever behind an impassable wall.
                c.Heal(c.GetMaxHealth()*(distance>=Policy.RetreatDistance?0.03f:0.005f)*Mathf.Min(dt,1),false);
                result=true;return false;
            }
            if(master==null||master.IsDead()||master.InInterior()||z.GetBool(Companion.Waiting,false))
            {
                BaseUpdate(ai,dt);SetTarget(ai,null);ai.SetFollowTarget(null);ai.StopMoving();st.Entrance=null;
                Status(st,master!=null&&master.InInterior()?"waiting outside":"waiting for my friend");result=true;return false;
            }
            ai.SetFollowTarget(master.gameObject);
            Character target=ThreatFor(c,master);
            if(target!=null){st.Entrance=null;Status(st,"protecting my friend");return true;}
            SetTarget(ai,null);
            Gift(st,master);
            if(Guide.Tick(st,ai,master,dt))
            {BaseUpdate(ai,dt);result=true;return false;}
            Status(st,"following");return true;
        }
        internal static Character ThreatFor(Character c,Player master)
        {
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
            Character target=master!=null&&!master.IsDead()&&!Companion.Data(c).GetBool(Companion.Retreating,false)&&!Companion.Data(c).GetBool(Companion.Waiting,false)?ThreatFor(c,master):null;
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
            Companion.ScheduleGift(z); // persist BEFORE spawning, so F6 or ownership transfer cannot repeat this gift
            string name=Foods[UnityEngine.Random.Range(0,Foods.Length)];GameObject prefab=ZNetScene.instance.GetPrefab(name);
            if(prefab==null)return;
            Vector3 start=c.GetCenterPoint()+c.transform.forward*0.8f;
            Vector3 end=master.transform.position+master.transform.right*0.8f+Vector3.up*0.3f;
            GameObject gift=UnityEngine.Object.Instantiate(prefab,start,Quaternion.identity);
            ItemDrop item=gift.GetComponent<ItemDrop>();if(item!=null)item.m_itemData.m_stack=1;
            Rigidbody body=gift.GetComponent<Rigidbody>();
            if(body!=null)
            {
                const float flight=0.8f;
                body.linearVelocity=(end-start)/flight-Physics.gravity*flight*0.5f;
                body.angularVelocity=UnityEngine.Random.insideUnitSphere*3;
            }
            if(master==Player.m_localPlayer)Plugin.Tell("Found you a snack!");
        }
    }
}
