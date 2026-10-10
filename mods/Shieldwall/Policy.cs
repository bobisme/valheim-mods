using System;
using System.Collections.Generic;
using System.Linq;

namespace Shieldwall
{
    // Pure rules for Shieldwall: the Warstone's marks and ward, who comes to a siege and how many, when waves come, what holding the
    // line earns, and the staves. No Unity or game types, so every number can be tested.
    internal enum Role{Grunt=1,Sapper=2,Flyer=3,Champion=4}
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
        internal static int Waves(int marks)=>4+Math.Min(4,Marks(marks)/2);
        internal static int Total(int marks,int players)=>Math.Min(220,30+8*Marks(marks)+14*Math.Max(0,Math.Min(6,players)-1));
        internal static float StoneHealth(int stage,int marks)=>4000f*(1+stage*0.75f)*(1+0.05f*Marks(marks));
        internal const float Reach=3.5f;
        internal const float Toughness=0.4f;                  // the stone feels this share of each blow                      // a raider this close to the stone strikes it
        internal const int MinAlive=4;

        // A deterministic plan: the same seed always makes the same siege, so a new owner of the stone carries on the same one.
        internal static List<List<Unit>> Plan(int stage,int marks,int players,int seed)
        {
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
                    if(w>=1&&roster.Sappers.Length>0&&rng.Next()<0.10)role=Role.Sapper;
                    else if(w>=2&&roster.Flyers.Length>0&&rng.Next()<0.08)role=Role.Flyer;
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
        internal static bool NextWave(int alive,int waveSize,double sinceWave,double maxGap)=>sinceWave>=maxGap||alive<=Math.Max(MinAlive/2,waveSize*0.3)&&sinceWave>=12;
        // Raiders stream out of the rift a few at a time while there is room.
        internal static int Release(int queued,int alive,int cap)=>Math.Max(0,Math.Min(Math.Min(queued,3),cap-alive));

        // ---- what holding the line earns ----
        internal static int Shards(int stage,int marks,double health,int kills)=>3+stage+Marks(marks)/2+(int)Math.Round(5*Clamp01(health))+kills/12;
        internal static int Coins(int min,int max,int marks,double roll)=>(int)Math.Round((min+(max-min)*Clamp01(roll))*(1+0.1*Marks(marks)));
        // A raider sometimes carries a shard; a warchief always carries several.
        internal static int Carried(Role role,double roll)=>role==Role.Champion?3+(int)(Clamp01(roll)*3):roll<0.12?1:0;
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
            new Stave{Kind=StaveKind.Thunder,Prefab="BobStaveThunder",Name="Thunder stave",Source="StaffLightning",Projectile="staff_lightning_projectile",Type="lightning",
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
