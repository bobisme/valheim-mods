using System;
using System.Collections.Generic;
using System.Linq;

namespace Shieldwall
{
    // Pure rules for Shieldwall: the Warstone's marks and ward, who comes to a siege and how many, when waves come, what holding the
    // line earns, and the staves. No Unity or game types, so every number can be tested.
    internal enum Role{Grunt=1,Sapper=2,Flyer=3,Champion=4,Digger=5,Guard=6}
    // Boasts: sworn at the horn, each makes the siege harder and its warshards richer.
    internal enum Boast{BloodMoon=0,Fog=1,Burrowers=2,Wings=3,Chosen=4,Tide=5,BareStone=6}
    internal sealed class BoastInfo{internal Boast Id;internal string Name,Text;internal double Bonus;}
    // Boons: one kept for good each time the stone holds a siege.
    internal sealed class Boon{internal string Id,Name,Text;}
    internal enum Phase{Idle=0,Gathering=1,Battle=2}
    internal enum Cause{Horn=0,Raid=1,Test=2,Early=3}
    internal enum StaveKind{None=-1,Ember=0,Frost=1,Thunder=2,Blast=3,Hearth=4}

    internal sealed class Unit
    {
        internal string Prefab;internal int Level;internal Role Role;
        public override string ToString()=>$"{Prefab}/{Level}/{(int)Role}";
    }
    internal sealed class Roster
    {
        internal string Key,Name;                            // the boss key that unlocks it ("" from the start), and what the horde is called
        internal (string prefab,int weight)[] Grunts;
        internal string[] Sappers,Flyers,Champions;
        internal string Chest;                               // the reward chest, by how far the world has come
        internal (string prefab,int min,int max)[] Spoils;  // added to the chest
    }
    internal struct WardStats
    {
        internal float HealthRegen,StaminaRegen,EitrRegen,Carry,Armor;internal int Comfort;
        internal bool Any=>HealthRegen>1||StaminaRegen>1||EitrRegen>1||Carry>0||Armor>0||Comfort>0;
    }
    internal sealed class Stave
    {
        internal StaveKind Kind;internal string Prefab,Name,Source,Projectile,Description;
        internal float Damage,Range,Cooldown;               // planted, at quality 1; damage is the main type's
        internal string Type;                               // fire, frost, lightning, blunt or heal
        internal float Splash;                              // blunt added beside the main type
        internal int StationLevel;                          // the Warstone's marks needed to craft it (station level = 1 + marks)
        internal (string prefab,int amount,int perLevel)[] Cost;
    }

    internal static class Policy
    {
        // ---- the Warstone ----
        internal const int MaxMarks=10;
        internal const float WardRadius=40,HearthRadius=10,MinApart=80;
        internal static int Marks(int marks)=>Math.Max(0,Math.Min(MaxMarks,marks));
        // A cracked stone (the horde reached it) works two marks lower until the next siege it holds.
        internal static int Strength(int marks,bool cracked)=>Math.Max(0,Marks(marks)-(cracked?2:0));
        internal static string Numeral(int n)=>n<=0?"0":new[]{"I","II","III","IV","V","VI","VII","VIII","IX","X"}[Math.Min(MaxMarks,n)-1];
        internal static string Title(int marks)=>marks<=0?"Unblooded":marks<2?"Blooded":marks<4?"Tested":marks<6?"Proven":marks<9?"Storied":"Legendary";
        // Its ward: full strength at a hearth (under a roof with a fire near), half in the open.
        internal static WardStats WardOf(int strength,bool hearth)
        {
            int t=Marks(strength);
            var full=new WardStats
            {
                HealthRegen=1.1f+0.025f*t,StaminaRegen=1.1f+0.025f*t,
                EitrRegen=t>=3?1+0.04f*t:1,
                Carry=t>=1?10*t:0,
                Armor=t>=6?2*(t-5):0,
                Comfort=t>=8?3:t>=5?2:t>=2?1:0,
            };
            if(hearth)return full;
            return new WardStats
            {
                HealthRegen=1+(full.HealthRegen-1)/2,StaminaRegen=1+(full.StaminaRegen-1)/2,EitrRegen=1+(full.EitrRegen-1)/2,
                Carry=(float)Math.Floor(full.Carry/2),Armor=(float)Math.Floor(full.Armor/2),Comfort=0,
            };
        }
        internal static IEnumerable<string> Describe(WardStats w)
        {
            if(w.HealthRegen>1)yield return $"Health regeneration +{Math.Round((w.HealthRegen-1)*100)}%";
            if(w.StaminaRegen>1)yield return $"Stamina regeneration +{Math.Round((w.StaminaRegen-1)*100)}%";
            if(w.EitrRegen>1)yield return $"Eitr regeneration +{Math.Round((w.EitrRegen-1)*100)}%";
            if(w.Carry>0)yield return $"Carry weight +{w.Carry:0}";
            if(w.Armor>0)yield return $"Armor +{w.Armor:0}";
            if(w.Comfort>0)yield return $"Comfort +{w.Comfort} under its roof";
        }

        // ---- who comes: one roster per boss beaten ----
        internal static readonly Roster[] Rosters=
        {
            new Roster{Key="",Name="greydwarf warband",Grunts=new[]{("Greyling",4),("Greydwarf",4),("Neck",1)},Sappers=new[]{"Greydwarf"},Flyers=new string[0],
                Champions=new[]{"Greydwarf_Elite"},Chest="TreasureChest_meadows",Spoils=new[]{("Coins",20,40),("Flint",4,8),("DeerHide",2,4)}},
            new Roster{Key="defeated_eikthyr",Name="host of the Black Forest",Grunts=new[]{("Greydwarf",5),("Greydwarf_Shaman",1),("Greydwarf_Elite",1),("Skeleton",2)},
                Sappers=new[]{"Greydwarf"},Flyers=new string[0],Champions=new[]{"Troll"},Chest="TreasureChest_blackforest",Spoils=new[]{("Coins",30,60),("SurtlingCore",1,3),("TrollHide",1,3)}},
            new Roster{Key="defeated_gdking",Name="drowned dead",Grunts=new[]{("Draugr",4),("Skeleton",2),("Skeleton_Poison",1),("Draugr_Ranged",1),("Blob",1)},
                Sappers=new[]{"Skeleton"},Flyers=new[]{"Bat"},Champions=new[]{"Draugr_Elite","Abomination"},Chest="TreasureChest_swamp",Spoils=new[]{("Coins",40,80),("Iron",4,8),("Guck",2,5)}},
            new Roster{Key="defeated_bonemass",Name="mountain pack",Grunts=new[]{("Wolf",4),("Ulv",2),("Fenring",1),("Draugr",2)},
                Sappers=new[]{"Ulv"},Flyers=new[]{"Hatchling"},Champions=new[]{"StoneGolem","Fenring_Cultist"},Chest="TreasureChest_mountains",Spoils=new[]{("Coins",50,100),("Silver",4,8),("WolfPelt",2,4)}},
            new Roster{Key="defeated_dragon",Name="Fuling warband",Grunts=new[]{("Goblin",5),("GoblinArcher",2),("GoblinShaman",1)},
                Sappers=new[]{"Goblin"},Flyers=new[]{"Deathsquito"},Champions=new[]{"GoblinBrute"},Chest="TreasureChest_heath",Spoils=new[]{("Coins",60,120),("BlackMetalScrap",6,12),("Barley",6,12)}},
            new Roster{Key="defeated_goblinking",Name="swarm of the mist",Grunts=new[]{("Seeker",4),("Tick",2),("Goblin",2),("GoblinArcher",1)},
                Sappers=new[]{"Tick"},Flyers=new[]{"Gjall"},Champions=new[]{"SeekerBrute"},Chest="TreasureChest_dvergrtower",Spoils=new[]{("Coins",80,150),("BlackCore",1,2),("Softtissue",4,8)}},
            new Roster{Key="defeated_queen",Name="Charred legion",Grunts=new[]{("Charred_Melee",4),("Charred_Archer",2),("Charred_Mage",1),("Asksvin",1),("Charred_Twitcher",1)},
                Sappers=new[]{"Charred_Melee"},Flyers=new[]{"Volture"},Champions=new[]{"Morgen"},Chest="TreasureChest_dvergrtower",Spoils=new[]{("Coins",100,200),("FlametalOreNew",4,8),("CharredBone",4,8)}},
        };
        // The furthest roster the world has unlocked (the keys it has).
        internal static int Stage(Func<string,bool> haveKey)
        {
            int stage=0;
            for(int i=1;i<Rosters.Length;i++)if(haveKey(Rosters[i].Key))stage=i;
            return stage;
        }

        // ---- how big a siege is ----
        // A stone's first siege is a proving: three waves of a smaller horde, two lands below the world's own (one below at the first
        // mark), so a new stone can be held by hand and its warshards buy the first staves.
        internal static int Waves(int marks)=>Marks(marks)==0?3:4+Math.Min(4,Marks(marks)/2);
        internal static int Total(int marks,int players)=>Marks(marks)==0?20+10*Math.Max(0,Math.Min(6,players)-1):
            Math.Min(220,30+8*Marks(marks)+14*Math.Max(0,Math.Min(6,players)-1));
        internal static int SiegeStage(int worldStage,int marks)=>Math.Max(0,worldStage-Math.Max(0,2-Marks(marks)));
        internal static float StoneHealth(int stage,int marks)=>4000f*(1+stage*0.75f)*(1+0.05f*Marks(marks));
        internal const float Reach=3.5f;
        internal const float Toughness=0.4f;                  // the stone feels this share of each blow
        // Earthworks: a stone whose ground sinks this far below where it was set topples (dug out by diggers, or by its owner's moat).
        internal const float Undermined=1.0f;
        internal static bool Toppled(double baseHeight,double[] ground)=>ground!=null&&ground.Length>0&&baseHeight-ground.Average()>Undermined;                      // a raider this close to the stone strikes it
        internal const int MinAlive=4;

        // A deterministic plan: the same seed always makes the same siege, so a new owner of the stone carries on the same one.
        internal static List<List<Unit>> Plan(int stage,int marks,int players,int seed,int boasts=0)
        {
            double digChance=Has(boasts,Boast.Burrowers)?0.22:0.06,flyChance=Has(boasts,Boast.Wings)?0.24:0.08;
            int digFrom=Has(boasts,Boast.Burrowers)?0:1,flyFrom=Has(boasts,Boast.Wings)?1:2;
            var rng=new Rng(seed);
            stage=Math.Max(0,Math.Min(Rosters.Length-1,stage));
            Roster roster=Rosters[stage],earlier=Rosters[Math.Max(0,stage-1)];
            int waves=Waves(marks),total=Total(marks,players),m=Marks(marks);
            var plan=new List<List<Unit>>();
            // Waves grow: the last is about twice the first.
            double sum=0;for(int w=0;w<waves;w++)sum+=1+w/(double)(waves-1);
            for(int w=0;w<waves;w++)
            {
                int count=Math.Max(3,(int)Math.Round(total*(1+w/(double)(waves-1))/sum));
                var wave=new List<Unit>();
                for(int i=0;i<count;i++)
                {
                    Role role=Role.Grunt;
                    double roll=rng.Next();
                    if(w>=1&&roster.Sappers.Length>0&&roll<0.10)role=Role.Sapper;
                    else if(w>=digFrom&&roll>=0.10&&roll<0.10+digChance)role=Role.Digger;
                    else if(w>=flyFrom&&roster.Flyers.Length>0&&rng.Next()<flyChance)role=Role.Flyer;
                    string prefab;
                    switch(role)
                    {
                        case Role.Sapper:prefab=roster.Sappers[rng.Index(roster.Sappers.Length)];break;
                        case Role.Flyer:prefab=roster.Flyers[rng.Index(roster.Flyers.Length)];break;
                        default:prefab=Pick(stage>0&&rng.Next()<0.3?earlier:roster,rng);break;
                    }
                    wave.Add(new Unit{Prefab=prefab,Level=Level(m,w,rng.Next()),Role=role});
                }
                plan.Add(wave);
            }
            // The warchief and his guard lead the last wave; a seasoned stone draws one to the middle wave too.
            int chiefs=1+m/4;
            for(int i=0;i<chiefs;i++)plan[waves-1].Insert(0,new Unit{Prefab=roster.Champions[i%roster.Champions.Length],Level=3,Role=Role.Champion});
            if(m>=3)plan[waves/2].Add(new Unit{Prefab=roster.Champions[0],Level=2,Role=Role.Champion});
            // The chosen chief's guard march at his side.
            if(Has(boasts,Boast.Chosen))for(int i=0;i<3;i++)plan[waves-1].Insert(1,new Unit{Prefab=Pick(roster,rng),Level=2,Role=Role.Guard});
            if(Has(boasts,Boast.BloodMoon))foreach(Unit u in plan.SelectMany(w=>w))u.Level=Math.Min(3,u.Level+1);
            return plan;
        }
        private static string Pick(Roster roster,Rng rng)
        {
            int all=roster.Grunts.Sum(g=>g.weight);
            double r=rng.Next()*all;
            foreach(var (prefab,weight) in roster.Grunts){if(r<weight)return prefab;r-=weight;}
            return roster.Grunts[roster.Grunts.Length-1].prefab;
        }
        // Stars: more with each mark and each wave.
        internal static int Level(int marks,int wave,double roll)
        {
            double three=0.01*marks+0.01*wave,two=0.08+0.04*marks+0.03*wave;
            return roll<three?3:roll<three+two?2:1;
        }
        internal static string Save(List<List<Unit>> plan)=>string.Join(";",plan.Select(w=>string.Join(",",w)));
        internal static List<List<Unit>> Load(string saved)
        {
            var plan=new List<List<Unit>>();
            foreach(string wave in (saved??"").Split(';'))
            {
                var units=new List<Unit>();
                foreach(string part in wave.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f=part.Split('/');
                    if(f.Length!=3||!int.TryParse(f[1],out int level)||!int.TryParse(f[2],out int role)||!Enum.IsDefined(typeof(Role),role))continue;
                    units.Add(new Unit{Prefab=f[0],Level=level,Role=(Role)role});
                }
                if(units.Count>0)plan.Add(units);
            }
            return plan;
        }

        // ---- pacing ----
        // The next wave comes when this one is mostly down, or after a while regardless.
        // No respite (a boast): half the wait, and the next comes when this one is half down.
        internal static bool NextWave(int alive,int waveSize,double sinceWave,double maxGap,bool tide=false)=>
            sinceWave>=maxGap*(tide?0.5:1)||alive<=Math.Max(MinAlive/2,waveSize*(tide?0.5:0.3))&&sinceWave>=(tide?6:12);
        // Raiders stream out of the rift a few at a time while there is room.
        internal static int Release(int queued,int alive,int cap)=>Math.Max(0,Math.Min(Math.Min(queued,3),cap-alive));

        // ---- what holding the line earns ----
        internal static int Shards(int stage,int marks,double health,int kills)=>ShardParts(stage,marks,health,kills).Sum(p=>p.amount);
        // The same, part by part, for the saga: what each warshard was for.
        internal static (string why,int amount)[] ShardParts(int stage,int marks,double health,int kills)=>new[]
        {
            ("holding the line",3+stage),("the stone's marks",Marks(marks)/2),($"the stone standing at {Math.Round(100*Clamp01(health))}%",(int)Math.Round(5*Clamp01(health))),($"{Math.Max(0,kills)} slain",Math.Max(0,kills)/12),
        };
        // Boasts sworn at the horn add their share on top.
        internal static int Boasted(int shards,int boasts)=>(int)Math.Round(shards*(1+BoastBonus(boasts)));
        // A fallen stone still pays a little for the raiders it took with it, so even a lost siege brings the first staves closer.
        internal static int Consolation(int kills)=>Math.Max(0,kills)/6;
        internal static int Coins(int min,int max,int marks,double roll)=>(int)Math.Round((min+(max-min)*Clamp01(roll))*(1+0.1*Marks(marks)));
        // A raider sometimes carries a shard; a warchief always carries several.
        internal static int Carried(Role role,double roll,bool lucky=false)=>role==Role.Champion?3+(int)(Clamp01(roll)*3):roll<(lucky?0.24:0.12)?1:0;

        // ---- boasts: offered two at a time at the horn ----
        internal static readonly BoastInfo[] Boasts=
        {
            new BoastInfo{Id=Boast.BloodMoon,Name="Blood moon",Bonus=0.6,Text="Every raider comes with a star more."},
            new BoastInfo{Id=Boast.Fog,Name="Fog of war",Bonus=0.4,Text="A mist rolls in: staves see only two thirds as far."},
            new BoastInfo{Id=Boast.Burrowers,Name="Burrowers",Bonus=0.35,Text="Many diggers from the first wave on: guard the stone's footing."},
            new BoastInfo{Id=Boast.Wings,Name="Black wings",Bonus=0.35,Text="Three times as many fliers come over the walls."},
            new BoastInfo{Id=Boast.Chosen,Name="Chosen chief",Bonus=0.5,Text="The warchief brings a guard of three, and no blow touches him while one of them lives."},
            new BoastInfo{Id=Boast.Tide,Name="No respite",Bonus=0.4,Text="Each wave comes when the last is half down, and twice as soon."},
            new BoastInfo{Id=Boast.BareStone,Name="Bare stone",Bonus=0.4,Text="The stone stands at only 60% of its strength."},
        };
        internal static bool Has(int boasts,Boast b)=>(boasts&(1<<(int)b))!=0;
        internal static int Mask(params Boast[] boasts)=>boasts.Aggregate(0,(m,b)=>m|1<<(int)b);
        internal static double BoastBonus(int boasts)=>Boasts.Where(b=>Has(boasts,b.Id)).Sum(b=>b.Bonus);
        internal static IEnumerable<BoastInfo> Sworn(int boasts)=>Boasts.Where(b=>Has(boasts,b.Id));
        // The two the stone offers before a siege: the same on every player's game (seeded by the stone and how many sieges it has seen).
        internal static Boast[] OfferBoasts(int seed,int stage)
        {
            var rng=new Rng(seed);
            var pool=Boasts.Select(b=>b.Id).Where(b=>b!=Boast.Wings||Rosters[Math.Max(0,Math.Min(Rosters.Length-1,stage))].Flyers.Length>0).ToList();
            var offer=new List<Boast>();
            while(offer.Count<2&&pool.Count>0){int i=rng.Index(pool.Count);offer.Add(pool[i]);pool.RemoveAt(i);}
            return offer.ToArray();
        }
        // Only what was offered can be sworn.
        internal static int Allowed(int asked,Boast[] offered)=>asked&Mask(offered);

        // ---- boons: three offered after a held siege, one kept ----
        internal static readonly Boon[] Boons=
        {
            new Boon{Id="ember",Name="Hungry flame",Text="Ember staves strike 35% harder."},
            new Boon{Id="frost",Name="Deep winter",Text="Frost staves strike 35% harder and see 4 m farther."},
            new Boon{Id="thunder",Name="Thor's ear",Text="Thunder staves strike a third faster."},
            new Boon{Id="blast",Name="Black powder",Text="Blast staves strike 35% harder."},
            new Boon{Id="hearth",Name="Warm hearth",Text="Hearth staves mend twice as much."},
            new Boon{Id="blood",Name="Bloodstone",Text="The stone mends half a percent of its strength for every raider slain."},
            new Boon{Id="rooted",Name="Deep roots",Text="The stone is a quarter stronger in a siege."},
            new Boon{Id="veins",Name="Wide veins",Text="The stone feeds two more staves."},
            new Boon{Id="reach",Name="Long reach",Text="The stone's power reaches 8 m farther."},
            new Boon{Id="eyes",Name="Raven's eyes",Text="Every stave sees 4 m farther."},
            new Boon{Id="bane",Name="Chieftain's bane",Text="Staves strike warchiefs and their guards half again as hard."},
            new Boon{Id="luck",Name="Shard-luck",Text="Raiders carry warshards twice as often."},
            new Boon{Id="ward",Name="Old blood",Text="The stone's ward is as strong as two marks more."},
            new Boon{Id="hands",Name="Many hands",Text="Borrowed staffs in sockets strike at 70% instead of 40%."},
        };
        internal static Boon BoonOf(string id)=>Boons.FirstOrDefault(b=>b.Id==id);
        internal static HashSet<string> ParseBoons(string saved)=>new HashSet<string>((saved??"").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries).Where(id=>BoonOf(id)!=null));
        internal static string[] OfferBoons(int seed,ICollection<string> kept)
        {
            var rng=new Rng(seed);
            var pool=Boons.Select(b=>b.Id).Where(id=>!kept.Contains(id)).ToList();
            var offer=new List<string>();
            while(offer.Count<3&&pool.Count>0){int i=rng.Index(pool.Count);offer.Add(pool[i]);pool.RemoveAt(i);}
            return offer.ToArray();
        }
        // What the boons do to a stave and to the stone.
        internal static float BoonDamage(StaveKind kind,ICollection<string> boons,bool chief)
        {
            float k=1;
            if(kind==StaveKind.Ember&&boons.Contains("ember")||kind==StaveKind.Frost&&boons.Contains("frost")||kind==StaveKind.Blast&&boons.Contains("blast"))k*=1.35f;
            if(kind==StaveKind.Hearth&&boons.Contains("hearth"))k*=2;
            if(chief&&boons.Contains("bane"))k*=1.5f;
            return k;
        }
        internal static float BoonRange(StaveKind kind,ICollection<string> boons)=>(kind==StaveKind.Frost&&boons.Contains("frost")?4:0)+(boons.Contains("eyes")?4:0);
        internal static float BoonCooldown(StaveKind kind,ICollection<string> boons)=>kind==StaveKind.Thunder&&boons.Contains("thunder")?0.75f:1;
        internal static int BoonCapacity(ICollection<string> boons)=>boons.Contains("veins")?2:0;
        internal static float BoonReach(ICollection<string> boons)=>boons.Contains("reach")?8:0;
        internal static float BoonHealth(ICollection<string> boons)=>boons.Contains("rooted")?1.25f:1;
        internal static int BoonWard(ICollection<string> boons)=>boons.Contains("ward")?2:0;
        internal static float BoonBorrowed(ICollection<string> boons)=>boons.Contains("hands")?0.7f:BorrowedPower;
        internal const float BloodMend=0.005f;                // Bloodstone: share of the stone's strength mended per raider slain
        private static double Clamp01(double v)=>double.IsNaN(v)?0:Math.Max(0,Math.Min(1,v));

        // ---- the stone's level: 1 + the upgrades built around it (a banner, a totem, a brazier, an obelisk) ----
        internal const int MaxLevel=5;
        internal static int Level(int extensions)=>Math.Max(1,Math.Min(MaxLevel,1+extensions));
        // How many staves it can feed, and how far its power reaches.
        internal static int Capacity(int level)=>2*Math.Max(1,Math.Min(MaxLevel,level));
        internal static float PowerRadius(int level)=>20+6*Math.Max(1,Math.Min(MaxLevel,level));
        internal const float SocketSpacing=5;                // no two stave sockets closer than this
        // The upgrades, in order: each needs more warshards (the horde's loot is the gate).
        internal static readonly (string prefab,string name,string source,int shards,(string item,int amount)[] also)[] Upgrades=
        {
            ("BobWarBanner","War banner","goblin_banner",4,new[]{("Wood",4),("LeatherScraps",4)}),
            ("BobWarTotem","Trophy totem","goblin_totempole",8,new[]{("Wood",6),("BoneFragments",10)}),
            ("BobWarBrazier","Horned brazier","piece_brazierfloor02",14,new[]{("Bronze",4),("Coal",10)}),
            ("BobWarObelisk","Rune obelisk","Piece_grausten_pillarbase_tapered",22,new[]{("Stone",30),("Iron",4)}),
        };
        // Upgrading a planted stave in its socket: shards per step, and the stone level the step needs.
        internal static int UpgradeShards(int toQuality)=>3*Math.Max(2,toQuality)-2;   // to ★2: 4, ★3: 7, ★4: 10
        internal static int UpgradeLevel(int toQuality)=>Math.Max(1,toQuality);        // ★2 needs level 2, ★4 level 4
        // Calling the next wave early pays: shards for the time it saves.
        internal static int EarlyBonus(double secondsSaved)=>Math.Max(1,Math.Min(4,(int)(secondsSaved/10)));

        // ---- the staves ----
        internal const string ShardPrefab="BobWarshard";
        internal static readonly Stave[] Staves=
        {
            new Stave{Kind=StaveKind.Ember,Prefab="BobStaveEmber",Name="Ember stave",Source="StaffFireball",Projectile="staff_fireball_projectile",Type="fire",
                Damage=18,Splash=6,Range=30,Cooldown=2.8f,StationLevel=1,Cost=new[]{("Wood",10,0),("SurtlingCore",2,1),(ShardPrefab,2,3)},
                Description="Planted near a Warstone, it hurls fire at raiders and monsters that come within 30 m. The blast cannot burn your buildings."},
            new Stave{Kind=StaveKind.Frost,Prefab="BobStaveFrost",Name="Frost stave",Source="StaffIceShards",Projectile="staff_iceshard_projectile",Type="frost",
                Damage=9,Splash=4,Range=28,Cooldown=0.7f,StationLevel=2,Cost=new[]{("FreezeGland",4,2),("Crystal",2,1),(ShardPrefab,4,3)},
                Description="Planted near a Warstone, it looses a stream of ice shards that chill and slow whatever they hit."},
            new Stave{Kind=StaveKind.Thunder,Prefab="BobStaveThunder",Name="Thunder stave",Source="StaffRedTroll",Projectile="staff_lightning_projectile",Type="lightning",
                Damage=55,Splash=0,Range=42,Cooldown=4f,StationLevel=3,Cost=new[]{("Iron",4,2),("Feathers",6,0),(ShardPrefab,6,4)},
                Description="Planted near a Warstone, it calls down a heavy bolt on the strongest foe within 42 m: the champions' bane."},
            new Stave{Kind=StaveKind.Blast,Prefab="BobStaveBlast",Name="Blast stave",Source="StaffClusterbomb",Projectile="staff_clusterbombstaff_projectile",Type="fire",
                Damage=30,Splash=25,Range=32,Cooldown=7f,StationLevel=4,Cost=new[]{("BlackMetal",4,2),("SurtlingCore",6,2),(ShardPrefab,8,4)},
                Description="Planted near a Warstone, it lobs a bomb that bursts into burning shards over the thickest crowd."},
            new Stave{Kind=StaveKind.Hearth,Prefab="BobStaveHearth",Name="Hearth stave",Source="StaffShield",Projectile=null,Type="heal",
                Damage=10,Splash=0,Range=12,Cooldown=4f,StationLevel=2,Cost=new[]{("FineWood",6,2),("Honey",4,2),(ShardPrefab,4,3)},
                Description="Planted near a Warstone, it mends you, your companions and your tamed animals within 12 m every few seconds."},
        };
        internal static Stave StaveOf(StaveKind kind)=>Staves.FirstOrDefault(s=>s.Kind==kind);
        internal static Stave StaveNamed(string prefab)=>Staves.FirstOrDefault(s=>s.Prefab==prefab);
        internal const int MaxQuality=4;
        internal static float Power(int quality)=>1+0.35f*(Math.Max(1,Math.Min(MaxQuality,quality))-1);
        internal static float RangeAt(Stave s,int quality)=>s.Range+2*(Math.Max(1,Math.Min(MaxQuality,quality))-1);
        // Any other staff with a projectile can be planted too, at a fraction of its power and a slow pace.
        internal const float BorrowedPower=0.4f,BorrowedCooldown=3f,BorrowedRange=25;

        // A launch direction that lands a shot of speed v at a point dx away (horizontally) and dy up under gravity g: the low arc.
        // Returns false when it is out of reach (then aim straight at it and hope).
        internal static bool Arc(double dx,double dy,double v,double g,out double angle)
        {
            angle=Math.Atan2(dy,Math.Max(0.001,dx));
            if(g<=0.0001)return true;
            double v2=v*v,root=v2*v2-g*(g*dx*dx+2*dy*v2);
            if(root<0)return false;
            angle=Math.Atan((v2-Math.Sqrt(root))/(g*Math.Max(0.001,dx)));
            return true;
        }
        // Where to aim at a moving target: where it will be when the shot arrives (two refinements are plenty).
        internal static (double x,double y,double z) Lead((double x,double y,double z) from,(double x,double y,double z) at,(double x,double y,double z) velocity,double speed)
        {
            var aim=at;
            for(int i=0;i<2;i++)
            {
                double d=Math.Sqrt((aim.x-from.x)*(aim.x-from.x)+(aim.y-from.y)*(aim.y-from.y)+(aim.z-from.z)*(aim.z-from.z));
                double t=speed>0?d/speed:0;
                t=Math.Min(t,1.5);
                aim=(at.x+velocity.x*t,at.y+velocity.y*t,at.z+velocity.z*t);
            }
            return aim;
        }

        // ---- deterministic randomness ----
        internal sealed class Rng
        {
            private uint _state;
            internal Rng(int seed){_state=(uint)seed^0x9E3779B9u;if(_state==0)_state=1;}
            internal double Next(){_state^=_state<<13;_state^=_state>>17;_state^=_state<<5;return _state/4294967296.0;}
            internal int Index(int n)=>Math.Min(n-1,(int)(Next()*n));
        }
    }
}
