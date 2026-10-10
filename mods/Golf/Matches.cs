using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace MeadowGolf
{
    // A saved first tee anchors a shared stroke-play match; each participant owns their ball/card.
    internal static class GolfMatches
    {
        internal const string Id="bob_golf_match",Length="bob_golf_holes",Anchor="bob_golf_anchor",Open="bob_golf_open",Host="bob_golf_host",Stopped="bob_golf_stopped";
        private const string Request="bob_golf_match_v1",BoardRpc="bob_golf_board_v1",StateRpc="bob_golf_state_v1";
        private static ZRoutedRpc _rpc;private static object _handler,_boardHandler,_stateHandler;private static readonly HashSet<long> _pending=new HashSet<long>();
        private static readonly HashSet<long> _cancelled=new HashSet<long>();
        private static readonly Dictionary<long,float> _scoresRequests=new Dictionary<long,float>(),_stopRequests=new Dictionary<long,float>();
        private static readonly Dictionary<long,float> _boardRequests=new Dictionary<long,float>();
        private static readonly Dictionary<long,float> _requests=new Dictionary<long,float>();
        private static readonly List<Coroutine> _jobs=new List<Coroutine>();
        internal sealed class Score {internal string Name,Card,State;internal long Player;internal int Holes;}
        private static ZDOID _tracked=ZDOID.None,_trackedAnchor=ZDOID.None;private static string _trackedMatch="";
        internal static ZDO MyData=>Plugin.MyBall()?.Data??ZDOMan.instance?.GetZDO(_tracked);
        internal static string MyMatch=>MyData?.GetString(Id,"")??_trackedMatch;
        internal static string BoardId="";internal static readonly List<Score> Board=new List<Score>();
        internal static void RefreshBoard()
        {
            string id=MyMatch;if(BoardId!=id){Board.Clear();BoardId=id;}
            Send(4,ZDOID.None,0);

        }
        internal static void Tick()
        {
            if(_rpc==ZRoutedRpc.instance)return;Stop();_rpc=ZRoutedRpc.instance;
            if(_rpc!=null){_rpc.Register<ZPackage>(Request,Receive);_rpc.Register<ZPackage>(BoardRpc,ReceiveBoard);_rpc.Register<ZPackage>(StateRpc,ReceiveState);var table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc);_handler=table[Request.GetStableHashCode()];_boardHandler=table[BoardRpc.GetStableHashCode()];_stateHandler=table[StateRpc.GetStableHashCode()];}
        }
        internal static void Stop()
        {
            foreach(var job in _jobs)if(job!=null&&Plugin.Instance!=null)Plugin.Instance.StopCoroutine(job);
            _jobs.Clear();_pending.Clear();_cancelled.Clear();_scoresRequests.Clear();_stopRequests.Clear();_requests.Clear();_boardRequests.Clear();Board.Clear();BoardId="";_tracked=_trackedAnchor=ZDOID.None;_trackedMatch="";
            if(_rpc!=null){IDictionary table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc);if(ReferenceEquals(table[Request.GetStableHashCode()],_handler))table.Remove(Request.GetStableHashCode());if(ReferenceEquals(table[BoardRpc.GetStableHashCode()],_boardHandler))table.Remove(BoardRpc.GetStableHashCode());if(ReferenceEquals(table[StateRpc.GetStableHashCode()],_stateHandler))table.Remove(StateRpc.GetStableHashCode());}_rpc=null;_handler=_boardHandler=_stateHandler=null;
        }
        internal static GolfMarker FirstTee()=>GolfMarker.Loaded.Where(m=>m!=null&&!m.Cup&&m.View.IsValid()&&m.Hole?.Number==1&&Player.m_localPlayer!=null&&Vector3.Distance(m.transform.position,Player.m_localPlayer.transform.position)<=5)
            .OrderBy(m=>Vector3.Distance(m.transform.position,Player.m_localPlayer.transform.position)).FirstOrDefault();
        internal static void RequestMatch(int holes,bool join=false)
        {
            GolfMarker tee=FirstTee();
            if(tee==null){GolfWorld.Say("Stand within 5 m of hole 1's tee with your club.");return;}
            Send(join?1:0,tee.View.GetZDO().m_uid,holes);GolfWorld.Say("Checking the course…");
        }
        internal static void EndMatch()
        {
            ZDO anchor=MyData==null?null:ZDOMan.instance?.GetZDO(MyData.GetZDOID(Anchor));
            GolfMarker nearby=FirstTee();if(nearby!=null)anchor=nearby.View.GetZDO();
            if(anchor==null){GolfWorld.Say("No shared match to end.");return;}Send(2,anchor.m_uid,0);
        }
        internal static void StopRound()=>Send(5,ZDOID.None,0);
        private static void Send(int action,ZDOID anchor,int holes,string token="")
        {Tick();if(_rpc==null)return;var p=new ZPackage();p.Write(action);p.Write(anchor);p.Write(holes);p.Write(token);
            if(action==5){ZDO own=MyData;p.Write(own?.m_uid??ZDOID.None);p.Write(own?.GetInt(GolfWorld.StrokesKey,0)??0);p.Write(own?.GetBool(GolfWorld.DoneKey,false)??false);string card=own?.GetString(GolfWorld.CardKey,"")??"";p.Write(card.Length<=256?card:"");}
            _rpc.InvokeRoutedRPC(Request,p);}
        internal static ZDO Match(ZDO ball)=>ball==null?null:ZDOMan.instance?.GetZDO(ball.GetZDOID(Anchor));
        internal static bool Closed(ZDO ball)
        {
            if(ball==null)return true;if(ball.GetBool(Stopped,false))return true;
            string id=ball.GetString(Id,"");if(id.Length==0)return false;
            ZDO anchor=Match(ball);
            return anchor==null||!anchor.GetBool(Open,false)||anchor.GetString(Id,"")!=id;
        }
        internal static string State(ZDO ball)
        {
            int n=ball.GetInt(Length,0);if(n==0)return "Practice";
            if(Rules.NextHole(ball.GetString(GolfWorld.CardKey,""),n)==0)return "Finished";
            return Closed(ball)?"Stopped":"Playing";
        }
        private static void Receive(long sender,ZPackage p)
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||p==null||p.Size()>768)return;
            try
            {
                int action=p.ReadInt();ZDOID id=p.ReadZDOID();int holes=p.ReadInt();string token=p.ReadString();
                ZDOID reported=ZDOID.None;int strokes=0;bool completed=false;string card="";
                if(action==5){reported=p.ReadZDOID();strokes=p.ReadInt();completed=p.ReadBool();card=p.ReadString();}
                if(p.GetPos()!=p.Size()||action<0||action>5||token.Length>32||card.Length>256||strokes<0||strokes>Rules.MaxStrokes)return;
                var rates=action==4?_boardRequests:action==3?_scoresRequests:action==5?_stopRequests:_requests;
                if((action<3&&_pending.Contains(sender))||(rates.TryGetValue(sender,out float last)&&Time.unscaledTime-last<1))return;
                if(rates.Count>=128)rates.Clear();rates[sender]=Time.unscaledTime;
                ZDO actor=GolfWorld.Actor(sender);if(actor==null)return;
                if(action==4)
                {
                    ZDO own=GolfWorld.PlayerBall(actor.GetLong(ZDOVars.s_playerID,0));
                    var state=new ZPackage();state.Write(own?.m_uid??ZDOID.None);state.Write(own?.GetString(Id,"")??"");state.Write(own?.GetZDOID(Anchor)??ZDOID.None);
                    if(own!=null)ZDOMan.instance.ForceSendZDO(own.m_uid);_rpc.InvokeRoutedRPC(sender,StateRpc,state);return;
                }
                if(action==5)
                {
                    if(_pending.Contains(sender))_cancelled.Add(sender);
                    ZDO own=GolfWorld.PlayerBall(actor.GetLong(ZDOVars.s_playerID,0));if(own==null){GolfWorld.Reply(sender,"Pending golf start canceled.");return;}
                    bool latest=own.m_uid==reported&&own.GetOwner()==sender&&strokes>=own.GetInt(GolfWorld.StrokesKey,0)&&Rules.ReadCard(card).Count>=Rules.ReadCard(own.GetString(GolfWorld.CardKey,"")).Count;
                    own.SetOwner(ZNet.GetUID());if(latest){own.Set(GolfWorld.StrokesKey,strokes);own.Set(GolfWorld.DoneKey,completed);own.Set(GolfWorld.CardKey,card);}
                    own.Set(Stopped,true);own.Set(ZDOVars.s_bodyVelHash,Vector3.zero);own.Set(ZDOVars.s_bodyAVelHash,Vector3.zero);ZDOMan.instance.ForceSendZDO(own.m_uid);
                    GolfWorld.Reply(sender,"Your round stopped. Your finished scores are kept on G.");return;
                }
                if(action==3)
                {
                    ZDO own=GolfWorld.PlayerBall(actor.GetLong(ZDOVars.s_playerID,0));
                    if(own==null||token.Length==0||own.GetString(Id,"")!=token||own.GetZDOID(Anchor)!=id)return;
                    var board=new ZPackage();var balls=GolfWorld.MatchBalls(token).ToArray();board.Write(token);board.Write(balls.Length);
                    foreach(ZDO z in balls)
                    {string name=z.GetString(GolfWorld.NameKey,"Golfer");board.Write(name.Substring(0,Math.Min(64,name.Length)));board.Write(z.GetLong(GolfWorld.PlayerKey,0));board.Write(z.GetString(GolfWorld.CardKey,""));board.Write(z.GetInt(Length,0));board.Write(State(z));}
                    _rpc.InvokeRoutedRPC(sender,BoardRpc,board);return;
                }
                ZDO tee=ZDOMan.instance?.GetZDO(id);
                if(actor==null||tee==null||tee.GetPrefab()!=Prefabs.Tee.GetStableHashCode()||!Rules.Parse(tee.GetString(GolfWorld.LabelKey,""),out var h)||h.Number!=1)return;
                if(action==2)
                {
                    if(tee.GetLong(Host,0)!=actor.GetLong(ZDOVars.s_playerID,0)&&tee.GetLong(ZDOVars.s_creator,0)!=actor.GetLong(ZDOVars.s_playerID,0)&&sender!=ZNet.GetUID()){GolfWorld.Reply(sender,"Only the match starter, course builder or server host can end the shared match; X stops your own round.");return;}
                    tee.SetOwner(ZNet.GetUID());tee.Set(Open,false);ZDOMan.instance.ForceSendZDO(id);GolfWorld.Reply(sender,"Match ended. Everyone's finished scores are kept.");return;
                }
                if(Vector3.Distance(actor.GetPosition(),tee.GetPosition())>5||actor.GetInt(ZDOVars.s_rightItem,0)!=Prefabs.Club.GetStableHashCode())return;
                if(action==1)
                {
                    if(!tee.GetBool(Open,false)){GolfWorld.Reply(sender,"No open match here. Start a 9- or 18-hole match first.");return;}
                    holes=tee.GetInt(Length,0);
                }
                else if(tee.GetBool(Open,false)){GolfWorld.Reply(sender,"This course already has an open match. J joins it; its starter can end it with M.");return;}
                if(holes!=9&&holes!=18)return;
                if(!GolfWorld.Ready){GolfWorld.Reply(sender,"Golf is loading. Try again in a moment.");return;}
                Vector3 spawn=tee.GetPosition()+tee.GetRotation()*new Vector3(0,.31f,.5f);
                if(ShotPhysics.Water(spawn)){GolfWorld.Reply(sender,"Hole 1's tee is flooded. Move it onto dry ground.");return;}
                if(_pending.Count>=32){GolfWorld.Reply(sender,"Course checks are busy. Try again shortly.");return;}
                _pending.Add(sender);_jobs.RemoveAll(job=>job==null);
                // Finished coroutine handles are retained only to cancel reloads, with a hard cap.
                if(_jobs.Count>=128)_jobs.RemoveAt(0);
                _jobs.Add(Plugin.Instance.StartCoroutine(CheckCourse(sender,id,holes,action==1)));
            }
            catch(System.IO.IOException){}catch(ArgumentException){}
        }
        private static void ReceiveState(long sender,ZPackage package)
        {
            if(!GolfWorld.FromServer(sender)||package==null||package.Size()>128)return;
            try
            {
                ZDOID ball=package.ReadZDOID();string id=package.ReadString();ZDOID anchor=package.ReadZDOID();
                if(package.GetPos()!=package.Size()||(id.Length!=0&&id.Length!=32))return;
                _tracked=ball;_trackedMatch=id;_trackedAnchor=anchor;
                if(id.Length>0)Send(3,anchor,0,id);
            }
            catch(System.IO.IOException){}catch(ArgumentException){}
        }
        private static void ReceiveBoard(long sender,ZPackage package)
        {
            if(!GolfWorld.FromServer(sender)||package==null||package.Size()>32768)return;
            try
            {
                string id=package.ReadString();int count=package.ReadInt();
                if(id.Length!=32||id!=MyMatch||count<0||count>64)return;
                var rows=new List<Score>();
                for(int i=0;i<count;i++)
                {
                    string name=package.ReadString();long player=package.ReadLong();string card=package.ReadString();int holes=package.ReadInt();string state=package.ReadString();
                    if(name.Length>64||card.Length>256||(holes!=9&&holes!=18)||state.Length>16)return;
                    rows.Add(new Score{Name=name,Player=player,Card=card,Holes=holes,State=state});
                }
                if(package.GetPos()!=package.Size())return;BoardId=id;Board.Clear();Board.AddRange(rows);
            }
            catch(System.IO.IOException){}catch(ArgumentException){}
        }
        private static IEnumerator CheckCourse(long sender,ZDOID id,int holes,bool join)
        {
            ZDOMan manager=ZDOMan.instance;ZDO tee=manager.GetZDO(id);Rules.Parse(tee.GetString(GolfWorld.LabelKey,""),out var course);
            string expected=tee.GetString(Id,"");
            var markers=new List<ZDO>();var batch=new List<ZDO>();var seen=new HashSet<ZDOID>();
            try
            {
                foreach(string prefab in new[]{Prefabs.Tee,Prefabs.Cup})
                {
                    int index=0;bool done=false;
                    while(!done)
                    {
                        if(manager!=ZDOMan.instance||_cancelled.Contains(sender))yield break;
                        done=manager.GetAllZDOsWithPrefabIterative(prefab,batch,ref index);
                        foreach(ZDO z in batch)if(Rules.Parse(z.GetString(GolfWorld.LabelKey,""),out var h)&&string.Equals(h.Course,course.Course,StringComparison.OrdinalIgnoreCase)&&h.Number<=holes&&seen.Add(z.m_uid))markers.Add(z);
                        batch.Clear();if(markers.Count>4096){GolfWorld.Reply(sender,"Too many markers share this course name. Split the courses into distinct names.");yield break;}
                        yield return null;
                    }
                }
                ZDO actor=GolfWorld.Actor(sender);tee=manager.GetZDO(id);
                if(_cancelled.Contains(sender)||actor==null||tee==null||Vector3.Distance(actor.GetPosition(),tee.GetPosition())>5||actor.GetInt(ZDOVars.s_rightItem,0)!=Prefabs.Club.GetStableHashCode())yield break;
                if(!Rules.Parse(tee.GetString(GolfWorld.LabelKey,""),out var current)||!current.Matches(course)){GolfWorld.Reply(sender,"The first tee changed during the course check. Try again.");yield break;}
                var layout=new List<Rules.CourseMarker>();ZDO firstCup=null;
                foreach(ZDO candidate in markers)
                {
                    ZDO z=manager.GetZDO(candidate.m_uid);
                    if(z==null||!Rules.Parse(z.GetString(GolfWorld.LabelKey,""),out var h)||!string.Equals(h.Course,course.Course,StringComparison.OrdinalIgnoreCase))continue;
                    Vector3 pos=z.GetPosition();bool cup=z.GetPrefab()==Prefabs.Cup.GetStableHashCode();
                    if(cup)GolfWorld.RememberCup(z);
                    layout.Add(new Rules.CourseMarker{Hole=h.Number,Cup=cup,X=pos.x,Y=pos.y,Z=pos.z});if(cup&&h.Number==1)firstCup=z;
                }
                string failure=Rules.CourseError(layout,holes);if(failure.Length>0){GolfWorld.Reply(sender,failure);yield break;}
                if(!join)
                {
                    if(tee.GetBool(Open,false)){GolfWorld.Reply(sender,"Someone started a match while the course was being checked. Join it with J.");yield break;}
                    tee.SetOwner(ZNet.GetUID());tee.Set(Id,Guid.NewGuid().ToString("N"));tee.Set(Length,holes);tee.Set(Host,actor.GetLong(ZDOVars.s_playerID,0));tee.Set(Open,true);manager.ForceSendZDO(id);
                }
                else if(!tee.GetBool(Open,false)||tee.GetInt(Length,0)!=holes||tee.GetString(Id,"")!=expected){GolfWorld.Reply(sender,"The match changed during the course check. Try joining again.");yield break;}
                if(!GolfWorld.StartOnServer(sender,id,firstCup.m_uid,true,ZDOID.None,0,false,"",tee)&&!join){tee.Set(Open,false);manager.ForceSendZDO(id);}
            }
            finally {_pending.Remove(sender);_cancelled.Remove(sender);}
        }
    }
}
