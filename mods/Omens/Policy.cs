using System;
using System.Collections.Generic;
using System.Linq;

namespace Omens
{
    internal enum Kind { DeadTroll=0, Ravens=1, AbandonedCamp=2, Cairn=3, DrainedDeer=4, Catch=5, BloodMoon=6,
        Wisps=7, GreatStag=8, FallenStar=9, Wanderer=10, Wolves=11, WarBanner=12, Drowned=13, Hoard=14, GraveCandles=15, Scorched=16,
        Shrine=17, Aurora=18, GhostShip=19, ThorStorm=20 }
    internal enum State { Placed=0, Seen=1, Fulfilled=2, Averted=3, Expired=4, Fizzled=5 }
    // What comes to pass: one of the game's raids, a hunting pack sent at the base, a blessing on those nearby, a gift in the world,
    // a blood moon night, a treasure the lights lead to, a great stag to hunt, the wanderer's favour, the dead hunting a thief,
    // the gods' favour for an offering, a night under the northern lights, or Thor's storm.
    internal enum Result { Raid, Stalkers, Blessing, Gift, BloodMoon, Treasure, Quarry, Favour, Curse, Offering, Aurora, Storm }
    // Where a sign may stand, beyond its biomes: anywhere, under open sky (birds must be seen), or on a shore.
    internal enum Site { Any, OpenSky, Shore }

    // One omen's fixed traits. Biomes use Heightmap.Biome flag values so this stays free of game types.
    internal sealed class Omen
    {
        internal Kind Kind;internal bool Bad;internal Result Result;internal Site Site;internal int Biomes;
        internal string Name,Reading,Outcome,Raid,Averted;
        internal string Test,Config;           // its name in the test command, and its setting's description
        internal float SeenFrom=14;            // metres from which walking near it counts as seeing it
        internal bool Linger;                  // its sign stays a while after coming to pass, to be watched
        internal string Action,Cost;internal int CostAmount; // a response: do this, paying this item (by prefab name; none: free)
        internal bool Softens;                 // the response only softens what comes, instead of averting it
        internal bool Provokes;                // the response averts it, but brings its pack at once
        internal bool Respondable=>Action!=null;
    }

    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        internal const int Meadows=1,Swamp=2,Mountain=4,BlackForest=8,Plains=16,Mistlands=512;
        internal const double SeenAtNightDelay=180; // seconds of world time before a raid foretold at night arrives

        internal const int AnyLand=Meadows|BlackForest|Swamp|Mountain|Plains;
        internal static readonly Omen[] All=
        {
            // ---- bad ----
            new Omen{Kind=Kind.DeadTroll,Bad=true,Result=Result.Raid,Biomes=BlackForest,Name="A dead troll",Raid="foresttrolls",Test="troll",
                Cost="Resin",CostAmount=5,Action="Burn the carcass",
                Reading="A troll lies dead, untouched by any blade. The forest is angry. Perhaps fire would calm it.",
                Outcome="The forest's anger comes for you.",
                Averted="The carcass burns. The smoke rises, and the forest's anger fades.",
                Config="Black Forest: a dead troll. Unless burned with resin, trolls raid the nearest base that night."},
            new Omen{Kind=Kind.AbandonedCamp,Bad=true,Result=Result.Raid,Biomes=Meadows|BlackForest,Name="An abandoned camp",Raid="army_theelder",Test="camp",
                Reading="A cold camp, left in haste. Others passed this way, and did not leave.",
                Outcome="Whatever drove them away has found you.",
                Config="Meadows or Black Forest: a cold, abandoned camp. Greydwarfs raid the nearest base that night."},
            new Omen{Kind=Kind.Cairn,Bad=true,Result=Result.Raid,Biomes=Meadows|BlackForest|Swamp,Name="A scattered cairn",Raid="skeletons",Test="cairn",
                Cost="BoneFragments",CostAmount=5,Action="Lay the bones to rest",
                Reading="Someone has scattered these stones and the bones beneath them. The dead are restless. Lay them to rest.",
                Outcome="The restless dead have come for you.",
                Averted="The bones lie beneath the stones again. The dead are quiet.",
                Config="Meadows, Black Forest or Swamp: a scattered cairn. Unless the bones are laid to rest (5 bone fragments), skeletons raid the nearest base that night."},
            new Omen{Kind=Kind.DrainedDeer,Bad=true,Result=Result.Stalkers,Biomes=Meadows|BlackForest,Name="A drained deer",Test="deer",
                Reading="A deer lies drained of blood. Something hunts in the night.",
                Outcome="Something has followed the scent of blood to your home.",
                Config="Meadows or Black Forest: a deer drained of blood. At night a hunting pack, led by a stronger one, comes for the nearest base."},
            new Omen{Kind=Kind.BloodMoon,Bad=true,Result=Result.BloodMoon,Biomes=AnyLand,Name="A blood-soaked circle",Test="bloodmoon",
                Cost="RawMeat",CostAmount=4,Action="Leave an offering",Softens=true,
                Reading="A ring of stones, wet with blood. The moon will bleed tonight. An offering of meat might sate it.",
                Outcome="The moon bleeds. The night is hungry.",
                Averted="The stones drink the offering. The night will hunger less.",
                Config="Most land: a blood-soaked circle. That night the moon bleeds: a red sky, and night creatures spawn twice as often, more at once and stronger. An offering of 4 raw meat softens it."},
            new Omen{Kind=Kind.Wolves,Bad=true,Result=Result.Raid,Biomes=Mountain,Name="A gnawed carcass",Raid="wolves",Test="wolves",
                Cost="RawMeat",CostAmount=3,Action="Leave meat for the pack",
                Reading="A deer, torn apart and gnawed to the bone. Wolf tracks circle it, many of them. The pack is hungry. Meat might turn them away.",
                Outcome="The pack has come down from the mountain.",
                Averted="You leave the meat. Far off, the wolves howl, and turn away.",
                Config="Mountains: a gnawed carcass ringed with wolf tracks. Unless you leave 3 raw meat for the pack, wolves raid the nearest base that night."},
            new Omen{Kind=Kind.WarBanner,Bad=true,Result=Result.Raid,Biomes=Plains,Name="A Fuling war banner",Raid="army_goblin",Test="banner",
                Action="Tear down the banner",Provokes=true,
                Reading="A Fuling war banner, planted in the earth and hung with skulls. They mean to march on you. Tear it down, if you are ready to fight.",
                Outcome="The Fulings march on your home.",
                Averted="The banner falls, and its guards burst from the grass!",
                Config="Plains: a Fuling war banner. Fulings raid the nearest base that night, unless you tear the banner down and fight its guards there and then."},
            new Omen{Kind=Kind.Drowned,Bad=true,Result=Result.Stalkers,Site=Site.Shore,Biomes=Meadows|BlackForest|Swamp|Plains,Name="A drowned man",Test="drowned",
                Cost="Coins",CostAmount=10,Action="Pay his passage",
                Reading="A drowned man lies on the shore, eyes open, with no coin for his passage. The drowned do not rest without it.",
                Outcome="The drowned have come ashore, and they are not alone.",
                Averted="You lay the coins on his eyes. He closes them, and rests.",
                Config="Shores: a drowned man. Unless you pay his passage (10 coins), draugr come ashore for the nearest base at night, once someone is home."},
            new Omen{Kind=Kind.Hoard,Bad=true,Result=Result.Curse,Biomes=AnyLand,Name="A cursed hoard",Test="hoard",
                Action="Take the hoard",
                Reading="Gold spills from a grave-chest, and the bones beside it still clutch at it. What is buried with the dead belongs to the dead.",
                Outcome="The dead have come for their gold.",
                Averted="You take the hoard. Somewhere, the dead stir.",
                Config="Most land: a cursed hoard. Leave it and nothing happens. Take its gold and gems, and that night the dead hunt whoever took it, wherever they are."},
            new Omen{Kind=Kind.GraveCandles,Bad=true,Result=Result.Raid,Biomes=BlackForest|Swamp|Mountain,Name="Unlit grave candles",Raid="ghosts",Test="candles",
                Cost="Resin",CostAmount=3,Action="Light the candles",
                Reading="Grave candles stand in a ring around an old skull, every one of them blown out. Without light, the dead walk. Relight them.",
                Outcome="The dead walk tonight.",
                Averted="The candles burn again. The dead are quiet.",
                Config="Black Forest, Swamp or Mountains: a ring of blown-out grave candles. Unless relit (3 resin), ghosts haunt the nearest base that night."},
            new Omen{Kind=Kind.Scorched,Bad=true,Result=Result.Raid,Biomes=BlackForest|Swamp|Plains,Name="A scorched circle",Raid="surtlings",Test="scorched",
                Reading="The ground is burnt black in a perfect circle, and a surtling core still smoulders at its heart. Fire is coming.",
                Outcome="Fire has come for you.",
                Config="Black Forest, Swamp or Plains: a scorched circle around a smouldering surtling core. Surtlings raid the nearest base that night."},
            // ---- good ----
            new Omen{Kind=Kind.Ravens,Bad=false,Result=Result.Blessing,Site=Site.OpenSky,Linger=true,Biomes=AnyLand|Mistlands,SeenFrom=35,Test="ravens",
                Name="Circling ravens",Reading="Ravens circle overhead. Odin is watching.",
                Outcome="Odin's ravens show you the land around you. You feel rested.",
                Config="Ravens circling: players nearby are rested and the land around is revealed on their map."},
            new Omen{Kind=Kind.Catch,Bad=false,Result=Result.Gift,Site=Site.Shore,Linger=true,Biomes=Meadows|BlackForest|Swamp|Plains|Mistlands,Test="catch",
                Name="Gulls over the shore",Reading="Gulls wheel over the shore. The sea is generous.",
                Outcome="Fish lie stranded on the shore. Take them before they slip away.",
                Config="Shores: gulls circling. Reading it strands real fish on the shore for the taking."},
            new Omen{Kind=Kind.Wisps,Bad=false,Result=Result.Treasure,Biomes=Meadows|BlackForest|Swamp|Mistlands,SeenFrom=16,Test="wisps",
                Name="Dancing lights",Reading="Lights dance between the trees, just out of reach. They want to show you something.",
                Outcome="The lights drift away. Follow them.",
                Config="Meadows, Black Forest, Swamp or Mistlands: dancing lights. Reading them sends one drifting off to a real treasure chest nearby, marked on your map."},
            new Omen{Kind=Kind.GreatStag,Bad=false,Result=Result.Quarry,Biomes=Meadows|BlackForest,Test="stag",
                Name="A shed antler",Reading="An antler lies in the moss, bigger than any you have seen, still warm. A great stag walks these woods.",
                Outcome="A great stag runs nearby. Hunt it, and take its antlers.",
                Config="Meadows or Black Forest: a huge shed antler. Reading it brings a great two-star stag nearby, which drops a hard antler and its trophy."},
            new Omen{Kind=Kind.FallenStar,Bad=false,Result=Result.Gift,Linger=true,Biomes=AnyLand,Test="star",
                Name="A fallen star",Reading="A stone fell from the sky and still smoulders where it landed. The sky has sent a gift.",
                Outcome="Shards of the star lie cooling in the ash. Take them.",
                Config="Most land: a smouldering fallen star. Reading it leaves ore suited to the land around it: copper and tin, iron, silver or black metal."},
            new Omen{Kind=Kind.Wanderer,Bad=false,Result=Result.Favour,Biomes=AnyLand|Mistlands,SeenFrom=18,Test="wanderer",
                Name="A cloaked wanderer",Reading="An old man in a grey cloak watches you from beneath his hat. Then he is gone.",
                Outcome="Where he stood, the world seems clearer. Your skills will grow faster for a while.",
                Config="Anywhere: a cloaked wanderer, who vanishes as you approach. Players nearby learn every skill 50% faster for 20 minutes."},
            new Omen{Kind=Kind.Shrine,Bad=false,Result=Result.Offering,Biomes=AnyLand,Test="shrine",
                Cost="Honey",CostAmount=3,Action="Leave an offering",
                Name="A forgotten shrine",Reading="A shrine to the old gods, its offering bowl long empty. Those who remember the gods are remembered by them.",
                Outcome="The gods remember you.",
                Averted="The shrine accepts your offering. The gods remember, and you feel rested.",
                Config="Most land: a forgotten shrine. Leave 3 honey in its bowl: everyone nearby is rested, and the gods' favour rises by two."},
            new Omen{Kind=Kind.Aurora,Bad=false,Result=Result.Aurora,Biomes=AnyLand|Mistlands,SeenFrom=16,Test="aurora",
                Name="A humming rune stone",Reading="An old rune stone hums, and its carvings glow faintly green. Tonight the sky will dance.",
                Outcome="The northern lights fill the sky. The night is quiet, and the land rests.",
                Config="Anywhere: a glowing rune stone. That night the northern lights fill the sky for everyone: half as many night creatures, and players under it heal faster and learn faster."},
            new Omen{Kind=Kind.GhostShip,Bad=true,Result=Result.Stalkers,Site=Site.Shore,Biomes=Meadows|BlackForest|Swamp|Plains,SeenFrom=30,Test="ship",
                Cost="Wood",CostAmount=20,Action="Light a warning beacon",
                Name="A ship with black sails",Reading="A longship with black sails lies off the shore, and no one moves aboard. It is waiting for the dark. A beacon fire might warn it off.",
                Outcome="The black ship has come ashore. Its crew are not alive.",
                Averted="The beacon blazes. Out at sea, the black ship turns away into the mist.",
                Config="Shores: a ghost ship offshore. Unless you light a warning beacon (20 wood), its draugr crew comes for the nearest base at night, once someone is home."},
            new Omen{Kind=Kind.ThorStorm,Bad=true,Result=Result.Storm,Biomes=AnyLand,Test="storm",
                Cost="Coins",CostAmount=20,Action="Bury coins for Thor",Softens=true,
                Name="A lightning-split oak",Reading="An oak split in two by lightning, still smouldering. Thor is angry, and he is coming. Silver buried at its roots might stay his hammer.",
                Outcome="Thor's storm breaks over you. Get under a roof!",
                Averted="You bury the coins at the oak's roots. Thunder rolls far off, but the hammer stays in Thor's hand.",
                Config="Most land: a lightning-split oak. That night Thor's storm breaks for eight minutes: thunder, rain, and lightning striking near anyone under the open sky (a crackle on the ground warns of each strike). Burying 20 coins at the oak keeps the storm but stops the strikes."},
        };

        // More ways to say what a sign shows, so a second dead troll does not read like the first. One per sign, by its id.
        internal static readonly Dictionary<Kind,string[]> Readings=new Dictionary<Kind,string[]>
        {
            [Kind.DeadTroll]=new[]{"A troll lies dead in the moss, with no wound on it. The forest is angry. Fire might calm it.",
                "Something killed this troll without a blade. The trees lean close, and they are not pleased. Burn it, and they may forgive."},
            [Kind.Ravens]=new[]{"Two ravens circle, then a third. Huginn and Muninn see you. Odin is watching.",
                "Ravens wheel above you, calling. The Allfather's eyes are on you today."},
            [Kind.AbandonedCamp]=new[]{"Someone slept here, and ran. Their spear is still on the ground. Whatever came for them is not far off.",
                "The fire is cold, the bedroll still laid out. They left everything. They did not leave by choice."},
            [Kind.Cairn]=new[]{"A grave cairn, torn open and kicked apart. The dead do not forgive that. Put their bones back.",
                "The stones of an old cairn lie scattered, the bones flung among them. The dead will come looking for whoever did this."},
            [Kind.DrainedDeer]=new[]{"The deer is white and empty, not a drop of blood left in it. Whatever did this is still hungry.",
                "Two small wounds in the throat, and no blood at all. Something hunts at night, and it has your scent."},
            [Kind.BloodMoon]=new[]{"Stones in a ring, red to the top. Someone has fed the moon, and tonight it will want more. Meat might sate it.",
                "A boar lies slaughtered in a circle of stones. The moon will rise red tonight, and hungry."},
            [Kind.Hoard]=new[]{"A chest of grave-gold, and a hand of bone still on the lid. The dead do not give up what is theirs.",
                "Coins and gems spill from a burial chest. Take it, and the dead will know your name."},
            [Kind.Wanderer]=new[]{"A tall old man with one eye and a wide hat leans on his staff, watching you. When you blink, he is gone.",
                "A grey-cloaked wanderer stands in your path. He nods, as if he knows you, and then there is no one there."},
            [Kind.Wisps]=new[]{"Little lights drift between the trees, waiting for you. They have found something.",
                "Wisps dance in the dusk, beckoning. Someone hid something near here, and the lights remember where."},
        };
        internal static string ReadingOf(Kind kind,long id)
        {
            Omen omen=Of(kind);
            if(!Readings.TryGetValue(kind,out string[] more)||more.Length==0)return omen.Reading;
            int pick=(int)(((ulong)id)%(ulong)(more.Length+1));
            return pick==0?omen.Reading:more[pick-1];
        }

        // ---- the gods' favour: one standing per world, moved by how players answer omens ----
        internal const int FateMin=-5,FateMax=5;
        internal const int FateAverted=1,FateIgnored=-1,FateTaken=-2,FateOffering=2;
        internal static int Fate(int fate,int delta)=>Math.Max(FateMin,Math.Min(FateMax,fate+delta));
        // Favoured worlds see fewer bad omens, forsaken ones more (never all of one kind).
        internal static double BadChance(double baseChance,int fate)=>Math.Max(0.25,Math.Min(0.85,(double.IsNaN(baseChance)?0.6:baseChance)-0.05*fate));
        // The forsaken see omens more often.
        internal static double IntervalFactor(int fate)=>fate<=-3?0.75:1;
        // The gods' wrath: packs and hunters a level stronger for the forsaken (at most three stars' worth).
        internal static int Wrath(int level,int fate)=>Math.Min(3,level+(fate<=-3?1:0));
        // The gods' generosity: gifts half as large again for the beloved.
        internal static int Generous(int count,int fate)=>fate>=3?(int)Math.Ceiling(count*1.5):count;
        internal static string Standing(int fate)=>fate<=-3?"forsaken":fate<0?"displeased":fate==0?"watched":fate<3?"favoured":"beloved";
        internal static string StandingNews(int before,int after)
        {
            string was=Standing(before),now=Standing(after);
            if(was==now)return null;
            switch(now)
            {
                case "forsaken":return "The gods have turned their faces from you. Omens will come thick and cruel.";
                case "displeased":return after<before?"The gods are displeased with you.":"The gods' anger cools a little.";
                case "watched":return "The gods watch you, and wait.";
                case "favoured":return after>before?"The gods are pleased with you.":"The gods' favour wanes.";
                default:return "The gods love you. Their gifts will be generous.";
            }
        }

        // ---- chains: an omen left to come to pass can return, worse, near the home it struck ----
        internal static Kind? Chain(Kind kind)
        {
            switch(kind)
            {
                case Kind.Cairn:return Kind.GraveCandles;      // the restless dead find their way home with you
                case Kind.DrainedDeer:return Kind.BloodMoon;   // the hunter feeds the moon
                case Kind.Wolves:return Kind.BloodMoon;
                case Kind.Drowned:return Kind.GhostShip;       // his crew comes looking for him
                case Kind.DeadTroll:return Kind.ThorStorm;     // the forest calls on Thor
                case Kind.AbandonedCamp:return Kind.Scorched;  // what drove them off comes back with fire
                default:return null;
            }
        }
        // How likely a chain is, by the gods' favour: certain for the forsaken, never for the beloved.
        internal static double ChainChance(int fate)=>fate<=-3?1:fate<0?0.75:fate==0?0.5:fate<3?0.25:0;
        internal const double ChainDelayDays=0.5; // it returns half a day later
        internal const string ChainedPrefix="The omen returns, worse than before. ";

        // ---- Thor's storm ----
        internal const double StormSeconds=480;
        internal const float StrikeDamage=18,StrikeRadius=2.5f,StrikeWarning=1.6f;
        // Seconds to the next strike near a player, from a 0–1 roll; close strikes are rarer.
        internal static float NextStrike(double roll)=>9f+11f*(float)Clamp01(roll);
        internal static bool CloseStrike(double roll)=>Clamp01(roll)<0.25;

        // ---- the rune bones: what the host tells whoever casts them ----
        internal static string Compass(double dx,double dz)
        {
            string[] names={"north","north-east","east","south-east","south","south-west","west","north-west"};
            double angle=Math.Atan2(dx,dz)*180/Math.PI; // 0 = north (+z), 90 = east (+x)
            int i=(int)Math.Round(((angle%360)+360)%360/45)%8;
            return names[i];
        }
        internal static int Roughly(double metres)=>metres<100?(int)Math.Max(10,Math.Round(metres/10)*10):(int)(Math.Round(metres/50)*50);
        internal static string Cast(bool any,double dx,double dz,bool bad,int fate,string tonight)
        {
            string lead=any?$"The bones fall toward the {Compass(dx,dz)}. Something waits there, about {Roughly(Math.Sqrt(dx*dx+dz*dz))} m away. It feels {(bad?"cold":"warm")}."
                :"The bones say nothing. No sign waits near you.";
            return lead+$" The gods find you {Standing(fate)}."+(string.IsNullOrEmpty(tonight)?"":" "+tonight);
        }
        internal const float CastCooldown=10;
        internal const double CastRange=2000;

        // ---- the northern lights ----
        internal static float AuroraSpawnChance(float chance)=>Math.Max(0f,chance)*0.5f;
        internal const float AuroraRegen=1.25f,AuroraSkill=0.25f;

        internal static Omen Of(Kind kind)=>All.First(o=>o.Kind==kind);

        // Bad with this chance, else good; falls back to the other polarity when none of the wanted kind fits here.
        internal static Kind? Pick(double polarityRoll,double kindRoll,double badChance,int biome,IEnumerable<Kind> enabled)
        {
            var fits=All.Where(o=>(o.Biomes&biome)!=0&&enabled.Contains(o.Kind)).ToList();
            if(fits.Count==0)return null;
            bool bad=polarityRoll<badChance;
            var wanted=fits.Where(o=>o.Bad==bad).ToList();
            if(wanted.Count==0)wanted=fits;
            return wanted[Math.Min(wanted.Count-1,(int)(Clamp01(kindRoll)*wanted.Count))].Kind;
        }
        // Average interval in game days, randomized 0.75–1.25×.
        internal static double NextDelay(double intervalDays,double dayLength,double roll)=>
            Math.Max(60,intervalDays*dayLength*(0.75+0.5*Clamp01(roll)));

        // A bad omen arrives that night: at nightfall when seen by day, or a little later when seen at night.
        internal static bool RaidDue(double now,double seenAt,bool seenAtNight,bool isNight)=>
            isNight&&now-seenAt>=(seenAtNight?SeenAtNightDelay:0);

        internal static State Advance(State state,double now,double placedAt,double expireAfter,bool seen,bool responded,bool outcomeDone,bool outcomeImpossible)
        {
            switch(state)
            {
                case State.Placed:
                    if(responded)return State.Averted;
                    if(seen)return State.Seen;
                    return now-placedAt>=expireAfter?State.Expired:State.Placed;
                case State.Seen:
                    if(responded)return State.Averted;
                    if(outcomeDone)return State.Fulfilled;
                    return outcomeImpossible?State.Fizzled:State.Seen;
                default:return state;
            }
        }
        internal static bool Finished(State state)=>state>=State.Fulfilled;
        internal const double Linger=120;     // seconds of world time a lingering sign stays after its omen comes to pass
        internal const double AvertLinger=20; // seconds an averted sign stays, so the response (fire, lit candles, a fallen banner) is seen
        // When a finished omen's sign leaves the world: at once, except one that lingers after coming to pass so it can be watched,
        // and one just averted.
        internal static bool SignGone(State state,bool lingers,double sinceResolved)=>
            Finished(state)&&(state==State.Averted?sinceResolved>=AvertLinger:!lingers||state!=State.Fulfilled||sinceResolved>=Linger);

        // A blood moon night: night spawns come more often, more at once, and stronger; an offering softens it.
        internal static float SpawnChance(float chance,bool softened)=>Math.Min(100f,Math.Max(0f,chance)*(softened?1.5f:2f));
        internal static int MaxSpawned(int max,bool softened)=>max<=0?max:(int)Math.Ceiling(max*(softened?1.25:1.5));
        internal static float LevelUpChance(float chance,bool softened)=>Math.Min(70f,Math.Max(0f,chance)*(softened?1.5f:2f));

        // The pack an omen sends, by the biome where it strikes (the base, or the thief): (creature prefab, level).
        internal static (string prefab,int level)[] Pack(Kind kind,int biome)
        {
            bool swamp=(biome&Swamp)!=0,mountain=(biome&Mountain)!=0,plains=(biome&Plains)!=0,mist=(biome&Mistlands)!=0,late=mountain||plains||mist;
            switch(kind)
            {
                case Kind.Drowned: // draugr come ashore
                case Kind.GhostShip:
                    if(late)return new[]{("Draugr_Elite",2),("Draugr",2),("Draugr_Ranged",1)};
                    if(swamp)return new[]{("Draugr_Elite",1),("Draugr",1),("Draugr_Ranged",1)};
                    return new[]{("Draugr",1),("Draugr",1)};
                case Kind.Hoard: // the dead want their gold back
                    if(late)return new[]{("Ghost",2),("Draugr_Elite",1),("Draugr",1)};
                    if(swamp)return new[]{("Ghost",1),("Draugr",1),("Draugr",1)};
                    if((biome&BlackForest)!=0)return new[]{("Ghost",1),("Skeleton",1),("Skeleton",1)};
                    return new[]{("Ghost",1),("Skeleton",1)};
                case Kind.WarBanner: // the banner's guards
                    return new[]{("Goblin",1),("Goblin",1),("GoblinArcher",1)};
                default: // a drained deer's hunters, led by a stronger one
                    if(swamp)return new[]{("Draugr_Elite",1),("Draugr",1),("Draugr",1)};
                    if(mountain)return new[]{("Wolf",2),("Wolf",1),("Wolf",1)};
                    if(plains)return new[]{("GoblinBrute",1),("Goblin",1),("Goblin",1)};
                    if(mist)return new[]{("Seeker",1),("Seeker",1)};
                    return new[]{("Greydwarf_Elite",1),("Greydwarf",2),("Greydwarf",1)};
            }
        }

        // What a gift leaves on the ground, by the biome where it lies: (item prefab, least, most).
        internal static (string prefab,int min,int max)[] Gifts(Kind kind,int biome)
        {
            if(kind==Kind.Catch)return new[]{("Fish1",3,5)};
            if(kind==Kind.Hoard)
            {
                if((biome&(Mountain|Plains|Mistlands))!=0)return new[]{("Coins",60,100),("Ruby",1,2),("SilverNecklace",1,1)};
                if((biome&Swamp)!=0)return new[]{("Coins",40,80),("Amber",1,2),("AmberPearl",1,2)};
                if((biome&BlackForest)!=0)return new[]{("Coins",30,60),("Amber",1,2)};
                return new[]{("Coins",20,40),("Amber",1,1)};
            }
            // A fallen star: ore a little ahead of the land it falls on.
            if((biome&Plains)!=0)return new[]{("BlackMetalScrap",4,6)};
            if((biome&Mountain)!=0)return new[]{("SilverOre",3,5),("Obsidian",2,4)};
            if((biome&Swamp)!=0)return new[]{("IronScrap",4,7)};
            if((biome&BlackForest)!=0)return new[]{("CopperOre",4,7),("TinOre",3,5)};
            return new[]{("Flint",4,6),("CopperOre",2,4)};
        }
        // The game's own treasure chest for the land the lights lead to; it fills itself with that biome's loot.
        internal static string Chest(int biome)
        {
            if((biome&Mistlands)!=0)return "TreasureChest_dvergrtower";
            if((biome&Plains)!=0)return "TreasureChest_heath";
            if((biome&Mountain)!=0)return "TreasureChest_mountains";
            if((biome&Swamp)!=0)return "TreasureChest_swamp";
            if((biome&BlackForest)!=0)return "TreasureChest_blackforest";
            return "TreasureChest_meadows";
        }
        internal static int Roll(int min,int max,double roll)=>min+Math.Min(max-min,(int)(Clamp01(roll)*(max-min+1)));

        // The great stag: a two-star deer, larger than life, whose drops add these.
        internal const int StagLevel=3;internal const float StagScale=1.4f;
        internal static readonly (string prefab,int amount)[] StagDrops={("HardAntler",1),("TrophyDeer",1)};
        // The wanderer's favour: every skill learns this much faster, for this many seconds.
        internal const float FavourSkill=0.5f,FavourSeconds=1200;
        // How long a thief's curse waits for a night when they can be found, before it gives up.
        internal const double CurseDays=2;

        // Index of the nearest base to a point, or -1 when none is within reach.
        internal static int Nearest(double x,double z,IList<(double x,double z)> bases,double maxDistance)
        {
            int best=-1;double bestD=maxDistance*maxDistance;
            for(int i=0;i<bases.Count;i++)
            {
                double dx=bases[i].x-x,dz=bases[i].z-z,d=dx*dx+dz*dz;
                if(d<=bestD){bestD=d;best=i;}
            }
            return best;
        }
        private static double Clamp01(double v)=>double.IsNaN(v)?0:Math.Min(0.999999,Math.Max(0,v));
    }
}
