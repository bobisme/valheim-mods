using System;
using HarmonyLib;
using UnityEngine;
namespace Gary
{
    // Only Gary's simulator writes activity state; every observer renders the saved pose.
    internal static class Activities
    {
        internal const int Wave=1,Cheer=2,Dance=3,Curl=4,Shake=5,Crouch=6,Show=7,Proud=8,Work=9;
        internal const string PoseKey="bob_gary_pose",PoseUntil="bob_gary_pose_until",PoseAt="bob_gary_pose_at",Home="bob_gary_home",HomeSpot="bob_gary_home_spot",ShowKind="bob_gary_show_kind";
        internal static readonly Action<BaseAI,Vector3> Look=AccessTools.MethodDelegate<Action<BaseAI,Vector3>>(AccessTools.Method(typeof(BaseAI),"LookAt"));
        internal static void Pose(Companion.State st,int kind,float seconds)
        {
            ZDO z=Companion.Data(st.Body);long until=Companion.Now+(long)(seconds*TimeSpan.TicksPerSecond);
            if(z.GetInt(PoseKey,0)!=kind||z.GetLong(PoseUntil,0)<Companion.Now)
            {z.Set(PoseKey,kind);z.Set(PoseAt,Companion.Now);z.Set(PoseUntil,until);}
            else if(z.GetLong(PoseUntil,0)-Companion.Now<TimeSpan.TicksPerSecond/2)z.Set(PoseUntil,until);
        }
        internal static void Cancel(Companion.State st)
        {
            ZDO z=Companion.Data(st.Body);if(z==null)return;
            if(z.GetLong(PoseUntil,0)!=0)z.Set(PoseUntil,0L);
            st.Shelter=null;st.ShowStarted=0;Fetch.Cancel(st);
        }
        internal static bool Active(Companion.State st,int kind)
        {ZDO z=Companion.Data(st.Body);return z.GetInt(PoseKey,0)==kind&&z.GetLong(PoseUntil,0)>Companion.Now;}
        internal static Character LocalGary(Player p)
        {
            foreach(Companion.State st in Companion.States.Values)
                if(st.Body!=null&&Companion.Owner(st.Body)==p&&Vector3.Distance(st.Body.transform.position,p.transform.position)<16)return st.Body;
            return null;
        }
        internal static bool SetHome(Player p,GameObject target,bool hold)
        {
            if(p!=Player.m_localPlayer||!Plugin.Instance.Nests.Value||hold||!Input.GetKey(KeyCode.LeftShift))return false;
            Piece piece=target!=null?target.GetComponentInParent<Piece>():null;ZDO home=Companion.Data(piece);
            if(home==null||home.GetPrefab()!="wood_stack".GetStableHashCode())return false;
            Character c=LocalGary(p);
            if(c==null||!c.GetComponent<ZNetView>().IsOwner()){Plugin.Tell("Call me nearby with F3 first, then Shift+E on your wood pile.");return true;}
            if(p.IsDead()||p.InInterior()||p.InAttack()||Vector3.Distance(p.transform.position,piece.transform.position)>5||
                piece.GetCreator()!=p.GetPlayerID()||!Nature.Allowed(piece.transform.position,p))
            {Plugin.Tell("Choose your own wood pile on accessible ground.");return true;}
            ZDO z=Companion.Data(c);
            if(z.GetZDOID(Home)==home.m_uid){z.Set(Home,ZDOID.None);Plugin.Tell("I'll leave this nest behind.");return true;}
            MonsterAI ai=c.GetComponent<MonsterAI>();Vector3 point=piece.transform.position;bool found=false;
            for(int i=0;i<12;i++)
            {
                Vector3 candidate=piece.transform.position+Quaternion.Euler(0,i*30,0)*Vector3.forward*2.3f;
                if(!Nature.Ground(ai,candidate,out point)||Vector3.Distance(point,p.transform.position)>8||!Nature.Allowed(point,p))continue;
                found=true;break;
            }
            if(!found){Plugin.Tell("I need a little clear space beside that pile for my nest.");return true;}
            z.Set(HomeSpot,point);z.Set(Home,home.m_uid);Plugin.Tell("My very own nest! I'll rest here when we settle in.");return true;
        }
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ZDO z=Companion.Data(st.Body);
            // Baseline the observed emote ID on load: saved old emotes never replay after F6.
            ZDO player=Companion.Data(master);int id=player?.GetInt(ZDOVars.s_emoteID,0)??0;
            if(!st.EmoteObserved){st.EmoteObserved=true;st.LastEmote=id;}
            else if(id!=st.LastEmote)
            {
                st.LastEmote=id;int kind=FunPolicy.Emote(player.GetString(ZDOVars.s_emote,""));
                if(Plugin.Instance.CopyEmotes.Value&&kind!=0&&Vector3.Distance(st.Body.transform.position,master.transform.position)<10&&Time.time>=st.NextEmote)
                {st.NextEmote=Time.time+5;st.PoseToFriend=false;Personality.CancelVibe(st);Pose(st,kind,kind==Dance?5:3);Personality.Mood(st,Personality.Greeting);}
            }
            if(z.GetLong(PoseUntil,0)>Companion.Now)
            {
                int kind=z.GetInt(PoseKey,0);
                if(kind==Wave||kind==Cheer||kind==Dance||kind==Proud||kind==Show)
                {ai.StopMoving();Look(ai,st.PoseToFriend&&st.WorkFriend!=null?st.WorkFriend.GetCenterPoint():master.GetCenterPoint());Brain.Status(st,kind==Proud?"proud of my fetch; a pat?":"trying your emote");return true;}
            }
            if(Rain(st,ai,master,dt))return true;
            if(Plugin.Instance.Nests.Value&&HomeTick(st,ai,master,dt))return true;
            if(Plugin.Instance.CompanionFriends.Value&&Friends(st,ai,master,dt))return true;
            return false;
        }
        private static bool HomeTick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ZDO z=Companion.Data(st.Body);ZDOID id=z.GetZDOID(Home);if(id.IsNone())return false;
            GameObject pile=ZNetScene.instance.FindInstance(id);ZDO data=Companion.Data(pile!=null?pile.transform:null);
            if(pile==null)return false; // unloaded is not destroyed
            if(data==null||data.GetPrefab()!="wood_stack".GetStableHashCode())return false;
            Vector3 spot=z.GetVec3(HomeSpot,Vector3.zero);
            bool resting=EnvMan.IsNight()||master.GetVelocity().sqrMagnitude<0.1f;
            if(!FunPolicy.HomeReady(!z.GetBool(Companion.Waiting,false),resting,st.Stationary,Vector3.Distance(master.transform.position,spot))||
                master.InPlaceMode()||Vector3.Distance(pile.transform.position,spot)>5)return false;
            st.Entrance=null;st.ForageTarget=null;
            if(Utils.DistanceXZ(st.Body.transform.position,spot)>0.7f)
            {if(!Nature.Ground(ai,spot,out Vector3 reachable))return false;Personality.CancelVibe(st);Brain.Move(ai,dt,reachable,0.6f,false);Brain.Status(st,"going to my nest");}
            else{ai.StopMoving();Pose(st,Curl,2);Brain.Status(st,"curled up in my nest");}
            return true;
        }
        private static bool Rain(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(!Plugin.Instance.RainAntics.Value)return false;
            bool raining=EnvMan.IsWet();bool roof=Cover.IsUnderRoof(st.Body.GetCenterPoint());
            if(raining&&!roof)st.WasWet=true;
            if(st.WasWet&&(!raining||roof)&&Vector3.Distance(st.Body.transform.position,master.transform.position)<12)
            {st.WasWet=false;Pose(st,Shake,2);Personality.Mood(st,Personality.Warning,"*indignant wet greydwarf noises*");}
            if(Active(st,Shake)){ai.StopMoving();Brain.Status(st,"shaking off the rain");return true;}
            if(!raining||st.Stationary<3||Vector3.Distance(st.Body.transform.position,master.transform.position)>12){st.Shelter=null;return false;}
            if(roof){ai.StopMoving();Brain.Status(st,"keeping my branches dry");return true;}
            if(Time.time>=st.NextShelter)
            {
                st.NextShelter=Time.time+6;st.Shelter=null;
                for(int i=0;i<16;i++)
                {
                    Vector3 candidate=master.transform.position+Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*(i%2==0?3:6);
                    if(Nature.Ground(ai,candidate,out Vector3 spot)&&Cover.IsUnderRoof(spot+Vector3.up*1.4f)&&Nature.Allowed(spot,master))
                    {st.Shelter=spot;break;}
                }
            }
            if(st.Shelter==null)return false;
            Brain.Move(ai,dt,st.Shelter.Value,0.7f,false);Brain.Status(st,"looking for a dry spot");return true;
        }
        private static bool Friends(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(st.Stationary<3||Vector3.Distance(st.Body.transform.position,master.transform.position)>10)return false;
            if(Time.time>=st.NextFriend)
            {
                st.NextFriend=Time.time+8;st.WorkFriend=null;float best=8;
                foreach(Character friend in Character.GetAllCharacters())
                {
                    ZDO other=Companion.Data(friend);
                    if(other==null||other.GetPrefab()!="dhack_companion".GetStableHashCode()||other.GetLong("dhc_master",0)!=master.GetPlayerID()||friend.IsDead())continue;
                    float distance=Vector3.Distance(st.Body.transform.position,friend.transform.position);
                    if(distance>=best||!ai.CanSeeTarget(friend))continue;
                    st.WorkFriend=friend;best=distance;
                }
                if(st.WorkFriend!=null&&Time.time>=st.NextFriendGreeting)
                {st.NextFriendGreeting=Time.time+90;st.PoseToFriend=true;Pose(st,Wave,2);Personality.Mood(st,Personality.Greeting);}
            }
            Character buddy=st.WorkFriend;if(buddy==null||buddy.IsDead()||Vector3.Distance(buddy.transform.position,master.transform.position)>12)return false;
            ZDO b=Companion.Data(buddy);if(b==null||b.GetLong("dhc_master",0)!=master.GetPlayerID())return false;
            string status=b.GetString("dhc_status","").ToLowerInvariant();
            bool working=status.Contains("build")||status.Contains("repair")||status.Contains("chop")||status.Contains("mine")||status.Contains("harvest");
            if(!working&&!Active(st,Wave))return false;
            ai.StopMoving();Look(ai,buddy.GetCenterPoint());
            if(working&&Time.time>=st.NextImitate){st.NextImitate=Time.time+UnityEngine.Random.Range(25,45);Pose(st,Work,3);}
            Brain.Status(st,working?"watching my friend work":"saying hello to my friend");return true;
        }
        // A display is only a visual preview: stash credit is spent at the actual toss, never at pose start.
        internal static bool ShowGift(Companion.State st,Player master,int kind)
        {
            if(!Plugin.Instance.ShowAndTell.Value)return true;
            if(st.ShowStarted==0||st.ShowGiftKind!=kind)
            {st.ShowGiftKind=kind;st.ShowStarted=Companion.Now;Companion.Data(st.Body).Set(ShowKind,kind);Personality.CancelVibe(st);Pose(st,Show,2);return false;}
            if(!FunPolicy.Recent((Companion.Now-st.ShowStarted)/(double)TimeSpan.TicksPerSecond,10))
            {st.ShowStarted=0;return false;}
            if(Companion.Now-st.ShowStarted<TimeSpan.TicksPerSecond*2)return false;
            st.ShowStarted=0;return true;
        }
        internal static void Sailing(Companion.State st,Ship ship,bool retreat)
        {
            if(!Plugin.Instance.SailingAntics.Value||retreat)return;
            float tilt=Vector3.Angle(ship.transform.up,Vector3.up);
            Rigidbody body=ship.GetComponent<Rigidbody>();bool rough=tilt>12||(body!=null&&Mathf.Abs(body.linearVelocity.y)>1.2f);
            if(rough)
            {
                Pose(st,Crouch,2);
                if(Time.time>=st.NextSailChirp){st.NextSailChirp=Time.time+30;Personality.Mood(st,Personality.Warning,"These waves are big...");}
                Brain.Status(st,"crouching through the waves");return;
            }
            if(Time.time>=st.NextBird)
            {
                st.NextBird=Time.time+10;
                foreach(IMonoUpdater updater in RandomFlyingBird.Instances)
                {
                    RandomFlyingBird bird=updater as RandomFlyingBird;
                    if(bird==null||Vector3.Distance(bird.transform.position,st.Body.transform.position)>25||Physics.Linecast(st.Body.GetCenterPoint(),bird.transform.position,LayerMask.GetMask("terrain","piece","static_solid","vehicle"),QueryTriggerInteraction.Ignore))continue;
                    Vector3 local=ship.transform.InverseTransformDirection(bird.transform.position-st.Body.transform.position);
                    CompanionYaw(st,Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg);
                    if(Time.time>=st.NextSailChirp){st.NextSailChirp=Time.time+30;Personality.Mood(st,Personality.Greeting);}
                    Brain.Status(st,"chirping at a seabird");return;
                }
                CompanionYaw(st,0);Brain.Status(st,"watching from the deck");
            }
        }
        private static void CompanionYaw(Companion.State st,float yaw)=>Companion.Data(st.Body).Set("bob_gary_deck_yaw",yaw);
    }
}
