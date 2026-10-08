using System;
using System.Collections.Generic;
using System.Linq;

namespace Omens
{
    internal enum Kind { DeadTroll=0, Ravens=1, AbandonedCamp=2 }
    internal enum State { Placed=0, Seen=1, Fulfilled=2, Averted=3, Expired=4, Fizzled=5 }

    // One omen's fixed traits. Biomes use Heightmap.Biome flag values so this stays free of game types.
    internal sealed class Omen
    {
        internal Kind Kind;internal bool Bad,Respondable;internal int Biomes;internal string Name,Reading,Outcome,Raid,Averted;
    }

    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        internal const int Meadows=1,Swamp=2,Mountain=4,BlackForest=8,Plains=16,Mistlands=512;
        internal const double SeenAtNightDelay=180; // seconds of world time before a raid foretold at night arrives

        internal static readonly Omen[] All=
        {
            new Omen{Kind=Kind.DeadTroll,Bad=true,Respondable=true,Biomes=BlackForest,Name="A dead troll",Raid="foresttrolls",
                Reading="A troll lies dead, untouched by any blade. The forest is angry. Perhaps fire would calm it.",
                Outcome="The forest's anger comes for you.",
                Averted="The carcass burns. The smoke rises, and the forest's anger fades."},
            new Omen{Kind=Kind.Ravens,Bad=false,Biomes=Meadows|BlackForest|Swamp|Mountain|Plains|Mistlands,Name="Circling ravens",
                Reading="Ravens circle overhead. Odin is watching.",
                Outcome="Odin's ravens show you the land around you. You feel rested."},
            new Omen{Kind=Kind.AbandonedCamp,Bad=true,Biomes=Meadows|BlackForest,Name="An abandoned camp",Raid="army_theelder",
                Reading="A cold camp, left in haste. Others passed this way, and did not leave.",
                Outcome="Whatever drove them away has found you."},
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
        internal const double Linger=120; // seconds of world time a fulfilled good omen stays to be watched
        // When a finished omen's sign leaves the world: at once, except a blessing, which lingers so it can be seen after it is read.
        internal static bool SignGone(State state,bool bad,double sinceResolved)=>
            Finished(state)&&(bad||state!=State.Fulfilled||sinceResolved>=Linger);

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
