using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using Random=UnityEngine.Random;
using Object=UnityEngine.Object;

namespace Omens
{
    // The host decides: where omens appear, when they come to pass. Its ledger is a small file per world beside the host's config,
    // keyed by each sign's own random id (a ZDOID changes every time the world loads).
    internal static class Director
    {
        private sealed class Entry
        {
            public long Id;public int Kind,State;public float X,Y,Z;
            public double PlacedAt,SeenAt,ResolvedAt;public bool SeenAtNight,Forced,Softened;public string SeenBy="";public bool SignRemoved;
            public bool Taken,TakenAtNight;public long TakenBy;public double TakenAt;public string TakenByName=""; // a hoard's thief
            [JsonIgnore]public Vector3 Pos=>new Vector3(X,Y,Z);
            [JsonIgnore]public Omen Omen=>Policy.Of((Kind)Kind);
        }
        private sealed class Ledger
        {
            public double NextAt;public List<Entry> Omens=new List<Entry>();
            public bool MoonActive,MoonSoftened;public double MoonUntil; // MoonUntil: a forced test moon's end; 0 ends at daybreak
            public bool AuroraActive;public double AuroraUntil;          // the northern lights, likewise
            public int Fate;                                             // the gods' favour for this world (Policy.FateMin–FateMax)
        }
        private static float _nextAuroraCall;
        private static float _nextMoonCall;

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
            foreach(Entry e in _ledger.Omens.Where(e=>Policy.Finished((State)e.State)&&!e.SignRemoved&&Policy.SignGone((State)e.State,e.Omen.Linger,now-e.ResolvedAt)).ToList())
            {RemoveSign(e.Id);Net.Resolve(e.Id);e.SignRemoved=true;changed=true;}
            changed|=MaybePlace(now);
            changed|=MoonTick(now);
            changed|=AuroraTick(now);
            if(changed)Save();
        }

        private static bool Advance(Entry e,double now)
        {
            var state=(State)e.State;bool done=false,impossible=false;string why="";
            Vector3 at=e.Pos;
            if(state==State.Seen)
            {
                Omen omen=e.Omen;
                int biome=(int)WorldGenerator.instance.GetBiome(e.X,e.Z);
                switch(omen.Result)
                {
                    case Result.Blessing:Net.Blessing(e.Pos,60);done=true;break;
                    case Result.Favour:Net.Favour(e.Pos,50);done=true;break;
                    case Result.Gift:done=Scatter(Policy.Gifts(omen.Kind,biome),e.Pos,2.5f);if(!done){impossible=true;why="the gift could not be left there";}break;
                    case Result.Treasure:done=Bury(e,biome);if(!done){impossible=true;why="no dry ground for a chest nearby";}break;
                    case Result.Quarry:done=Release(e.Pos);if(!done){impossible=true;why="no room for the stag";}break;
                    case Result.Curse:
                        if(!e.Taken)
                        {
                            // Left alone, the hoard is harmless and sinks back into the earth in time.
                            if(now-e.PlacedAt>=Plugin.Instance.ExpireDays.Value*DayLength*2)
                            {
                                e.State=(int)State.Expired;e.ResolvedAt=now;
                                Net.Tell("The hoard sinks back into the earth, untouched.",e.Pos,-1,0);
                                Plugin.Log($"{omen.Name} ({e.Id}) was left alone");
                                return true;
                            }
                            break;
                        }
                        if(!e.Forced&&!Policy.RaidDue(now,e.TakenAt,e.TakenAtNight,EnvMan.IsNight()))break;
                        Vector3? thief=PlayerPos(e.TakenBy);
                        why=ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs)?"monsters are passive in this world"
                            :now-e.TakenAt>Policy.CurseDays*DayLength?$"{e.TakenByName} was never found":"";
                        if(why!=""){impossible=true;break;}
                        if(thief==null)break; // away, or in a dungeon: the dead wait for them
                        done=Hunt(thief.Value,Policy.Pack(omen.Kind,(int)WorldGenerator.instance.GetBiome(thief.Value.x,thief.Value.z)),25,35);
                        at=thief.Value;break;
                    case Result.Offering:
                        // Nothing comes of a shrine unless someone leaves an offering (OnRespond); left alone it is forgotten again.
                        if(now-e.PlacedAt>=Plugin.Instance.ExpireDays.Value*DayLength*2)
                        {
                            e.State=(int)State.Expired;e.ResolvedAt=now;
                            Plugin.Log($"{omen.Name} ({e.Id}) was left without an offering");
                            return true;
                        }
                        break;
                    case Result.Aurora:
                        if(!e.Forced&&!Policy.RaidDue(now,e.SeenAt,e.SeenAtNight,EnvMan.IsNight()))break;
                        if(_ledger.AuroraActive&&!e.Forced)break;
                        _ledger.AuroraActive=true;
                        _ledger.AuroraUntil=e.Forced&&!EnvMan.IsNight()?now+300:0;
                        Net.Aurora(true);_nextAuroraCall=Time.time+20;
                        done=true;break;
                    case Result.BloodMoon:
                        if(!e.Forced&&!Policy.RaidDue(now,e.SeenAt,e.SeenAtNight,EnvMan.IsNight()))break;
                        if(_ledger.MoonActive&&!e.Forced)break; // one blood moon at a time: it waits for tonight's to wane
                        _ledger.MoonActive=true;_ledger.MoonSoftened|=e.Softened;
                        _ledger.MoonUntil=e.Forced&&!EnvMan.IsNight()?now+300:0;
                        Net.BloodMoon(true,_ledger.MoonSoftened);_nextMoonCall=Time.time+20;
                        done=true;break;
                    default: // a raid or a hunting pack at the nearest base
                        if(!e.Forced&&!Policy.RaidDue(now,e.SeenAt,e.SeenAtNight,EnvMan.IsNight()))break;
                        Vector3? home=NearestBase(e.Pos);
                        why=Game.m_eventRate<=0?"raids are turned off in this world"
                            :omen.Result==Result.Stalkers&&ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs)?"monsters are passive in this world"
                            :home==null?$"no workbench or bed within {Plugin.Instance.BaseRange.Value:0} m"
                            :now-e.SeenAt>DayLength*1.5?"it waited too long for its moment"
                            :omen.Result==Result.Raid&&!RandEventSystem.instance.HaveEvent(omen.Raid)?$"the game has no {omen.Raid} raid":"";
                        if(why!="")impossible=true;
                        else if(omen.Result==Result.Raid&&RandEventSystem.instance.GetCurrentRandomEvent()==null) // never interrupts a raid in progress
                        {RandEventSystem.instance.SetRandomEventByName(omen.Raid,home.Value);at=home.Value;done=true;}
                        else if(omen.Result==Result.Stalkers&&Players().Any(p=>Vector3.Distance(p,home.Value)<150)) // they come when someone is home
                        {done=Hunt(home.Value,Policy.Pack(omen.Kind,(int)WorldGenerator.instance.GetBiome(home.Value.x,home.Value.z)),35,45);at=home.Value;}
                        break;
                }
            }
            var next=Policy.Advance(state,now,e.PlacedAt,Plugin.Instance.ExpireDays.Value*DayLength,false,false,done,impossible);
            if(next==state)return false;
            e.State=(int)next;e.ResolvedAt=now;
            switch(next)
            {
                case State.Fulfilled:
                    Net.Tell(e.Omen.Result==Result.BloodMoon&&e.Softened?"The moon bleeds, but the offering has dulled its hunger.":e.Omen.Outcome,at,e.Omen.Bad?0:60,0);
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) came to pass near {at:F0}"+(e.Omen.Result==Result.Raid?$": {e.Omen.Raid}":""));
                    // A warning that could have been answered, and was not: the gods notice. (A thief's curse was already counted.)
                    if(e.Omen.Bad&&e.Omen.Respondable&&!e.Softened&&e.Omen.Result!=Result.Curse)Favour(Policy.FateIgnored,at,"left "+e.Omen.Name.ToLowerInvariant()+" unanswered");
                    break;
                case State.Fizzled:
                    Net.Tell("The omen passes. Whatever it foretold did not find you.",e.Pos,-1,0);
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) fizzled: {why}");break;
                case State.Expired:
                    Plugin.Log($"{e.Omen.Name} ({e.Id}) at {e.Pos:F0} faded unseen");break;
            }
            return true;
        }

        // The gods' favour moves; everyone hears when their standing changes.
        private static void Favour(int delta,Vector3 at,string why)
        {
            int before=_ledger.Fate;
            _ledger.Fate=Policy.Fate(before,delta);
            if(_ledger.Fate==before)return;
            Plugin.Log($"The gods' favour {before} -> {_ledger.Fate} ({Policy.Standing(_ledger.Fate)}): {why}");
            string news=Policy.StandingNews(before,_ledger.Fate);
            if(news!=null)Net.Tell(news,at,0,0);
        }

        // The northern lights: told to everyone now and then; they fade at daybreak (a forced test one after five minutes).
        private static bool AuroraTick(double now)
        {
            if(!_ledger.AuroraActive)return false;
            bool over=_ledger.AuroraUntil>0?now>=_ledger.AuroraUntil:!EnvMan.IsNight();
            if(over)
            {
                _ledger.AuroraActive=false;_ledger.AuroraUntil=0;
                Net.Aurora(false);Net.Tell("The northern lights fade with the dawn.",Vector3.zero,-1,0);
                Plugin.Log("The northern lights faded");
                return true;
            }
            if(Time.time>=_nextAuroraCall){_nextAuroraCall=Time.time+20;Net.Aurora(true);}
            return false;
        }

        // While a blood moon lasts, tell everyone now and then (late arrivals, reloads); it wanes at daybreak (a forced test one after five minutes).
        private static bool MoonTick(double now)
        {
            if(!_ledger.MoonActive)return false;
            bool over=_ledger.MoonUntil>0?now>=_ledger.MoonUntil:!EnvMan.IsNight();
            if(over)
            {
                _ledger.MoonActive=false;_ledger.MoonSoftened=false;_ledger.MoonUntil=0;
                Net.BloodMoon(false,false);Net.Tell("The blood moon wanes.",Vector3.zero,0,0);
                Plugin.Log("The blood moon waned");
                return true;
            }
            if(Time.time>=_nextMoonCall){_nextMoonCall=Time.time+20;Net.BloodMoon(true,_ledger.MoonSoftened);}
            return false;
        }

        // A pack between min and max metres out on dry ground, set to hunt players. Saved creatures keep hunting
        // when their area unloads and loads again.
        private static bool Hunt(Vector3 center,(string prefab,int level)[] pack,float min,float max)
        {
            for(int attempt=0;attempt<12;attempt++)
            {
                Vector2 dir=Random.insideUnitCircle.normalized*Random.Range(min,max);
                if(!DryGround(new Vector3(center.x+dir.x,center.y,center.z+dir.y),out Vector3 spot))continue;
                int made=0;
                foreach(var (name,level) in pack)
                {
                    GameObject prefab=ZNetScene.instance.GetPrefab(name);
                    if(prefab==null){Plugin.Log("Omens: no creature "+name);continue;}
                    Vector2 jitter=Random.insideUnitCircle*3;
                    GameObject go=Object.Instantiate(prefab,spot+new Vector3(jitter.x,0.5f,jitter.y),Quaternion.LookRotation(center-spot));
                    go.GetComponent<Character>()?.SetLevel(Policy.Wrath(level,_ledger.Fate));
                    go.GetComponent<BaseAI>()?.SetHuntPlayer(true);
                    made++;
                }
                if(made>0)Plugin.Log($"Sent {made} hunters toward {center:F0}");
                return made>0;
            }
            return false;
        }
        private static bool DryGround(Vector3 at,out Vector3 spot)
        {
            spot=at;
            spot.y=ZoneSystem.instance.GetSolidHeight(at,out float solid)?solid:WorldGenerator.instance.GetHeight(at.x,at.z);
            return spot.y>=ZoneSystem.instance.m_waterLevel+0.5f;
        }
        // Real items on the ground around a spot, ready to be picked up: stranded fish, a star's ore, a hoard's gold.
        private static bool Scatter((string prefab,int min,int max)[] gifts,Vector3 at,float radius)
        {
            int made=0;
            foreach(var (name,min,max) in gifts)
            {
                GameObject prefab=ZNetScene.instance.GetPrefab(name);
                if(prefab==null){Plugin.Log("Omens: no item "+name);continue;}
                int count=Policy.Generous(Policy.Roll(min,max,Random.value),_ledger.Fate);
                // Stackable items land as a few stacks, not one pile or a hundred coins.
                int stacks=Math.Min(count,name=="Fish1"?count:3),left=count;
                for(int i=0;i<stacks;i++)
                {
                    int amount=i==stacks-1?left:count/stacks;left-=amount;
                    Vector2 jitter=Random.insideUnitCircle*radius;
                    var spot=at+new Vector3(jitter.x,0,jitter.y);
                    if(ZoneSystem.instance.GetSolidHeight(spot,out float h))spot.y=h;
                    GameObject go=Object.Instantiate(prefab,spot+Vector3.up*0.4f,Quaternion.Euler(0,Random.Range(0,360f),name=="Fish1"?90:0));
                    if(amount>1)go.GetComponent<ItemDrop>()?.SetStack(amount);
                    made++;
                }
            }
            return made>0;
        }
        // The lights' treasure: the game's own chest for the land, 40–70 m away on dry ground, which fills itself with that land's loot.
        private static bool Bury(Entry e,int biome)
        {
            GameObject chest=ZNetScene.instance.GetPrefab(Policy.Chest(biome))??ZNetScene.instance.GetPrefab(Policy.Chest(Policy.Meadows));
            if(chest==null)return false;
            for(int attempt=0;attempt<16;attempt++)
            {
                Vector2 dir=Random.insideUnitCircle.normalized*Random.Range(40f,70f);
                if(!DryGround(new Vector3(e.X+dir.x,e.Y,e.Z+dir.y),out Vector3 spot)||NearBuilding(spot))continue;
                Object.Instantiate(chest,spot,Quaternion.Euler(0,Random.Range(0,360f),0));
                Net.Lead(e.Pos,spot);
                Net.Mark(spot,"Treasure");
                Plugin.Log($"The lights lead to a {chest.name} at {spot:F0}");
                return true;
            }
            return false;
        }
        // The great stag, 20–35 m from its antler: a two-star deer, marked so every game draws it larger and its drops include its antlers.
        private static bool Release(Vector3 at)
        {
            GameObject deer=ZNetScene.instance.GetPrefab("Deer");
            if(deer==null)return false;
            for(int attempt=0;attempt<12;attempt++)
            {
                Vector2 dir=Random.insideUnitCircle.normalized*Random.Range(20f,35f);
                if(!DryGround(new Vector3(at.x+dir.x,at.y,at.z+dir.y),out Vector3 spot))continue;
                GameObject go=Object.Instantiate(deer,spot+Vector3.up*0.3f,Quaternion.LookRotation(spot-at));
                go.GetComponent<ZNetView>()?.GetZDO()?.Set(Stag.Key,true); // before its Start, where every game reads it
                go.GetComponent<Character>()?.SetLevel(Policy.StagLevel);
                Net.Mark(spot,"Great stag");
                Plugin.Log($"A great stag runs at {spot:F0}");
                return true;
            }
            return false;
        }
        // Where a player is now, unless away, dead or in a dungeon.
        private static Vector3? PlayerPos(long uid)
        {
            if(uid==ZNet.GetUID())
                return Player.m_localPlayer!=null&&!Player.m_localPlayer.IsDead()&&Player.m_localPlayer.transform.position.y<3000?Player.m_localPlayer.transform.position:(Vector3?)null;
            ZNetPeer peer=ZNet.instance.GetPeer(uid);
            return peer!=null&&peer.IsReady()&&!peer.m_characterID.IsNone()&&peer.m_refPos.y<3000?peer.m_refPos:(Vector3?)null;
        }

        internal static void OnSeen(long sender,long id)
        {
            if(!Hosting)return;
            Load();
            Entry e=_ledger.Omens.FirstOrDefault(x=>x.Id==id);
            if(e==null||(State)e.State!=State.Placed)return;
            e.State=(int)Policy.Advance(State.Placed,Now,e.PlacedAt,double.MaxValue,true,false,false,false);
            e.SeenAt=Now;e.SeenAtNight=EnvMan.IsNight();e.SeenBy=NameOf(sender);
            Net.Tell(Policy.ReadingOf(e.Omen.Kind,e.Id),e.Pos,40,e.Id);
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
            if(e.Omen.Softens)
            {
                // It still comes, weaker; the offering is taken, so the sign goes now.
                if(e.Softened)return;
                e.Softened=true;
                if((State)e.State==State.Placed){e.State=(int)State.Seen;e.SeenAt=Now;e.SeenAtNight=EnvMan.IsNight();e.SeenBy=NameOf(sender);}
                RemoveSign(e.Id);Net.Resolve(e.Id);
                Net.Tell(e.Omen.Averted,e.Pos,60,0);
                Plugin.Log($"{NameOf(sender)} softened {e.Omen.Name} ({e.Id})");
                Favour(Policy.FateAverted,e.Pos,NameOf(sender)+" softened "+e.Omen.Name.ToLowerInvariant());
                Save();_nextTick=0;
                return;
            }
            if(e.Omen.Result==Result.Curse)
            {
                // The thief takes the gold; the dead remember who.
                if(e.Taken)return;
                e.Taken=true;e.TakenBy=sender;e.TakenByName=NameOf(sender);e.TakenAt=Now;e.TakenAtNight=EnvMan.IsNight();
                if((State)e.State==State.Placed){e.State=(int)State.Seen;e.SeenAt=Now;e.SeenAtNight=e.TakenAtNight;e.SeenBy=e.TakenByName;}
                Scatter(Policy.Gifts(e.Omen.Kind,(int)WorldGenerator.instance.GetBiome(e.X,e.Z)),e.Pos,1.2f);
                RemoveSign(e.Id);Net.Resolve(e.Id);
                Net.Tell(e.Omen.Averted,e.Pos,60,0);
                Plugin.Log($"{e.TakenByName} took {e.Omen.Name} ({e.Id})");
                Favour(Policy.FateTaken,e.Pos,e.TakenByName+" robbed the dead");
                Save();_nextTick=0;
                return;
            }
            e.State=(int)Policy.Advance((State)e.State,Now,e.PlacedAt,double.MaxValue,false,true,false,false);
            e.ResolvedAt=Now;
            Net.Responded(e.Id); // before the sign leaves the world
            if(e.Omen.Provokes)Hunt(e.Pos,Policy.Pack(e.Omen.Kind,(int)WorldGenerator.instance.GetBiome(e.X,e.Z)),12,20); // the fight comes now instead
            Net.Tell(e.Omen.Averted,e.Pos,60,0);
            if(e.Omen.Result==Result.Offering){Net.Blessing(e.Pos,40);Favour(Policy.FateOffering,e.Pos,NameOf(sender)+" made an offering");}
            else Favour(Policy.FateAverted,e.Pos,NameOf(sender)+" answered "+e.Omen.Name.ToLowerInvariant());
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
            double badChance=Game.m_eventRate>0?Policy.BadChance(Plugin.Instance.BadChance.Value,_ledger.Fate):0; // a world without raids gets only good omens
            float water=ZoneSystem.instance.m_waterLevel;
            for(int attempt=0;attempt<16;attempt++)
            {
                Vector3 near=players[Random.Range(0,players.Count)];
                Vector2 offset=Random.insideUnitCircle.normalized*Random.Range(45f,85f);
                var spot=new Vector3(near.x+offset.x,near.y,near.z+offset.y);
                if(!ZoneSystem.instance.GetSolidHeight(spot,out float height)||height<water+0.6f)continue;
                spot.y=height;
                if(NearBuilding(spot)||players.Any(p=>Vector3.Distance(p,spot)<35))continue;
                int biome=(int)WorldGenerator.instance.GetBiome(spot.x,spot.z);
                // Only omens that fit this very spot: birds need open sky to be seen, the catch needs a shore.
                Kind? kind=Policy.Pick(Random.value,Random.value,badChance,biome,enabled.Where(k=>Fits(Policy.Of(k).Site,spot)).ToList());
                if(kind==null||
                   _ledger.Omens.Any(e=>!Policy.Finished((State)e.State)&&Vector3.Distance(e.Pos,spot)<150))continue;
                Place(kind.Value,spot,now);
                _ledger.NextAt=now+Policy.NextDelay(Plugin.Instance.IntervalDays.Value,DayLength,Random.value)*Policy.IntervalFactor(_ledger.Fate);
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
        private static bool Fits(Site site,Vector3 spot)=>site==Site.Any||(site==Site.OpenSky?OpenSky(spot):Shore(spot));
        // Dry ground just above the water, with water within 16 metres: a beach or a lake shore.
        private static bool Shore(Vector3 spot)
        {
            float water=ZoneSystem.instance.m_waterLevel;
            if(spot.y>water+3||spot.y<water+0.3f)return false;
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;
                foreach(float reach in new[]{8f,16f})
                    if(WorldGenerator.instance.GetHeight(spot.x+Mathf.Cos(a)*reach,spot.z+Mathf.Sin(a)*reach)<water-0.5f)return true;
            }
            return false;
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
            Site site=Policy.Of(kind).Site;
            if(site!=Site.Any)
            {
                // The nearest spot that fits within 80 metres of the one asked for.
                Vector3 asked=spot;bool found=false;
                for(int ring=0;ring<=80&&!found;ring+=5)for(int step=0;step<Math.Max(1,ring)&&!found;step++)
                {
                    float a=step*Mathf.PI*2/Math.Max(1,ring);var test=asked+new Vector3(Mathf.Cos(a)*ring,0,Mathf.Sin(a)*ring);
                    if(ZoneSystem.instance.GetSolidHeight(test,out float h)){test.y=h;if(Fits(site,test)){spot=test;found=true;}}
                }
                if(!found)return $"none: no {(site==Site.OpenSky?"open sky":"shore")} within 80 m";
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
            if(e.Omen.Result==Result.Curse&&!e.Taken)OnRespond(ZNet.GetUID(),e.Id); // the host takes the hoard, so the dead come for the host
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
            yield return $"next omen in {Math.Max(0,_ledger.NextAt-Now):0} s; the gods' favour {_ledger.Fate} ({Policy.Standing(_ledger.Fate)})"+
                (_ledger.MoonActive?"; a blood moon is up":"")+(_ledger.AuroraActive?"; the northern lights are up":"");
            foreach(Entry e in _ledger.Omens.OrderByDescending(x=>x.PlacedAt))
                yield return $"{e.Id} {e.Omen.Name}: {(State)e.State}, {Vector3.Distance(me,e.Pos):0} m away at {e.Pos:F0}"+(e.SeenBy!=""?$", seen by {e.SeenBy}":"");
        }
        internal static string TestFate(string value)
        {
            if(!Hosting)return "not the host";
            Load();
            if(int.TryParse(value,out int to))Favour(to-_ledger.Fate,Player.m_localPlayer!=null?Player.m_localPlayer.transform.position:Vector3.zero,"set for testing");
            Save();
            return $"{_ledger.Fate} ({Policy.Standing(_ledger.Fate)})";
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
