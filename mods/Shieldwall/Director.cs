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
        internal static int TestBoasts,TestTrick=-1; // the test command's boasts and warchief's trick
        internal static void OnCall(Warstone stone,Cause cause,int boasts=0)
        {
            ZDO z=stone.Z;
            if(z==null)return;
            if(cause==Cause.Early){CallEarly(stone);return;}
            if(stone.Phase!=Phase.Idle)return;
            if(cause==Cause.Horn&&z.GetLong(Stone.CooldownKey,0L)>Now)return;
            Vector3 rift=FindRift(stone.transform.position);
            int marks=stone.Strength,stage=TestStage>=0&&cause==Cause.Test?TestStage:Policy.SiegeStage(StageNow(),marks);
            int players=Player.GetAllPlayers().Count(p=>p!=null&&Vector3.Distance(p.transform.position,stone.transform.position)<80);
            int seed=Random.Range(1,int.MaxValue);
            // Boasts: only those the stone offered (a test may swear any).
            boasts=cause==Cause.Test?TestBoasts:cause==Cause.Horn?Policy.Allowed(boasts,stone.BoastOffer):0;
            var plan=Policy.Plan(stage,marks,Math.Max(1,players),seed,boasts,Plugin.Instance.ExtraWaves.Value);
            double gather=cause==Cause.Test?10:cause==Cause.Raid?Plugin.Instance.RaidWarning.Value:Plugin.Instance.HornWarning.Value;
            float health=Policy.StoneHealth(stage,marks)*Policy.BoonHealth(stone.Boons)*(Policy.Has(boasts,Boast.BareStone)?0.6f:1);
            z.Set(Stone.SiegeKey,((long)Random.Range(1,int.MaxValue)<<16)^Now);
            z.Set(Stone.RiftKey,rift);z.Set(Stone.StartKey,After(gather));z.Set(Stone.CalledKey,Now);z.Set(Stone.PlanKey,Policy.Recipe(stage,marks,Math.Max(1,players),seed,boasts,Plugin.Instance.ExtraWaves.Value));
            z.Set(Stone.WaveKey,0);z.Set(Stone.QueueKey,0);z.Set(Stone.SpawnedKey,0);z.Set(Stone.BreatherKey,0L);z.Set(Stone.KillsKey,0);z.Set(Stone.StageKey,stage);z.Set(Stone.CauseKey,(int)cause);
            z.Set(Stone.HealthKey,health);z.Set(Stone.MaxHealthKey,health);stone.PendingHealth=-1;
            z.Set(Stone.SagaKey,"");z.Set(Stone.SagaTopicKey,""); // the last saga leaves the record (resent with every blow) once a new siege is called
            z.Set(Stone.BoastsKey,boasts);z.Set(Stone.TallyKey,"");z.Set(Stone.ChiefsKey,"");z.Set(Stone.ChiefFellKey,"");z.Set(Stone.LowKey,1f);z.Set(Stone.LowWaveKey,0);
            z.Set(Stone.PhaseKey,(int)Phase.Gathering);
            foreach(Raider r in Raider.Loaded.ToList())if(r!=null&&r.Siege!=0&&r.Siege!=stone.Siege&&r.Stone()==stone)r.Melt(); // an old horde's stragglers
            Assets.Effect("sfx_fader_bell",stone.transform.position+Vector3.up*2);
            Assets.Effect("vfx_prespawn",rift);Assets.Effect("sfx_prespawn",rift);
            Roster roster=Policy.Rosters[stage];
            string opening=cause==Cause.Raid?$"The Warstone draws the raid to itself! A {roster.Name} gathers to the {Compass(stone.transform.position,rift)}.":
                $"The war horn sounds! A {roster.Name} gathers to the {Compass(stone.transform.position,rift)}.";
            string sworn=boasts!=0?$" Sworn: {string.Join(", ",Policy.Sworn(boasts).Select(b=>b.Name))} (+{Math.Round(100*Policy.BoastBonus(boasts))}% warshards).":"";
            Net.Say($"{opening} They march in {gather:0} seconds: {plan.Count} waves, {plan.Sum(w=>w.Count)} strong.{sworn}",stone.transform.position,250);
            Plugin.Log($"Siege called ({cause}) at {stone.transform.position:F0}: {roster.Name}, {plan.Count} waves, {plan.Sum(w=>w.Count)} raiders, rift {rift:F0}");
            if(cause==Cause.Horn)RespiteLink.Raided(stone.transform.position,"the war horn"); // a siege called is a raid on Respite's clock (a drawn raid is noted as the game's)
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
            if(stone.PendingHealth>=0&&Time.time>=stone.HealthFlushAt)FlushHealth(stone);
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
                // The wave is broken (or has dragged on long enough): a breather, counted down, then the next marches.
                long breather=z.GetLong(Stone.BreatherKey,0L);
                bool tide=Policy.Has(stone.Boasts,Boast.Tide);
                if(breather==0)
                {
                    if(!Policy.NextWave(alive,units.Count,since,Plugin.Instance.WaveSeconds.Value,tide))return;
                    double rest=Policy.Breather(Plugin.Instance.BreatherSeconds.Value,tide);
                    if(rest>=1)
                    {
                        z.Set(Stone.BreatherKey,After(rest));
                        string coming=wave+1==plan.Count-1?"The warchief comes":$"Wave {wave+2} of {plan.Count} comes";
                        // Mostly down is enough to call the next wave: say so plainly when some still fight.
                        string state=alive==0?$"Wave {wave+1} is broken!":$"Wave {wave+1} is breaking, {alive} still {(alive==1?"fights":"fight")}.";
                        Net.Say($"{state} {coming} in {Mathf.RoundToInt((float)rest)} s.",stone.transform.position,250);
                        return;
                    }
                }
                else if(Now<breather)return;
                z.Set(Stone.BreatherKey,0L);
                wave++;
                z.Set(Stone.WaveKey,wave);z.Set(Stone.QueueKey,0);z.Set(Stone.WaveAtKey,Now);
                Assets.Effect("sfx_gdking_scream",rift);
                Net.Say(wave==plan.Count-1?"The warchief comes! The last wave!":$"Wave {wave+1} of {plan.Count}!",stone.transform.position,250);
                return;
            }
            // The last wave is out: the siege is won when it is down (stragglers hiding past a while count as down).
            // A straggler or two stuck out of sight does not hold the siege open, but the warchief must be beaten (a stuck one leaps in).
            bool chief=Raider.Loaded.Any(r=>r!=null&&r.Siege==siege&&r.Role==Role.Champion&&r.Body!=null&&!r.Body.IsDead());
            if(alive==0||alive<=2&&since>75&&!chief)End(stone,Outcome.Held);
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
            // A warchief: a name and a trick of his own, told to all, and remembered by the stone for the saga.
            if(unit.Role==Role.Champion)
            {
                var (trick,name)=Policy.Chief(Random.Range(1,int.MaxValue),(Cause)stone.Z.GetInt(Stone.CauseKey,0)==Cause.Test?TestTrick:-1);
                z.Set(Raider.TrickKey,(int)trick);z.Set(Raider.ChiefKey,name);
                ZDO sz=stone.Z;string chiefs=sz.GetString(Stone.ChiefsKey,"");
                sz.Set(Stone.ChiefsKey,chiefs==""?name:chiefs+";"+name);
                Net.Say($"{name} leads them! He {Policy.Does(trick)}.",stone.transform.position,250);
            }
            Raider.Attach(go);
        }
        // A Grave-caller's dead, risen beside him to fight for the siege.
        internal static void Raise(Warstone stone,string prefab,Vector3 at,long siege)
        {
            GameObject source=ZNetScene.instance.GetPrefab(prefab);
            if(source==null)return;
            if(ZoneSystem.instance.GetSolidHeight(at,out float h))at.y=h+0.2f;
            Vector3 face=stone.transform.position-at;face.y=0;
            GameObject go=Object.Instantiate(source,at,Quaternion.LookRotation(face.sqrMagnitude>0.01f?face:Vector3.forward));
            ZNetView view=go.GetComponent<ZNetView>();
            if(view==null||!view.IsValid()||go.GetComponent<MonsterAI>()==null){if(view!=null&&view.IsValid())ZNetScene.instance.Destroy(go);else Object.Destroy(go);return;}
            ZDO z=view.GetZDO();
            z.Set(Raider.RoleKey,(int)Role.Grunt);z.Set(Raider.SiegeKey,siege);z.Set(Raider.StoneKey,stone.transform.position);
            Raider.Attach(go);
            Assets.Effect("vfx_spawn_small",at+Vector3.up*0.5f);
        }

        // ---- calling the next wave early: the rift is empty, the horn calls the next wave at once, and the stone pays for the time saved ----
        internal static bool CanCallEarly(Warstone stone,out int bonus)
        {
            bonus=0;ZDO z=stone.Z;
            if(z==null||stone.Phase!=Phase.Battle)return false;
            var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
            int wave=z.GetInt(Stone.WaveKey,0);
            if(plan.Count==0||wave>=plan.Count-1||z.GetInt(Stone.QueueKey,0)<plan[wave].Count)return false;
            long breather=z.GetLong(Stone.BreatherKey,0L);
            double rest=Policy.Breather(Plugin.Instance.BreatherSeconds.Value,Policy.Has(stone.Boasts,Boast.Tide));
            double left=breather!=0?(breather-Now)/(double)TimeSpan.TicksPerSecond:Plugin.Instance.WaveSeconds.Value-Since(z.GetLong(Stone.WaveAtKey,Now))+rest;
            if(left<5)return false;
            bonus=Policy.EarlyBonus(left);
            return true;
        }
        private static void CallEarly(Warstone stone)
        {
            if(!CanCallEarly(stone,out int bonus))return;
            ZDO z=stone.Z;
            var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
            int wave=z.GetInt(Stone.WaveKey,0)+1;
            z.Set(Stone.WaveKey,wave);z.Set(Stone.QueueKey,0);z.Set(Stone.WaveAtKey,Now);z.Set(Stone.BreatherKey,0L);
            GameObject shard=Items.Get(Policy.ShardPrefab);
            if(shard!=null&&shard.GetComponent<ItemDrop>() is ItemDrop drop)
            {ItemDrop.ItemData data=drop.m_itemData.Clone();data.m_dropPrefab=shard;ItemDrop.DropItem(data,bonus,stone.transform.position+Vector3.up*3.5f,Quaternion.identity);}
            Assets.Effect("sfx_gdking_scream",stone.Rift);Assets.Effect("sfx_fader_bell",stone.transform.position+Vector3.up*2);
            Net.Say($"The horn calls them on! Wave {wave+1} of {plan.Count} comes now. +{bonus} warshards for your daring.",stone.transform.position,250);
        }
        // What the next wave holds, for the stone's hover: "6 Greydwarfs, 2 Necks, a warchief".
        internal static string NextWaveText(Warstone stone)
        {
            ZDO z=stone.Z;if(z==null)return "";
            var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
            int wave=z.GetInt(Stone.WaveKey,0)+1;
            if(wave>=plan.Count)return "";
            return string.Join(", ",plan[wave].GroupBy(u=>u.Role==Role.Champion?"warchief":u.Prefab).Select(g=>
            {
                if(g.Key=="warchief")return "a warchief";
                string name=ZNetScene.instance?.GetPrefab(g.Key)?.GetComponent<Character>()?.m_name??g.Key;
                return $"{g.Count()} {Localization.instance.Localize(name)}";
            }));
        }

        // ---- the end ----
        internal enum Outcome{Held,Fallen,Abandoned,Bought}
        private static void End(Warstone stone,Outcome outcome)
        {
            FlushHealth(stone);ZDO z=stone.Z;
            int stage=z.GetInt(Stone.StageKey,0),marks=stone.Marks,kills=z.GetInt(Stone.KillsKey,0);
            float health=z.GetFloat(Stone.HealthKey,0),max=Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1));
            Roster roster=Policy.Rosters[Mathf.Clamp(stage,0,Policy.Rosters.Length-1)];
            Cause cause=(Cause)z.GetInt(Stone.CauseKey,0);
            z.Set(Stone.PhaseKey,(int)Phase.Idle);
            z.Set(Stone.CooldownKey,After(Plugin.Instance.CooldownMinutes.Value*60));
            NextRelease.Remove(stone);Lonely.Remove(stone);
            // Whoever of the horde still stands melts away, even one whose own wits have stalled (each game clears the ones it owns).
            foreach(Raider r in Raider.Loaded.ToList())
                if(r!=null&&r.Siege==stone.Siege&&r.Body!=null&&!r.Body.IsDead())r.Melt();
            switch(outcome)
            {
                case Outcome.Held:
                    int now=cause==Cause.Test?marks:Policy.Marks(marks+1);
                    z.Set(Stone.MarksKey,now);z.Set(Stone.CrackedKey,false);z.Set(Stone.HeldKey,z.GetInt(Stone.HeldKey,0)+1);
                    int shards=Reward(stone,roster,stage,marks,health/max,kills,out var parts);
                    if(cause!=Cause.Test)Offer(stone);
                    Assets.Effect("sfx_fader_bell",stone.transform.position+Vector3.up*2);
                    Assets.Effect("vfx_HealthUpgrade",stone.transform.position);Assets.Effect("fx_DvergerMage_Support_start",stone.transform.position+Vector3.up);
                    string rank=now>marks?$" It is now {Policy.Title(now)} ({Policy.Numeral(now)}).":"";
                    Net.Say($"The {roster.Name} breaks! {kills} slain, the stone at {Mathf.RoundToInt(100*health/max)}%.{rank} Spoils with {shards} warshards lie at its foot.",stone.transform.position,250);
                    Saga.Tell(stone,outcome,roster,kills,health/max,parts,shards);
                    break;
                case Outcome.Fallen:
                    z.Set(Stone.CrackedKey,true);z.Set(Stone.FallenKey,z.GetInt(Stone.FallenKey,0)+1);
                    Assets.Effect("sfx_gdking_scream",stone.transform.position);
                    int left=Policy.Consolation(kills);
                    if(left>0)DropShards(stone,left);
                    bool shatter=Plugin.Instance.ShatteredStone.Value||_testShatter;
                    Net.Say((shatter?"The Warstone shatters! The horde howls and melts away. The stone is gone, and all it had won with it.":
                        "The Warstone cracks! The horde howls and melts away. Its ward is weaker until it holds a siege again.")+
                        (left>0?$" {left} warshards lie at its foot, won from the {kills} slain.":""),stone.transform.position,250);
                    Saga.Tell(stone,outcome,roster,kills,0,new[]{($"{kills} slain before it {(shatter?"shattered":"cracked")}",left)},left,shatter);
                    if(shatter)Shatter(stone);
                    break;
                case Outcome.Bought:
                    Net.Say("The horde has been paid off: it turns back into the wilds.",stone.transform.position,250);
                    break;
                default:
                    Net.Say("With no one to face them, the horde melts back into the wilds.",stone.transform.position,250);
                    break;
            }
            Plugin.Log($"Siege at {stone.transform.position:F0} ended: {outcome}, {kills} slain, stone {Mathf.RoundToInt(100*health/max)}%");
        }
        // ShatteredStone: a stone broken in a siege is gone, with its marks, level and boons. What was built round it stands.
        private static void Shatter(Warstone stone)
        {
            Vector3 at=stone.transform.position;
            WearNTear wear=stone.GetComponent<WearNTear>();
            if(wear!=null)wear.m_destroyedEffect.Create(at,stone.transform.rotation,stone.transform);
            Assets.Effect("sfx_rock_destroyed",at);Assets.Effect("vfx_RockDestroyed_large",at+Vector3.up);
            Plugin.Log($"The Warstone at {at:F0} shattered");
            ZNetScene.instance.Destroy(stone.gameObject);
        }
        // Test hooks (Claude Tools): end the siege under way as if lost, or leave a spoils chest without a siege.
        private static bool _testShatter;
        // Another mod paid the horde to go (Jarldom's Fuling tribute): the siege ends with nothing won or lost.
        internal static void CallOff(Warstone stone){if(stone.Phase!=Phase.Idle)End(stone,Outcome.Bought);}
        internal static void TestLose(Warstone stone,bool shatter){if(stone.Phase!=Phase.Battle)return;_testShatter=shatter;try{End(stone,Outcome.Fallen);}finally{_testShatter=false;}}
        internal static int TestSpoils(Warstone stone){int stage=stone.Z.GetInt(Stone.StageKey,0);return Reward(stone,Policy.Rosters[Mathf.Clamp(stage,0,Policy.Rosters.Length-1)],stage,stone.Marks,1,30,out _);}
        private static void DropShards(Warstone stone,int amount)
        {
            GameObject item=Items.Get(Policy.ShardPrefab);
            if(item==null||item.GetComponent<ItemDrop>() is not ItemDrop drop)return;
            Vector3 away=stone.transform.position-stone.Rift;away.y=0;
            away=away.sqrMagnitude>1?away.normalized:-stone.transform.forward;
            ItemDrop.ItemData data=drop.m_itemData.Clone();data.m_dropPrefab=item;
            ItemDrop.DropItem(data,amount,stone.transform.position+away*3f+Vector3.up,Quaternion.identity);
        }
        // The boons the stone may choose from, saved on it until someone chooses at the war council.
        internal static void Offer(Warstone stone)
        {
            string[] offer=Policy.OfferBoons(stone.Seed^0x5bd1e995,stone.Boons);
            if(offer.Length==0)return;
            stone.Z.Set(Stone.OfferKey,string.Join(",",offer));
        }
        internal static void Choose(Warstone stone,string boon,string who)
        {
            ZDO z=stone.Z;
            if(z==null||!stone.BoonOffer.Contains(boon))return;
            z.Set(Stone.BoonsKey,string.Join(",",stone.Boons.Concat(new[]{boon}).Distinct()));
            z.Set(Stone.OfferKey,"");
            Assets.Effect("fx_DvergerMage_Support_start",stone.transform.position+Vector3.up);
            Net.Say($"{who} chose the stone's boon: {Policy.BoonOf(boon).Name}. {Policy.BoonOf(boon).Text}",stone.transform.position,100);
        }
        private static int Reward(Warstone stone,Roster roster,int stage,int marks,float health,int kills,out (string why,int amount)[] parts)
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
                inventory?.RemoveAll(); // not the game's own treasure (a pile of coins and gems): the horde's land's building stuff instead
                Spoils.Mark(chest); // gone once emptied
            }
            parts=Policy.ShardParts(stage,marks,health,kills);
            int boasts=stone.Z.GetInt(Stone.BoastsKey,0),plain=parts.Sum(p=>p.amount),shards=Policy.Boasted(plain,boasts);
            if(shards>plain)parts=parts.Concat(new[]{($"boasts sworn (+{Math.Round(100*Policy.BoastBonus(boasts))}%)",shards-plain)}).ToArray();
            var gifts=new List<(string prefab,int amount)>{(Policy.ShardPrefab,shards)};
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
            float max=Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1)),before=stone.PendingHealth>=0?stone.PendingHealth:z.GetFloat(Stone.HealthKey,max);
            float after=Mathf.Clamp(before-(amount>0?amount*Policy.Toughness:amount),0,max);
            stone.PendingHealth=after;bool crossed=false;
            if(amount>0)
            {
                foreach(float mark in new[]{0.5f,0.25f,0.1f})
                    if(before/max>mark&&after/max<=mark){Net.Say($"The Warstone is cracking! {Mathf.RoundToInt(mark*100)}% left!",stone.transform.position,250);crossed=true;}
                if(Time.time>=stone.NextRockHit){stone.NextRockHit=Time.time+0.3f;Assets.Effect("vfx_RockHit",at);}
            }
            if(crossed||after<=0||Time.time>=stone.HealthFlushAt)FlushHealth(stone);
        }
        // Blows land many times a second, and the stone's record is resent whole with each change: it takes them at most twice a second
        // (and at once when the stone cracks further or falls).
        internal static void FlushHealth(Warstone stone)
        {
            ZDO z=stone.Z;float health=stone.PendingHealth;
            stone.PendingHealth=-1;stone.HealthFlushAt=Time.time+0.5f;
            if(z==null||health<0)return;
            float max=Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1));
            z.Set(Stone.HealthKey,health);
            if(health/max<z.GetFloat(Stone.LowKey,1)){z.Set(Stone.LowKey,health/max);z.Set(Stone.LowWaveKey,z.GetInt(Stone.WaveKey,0)+1);} // its closest call, for the saga
        }

        // ---- where the rift opens: dry ground in reach of the stone by foot, not too near, inside the loaded world ----
        // As far out as the game lets the horde live (up to 150 m: room for a fortress), on dry ground with a road to the stone, and well
        // away from anything anyone has built. The farthest good spot of many tried wins; if none, the rules ease (nearer, less room).
        private static Vector3 FindRift(Vector3 stone)
        {
            foreach(var (least,most,clear) in new[]{(70f,150f,Policy.RiftClear),(45f,150f,Policy.RiftClear/2),(35f,95f,0f)})
            {
                Vector3 best=Vector3.zero,roadless=Vector3.zero;float far=-1,farRoadless=-1;int tried=0,roads=0;
                for(int attempt=0;attempt<80;attempt++)
                {
                    Vector2 dir=Random.insideUnitCircle.normalized*Random.Range(least,most);
                    if(dir.magnitude<=far)continue; // no farther than the best yet
                    Vector3 at=new Vector3(stone.x+dir.x,stone.y,stone.z+dir.y);
                    if(!ZoneSystem.instance.IsZoneLoaded(at)||!Alive(at))continue;
                    if(!ZoneSystem.instance.GetSolidHeight(at,out float height)||height<ZoneSystem.instance.m_waterLevel+0.5f)continue;
                    at.y=height;
                    if(EffectArea.IsPointInsideArea(at,EffectArea.Type.PlayerBase,5)!=null||clear>0&&Built(at,clear))continue; // not in anyone's yard
                    tried++;
                    // A road to the stone if it can (the game's pathfinding may not reach that far yet; the horde breaks or ramps its way).
                    if(Pathfinding.instance!=null&&!Pathfinding.instance.HavePath(at,Doorstep(stone,at),Pathfinding.AgentType.Humanoid))
                    {if(dir.magnitude>farRoadless){roadless=at;farRoadless=dir.magnitude;}continue;}
                    best=at;far=dir.magnitude;roads++;
                }
                if(far>0){Plugin.Log($"Rift {far:0} m out, with a road ({roads} of {tried} good spots had one; {least:0}-{most:0} m, {clear:0} m clear)");return best;}
                if(farRoadless>0){Plugin.Log($"Rift {farRoadless:0} m out, no road found ({tried} good spots; {least:0}-{most:0} m)");return roadless;}
            }
            return stone+Vector3.forward*40;
        }
        // The ground just in front of the stone on the rift's side: a road can reach that, not the stone's own middle.
        private static Vector3 Doorstep(Vector3 stone,Vector3 from)
        {
            Vector3 dir=from-stone;dir.y=0;
            Vector3 at=stone+(dir.sqrMagnitude>0.01f?dir.normalized:Vector3.forward)*3f;
            if(ZoneSystem.instance.GetSolidHeight(at,out float h))at.y=h;
            return at;
        }
        private static readonly Collider[] Pieces=new Collider[1];private static int _pieceMask;
        private static bool Built(Vector3 at,float radius)
        {
            if(_pieceMask==0)_pieceMask=LayerMask.GetMask("piece","piece_nonsolid");
            return Physics.OverlapSphereNonAlloc(at,radius,Pieces,_pieceMask,QueryTriggerInteraction.Collide)>0;
        }
        // Inside the area this game simulates (with room for the horde to spread): beyond it creatures are loaded but stand frozen, their
        // wits run by no one, and the game's simulation distance setting can make it as small as about one zone around the player.
        private static bool Alive(Vector3 at)
        {
            Vector3 me=ZNet.instance.GetReferencePosition();
            foreach(Vector3 off in new[]{Vector3.zero,new Vector3(8,0,0),new Vector3(-8,0,0),new Vector3(0,0,8),new Vector3(0,0,-8)})
                if(!ZNetScene.InActiveArea(at+off,me))return false;
            return true;
        }
        private static string Compass(Vector3 from,Vector3 to)
        {
            float angle=Mathf.Atan2(to.x-from.x,to.z-from.z)*Mathf.Rad2Deg;
            string[] names={"north","north-east","east","south-east","south","south-west","west","north-west"};
            return names[((int)Mathf.Round(((angle%360)+360)%360/45f))%8];
        }
        // ---- the stone's footing: dig the ground out from under it (a digger, or a moat too close) and it topples ----
        private static readonly Dictionary<Warstone,float> NextFooting=new Dictionary<Warstone,float>();
        internal static void Footing(Warstone stone)
        {
            if(NextFooting.TryGetValue(stone,out float next)&&Time.time<next)return;
            NextFooting[stone]=Time.time+2;
            ZDO z=stone.Z;if(z==null)return;
            Vector3 at=stone.transform.position;
            var ground=new List<double>();
            foreach(Vector3 offset in new[]{Vector3.zero,new Vector3(0.7f,0,0),new Vector3(-0.7f,0,0),new Vector3(0,0,0.5f),new Vector3(0,0,-0.5f)})
                if(ZoneSystem.instance.GetGroundHeight(at+stone.transform.rotation*offset,out float h))ground.Add(h);
            if(ground.Count==0)return;
            // The land under it when it was set (a stone on a floor indoors stands above it, and that is fine): it topples if that land is dug away.
            if(!z.GetBool(Stone.BaseKey+"_set",false)){z.Set(Stone.BaseKey,(float)ground.Average());z.Set(Stone.BaseKey+"_set",true);return;}
            if(!Policy.Toppled(z.GetFloat(Stone.BaseKey,0),ground.ToArray()))return;
            float settle=Mathf.Min(at.y,(float)ground.Average());
            stone.transform.position=new Vector3(at.x,settle,at.z);
            z.SetPosition(stone.transform.position);z.Set(Stone.BaseKey,settle);
            Assets.Effect("sfx_gdking_scream",at);Assets.Effect("vfx_spawn_large",at);
            if(stone.Phase==Phase.Battle)
            {
                // Undermined in a siege, it sinks into the pit with a heavy blow (a quarter of its strength): diggers are a real threat,
                // but not a siege lost at a stroke. It cracks only if that empties it.
                float max=z.GetFloat(Stone.MaxHealthKey,1);
                OnDamage(stone,max*Policy.SinkBlow/Policy.Toughness,at); // OnDamage takes the stone's share of a blow
                Net.Say($"The ground gives way and the Warstone sinks into the pit! It loses a quarter of its strength.",at,250);
                return;
            }
            // Between sieges (its own folk's moat dug too close): it topples, cracks and loses a mark.
            z.Set(Stone.MarksKey,Policy.Marks(stone.Marks-1));
            z.Set(Stone.CrackedKey,true);
            Net.Say("The ground gives way beneath the Warstone and it topples into the pit! It cracks and loses a mark.",at,250);
        }
        internal static void Forget(Warstone stone){NextFooting.Remove(stone);NextTick.Remove(stone);NextRelease.Remove(stone);NextGlow.Remove(stone);Lonely.Remove(stone);}
        internal static void Reset(){NextTick.Clear();NextRelease.Clear();NextGlow.Clear();Lonely.Clear();}
    }
}
