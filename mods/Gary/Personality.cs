using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Personality
    {
        internal const int Pet=1,Greeting=2,Victory=3,Warning=4,Cozy=5;
        private const string MoodAt="bob_gary_mood_at",MoodKind="bob_gary_mood_kind",VibeUntil="bob_gary_vibe_until";
        private static readonly Action<BaseAI,Vector3> Look=AccessTools.MethodDelegate<Action<BaseAI,Vector3>>(AccessTools.Method(typeof(BaseAI),"LookAt"));
        internal static readonly Action<Humanoid,GameObject> Pat=AccessTools.MethodDelegate<Action<Humanoid,GameObject>>(AccessTools.Method(typeof(Humanoid),"DoInteractAnimation"));
        private static readonly AccessTools.FieldRef<Player,GameObject> Ghost=AccessTools.FieldRefAccess<Player,GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Character,HitData> LastHit=AccessTools.FieldRefAccess<Character,HitData>("m_lastHit");
        private static readonly List<GameObject> Effects=new List<GameObject>();
        internal static bool Mood(Companion.State st,int kind,string message=null)
        {
            if(kind!=Pet&&kind!=Warning&&!Plugin.Instance.Reactions.Value)return false;
            ZDO z=Companion.Data(st.Body);
            if(!Policy.MoodDue((Companion.Now-z.GetLong(MoodAt,0))/(double)TimeSpan.TicksPerSecond))return false;
            z.Set(MoodKind,kind);z.Set(MoodAt,Companion.Now);
            z.Set(VibeUntil,kind==Warning?0L:Companion.Now+TimeSpan.TicksPerSecond*2);
            if(message!=null&&Companion.Owner(st.Body)==Player.m_localPlayer)Plugin.Tell(message);
            return true;
        }
        internal static void Cancel(Companion.State st)
        {CancelVibe(st);st.ForageTarget=null;}
        internal static void CancelVibe(Companion.State st)
        {
            ZDO z=Companion.Data(st.Body);
            if(z.GetLong(VibeUntil,0)!=0)z.Set(VibeUntil,0L);
            Vibe(st,false);
        }
        private static void Vibe(Companion.State st,bool value)
        {
            if(st.VibeSupported)st.Body.GetZAnim()?.SetBool("george_vibing",value);
        }
        // Native george_vibing is not in the prefab's synchronized bool list. Apply on every observer.
        internal static void Visual(Companion.State st)
        {
            if(ZNet.instance!=null&&ZNet.instance.IsDedicated())return;
            ZDO z=Companion.Data(st.Body);long now=Companion.Now,at=z.GetLong(MoodAt,0),until=z.GetLong(VibeUntil,0);
            Vibe(st,until>now&&until-now<=TimeSpan.TicksPerSecond*3&&!z.GetBool(Companion.Retreating,false)&&!st.Body.InAttack()&&st.Body.GetVelocity().sqrMagnitude<0.1f);
            if(at!=st.SeenMood)
            {
                st.SeenMood=at;
                if(Policy.PlayRecentMood((now-at)/(double)TimeSpan.TicksPerSecond))
                {
                    int kind=z.GetInt(MoodKind,0);
                    if(kind>=Pet&&kind<=Cozy)
                    {
                        Chirp(st.Body,kind==Warning?1.1f:1.45f);
                        if(kind==Pet||kind==Greeting||kind==Victory)Sparkle(st.Body.GetCenterPoint());
                    }
                }
            }
            Effects.RemoveAll(go=>go==null);
        }
        private static void Chirp(Character body,float pitch)
        {
            foreach(EffectList.EffectData effect in body.GetComponent<MonsterAI>().m_idleSound.m_effectPrefabs)
            {
                ZSFX native=effect.m_prefab!=null?effect.m_prefab.GetComponentInChildren<ZSFX>(true):null;
                if(native==null||native.m_audioClips==null||native.m_audioClips.Length==0)continue;
                AudioClip clip=native.m_audioClips[UnityEngine.Random.Range(0,native.m_audioClips.Length)];if(clip==null)continue;
                var go=new GameObject("Gary's gentle chirp");go.transform.position=body.GetCenterPoint();
                AudioSource source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;
                source.clip=clip;source.volume=0.15f;source.pitch=pitch;source.spatialBlend=1;source.dopplerLevel=0;
                source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=2;source.maxDistance=18;
                AudioSource original=native.GetComponent<AudioSource>();if(original!=null)source.outputAudioMixerGroup=original.outputAudioMixerGroup;
                source.Play();Effects.Add(go);UnityEngine.Object.Destroy(go,clip.length/pitch+0.2f);break;
            }
        }
        private static void Sparkle(Vector3 point)
        {
            GameObject source=ZNetScene.instance.GetPrefab("fx_skeleton_pet");
            ParticleSystemRenderer native=source!=null?source.GetComponentInChildren<ParticleSystemRenderer>(true):null;
            var go=new GameObject("Gary's happy forest sparkles");go.SetActive(false);go.transform.position=point;
            ParticleSystem particles=go.AddComponent<ParticleSystem>();
            var main=particles.main;main.loop=false;main.playOnAwake=false;main.duration=0.5f;main.startLifetime=1.5f;
            main.startSpeed=0.45f;main.startSize=0.12f;main.startColor=new Color(0.8f,0.35f,1f,0.9f);main.maxParticles=20;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=particles.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,16)});
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=0.35f;
            var color=particles.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});color.color=gradient;
            if(native!=null&&native.sharedMaterial!=null)go.GetComponent<ParticleSystemRenderer>().sharedMaterial=native.sharedMaterial;
            go.SetActive(true);particles.Play();Effects.Add(go);UnityEngine.Object.Destroy(go,2.5f);
        }
        internal static void Clear()
        {
            foreach(GameObject go in Effects)if(go!=null)UnityEngine.Object.Destroy(go);
            Effects.Clear();foreach(Companion.State st in Companion.States.Values)
                if(st.Body!=null)
                {ZDO z=Companion.Data(st.Body);if(z!=null&&st.Body.GetComponent<ZNetView>().IsOwner())z.Set(VibeUntil,0L);Vibe(st,false);}
        }
        internal static void Observe(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ZDO z=Companion.Data(st.Body);
            if(master==null||master.IsDead()||master.InInterior())
            {if(!z.GetBool("bob_gary_friend_away",false))z.Set("bob_gary_friend_away",true);st.Stationary=0;return;}
            if(z.GetBool("bob_gary_friend_away",false)&&Vector3.Distance(st.Body.transform.position,master.transform.position)<8)
            {if(Mood(st,Greeting,"There you are!"))z.Set("bob_gary_friend_away",false);}
            if(Vector3.Distance(master.transform.position,st.MasterPosition)>0.15f||master.GetVelocity().sqrMagnitude>0.1f)
            {st.MasterPosition=master.transform.position;st.Stationary=0;}
            else st.Stationary+=Mathf.Min(dt,1);
        }
        internal static void EnemyDied(Character enemy)
        {
            if(enemy.IsPlayer()||enemy.IsTamed())return;
            foreach(Companion.State st in Companion.States.Values)
            {
                if(st.Body==null||Vector3.Distance(st.Body.transform.position,enemy.transform.position)>25)continue;
                bool fought=st.LastEnemy==enemy&&Time.time-st.LastFight<=5;
                Character killer=LastHit(enemy)?.GetAttacker();
                bool friendVictory=(enemy.IsBoss()||enemy.GetMaxHealth()>=300)&&killer!=null&&(killer==st.Body||killer==Companion.Owner(st.Body));
                if(!fought&&!friendVictory)continue;
                if(st.Body.GetComponent<ZNetView>().IsOwner()){st.VictoryPending=true;st.VictoryUntil=Time.time+5;}
            }
        }
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(st.VictoryPending)
            {
                st.VictoryPending=false;
                if(Time.time<=st.VictoryUntil&&Time.time>=st.NextVictory){st.NextVictory=Time.time+30;Mood(st,Victory,"We did it!");}
            }
            if(Plugin.Instance.Warnings.Value&&Time.time>=st.NextDanger)
            {
                st.NextDanger=Time.time+2;float best=16;Character danger=null;
                foreach(Character enemy in Character.GetAllCharacters())
                {
                    if(enemy==null||enemy.IsDead()||enemy.IsPlayer()||enemy.IsTamed()||!BaseAI.IsEnemy(st.Body,enemy))continue;
                    float distance=Vector3.Distance(enemy.transform.position,master.transform.position);
                    if(distance>=best||Vector3.Distance(enemy.transform.position,st.Body.transform.position)>20||!ai.CanSeeTarget(enemy))continue;
                    best=distance;danger=enemy;
                }
                if(danger!=null&&Time.time>=st.NextWarning)
                {
                    bool huge=danger.IsBoss()||danger.GetMaxHealth()>=300;
                    if(Mood(st,Warning,huge?"That one's big...":"Keep an eye out."))
                    {st.NextWarning=Time.time+45;st.LookPoint=danger.GetCenterPoint();st.LookUntil=Time.time+2;}
                }
            }
            if(Time.time<st.LookUntil&&Vector3.Distance(st.Body.transform.position,master.transform.position)<5)
            {ai.StopMoving();Look(ai,st.LookPoint);Brain.Status(st,"watching something worrying");return true;}
            ZDO z=Companion.Data(st.Body);
            if(z.GetLong(VibeUntil,0)>Companion.Now&&Vector3.Distance(st.Body.transform.position,master.transform.position)<6)
            {ai.StopMoving();Look(ai,master.GetCenterPoint());Brain.Status(st,"happy to see you");return true;}
            if(Plugin.Instance.Building.Value&&master.InPlaceMode()&&st.Stationary>=2&&Vector3.Distance(st.Body.transform.position,master.transform.position)<=12)
            {
                st.Entrance=null;st.ForageTarget=null;
                GameObject ghost=Ghost(master);Vector3 aim=ghost!=null&&ghost.activeInHierarchy?ghost.transform.position:master.transform.position+master.GetLookDir()*4;
                if(Time.time>=st.NextBuildSpot||Vector3.Distance(st.BuildOrigin,master.transform.position)>1||(st.BuildSpot!=null&&Vector3.Distance(st.BuildSpot.Value,aim)<3))
                {
                    st.NextBuildSpot=Time.time+3;st.BuildOrigin=master.transform.position;st.BuildSpot=null;
                    Vector3 forward=master.GetLookDir();forward.y=0;if(forward.sqrMagnitude<0.01f)forward=master.transform.forward;
                    for(int i=0;i<8;i++)
                    {
                        Vector3 candidate=master.transform.position+Quaternion.Euler(0,90+i*25,0)*forward.normalized*4.5f;
                        if(Vector3.Distance(candidate,aim)<3||!Nature.Ground(ai,candidate,out Vector3 spot))continue;
                        st.BuildSpot=spot;break;
                    }
                }
                if(st.BuildSpot!=null&&Utils.DistanceXZ(st.Body.transform.position,st.BuildSpot.Value)>1)
                {Cancel(st);Brain.Move(ai,dt,st.BuildSpot.Value,0.8f,false);}
                else {ai.StopMoving();Look(ai,aim);}
                Brain.Status(st,"watching you build");return true;
            }
            if(Plugin.Instance.Campfires.Value&&st.Stationary>=6)
            {
                if(Time.time>=st.NextFire)
                {
                    st.NextFire=Time.time+5;st.Fire=null;float best=7;
                    foreach(EffectArea area in EffectArea.GetAllAreas())
                    {
                        if(area==null||!area.isActiveAndEnabled||(area.m_type&EffectArea.Type.Heat)==0)continue;
                        Fireplace fire=area.GetComponentInParent<Fireplace>();
                        if(fire==null||!fire.isActiveAndEnabled||Companion.Data(fire)==null||!fire.IsBurning())continue;
                        float distance=Vector3.Distance(fire.transform.position,master.transform.position);
                        if(distance>=best)continue;
                        Vector3 toward=master.transform.position-fire.transform.position;toward.y=0;
                        if(toward.sqrMagnitude<0.01f)toward=-master.transform.forward;
                        Vector3 candidate=fire.transform.position+Quaternion.Euler(0,35,0)*toward.normalized*3;
                        if(!Nature.Ground(ai,candidate,out Vector3 spot)||Vector3.Distance(spot,master.transform.position)>8)continue;
                        best=distance;st.Fire=fire;st.FireSpot=spot;
                    }
                }
                if(st.Fire!=null&&Companion.Data(st.Fire)!=null&&st.Fire.IsBurning()&&Vector3.Distance(st.Fire.transform.position,master.transform.position)<=7)
                {
                    st.Entrance=null;st.ForageTarget=null;Brain.Status(st,"warming up by the fire");
                    if(Utils.DistanceXZ(st.Body.transform.position,st.FireSpot)>1)
                    {Cancel(st);Brain.Move(ai,dt,st.FireSpot,0.8f,false);}
                    else
                    {
                        ai.StopMoving();Look(ai,st.Fire.transform.position+Vector3.up);
                        if(Time.time>=st.NextCozy){st.NextCozy=Time.time+UnityEngine.Random.Range(35,60);Mood(st,Cozy);}
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
