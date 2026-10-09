using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Companion
    {
        internal const string Master="bob_gary_master",Waiting="bob_gary_wait",Retreating="bob_gary_retreat",RecoverAt="bob_gary_recover",GiftAt="bob_gary_gift",Seen="bob_gary_seen";
        internal const string Threat="bob_gary_threat",ThreatAt="bob_gary_threat_time",ThreatOwner="bob_gary_threat_owner";
        internal sealed class State
        {
            internal Character Body;
            internal bool EmoteObserved,WasWet,WasRiding,PoseToFriend;
            internal int LastEmote,ShowGiftKind;
            internal long ShowStarted;
            internal float NextEmote,NextShelter,NextFriend,NextFriendGreeting,NextImitate,NextBird,NextSailChirp,NextFetchScan;
            internal Vector3? Shelter;
            internal Character WorkFriend;
            internal ItemDrop FetchDrop;
            internal float FetchPathWait;
            internal bool OwnerConfigured,VibeSupported,VictoryPending;
            internal long SeenMood;
            internal float Stationary,NextForage,ForageUntil,NextRetreatSpot,NextDanger,NextWarning,NextVictory,NextCozy,NextFire,NextBuildSpot,LookUntil,LastFight,VictoryUntil;
            internal Vector3 MasterPosition,FireSpot,BuildOrigin,LookPoint;
            internal Vector3? RetreatSpot,RetreatDanger,BuildSpot;
            internal Pickable ForageTarget;
            internal Fireplace Fire;
            internal Character LastEnemy;
            internal string Status="following";
            internal float BoatMissingUntil,NextBoat,NextGuide,GuideUntil,StuckTime,LastError;
            // Local, short-lived combat memory: do not save attacker IDs in Gary's persistent ZDO.
            internal Character SelfAttacker;
            internal long SelfThreatAt,SelfThreatOwner;
            internal Vector3 SelfThreatOrigin;
            internal Vector3? Entrance,PlantSpot;
            internal int PlantKind;
            internal bool PlantSeed;
            // Caretaking errands (Chores): one at a time.
            internal int Errand;internal Component ErrandTarget;internal Vector3 ErrandSpot;internal float ErrandUntil,ErrandStarted,NextErrand;
            internal Replanting.Spot ErrandCrop;internal Pickable ShownTarget;
            internal float NextDeadfall,NextTrophy,NextTreeTalk,NextShowSpot,NextWave,NextSense,NextSky,TimberUntil,NextTimberShout;
            internal Vector3 TimberFrom;
            internal float NextPlantLook,PlantUntil,PlantStarted;
            internal string EntranceId;
            internal Vector3 LastPosition,GuideInterior;
            internal readonly Dictionary<Renderer,MaterialPropertyBlock> Original=new Dictionary<Renderer,MaterialPropertyBlock>();
        }
        internal static readonly Dictionary<Character,State> States=new Dictionary<Character,State>();
        internal static ZDO Data(Component c)
        {ZNetView v=c!=null?c.GetComponent<ZNetView>():null;return v!=null&&v.IsValid()?v.GetZDO():null;}
        internal static bool Is(Component c)
        {ZDO z=Data(c);return z!=null && z.GetLong(Master,0)!=0 && z.GetPrefab()=="Greydwarf".GetStableHashCode() && c.GetComponent<MonsterAI>()!=null;}
        internal static long Now => ZNet.instance!=null?ZNet.instance.GetTime().Ticks:0;
        internal static State Get(Character c)
        {
            if(!States.TryGetValue(c,out State st))
            {
                st=new State{Body=c,LastPosition=c.transform.position};States.Add(c,st);
                ZSyncAnimation animation=c.GetZAnim();
                st.VibeSupported=animation!=null&&animation.HasParameter("george_vibing",AnimatorControllerParameterType.Bool);
                c.m_name="Gary the Greydwarf";c.m_faction=Character.Faction.Players;c.m_group="bob_gary";
                c.m_tolerateWater=true;c.m_regenAllHPTime=180;
                c.m_speed=4f;c.m_walkSpeed=2f;c.m_runSpeed=7.5f;
                c.m_turnSpeed=300f;c.m_runTurnSpeed=300f;
                MonsterAI ai=c.GetComponent<MonsterAI>();
                ai.m_attackPlayerObjects=false;ai.m_aggravatable=false;ai.m_afraidOfFire=false;ai.m_avoidFire=false;
                ai.m_fleeIfNotAlerted=false;ai.m_fleeIfLowHealth=0;ai.m_fleeIfHurtWhenTargetCantBeReached=false;
                ai.m_alertRange=35;ai.m_maxChaseDistance=0;ai.m_sleeping=false;ai.m_fleeRange=20;ai.m_fleeInterval=1;
                ai.m_avoidWater=true;
                foreach(Renderer renderer in c.GetVisual().GetComponentsInChildren<Renderer>(true))
                {
                    var old=new MaterialPropertyBlock();renderer.GetPropertyBlock(old);st.Original[renderer]=old;
                    var tint=new MaterialPropertyBlock();renderer.GetPropertyBlock(tint);
                    tint.SetColor("_Color",new Color(0.75f,0.35f,1f));tint.SetColor("_EmissionColor",new Color(0.12f,0.025f,0.22f));
                    renderer.SetPropertyBlock(tint);
                }
            }
            ZNetView view=c.GetComponent<ZNetView>();
            if(!view.IsOwner()){st.OwnerConfigured=false;return st;}
            if(!st.OwnerConfigured || c.GetMaxHealth()!=Plugin.Instance.Health.Value)
            {
                st.OwnerConfigured=true;ZDO z=view.GetZDO();z.Persistent=true;view.m_persistent=true;
                MonsterAI ai=c.GetComponent<MonsterAI>();ai.SetHuntPlayer(false);ai.SetDespawnInDay(false);ai.SetEventCreature(false);
                if(!c.IsTamed())c.SetTamed(true);
                float before=c.GetMaxHealth(),fraction=before>0?c.GetHealth()/before:1;
                float maximum=Plugin.Instance.Health.Value;
                if(before!=maximum){c.SetMaxHealth(maximum);c.SetHealth(maximum*Mathf.Clamp01(fraction));}
                if(z.GetLong(GiftAt,0)==0)ScheduleGift(z);
            }
            return st;
        }
        internal static void Scan()
        {
            var gone=new List<Character>();foreach(Character c in States.Keys)if(c==null||Data(c)==null)gone.Add(c);
            foreach(Character c in gone)States.Remove(c);
            foreach(Character c in Character.GetAllCharacters())
            {
                if(!Is(c))continue;
                State st=Get(c);Player master=Owner(c);
                if(master!=null&&master==Player.m_localPlayer&&Vector3.Distance(c.transform.position,master.transform.position)<100)
                {ZNetView view=c.GetComponent<ZNetView>();if(!view.IsOwner())view.ClaimOwnership();}
            }
        }
        internal static Player Owner(Character c)
        {
            long master=Data(c)?.GetLong(Master,0)??0;
            foreach(Player p in Player.GetAllPlayers())if(p!=null&&p.GetPlayerID()==master)return p;
            return null;
        }
        internal static void ScheduleGift(ZDO z) => z.Set(GiftAt,Now+(long)(TimeSpan.TicksPerSecond*Plugin.Instance.GiftSeconds.Value*UnityEngine.Random.Range(0.75f,1.25f)));
        internal static string Command(ZDO player,bool wait,Vector3 spot)
        {
            if(player.GetFloat(ZDOVars.s_health,0)<=0||player.GetBool(ZDOVars.s_dead,false))return "Come back to life first.";
            Vector3 origin=player.GetPosition();
            if(origin.y>3000)return "I'll wait outside the dungeon. Call me when you're back outside.";
            if(!wait && (float.IsNaN(spot.x)||float.IsNaN(spot.y)||float.IsNaN(spot.z)||float.IsInfinity(spot.x)||float.IsInfinity(spot.y)||float.IsInfinity(spot.z)||
                Vector3.Distance(origin,spot)>5 || Mathf.Abs(origin.y-spot.y)>3.5f))return "Move to clear ground and try again.";
            long owner=player.GetLong(ZDOVars.s_playerID,0);
            ZDO gary=null;
            foreach(ZDOID id in ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Long,Master.GetStableHashCode()))
            {
                ZDO z=ZDOMan.instance.GetZDO(id);
                if(z!=null&&z.GetPrefab()=="Greydwarf".GetStableHashCode()&&z.GetLong(Master,0)==owner){gary=z;break;}
            }
            if(wait)
            {
                if(gary==null)return "Call me with F3 first.";
                gary.SetOwner(ZNet.GetUID());gary.Set(Waiting,true);return "I'll wait here. F3 calls me back.";
            }
            if(gary!=null&&gary.GetBool(Retreating,false))
            {
                long recovery=gary.GetLong(RecoverAt,0);
                if(recovery==0||Now<recovery)return "I'm healing. I'll come back when I'm ready.";
                // The world kept running while Gary's zone was unloaded. Finish his recovery without creating another Gary.
                gary.SetOwner(ZNet.GetUID());gary.Set(Retreating,false);gary.Set(ZDOVars.s_health,gary.GetFloat(ZDOVars.s_maxHealth,150)*0.90f);
            }
            if(gary==null)
            {
                GameObject prefab=ZNetScene.instance.GetPrefab("Greydwarf");
                if(prefab==null)return "The native Greydwarf prefab is unavailable.";
                GameObject go=UnityEngine.Object.Instantiate(prefab,spot,Quaternion.identity);
                ZNetView view=go.GetComponent<ZNetView>();
                if(!view.IsValid()){UnityEngine.Object.Destroy(go);return "The world isn't ready yet. Try again in a moment.";}
                gary=view.GetZDO();gary.Set(Master,owner);gary.Persistent=true;view.m_persistent=true;
                Get(go.GetComponent<Character>());
            }
            else
            {
                gary.SetOwner(ZNet.GetUID());BoatRide.Forget(gary);gary.SetPosition(spot);gary.SetRotation(Quaternion.identity);
                GameObject go=ZNetScene.instance.FindInstance(gary.m_uid);
                if(go!=null)
                {
                    go.transform.SetPositionAndRotation(spot,Quaternion.identity);
                    Rigidbody body=go.GetComponent<Rigidbody>();if(body!=null){body.position=spot;body.linearVelocity=Vector3.zero;}
                    Character c=go.GetComponent<Character>();State state=Get(c);Activities.Cancel(state);state.Entrance=null;state.NextBoat=0;state.BoatMissingUntil=0;
                }
            }
            gary.Set(Waiting,false);
            GameObject loaded=ZNetScene.instance.FindInstance(gary.m_uid);
            if(loaded!=null&&loaded.GetComponent<ZNetView>().IsOwner())Personality.Mood(Get(loaded.GetComponent<Character>()),Personality.Greeting);
            return "Hello, friend! I'll follow you.";
        }
        internal static bool SafeSpot(Vector3 origin,out Vector3 spot)
        {
            int mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","blocker","vehicle","character","character_net","character_noenv");
            for(int i=0;i<12;i++)
            {
                Vector3 p=origin+Quaternion.Euler(0,i*30,0)*Vector3.forward*3;
                if(!ZoneSystem.instance.GetGroundHeight(p,out float y)||y<ZoneSystem.instance.m_waterLevel+0.1f||Mathf.Abs(y-origin.y)>3)continue;
                p.y=y+0.1f;
                if(Physics.CheckCapsule(p+Vector3.up*0.5f,p+Vector3.up*1.5f,0.4f,mask,QueryTriggerInteraction.Ignore))continue;
                spot=p;return true;
            }
            spot=default;return false;
        }
        internal static void Clear()
        {
            BoatRide.Clear();Personality.Clear();
            foreach(State st in States.Values)
            {
                if(st.Body!=null&&Data(st.Body)!=null&&st.Body.GetComponent<ZNetView>().IsOwner())Activities.Cancel(st);
                foreach(var pair in st.Original)if(pair.Key!=null)pair.Key.SetPropertyBlock(pair.Value);
            }
            States.Clear();
        }
    }
}
