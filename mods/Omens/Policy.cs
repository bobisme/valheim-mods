using System;
using System.Collections.Generic;
using System.Linq;

namespace Omens
{
    internal enum Kind { DeadTroll=0, Ravens=1, AbandonedCamp=2, Cairn=3, DrainedDeer=4, Catch=5 }
    internal enum State { Placed=0, Seen=1, Fulfilled=2, Averted=3, Expired=4, Fizzled=5 }
    // What comes to pass: one of the game's raids, a hunting pack sent at the base, a blessing on those nearby, or a gift in the world.
    internal enum Result { Raid, Stalkers, Blessing, Gift }
    // Where a sign may stand, beyond its biomes: anywhere, under open sky (birds must be seen), or on a shore.
    internal enum Site { Any, OpenSky, Shore }

    // One omen's fixed traits. Biomes use Heightmap.Biome flag values so this stays free of game types.
    internal sealed class Omen
    {
        internal Kind Kind;internal bool Bad;internal Result Result;internal Site Site;internal int Biomes;
        internal string Name,Reading,Outcome,Raid,Averted;
        internal bool Linger;                  // its sign stays a while after coming to pass, to be watched
        internal string Cost,Action;internal int CostAmount; // a response: pay this item to avert it
        internal bool Respondable=>Cost!=null;
    }

    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        internal const int Meadows=1,Swamp=2,Mountain=4,BlackForest=8,Plains=16,Mistlands=512;
        internal const double SeenAtNightDelay=180; // seconds of world time before a raid foretold at night arrives

        internal static readonly Omen[] All=
        {
            new Omen{Kind=Kind.DeadTroll,Bad=true,Result=Result.Raid,Biomes=BlackForest,Name="A dead troll",Raid="foresttrolls",
                Cost="$item_resin",CostAmount=5,Action="Burn the carcass",
                Reading="A troll lies dead, untouched by any blade. The forest is angry. Perhaps fire would calm it.",
                Outcome="The forest's anger comes for you.",
                Averted="The carcass burns. The smoke rises, and the forest's anger fades."},
            new Omen{Kind=Kind.Ravens,Bad=false,Result=Result.Blessing,Site=Site.OpenSky,Linger=true,Biomes=Meadows|BlackForest|Swamp|Mountain|Plains|Mistlands,
                Name="Circling ravens",Reading="Ravens circle overhead. Odin is watching.",
                Outcome="Odin's ravens show you the land around you. You feel rested."},
            new Omen{Kind=Kind.AbandonedCamp,Bad=true,Result=Result.Raid,Biomes=Meadows|BlackForest,Name="An abandoned camp",Raid="army_theelder",
                Reading="A cold camp, left in haste. Others passed this way, and did not leave.",
                Outcome="Whatever drove them away has found you."},
            new Omen{Kind=Kind.Cairn,Bad=true,Result=Result.Raid,Biomes=Meadows|BlackForest|Swamp,Name="A scattered cairn",Raid="skeletons",
                Cost="$item_bonefragments",CostAmount=5,Action="Lay the bones to rest",
                Reading="Someone has scattered these stones and the bones beneath them. The dead are restless. Lay them to rest.",
                Outcome="The restless dead have come for you.",
                Averted="The bones lie beneath the stones again. The dead are quiet."},
            new Omen{Kind=Kind.DrainedDeer,Bad=true,Result=Result.Stalkers,Biomes=Meadows|BlackForest,Name="A drained deer",
                Reading="A deer lies drained of blood. Something hunts in the night.",
                Outcome="Something has followed the scent of blood to your home."},
            new Omen{Kind=Kind.Catch,Bad=false,Result=Result.Gift,Site=Site.Shore,Linger=true,Biomes=Meadows|BlackForest|Swamp|Plains|Mistlands,
                Name="Gulls over the shore",Reading="Gulls wheel over the shore. The sea is generous.",
                Outcome="Fish lie stranded on the shore. Take them before they slip away."},
        };
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
        internal const double Linger=120; // seconds of world time a lingering sign stays after its omen comes to pass
        // When a finished omen's sign leaves the world: at once, except one that lingers after coming to pass so it can be watched.
        internal static bool SignGone(State state,bool lingers,double sinceResolved)=>
            Finished(state)&&(!lingers||state!=State.Fulfilled||sinceResolved>=Linger);

        // The hunting pack a drained deer sends, by the biome of the base it hunts: (creature prefab, level).
        internal static (string prefab,int level)[] Pack(int baseBiome)
        {
            if((baseBiome&Swamp)!=0)return new[]{("Draugr_Elite",1),("Draugr",1),("Draugr",1)};
            if((baseBiome&Mountain)!=0)return new[]{("Wolf",2),("Wolf",1),("Wolf",1)};
            if((baseBiome&Plains)!=0)return new[]{("GoblinBrute",1),("Goblin",1),("Goblin",1)};
            if((baseBiome&Mistlands)!=0)return new[]{("Seeker",1),("Seeker",1)};
            return new[]{("Greydwarf_Elite",1),("Greydwarf",2),("Greydwarf",1)};
        }

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
