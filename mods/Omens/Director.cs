using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using Random=UnityEngine.Random;

namespace Omens
{
    // The host decides: where omens appear, when they come to pass. Its ledger is a small file per world beside the host's config,
    // keyed by each sign's own random id (a ZDOID changes every time the world loads).
    internal static class Director
    {
        private sealed class Entry
        {
            public long Id;public int Kind,State;public float X,Y,Z;
            public double PlacedAt,SeenAt,ResolvedAt;public bool SeenAtNight,Forced;public string SeenBy="";public bool SignRemoved;
            [JsonIgnore]public Vector3 Pos=>new Vector3(X,Y,Z);
            [JsonIgnore]public Omen Omen=>Policy.Of((Kind)Kind);
        }
        private sealed class Ledger{public double NextAt;public List<Entry> Omens=new List<Entry>();}

        private static readonly string[] BasePieces={"piece_workbench","bed","piece_bed02"};
        private static Ledger _ledger;
        private static string _world,_path;
        private static float _nextTick;
        private static bool _reconciled;

        private static bool Hosting=>ZNet.instance!=null&&ZNet.instance.IsServer()&&ZDOMan.instance!=null&&ZoneSystem.instance!=null&&
            EnvMan.instance!=null&&RandEventSystem.instance!=null&&WorldGenerator.instance!=null;
        private static double Now=>ZNet.instance.GetTimeSeconds();
        private static double DayLength=>Math.Max(60,EnvMan.instance.m_dayLengthSec);

        internal static void Tick()
        {
            if(!Plugin.Instance.Enabled.Value||!Hosting||Time.time<_nextTick)return;
            _nextTick=Time.time+5;
            Load();
            if(!_reconciled){Reconcile();_reconciled=true;}
            double now=Now;
            if(_ledger.NextAt<=0)_ledger.NextAt=now+Policy.NextDelay(Plugin.Instance.IntervalDays.Value,DayLength,Random.value);
            bool changed=false;
            foreach(Entry e in _ledger.Omens.Where(e=>!Policy.Finished((State)e.State)).ToList())changed|=Advance(e,now);
            foreach(Entry e in _ledger.Omens.Where(e=>Policy.Finished((State)e.State)&&!e.SignRemoved&&Policy.SignGone((State)e.State,e.Omen.Bad,now-e.ResolvedAt)).ToList())
            {RemoveSign(e.Id);Net.Resolve(e.Id);e.SignRemoved=true;changed=true;}
            changed|=MaybePlace(now);
            if(changed)Save();
        }

        private static bool Advance(Entry e,double now)
        {
            var state=(State)e.State;bool done=false,impossible=false;string why="";
            Vector3 raidAt=Vector3.zero;
            if(state==State.Seen)
            {
                if(!e.Omen.Bad)done=true;
                else if(e.Forced||Policy.RaidDue(now,e.SeenAt,e.SeenAtNight,EnvMan.IsNight()))
                {
                    Vector3? home=NearestBase(e.Pos);
                    why=Game.m_eventRate<=0?"raids are turned off in this world":home==null?$"no workbench or bed within {Plugin.Instance.BaseRange.Value:0} m"
                        :now-e.SeenAt>DayLength*1.5?"another raid kept it waiting too long":!RandEventSystem.instance.HaveEvent(e.Omen.Raid)?$"the game has no {e.Omen.Raid} raid":"";
                    if(why!="")impossible=true;
                    else if(RandEventSystem.instance.GetCurrentRandomEvent()==null) // never interrupts a raid in progress; waits for it to end
                    {RandEventSystem.instance.SetRandomEventByName(e.Omen.Raid,home.Value);raidAt=home.Value;done=true;}
                }
            }
            var next=Policy.Advance(state,now,e.PlacedAt,Plugin.Instance.ExpireDays.Value*DayLength,false,false,done,impossible);
            if(next==state)return false;
            e.State=(int)next;e.ResolvedAt=now;
            switch(next)
            {
                case State.Fulfilled when e.Omen.Bad:
                    Net.Tell(e.Omen.Outcome,raidAt,0,0);
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) came to pass: {e.Omen.Raid} at the base near {raidAt:F0}");break;
                case State.Fulfilled:
                    Net.Blessing(e.Pos,60);Net.Tell(e.Omen.Outcome,e.Pos,60,0);
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) blessed players near {e.Pos:F0}");break;
                case State.Fizzled:
                    Net.Tell("The omen passes. Whatever it foretold did not find you.",e.Pos,-1,0);
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) fizzled: {why}");break;
                case State.Expired:
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) at {e.Pos:F0} faded unseen");break;
            }
            return true;
        }

        internal static void OnSeen(long sender,long id)
        {
            if(!Hosting)return;
            Load();
            Entry e=_ledger.Omens.FirstOrDefault(x=>x.Id==id);
            if(e==null||(State)e.State!=State.Placed)return;
            e.State=(int)Policy.Advance(State.Placed,Now,e.PlacedAt,double.MaxValue,true,false,false,false);
            e.SeenAt=Now;e.SeenAtNight=EnvMan.IsNight();e.SeenBy=NameOf(sender);
            Net.Tell(e.Omen.Reading,e.Pos,40,e.Id);
            Plugin.Log($"{e.SeenBy} saw {e.Omen.Name} ({e.Id}) at {e.Pos:F0}{(e.SeenAtNight?" at night":"")}");
            Save();
            _nextTick=0; // a good omen comes to pass at once
        }
        internal static void OnRespond(long sender,long id)
        {
            if(!Hosting)return;
            Load();
            Entry e=_ledger.Omens.FirstOrDefault(x=>x.Id==id);
            if(e==null||!e.Omen.Respondable||Policy.Finished((State)e.State))return;
            e.State=(int)Policy.Advance((State)e.State,Now,e.PlacedAt,double.MaxValue,false,true,false,false);
            e.ResolvedAt=Now;
            Net.Tell(e.Omen.Averted,e.Pos,60,0);
            Plugin.Log($"{NameOf(sender)} averted {e.Omen.Name} ({e.Id})");
            Save();
            _nextTick=0;
        }

        // ---- placing ----
        private static bool MaybePlace(double now)
        {
            if(now<_ledger.NextAt)return false;
            List<Vector3> players=Players();
            int active=_ledger.Omens.Count(e=>!Policy.Finished((State)e.State));
            if(players.Count==0||active>=Plugin.Instance.MaxActive.Value){_ledger.NextAt=now+120;return true;}
            var enabled=Plugin.Instance.EnabledKinds();
            double badChance=Game.m_eventRate>0?Plugin.Instance.BadChance.Value:0; // a world without raids gets only good omens
            float water=ZoneSystem.instance.m_waterLevel;
            for(int attempt=0;attempt<16;attempt++)
            {
                Vector3 near=players[Random.Range(0,players.Count)];
                Vector2 offset=Random.insideUnitCircle.normalized*Random.Range(45f,85f);
                var spot=new Vector3(near.x+offset.x,near.y,near.z+offset.y);
                if(!ZoneSystem.instance.GetSolidHeight(spot,out float height)||height<water+0.6f)continue;
                spot.y=height;
                int biome=(int)WorldGenerator.instance.GetBiome(spot.x,spot.z);
                Kind? kind=Policy.Pick(Random.value,Random.value,badChance,biome,enabled);
                if(kind==Kind.Ravens&&!OpenSky(spot))continue; // birds under a canopy go unseen: try another spot, keeping the good/bad roll fair
                if(kind==null||NearBuilding(spot)||players.Any(p=>Vector3.Distance(p,spot)<35)||
                   _ledger.Omens.Any(e=>!Policy.Finished((State)e.State)&&Vector3.Distance(e.Pos,spot)<150))continue;
                Place(kind.Value,spot,now);
                _ledger.NextAt=now+Policy.NextDelay(Plugin.Instance.IntervalDays.Value,DayLength,Random.value);
                return true;
            }
            _ledger.NextAt=now+120; // nowhere suitable near anyone right now
            return true;
        }
        private static long Place(Kind kind,Vector3 spot,double now)
        {
            long id;do{id=((long)Random.Range(1,int.MaxValue)<<31)^Random.Range(1,int.MaxValue);}while(id==0||_ledger.Omens.Any(e=>e.Id==id));
            ZDO zdo=ZDOMan.instance.CreateNewZDO(spot,SignPrefab.Hash);
            zdo.Persistent=true;zdo.Type=ZDO.ObjectType.Default;zdo.Distant=false;
            zdo.SetPrefab(SignPrefab.Hash);zdo.SetRotation(Quaternion.Euler(0,Random.Range(0,360f),0));
            zdo.Set(SignPrefab.KindKey,(int)kind);zdo.Set(SignPrefab.IdKey,id);
            _ledger.Omens.Add(new Entry{Id=id,Kind=(int)kind,X=spot.x,Y=spot.y,Z=spot.z,PlacedAt=now,State=(int)State.Placed});
            Plugin.Log($"Placed {Policy.Of(kind).Name} ({id}) at {spot:F0}");
            return id;
        }
        // A high ray at the centre and four points around it meets nothing more than 4 m above the ground (bushes and rocks are fine,
        // treetops are not): the sky over the spot is open.
        private static bool OpenSky(Vector3 spot)
        {
            foreach(Vector3 offset in new[]{Vector3.zero,new Vector3(7,0,0),new Vector3(-7,0,0),new Vector3(0,0,7),new Vector3(0,0,-7)})
            {
                Vector3 from=spot+offset+Vector3.up*70;
                if(!Physics.Raycast(from,Vector3.down,out RaycastHit hit,90,~0,QueryTriggerInteraction.Ignore))return false;
                if(!ZoneSystem.instance.GetGroundHeight(hit.point,out float ground)||hit.point.y>ground+4)return false;
            }
            return true;
        }
        private static bool NearBuilding(Vector3 spot)
        {
            foreach(Collider c in Physics.OverlapSphere(spot,40,LayerMask.GetMask("piece","piece_nonsolid")))
                if(c.GetComponentInParent<Piece>() is Piece piece&&piece.GetCreator()!=0)return true;
            return false;
        }
        private static List<Vector3> Players()
        {
            var list=new List<Vector3>();
            if(Player.m_localPlayer!=null&&!Player.m_localPlayer.IsDead())list.Add(Player.m_localPlayer.transform.position);
            foreach(ZNetPeer peer in ZNet.instance.GetPeers())if(peer.IsReady()&&!peer.m_characterID.IsNone())list.Add(peer.m_refPos);
            return list.Where(p=>p.y<3000).ToList(); // not inside dungeons
        }
        private static string NameOf(long sender)
        {
            if(sender==ZNet.GetUID())return Player.m_localPlayer!=null?Player.m_localPlayer.GetPlayerName():"the host";
            return ZNet.instance.GetPeer(sender)?.m_playerName??"someone";
        }

        // ---- for testing through Claude Tools (host only) ----
        internal static string TestPlace(Kind kind,Vector3 spot)
        {
            if(!Hosting)return null;
            Load();
            if(kind==Kind.Ravens)
            {
                // The nearest open-sky spot within 40 metres of the one asked for.
                Vector3 asked=spot;bool found=false;
                for(int ring=0;ring<=40&&!found;ring+=5)for(int step=0;step<Math.Max(1,ring)&&!found;step++)
                {
                    float a=step*Mathf.PI*2/Math.Max(1,ring);var test=asked+new Vector3(Mathf.Cos(a)*ring,0,Mathf.Sin(a)*ring);
                    if(ZoneSystem.instance.GetSolidHeight(test,out float h)){test.y=h;if(OpenSky(test)){spot=test;found=true;}}
                }
                if(!found)return "none: no open sky within 40 m";
            }
            if(ZoneSystem.instance.GetSolidHeight(spot,out float height))spot.y=height;
            long id=Place(kind,spot,Now);Save();
            return id.ToString();
        }
        // Mark it seen and bring its outcome now, ignoring night.
        internal static string TestNow(string which)
        {
            if(!Hosting)return "not the host";
            Load();
            Entry e=which=="last"?_ledger.Omens.Where(x=>!Policy.Finished((State)x.State)).OrderByDescending(x=>x.PlacedAt).FirstOrDefault()
                :_ledger.Omens.FirstOrDefault(x=>x.Id.ToString()==which);
            if(e==null||Policy.Finished((State)e.State))return "no open omen "+which;
            if((State)e.State==State.Placed)OnSeen(ZNet.GetUID(),e.Id);
            e.Forced=true;Save();_nextTick=0;
            return $"{e.Omen.Name} ({e.Id}) comes to pass";
        }
        // As if a player had responded (burned the troll): averts a respondable omen.
        internal static string TestAvert(string id)
        {
            if(!Hosting)return "not the host";
            Load();
            Entry e=_ledger.Omens.FirstOrDefault(x=>x.Id.ToString()==id);
            if(e==null||Policy.Finished((State)e.State)||!e.Omen.Respondable)return "no open omen "+id+" that can be averted";
            OnRespond(ZNet.GetUID(),e.Id);
            return $"{e.Omen.Name} ({e.Id}) averted";
        }
        // As if it had faded unseen: no outcome, its sign leaves the world.
        internal static string TestClear(string id)
        {
            if(!Hosting)return "not the host";
            Load();
            Entry e=_ledger.Omens.FirstOrDefault(x=>x.Id.ToString()==id);
            if(e==null||Policy.Finished((State)e.State))return "no open omen "+id;
            e.State=(int)State.Expired;e.ResolvedAt=Now;Save();_nextTick=0;
            Plugin.Log($"{e.Omen.Name} ({e.Id}) cleared for testing");
            return $"{e.Omen.Name} ({e.Id}) cleared";
        }
        internal static IEnumerable<string> TestList()
        {
            if(!Hosting)yield break;
            Load();
            Vector3 me=Player.m_localPlayer!=null?Player.m_localPlayer.transform.position:Vector3.zero;
            yield return $"next omen in {Math.Max(0,_ledger.NextAt-Now):0} s";
            foreach(Entry e in _ledger.Omens.OrderByDescending(x=>x.PlacedAt))
                yield return $"{e.Id} {e.Omen.Name}: {(State)e.State}, {Vector3.Distance(me,e.Pos):0} m away at {e.Pos:F0}"+(e.SeenBy!=""?$", seen by {e.SeenBy}":"");
        }
        internal static void TestSoon(){if(Hosting){Load();_ledger.NextAt=Now+5;Save();_nextTick=0;}}

        // ---- the world's own objects ----
        private static List<ZDO> All(string prefab)
        {
            var found=new List<ZDO>();int index=0;
            while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab,found,ref index)){}
            return found;
        }
        private static Vector3? NearestBase(Vector3 from)
        {
            var bases=BasePieces.SelectMany(All).Where(z=>z.GetLong(ZDOVars.s_creator,0L)!=0).Select(z=>z.GetPosition()).ToList();
            int i=Policy.Nearest(from.x,from.z,bases.Select(b=>((double)b.x,(double)b.z)).ToList(),Plugin.Instance.BaseRange.Value);
            return i<0?(Vector3?)null:bases[i];
        }
        private static void RemoveSign(long id)
        {
            foreach(ZDO zdo in All(SignPrefab.Name).Where(z=>z.GetLong(SignPrefab.IdKey,0L)==id))
            {zdo.SetOwner(ZDOMan.GetSessionID());ZDOMan.instance.DestroyZDO(zdo);}
        }
        // Signs whose ledger entry is gone (a deleted file) or finished (a crash before saving) leave the world.
        private static void Reconcile()
        {
            var known=new HashSet<long>(_ledger.Omens.Where(e=>!Policy.Finished((State)e.State)).Select(e=>e.Id));
            foreach(ZDO zdo in All(SignPrefab.Name).Where(z=>!known.Contains(z.GetLong(SignPrefab.IdKey,0L))))
            {zdo.SetOwner(ZDOMan.GetSessionID());ZDOMan.instance.DestroyZDO(zdo);Plugin.Log("Removed a sign with no open omen at "+zdo.GetPosition().ToString("F0"));}
        }

        // ---- the ledger file ----
        private static void Load()
        {
            string world=ZNet.instance.GetWorldName()+"-"+ZNet.instance.GetWorldUID();
            if(_ledger!=null&&_world==world)return;
            _world=world;_reconciled=false;
            string safe=new string(world.Select(c=>char.IsLetterOrDigit(c)||c=='-'?c:'_').ToArray());
            _path=Path.Combine(Paths.ConfigPath,"omens",safe+".json");
            try{_ledger=File.Exists(_path)?JsonConvert.DeserializeObject<Ledger>(File.ReadAllText(_path)):null;}
            catch(Exception ex){Plugin.Log("Could not read "+_path+": "+ex.Message);}
            _ledger=_ledger??new Ledger();
        }
        internal static void Save()
        {
            if(_ledger==null||_path==null)return;
            try
            {
                // Keep every open omen and the most recent finished ones as history.
                var finished=_ledger.Omens.Where(e=>Policy.Finished((State)e.State)&&e.SignRemoved).OrderByDescending(e=>e.ResolvedAt).Skip(40).ToList();
                _ledger.Omens.RemoveAll(finished.Contains);
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path+".tmp",JsonConvert.SerializeObject(_ledger,Formatting.Indented));
                if(File.Exists(_path))File.Delete(_path);
                File.Move(_path+".tmp",_path);
            }
            catch(Exception ex){Plugin.Log("Could not save "+_path+": "+ex.Message);}
        }
        internal static void Reset(){Save();_ledger=null;_world=null;_reconciled=false;}
    }
}
