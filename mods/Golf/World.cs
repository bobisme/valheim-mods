using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MeadowGolf
{
    internal static class GolfWorld
    {
        internal const string LabelKey="bob_golf_label",PlayerKey="bob_golf_player",NameKey="bob_golf_name",CupKey="bob_golf_cup",TeeKey="bob_golf_tee",
            StrokesKey="bob_golf_strokes",DoneKey="bob_golf_done",CardKey="bob_golf_card",LieKey="bob_golf_lie";
        private const string StartRpc="bob_golf_start_v1",ReplyRpc="bob_golf_reply_v1";
        private static ZRoutedRpc Rpc;
        private static ZDOMan Manager;
        private static object StartHandler,ReplyHandler;
        private static readonly Dictionary<long,ZDOID> Active=new Dictionary<long,ZDOID>();
        private static readonly Dictionary<long,float> Requests=new Dictionary<long,float>();
        private static bool Indexed;
        private static Coroutine IndexJob;
        internal static ZDO Actor(long sender)
        {
            if(ZNet.instance==null||ZDOMan.instance==null)return null;
            ZDOID id=sender==ZNet.GetUID()?ZNet.instance.LocalPlayerCharacterID:ZNet.instance.GetPeer(sender)?.m_characterID??ZDOID.None;
            ZDO z=ZDOMan.instance.GetZDO(id);
            return z!=null&&z.GetOwner()==sender&&z.GetLong(ZDOVars.s_playerID,0)>0&&!z.GetBool(ZDOVars.s_dead,false)&&z.GetFloat(ZDOVars.s_health,0)>0?z:null;
        }
        internal static bool FromServer(long sender)=>ZNet.instance!=null&&(ZNet.instance.IsServer()?sender==ZNet.GetUID():ZNet.instance.GetPeer(sender)?.m_server==true);
        internal static void Tick()
        {
            if(Rpc!=ZRoutedRpc.instance)
            {
                StopRpc();Rpc=ZRoutedRpc.instance;
                if(Rpc!=null)
                {
                    Rpc.Register<ZPackage>(StartRpc,OnStart);Rpc.Register<string>(ReplyRpc,OnReply);
                    IDictionary table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(Rpc);
                    StartHandler=table[StartRpc.GetStableHashCode()];ReplyHandler=table[ReplyRpc.GetStableHashCode()];
                }
            }
            if(Manager!=ZDOMan.instance)
            {
                if(IndexJob!=null)Plugin.Instance.StopCoroutine(IndexJob);
                IndexJob=null;Manager=ZDOMan.instance;Indexed=false;Active.Clear();Requests.Clear();
            }
            if(Manager!=null&&!Indexed&&IndexJob==null&&ZNetScene.instance!=null&&ZNet.instance!=null&&ZNet.instance.IsServer())
                IndexJob=Plugin.Instance.StartCoroutine(Index(Manager));
        }
        // Rebuild once per world/reload, a few sectors per frame; there are no periodic world-object searches.
        private static IEnumerator Index(ZDOMan manager)
        {
            int index=0;var found=new List<ZDO>();bool finished=false;
            while(!finished&&Manager==manager)
            {
                finished=manager.GetAllZDOsWithPrefabIterative(Prefabs.Ball,found,ref index);
                foreach(ZDO z in found)
                {
                    long player=z.GetLong(PlayerKey,0);if(player<=0)continue;
                    if(!Active.TryGetValue(player,out ZDOID old)||(manager.GetZDO(old)==null||manager.GetZDO(old).GetLong("bob_golf_sequence",0)<z.GetLong("bob_golf_sequence",0)))Active[player]=z.m_uid;
                }
                found.Clear();yield return null;
            }
            if(Manager==manager)Indexed=true;IndexJob=null;
        }
        internal static void Start(GolfMarker tee,GolfMarker cup,bool fresh=false)
        {
            Tick();if(Rpc==null){Say("Golf needs the host and every player to install Meadow Golf.");return;}
            var package=new ZPackage();GolfBall old=Plugin.MyBall();
            package.Write(tee.View.GetZDO().m_uid);package.Write(cup.View.GetZDO().m_uid);package.Write(fresh);
            package.Write(old?.Data?.m_uid??ZDOID.None);package.Write(old?.Strokes??0);package.Write(old?.Done??false);
            string card=old?.Data?.GetString(CardKey,"")??"";package.Write(card.Length<=256?card:"");
            Rpc.InvokeRoutedRPC(StartRpc,package);
            Say("Setting your ball on the tee…");
        }
        private static void Reply(long sender,string message)=>Rpc?.InvokeRoutedRPC(sender,ReplyRpc,message);
        private static void OnReply(long sender,string message){if(FromServer(sender)&&message!=null&&message.Length<=256)Say(message);}
        private static void OnStart(long sender,ZPackage package)
        {
            if(package==null||package.Size()>768)return;
            try
            {
                ZDOID tee=package.ReadZDOID(),cup=package.ReadZDOID();bool fresh=package.ReadBool();
                ZDOID previous=package.ReadZDOID();int strokes=package.ReadInt();bool done=package.ReadBool();string card=package.ReadString();
                if(package.GetPos()!=package.Size()||card.Length>256||strokes<0||strokes>Rules.MaxStrokes)return;
                StartOnServer(sender,tee,cup,fresh,previous,strokes,done,card);
            }
            catch(System.IO.EndOfStreamException){}catch(System.IO.IOException){}catch(ArgumentException){}
        }
        private static void StartOnServer(long sender,ZDOID teeId,ZDOID cupId,bool fresh,ZDOID reportedBall,int reportedStrokes,bool reportedDone,string reportedCard)
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||Manager==null)return;
            if(Requests.TryGetValue(sender,out float last)&&Time.unscaledTime-last<1)return;
            Requests[sender]=Time.unscaledTime;if(Requests.Count>128)Requests.Clear();
            if(!Indexed){Reply(sender,"The course is loading. Try the tee again in a moment.");return;}
            ZDO actor=Actor(sender),tee=Manager.GetZDO(teeId),cup=Manager.GetZDO(cupId);
            if(actor==null||tee==null||cup==null||tee.GetPrefab()!=Prefabs.Tee.GetStableHashCode()||cup.GetPrefab()!=Prefabs.Cup.GetStableHashCode()||
               Vector3.Distance(actor.GetPosition(),tee.GetPosition())>5||Vector3.Distance(tee.GetPosition(),cup.GetPosition())>240||
               !Rules.Parse(tee.GetString(LabelKey,"Meadow:1:3"),out var hole)||!Rules.Parse(cup.GetString(LabelKey,"Meadow:1:3"),out var end)||!hole.Matches(end))
            {Reply(sender,"Use a nearby tee with a matching cup within 240 m.");return;}
            if(actor.GetInt(ZDOVars.s_rightItem,0)!=Prefabs.Club.GetStableHashCode()){Reply(sender,"Equip your Meadow golf club first.");return;}
            long player=actor.GetLong(ZDOVars.s_playerID,0);string card="";int strokes=0;long sequence=1;
            if(Active.TryGetValue(player,out ZDOID oldId))
            {
                ZDO old=Manager.GetZDO(oldId);
                if(old!=null)
                {
                    sequence=old.GetLong("bob_golf_sequence",0)+1;
                    // The ball owner supplies its latest score snapshot alongside the start request:
                    // a completed putt must not become a penalty while its ZDO update is still in transit.
                    bool report=oldId==reportedBall&&old.GetOwner()==sender&&reportedStrokes>=old.GetInt(StrokesKey,0);
                    var begin=Rules.Begin(old.GetString(LabelKey,""),report?reportedCard:old.GetString(CardKey,""),
                        report?reportedStrokes:old.GetInt(StrokesKey,0),report?reportedDone:old.GetBool(DoneKey,false),hole,fresh);
                    card=begin.Card;strokes=begin.Strokes;
                    old.SetOwner(ZNet.GetUID());Manager.DestroyZDO(old);
                }
            }
            Vector3 position=tee.GetPosition()+tee.GetRotation()*new Vector3(0,.31f,.5f);
            ZDO z=Manager.CreateNewZDO(position,Prefabs.Ball.GetStableHashCode());z.Persistent=true;z.Type=ZDO.ObjectType.Default;z.Distant=false;z.SetPrefab(Prefabs.Ball.GetStableHashCode());z.SetRotation(Quaternion.identity);
            z.Set(PlayerKey,player);z.Set(NameKey,actor.GetString(ZDOVars.s_playerName,"Golfer"));z.Set(LabelKey,hole.Label);
            z.Set(CupKey,cupId);z.Set(TeeKey,teeId);z.Set(StrokesKey,strokes);z.Set(DoneKey,false);z.Set(LieKey,position);
            z.Set(CardKey,card);z.Set("bob_golf_sequence",sequence);z.SetOwner(sender);
            Active[player]=z.m_uid;Manager.ForceSendZDO(z.m_uid);
            Reply(sender,$"{hole.Course} · hole {hole.Number} · par {hole.Par}"+(strokes>0?$" — back to the tee, +1 penalty ({strokes} strokes).":" — your ball is ready."));
        }
        internal static void Say(string text)=>Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,text);
        internal static void AttachLoaded()
        {
            if(ZNetScene.instance==null)return;
            foreach(ZNetView view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if(!view.IsValid())continue;
                int hash=view.GetZDO().GetPrefab();Type type=hash==Prefabs.Ball.GetStableHashCode()?typeof(GolfBall):
                    hash==Prefabs.Tee.GetStableHashCode()||hash==Prefabs.Cup.GetStableHashCode()?typeof(GolfMarker):null;
                if(type==null||view.GetComponent(type)!=null)continue;
                foreach(MonoBehaviour b in view.GetComponents<MonoBehaviour>())if(b!=null&&b.GetType().FullName==type.FullName)Object.DestroyImmediate(b);
                view.gameObject.AddComponent(type);
            }
        }
        internal static void Stop()
        {
            if(IndexJob!=null)Plugin.Instance.StopCoroutine(IndexJob);IndexJob=null;Manager=null;Active.Clear();Requests.Clear();Indexed=false;
            StopRpc();
            foreach(var ball in GolfBall.Loaded.ToArray())if(ball!=null)Object.DestroyImmediate(ball);
            foreach(var marker in GolfMarker.Loaded.ToArray())if(marker!=null)Object.DestroyImmediate(marker);
        }
        private static void StopRpc()
        {
            if(Rpc!=null)
            {
                IDictionary table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(Rpc);
                if(ReferenceEquals(table[StartRpc.GetStableHashCode()],StartHandler))table.Remove(StartRpc.GetStableHashCode());
                if(ReferenceEquals(table[ReplyRpc.GetStableHashCode()],ReplyHandler))table.Remove(ReplyRpc.GetStableHashCode());
            }
            Rpc=null;StartHandler=ReplyHandler=null;
        }
    }

    internal sealed class GolfMarker:MonoBehaviour,Hoverable,Interactable,TextReceiver
    {
        internal static readonly List<GolfMarker> Loaded=new List<GolfMarker>();
        internal ZNetView View;
        internal bool Cup=>View!=null&&View.GetZDO()?.GetPrefab()==Prefabs.Cup.GetStableHashCode();
        internal Rules.Hole Hole=>Rules.Parse(GetText(),out var h)?h:null;
        private void Awake()
        {
            View=GetComponent<ZNetView>();if(View==null||!View.IsValid())return;
            View.Register<string>("GolfLabel",Rename);Loaded.Add(this);
            foreach(GolfBall ball in GolfBall.Loaded)if(ball!=null)ball.Ignore(GetComponentsInChildren<Collider>());
        }
        private void OnDestroy(){Loaded.Remove(this);if(View!=null)View.Unregister("GolfLabel");}
        public string GetText()=>View?.GetZDO()?.GetString(GolfWorld.LabelKey,"Meadow:1:3")??"Meadow:1:3";
        public void SetText(string text)
        {
            if(!Rules.Parse(text,out var hole)){GolfWorld.Say("Use Course:Hole:Par, e.g. Meadow:1:3. Holes 1–18; par 2–8.");return;}
            if(View.IsValid())View.InvokeRPC("GolfLabel",hole.Label);
        }
        private void Rename(long sender,string label)
        {
            if(!View.IsOwner()||!Rules.Parse(label,out var hole))return;
            ZDO actor=GolfWorld.Actor(sender);if(actor==null||Vector3.Distance(actor.GetPosition(),transform.position)>5)return;
            long creator=GetComponent<Piece>()?.GetCreator()??0;
            if(creator!=0&&creator!=actor.GetLong(ZDOVars.s_playerID,0))return;
            View.GetZDO().Set(GolfWorld.LabelKey,hole.Label);
        }
        public string GetHoverName()=>Cup?"Golf cup":"Golf tee";
        public float GetHoverOffset()=>0;
        public string GetHoverText()=>Localization.instance.Localize($"{GetHoverName()} · {GetText()}\n"+(Cup?"":"[<color=yellow><b>$KEY_Use</b></color>] Start / return to tee (+1 penalty)\n[<color=yellow>Ctrl+$KEY_Use</color>] Fresh round\n")+"[<color=yellow>Shift+$KEY_Use</color>] Label (builder)");
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||!(user is Player player)||player!=Player.m_localPlayer||!View.IsValid())return false;
            if(!PrivateArea.CheckAccess(transform.position))return false;
            if(alt)
            {
                long creator=GetComponent<Piece>()?.GetCreator()??0;
                if(creator!=0&&creator!=player.GetPlayerID()){GolfWorld.Say("Only the builder can relabel this hole.");return true;}
                TextInput.instance.RequestText(this,"Course:Hole:Par",64);return true;
            }
            if(Cup)return false;
            if(!Prefabs.IsClub(Prefabs.Right(player))){GolfWorld.Say("Equip your Meadow golf club first.");return true;}
            var matches=Loaded.Where(m=>m!=null&&m.Cup&&m.View.IsValid()&&Hole!=null&&Hole.Matches(m.Hole)&&Vector3.Distance(transform.position,m.transform.position)<=240).Take(2).ToArray();
            if(matches.Length!=1){GolfWorld.Say(matches.Length==0?"Place a cup with the same course and hole label nearby (within 240 m, loaded).":"Two cups share this hole label. Relabel one first.");return true;}
            GolfWorld.Start(this,matches[0],Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl));return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    }

    internal sealed class GolfBall:MonoBehaviour,Hoverable,Interactable
    {
        internal static readonly List<GolfBall> Loaded=new List<GolfBall>();
        internal static readonly Dictionary<ZSyncTransform,GolfBall> Syncs=new Dictionary<ZSyncTransform,GolfBall>();
        internal ZNetView View;internal Rigidbody Body;private ZSyncTransform _sync;private float _lastShot=-10,_stillTime,_nextCheck;private bool _owned,_finished;
        private float _nextBounce;
        internal bool Pending {get;private set;}
        private long _shooter;private ZDOID _actor;private int _shotMode,_expected;private float _shotPower,_impactAt;private Vector3 _shotDirection;
        internal const float ImpactDelay=.8f;
        internal float LastImpactDelay {get;private set;}
        internal string LastImpactSource {get;private set;}="";
        internal ZDO Data=>View!=null&&View.IsValid()?View.GetZDO():null;
        internal bool Mine=>Data!=null&&Player.m_localPlayer!=null&&Data.GetLong(GolfWorld.PlayerKey,0)==Player.m_localPlayer.GetPlayerID();
        internal bool Done=>Data?.GetBool(GolfWorld.DoneKey,false)??false;
        internal int Strokes=>Data?.GetInt(GolfWorld.StrokesKey,0)??0;
        internal bool Still=>!Pending&&(View!=null&&View.IsOwner()?Body!=null&&Body.linearVelocity.sqrMagnitude<.09f&&_stillTime>.4f:
            Data!=null&&Data.GetVec3(ZDOVars.s_bodyVelHash,Vector3.zero).sqrMagnitude<.09f);
        private void Awake()
        {
            View=GetComponent<ZNetView>();Body=GetComponent<Rigidbody>();if(View==null||!View.IsValid()||Body==null)return;
            View.Register<ZDOID,int,float,Vector3,int>("GolfShot",Shot);View.Register<ZDOID,int>("GolfReset",Reset);
            ShotPhysics.Configure(Body);Loaded.Add(this);_sync=GetComponent<ZSyncTransform>();if(_sync!=null){_sync.enabled=true;Syncs[_sync]=this;}
            int color=(int)((ulong)Data.GetLong(GolfWorld.PlayerKey,0)%(uint)Models.Colors.Length);
            Transform model=transform.Find("GolfModel");if(model!=null)foreach(Renderer r in model.GetComponentsInChildren<Renderer>())r.sharedMaterial=Models.BallColors[color];
            foreach(Character c in Character.GetAllCharacters())if(c!=null)Ignore(c.GetComponentsInChildren<Collider>());
            foreach(GolfMarker m in GolfMarker.Loaded)if(m!=null)Ignore(m.GetComponentsInChildren<Collider>());
            foreach(GolfBall b in Loaded)if(b!=null&&b!=this)Ignore(b.GetComponents<Collider>());
        }
        private void Start(){if(Data!=null)SetPhysics();}
        private void OnDestroy()
        {
            Loaded.Remove(this);if(!ReferenceEquals(_sync,null))Syncs.Remove(_sync);if(_sync!=null)_sync.enabled=false;if(View!=null){View.Unregister("GolfShot");View.Unregister("GolfReset");}
            // Freeze physics during F6's short gap; the fresh component restores the native owner state.
            if(Body!=null&&Plugin.Instance!=null)Body.isKinematic=true;
        }
        internal void Ignore(Collider[] colliders)
        {
            Collider mine=GetComponent<Collider>();if(mine==null)return;
            foreach(Collider c in colliders)if(c!=null&&c!=mine)Physics.IgnoreCollision(mine,c);
        }
        private void SetPhysics()
        {
            _finished=Done;_owned=View.IsOwner();bool frozen=!_owned||Done;Body.isKinematic=frozen;
            Body.useGravity=_owned&&!Done;
            if(!frozen){Body.linearVelocity=Data.GetVec3(ZDOVars.s_bodyVelHash,Vector3.zero);Body.angularVelocity=Data.GetVec3(ZDOVars.s_bodyAVelHash,Vector3.zero);}
            foreach(Renderer r in GetComponentsInChildren<Renderer>())r.enabled=!Done;
            GetComponent<Collider>().enabled=!Done;
        }
        internal void PreparePhysics()
        {
            if(Data!=null&&(_finished!=Done||_owned!=View.IsOwner()||Body.isKinematic!=(Done||!View.IsOwner())))SetPhysics();
        }
        private void FixedUpdate()
        {
            if(Data==null)return;
            PreparePhysics();
            if(!View.IsOwner()||Done){Pending=false;return;}
            if(Pending&&Time.time>=_impactAt)Impact("fallback");
            ShotPhysics.Roll(Body,Physics.defaultPhysicsScene,Time.fixedDeltaTime,ref _stillTime);
            if(Time.time<_nextCheck)return;_nextCheck=Time.time+.10f;
            ZDO cup=ZDOMan.instance?.GetZDO(Data.GetZDOID(GolfWorld.CupKey));
            if(cup==null||cup.GetPrefab()!=Prefabs.Cup.GetStableHashCode()||!Rules.Parse(cup.GetString(GolfWorld.LabelKey,"Meadow:1:3"),out var hole)||
               !Rules.Parse(Data.GetString(GolfWorld.LabelKey,""),out var ours)||!hole.Matches(ours))return;
            Vector3 d=Body.position-(cup.GetPosition()+Vector3.up*.12f);
            if(!Rules.Captures(new Vector2(d.x,d.z).magnitude,d.y,Body.linearVelocity.magnitude,Strokes))return;
            Data.SetPosition(Body.position);Data.SetRotation(Body.rotation);Data.Set(ZDOVars.s_velHash,Vector3.zero);
            Data.Set(GolfWorld.DoneKey,true);Data.Set(GolfWorld.CardKey,Rules.Record(Data.GetString(GolfWorld.CardKey,""),ours.Number,ours.Par,Mathf.Clamp(Strokes,1,Rules.MaxStrokes)));
            Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;Data.Set(ZDOVars.s_bodyVelHash,Vector3.zero);Data.Set(ZDOVars.s_bodyAVelHash,Vector3.zero);
            SetPhysics();GetComponent<ZSyncTransform>()?.SyncNow();
        }
        private void OnCollisionEnter(Collision collision)
        {
            if(Data==null||!View.IsOwner()||Done||Strokes==0||Time.time<_nextBounce||collision.relativeVelocity.magnitude<1.4f)return;
            _nextBounce=Time.time+.18f;
            ShotPhysics.Sound("sfx_wood_hit",Body.position,Mathf.Clamp(collision.relativeVelocity.magnitude*.025f,.06f,.3f),1.15f);
        }
        private bool ValidActor(long sender,ZDOID actorId)
        {
            ZDO actor=GolfWorld.Actor(sender);
            return actor!=null&&actor.m_uid==actorId&&actor.GetLong(ZDOVars.s_playerID,0)==Data.GetLong(GolfWorld.PlayerKey,0)&&
                Vector3.Distance(actor.GetPosition(),transform.position)<=3.5f;
        }
        internal void RequestShot(Player player,int mode,float power,Vector3 direction)=>View.InvokeRPC("GolfShot",player.GetZDOID(),mode,power,direction,Strokes);
        private void Shot(long sender,ZDOID actorId,int mode,float power,Vector3 direction,int expected)
        {
            if(Data==null||!View.IsOwner()||Done||!Still||Strokes>=Rules.MaxStrokes||expected!=Strokes||Time.time-_lastShot<.6f||
                !Rules.ValidShot(mode,power,direction.magnitude,direction.y)||!ValidActor(sender,actorId)||
                GolfWorld.Actor(sender).GetInt(ZDOVars.s_rightItem,0)!=Prefabs.Club.GetStableHashCode())return;
            _lastShot=Time.time;Pending=true;_shooter=sender;_actor=actorId;_shotMode=mode;_shotPower=power;_shotDirection=direction;_expected=expected;
            _impactAt=Time.time+ImpactDelay;
            ShotPhysics.Sound("sfx_club_swing",transform.position);
        }
        internal void AnimationImpact(ZDOID actor)
        {if(Pending&&_actor==actor&&Time.time-_lastShot>=.08f)Impact("animation");}
        private void Impact(string source)
        {
            Pending=false;
            if(Data==null||!View.IsOwner()||Done||Strokes!=_expected||!ValidActor(_shooter,_actor)||
                GolfWorld.Actor(_shooter).GetInt(ZDOVars.s_rightItem,0)!=Prefabs.Club.GetStableHashCode()||Body.linearVelocity.sqrMagnitude>=.09f)return;
            LastImpactDelay=Time.time-_lastShot;LastImpactSource=source;
            _stillTime=0;Data.Set(GolfWorld.LieKey,Body.position);Data.Set(GolfWorld.StrokesKey,Strokes+1);
            ShotPhysics.Launch(Body,_shotMode,_shotPower,_shotDirection);
            ShotPhysics.Sound("sfx_wood_hit",Body.position,.35f+.4f*_shotPower);
            _sync?.SyncNow();
        }
        private void Reset(long sender,ZDOID actorId,int expected)
        {
            if(Data==null||!View.IsOwner()||Done||Pending||!ValidActor(sender,actorId)||expected!=Strokes||Strokes>=Rules.MaxStrokes||Time.time-_lastShot<1)return;
            _lastShot=Time.time;Data.Set(GolfWorld.StrokesKey,Strokes+1);Body.position=Data.GetVec3(GolfWorld.LieKey,transform.position);
            transform.position=Body.position;Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;_stillTime=0;
            GetComponent<ZSyncTransform>()?.SyncNow();
        }
        public string GetHoverName()=>Data?.GetString(GolfWorld.NameKey,"Golfer")+"’s ball";
        public float GetHoverOffset()=>0;
        public string GetHoverText()=>GetHoverName()+$" · {Strokes} strokes"+(Mine&&!Done?"\n[E] Return to last lie (+1 penalty)":"");
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||!Mine||Done||user!=Player.m_localPlayer)return false;
            View.InvokeRPC("GolfReset",Player.m_localPlayer.GetZDOID(),Strokes);GolfWorld.Say("Returning to your last lie costs one stroke.");return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    }
}
