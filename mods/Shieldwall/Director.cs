using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;
using Random=UnityEngine.Random;

namespace Shieldwall
{
    // Runs a siege, on the game that owns its Warstone. Everything it needs is saved on the stone, so if that player leaves and
    // another takes the stone over, the siege carries on where it was.
    internal static class Director
    {
        private static readonly Dictionary<Warstone,float> NextTick=new Dictionary<Warstone,float>();
        private static readonly Dictionary<Warstone,float> NextRelease=new Dictionary<Warstone,float>();
        private static readonly Dictionary<Warstone,float> NextGlow=new Dictionary<Warstone,float>();
        private static readonly Dictionary<Warstone,float> Lonely=new Dictionary<Warstone,float>();

        internal static int TestStage=-1; // the test command's choice of horde
        internal static int StageNow()=>ZoneSystem.instance==null?0:Policy.Stage(key=>ZoneSystem.instance.GetGlobalKey(key));
        private static long Now=>ZNet.instance.GetTime().Ticks;
        private static long After(double seconds)=>Now+(long)(seconds*TimeSpan.TicksPerSecond);
        private static double Since(long ticks)=>(Now-ticks)/(double)TimeSpan.TicksPerSecond;

        // ---- calling a siege: the horn, a raid the stone draws to itself, or a test ----
        internal static void OnCall(Warstone stone,Cause cause)
        {
            ZDO z=stone.Z;
            if(z==null||stone.Phase!=Phase.Idle)return;
            if(cause==Cause.Horn&&z.GetLong(Stone.CooldownKey,0L)>Now)return;
            Vector3 rift=FindRift(stone.transform.position);
            int stage=TestStage>=0&&cause==Cause.Test?TestStage:StageNow(),marks=stone.Strength;
            int players=Player.GetAllPlayers().Count(p=>p!=null&&Vector3.Distance(p.transform.position,stone.transform.position)<80);
            int seed=Random.Range(1,int.MaxValue);
            var plan=Policy.Plan(stage,marks,Math.Max(1,players),seed);
            double gather=cause==Cause.Test?10:cause==Cause.Raid?Plugin.Instance.RaidWarning.Value:Plugin.Instance.HornWarning.Value;
            float health=Policy.StoneHealth(stage,marks);
            z.Set(Stone.SiegeKey,((long)Random.Range(1,int.MaxValue)<<16)^Now);
            z.Set(Stone.RiftKey,rift);z.Set(Stone.StartKey,After(gather));z.Set(Stone.CalledKey,Now);z.Set(Stone.PlanKey,Policy.Save(plan));
            z.Set(Stone.WaveKey,0);z.Set(Stone.QueueKey,0);z.Set(Stone.SpawnedKey,0);z.Set(Stone.KillsKey,0);z.Set(Stone.StageKey,stage);z.Set(Stone.CauseKey,(int)cause);
            z.Set(Stone.HealthKey,health);z.Set(Stone.MaxHealthKey,health);
            z.Set(Stone.PhaseKey,(int)Phase.Gathering);
            Assets.Effect("sfx_fader_bell",stone.transform.position+Vector3.up*2);
            Assets.Effect("vfx_prespawn",rift);Assets.Effect("sfx_prespawn",rift);
            Roster roster=Policy.Rosters[stage];
            string opening=cause==Cause.Raid?$"The Warstone draws the raid to itself! A {roster.Name} gathers to the {Compass(stone.transform.position,rift)}.":
                $"The war horn sounds! A {roster.Name} gathers to the {Compass(stone.transform.position,rift)}.";
            Net.Say($"{opening} They march in {gather:0} seconds: {plan.Count} waves, {plan.Sum(w=>w.Count)} strong.",stone.transform.position,250);
            Plugin.Log($"Siege called ({cause}) at {stone.transform.position:F0}: {roster.Name}, {plan.Count} waves, {plan.Sum(w=>w.Count)} raiders, rift {rift:F0}");
        }

        // ---- every moment of a siege ----
        internal static void Run(Warstone stone)
        {
            Phase phase=stone.Phase;
            if(phase==Phase.Idle)return;
            if(NextTick.TryGetValue(stone,out float next)&&Time.time<next)return;
            NextTick[stone]=Time.time+0.25f;
            ZDO z=stone.Z;
            Vector3 rift=stone.Rift;
            // The rift churns while it is open.
            if(!NextGlow.TryGetValue(stone,out float glow)||Time.time>=glow){NextGlow[stone]=Time.time+4;Assets.Effect("vfx_prespawn",rift);}
            if(phase==Phase.Gathering)
            {
                if(Now<z.GetLong(Stone.StartKey,0L))return;
                z.Set(Stone.PhaseKey,(int)Phase.Battle);z.Set(Stone.WaveAtKey,Now);
                Assets.Effect("sfx_gdking_scream",rift);
                Net.Say("They come! Hold the line!",stone.transform.position,250);
                return;
            }
            // Battle.
            var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
            if(plan.Count==0){End(stone,Outcome.Abandoned);return;}
            int wave=Mathf.Clamp(z.GetInt(Stone.WaveKey,0),0,plan.Count-1),queue=z.GetInt(Stone.QueueKey,0),spawned=z.GetInt(Stone.SpawnedKey,0);
            long siege=stone.Siege;
            int alive=Raider.Loaded.Count(r=>r!=null&&r.Siege==siege&&r.Body!=null&&!r.Body.IsDead());
            z.Set(Stone.KillsKey,Math.Max(z.GetInt(Stone.KillsKey,0),spawned-alive));
            if(z.GetFloat(Stone.HealthKey,1)<=0){End(stone,Outcome.Fallen);return;}
            // Nobody left to defend it: the horde drifts away (no loss, no gain).
            bool defended=Player.GetAllPlayers().Any(p=>p!=null&&!p.IsDead()&&Vector3.Distance(p.transform.position,stone.transform.position)<150);
            if(defended)Lonely.Remove(stone);
            else if(!Lonely.ContainsKey(stone))Lonely[stone]=Time.time;
            else if(Time.time-Lonely[stone]>60){End(stone,Outcome.Abandoned);return;}
            if(Since(z.GetLong(Stone.StartKey,0L))>Plugin.Instance.MaxMinutes.Value*60){End(stone,Outcome.Held);return;}

            List<Unit> units=plan[wave];
            if(queue<units.Count)
            {
                if(!NextRelease.TryGetValue(stone,out float release)||Time.time>=release)
                {
                    NextRelease[stone]=Time.time+1.5f;
                    int count=Policy.Release(units.Count-queue,alive,Plugin.Instance.MaxAlive.Value);
                    for(int i=0;i<count;i++)Spawn(stone,units[queue+i],rift,siege);
                    if(count>0){Assets.Effect("vfx_spawn_large",rift);queue+=count;spawned+=count;z.Set(Stone.QueueKey,queue);z.Set(Stone.SpawnedKey,spawned);}
                }
                return;
            }
            double since=Since(z.GetLong(Stone.WaveAtKey,Now));
            if(wave<plan.Count-1)
            {
                if(!Policy.NextWave(alive,units.Count,since,Plugin.Instance.WaveSeconds.Value))return;
                wave++;
                z.Set(Stone.WaveKey,wave);z.Set(Stone.QueueKey,0);z.Set(Stone.WaveAtKey,Now);
                Assets.Effect("sfx_gdking_scream",rift);
                Net.Say(wave==plan.Count-1?"The warchief comes! The last wave!":$"Wave {wave+1} of {plan.Count}!",stone.transform.position,250);
                return;
            }
            // The last wave is out: the siege is won when it is down (stragglers hiding past a while count as down).
            if(alive==0||alive<=2&&since>75)End(stone,Outcome.Held); // a straggler or two stuck out of sight does not hold the siege open
        }

        private static void Spawn(Warstone stone,Unit unit,Vector3 rift,long siege)
        {
            GameObject prefab=ZNetScene.instance.GetPrefab(unit.Prefab);
            if(prefab==null){Plugin.Log("Shieldwall: no creature "+unit.Prefab);return;}
            Vector2 jitter=Random.insideUnitCircle*4;
            Vector3 at=new Vector3(rift.x+jitter.x,rift.y,rift.z+jitter.y);
            if(ZoneSystem.instance.GetSolidHeight(at,out float height))at.y=height;
            at.y+=unit.Role==Role.Flyer?4:0.5f;
            Vector3 face=stone.transform.position-at;face.y=0;
            GameObject go=Object.Instantiate(prefab,at,Quaternion.LookRotation(face.sqrMagnitude>0.01f?face:Vector3.forward));
            ZNetView view=go.GetComponent<ZNetView>();
            Character body=go.GetComponent<Character>();
            if(view==null||!view.IsValid()||body==null||go.GetComponent<MonsterAI>()==null){if(view!=null&&view.IsValid())ZNetScene.instance.Destroy(go);else Object.Destroy(go);return;}
            ZDO z=view.GetZDO();
            z.Set(Raider.RoleKey,(int)unit.Role);z.Set(Raider.SiegeKey,siege);z.Set(Raider.StoneKey,stone.transform.position);
            body.SetLevel(unit.Level);
            Raider.Attach(go);
        }

        // ---- the end ----
        internal enum Outcome{Held,Fallen,Abandoned}
        private static void End(Warstone stone,Outcome outcome)
        {
            ZDO z=stone.Z;
            int stage=z.GetInt(Stone.StageKey,0),marks=stone.Marks,kills=z.GetInt(Stone.KillsKey,0);
            float health=z.GetFloat(Stone.HealthKey,0),max=Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1));
            Roster roster=Policy.Rosters[Mathf.Clamp(stage,0,Policy.Rosters.Length-1)];
            Cause cause=(Cause)z.GetInt(Stone.CauseKey,0);
            z.Set(Stone.PhaseKey,(int)Phase.Idle);
            z.Set(Stone.CooldownKey,After(Plugin.Instance.CooldownMinutes.Value*60));
            NextRelease.Remove(stone);Lonely.Remove(stone);
            switch(outcome)
            {
                case Outcome.Held:
                    int now=cause==Cause.Test?marks:Policy.Marks(marks+1);
                    z.Set(Stone.MarksKey,now);z.Set(Stone.CrackedKey,false);z.Set(Stone.HeldKey,z.GetInt(Stone.HeldKey,0)+1);
                    int shards=Reward(stone,roster,stage,marks,health/max,kills);
                    Assets.Effect("sfx_fader_bell",stone.transform.position+Vector3.up*2);
                    Assets.Effect("vfx_HealthUpgrade",stone.transform.position);Assets.Effect("fx_DvergerMage_Support_start",stone.transform.position+Vector3.up);
                    string rank=now>marks?$" It is now {Policy.Title(now)} ({Policy.Numeral(now)}).":"";
                    Net.Say($"The {roster.Name} breaks! {kills} slain, the stone at {Mathf.RoundToInt(100*health/max)}%.{rank} Spoils with {shards} warshards lie at its foot.",stone.transform.position,250);
                    break;
                case Outcome.Fallen:
                    z.Set(Stone.CrackedKey,true);z.Set(Stone.FallenKey,z.GetInt(Stone.FallenKey,0)+1);
                    Assets.Effect("sfx_gdking_scream",stone.transform.position);
                    Net.Say("The Warstone cracks! The horde howls and melts away. Its ward is weaker until it holds a siege again.",stone.transform.position,250);
                    break;
                default:
                    Net.Say("With no one to face them, the horde melts back into the wilds.",stone.transform.position,250);
                    break;
            }
            Plugin.Log($"Siege at {stone.transform.position:F0} ended: {outcome}, {kills} slain, stone {Mathf.RoundToInt(100*health/max)}%");
        }
        private static int Reward(Warstone stone,Roster roster,int stage,int marks,float health,int kills)
        {
            // On the far side from the rift, out of the next horde's way.
            Vector3 away=stone.transform.position-stone.Rift;away.y=0;
            away=away.sqrMagnitude>1?away.normalized:-stone.transform.forward;
            Vector3 front=stone.transform.position+away*4.5f;
            if(ZoneSystem.instance.GetSolidHeight(front,out float height))front.y=height;
            GameObject prefab=ZNetScene.instance.GetPrefab(roster.Chest);
            Inventory inventory=null;
            if(prefab!=null)
            {
                GameObject chest=Object.Instantiate(prefab,front,Quaternion.LookRotation(stone.transform.position-front));
                inventory=chest.GetComponent<Container>()?.GetInventory();
            }
            var gifts=new List<(string prefab,int amount)>{(Policy.ShardPrefab,Policy.Shards(stage,marks,health,kills))};
            foreach(var (name,min,max) in roster.Spoils)gifts.Add((name,Policy.Coins(min,max,marks,Random.value)));
            foreach(var (name,amount) in gifts)
            {
                GameObject item=name==Policy.ShardPrefab?Items.Get(name):ZNetScene.instance.GetPrefab(name);
                if(item==null||amount<=0)continue;
                if(inventory!=null&&inventory.CanAddItem(item,amount))inventory.AddItem(item,amount);
                else if(item.GetComponent<ItemDrop>() is ItemDrop drop){ItemDrop.ItemData data=drop.m_itemData.Clone();data.m_dropPrefab=item;ItemDrop.DropItem(data,amount,front+Vector3.up,Quaternion.identity);}
            }
            return gifts[0].amount;
        }

        // ---- damage to the stone ----
        internal static void OnDamage(Warstone stone,float amount,Vector3 at)
        {
            ZDO z=stone.Z;
            if(z==null||stone.Phase!=Phase.Battle)return;
            float max=Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1)),before=z.GetFloat(Stone.HealthKey,max);
            float after=Mathf.Clamp(before-(amount>0?amount*Policy.Toughness:amount),0,max);
            z.Set(Stone.HealthKey,after);
            if(amount>0)
            {
                foreach(float mark in new[]{0.5f,0.25f,0.1f})
                    if(before/max>mark&&after/max<=mark)Net.Say($"The Warstone is cracking! {Mathf.RoundToInt(mark*100)}% left!",stone.transform.position,250);
                Assets.Effect("vfx_RockHit",at);
            }
        }

        // ---- where the rift opens: dry ground in reach of the stone by foot, not too near, inside the loaded world ----
        private static Vector3 FindRift(Vector3 stone)
        {
            Vector3 fallback=stone+Vector3.forward*70;
            for(int attempt=0;attempt<60;attempt++)
            {
                Vector2 dir=Random.insideUnitCircle.normalized*Random.Range(60f,95f);
                Vector3 at=new Vector3(stone.x+dir.x,stone.y,stone.z+dir.y);
                if(!ZoneSystem.instance.IsZoneLoaded(at))continue;
                if(!ZoneSystem.instance.GetSolidHeight(at,out float height)||height<ZoneSystem.instance.m_waterLevel+0.5f)continue;
                at.y=height;
                if(EffectArea.IsPointInsideArea(at,EffectArea.Type.PlayerBase,5)!=null)continue; // not in someone's yard
                if(attempt==0||fallback==stone+Vector3.forward*70)fallback=at;
                if(Pathfinding.instance!=null&&Pathfinding.instance.HavePath(at,stone,Pathfinding.AgentType.Humanoid))return at;
                if(attempt>40)return fallback;
            }
            return fallback;
        }
        private static string Compass(Vector3 from,Vector3 to)
        {
            float angle=Mathf.Atan2(to.x-from.x,to.z-from.z)*Mathf.Rad2Deg;
            string[] names={"north","north-east","east","south-east","south","south-west","west","north-west"};
            return names[((int)Mathf.Round(((angle%360)+360)%360/45f))%8];
        }
        internal static void Forget(Warstone stone){NextTick.Remove(stone);NextRelease.Remove(stone);NextGlow.Remove(stone);Lonely.Remove(stone);}
        internal static void Reset(){NextTick.Clear();NextRelease.Clear();NextGlow.Clear();Lonely.Clear();}
    }
}
