using System;

namespace Gary
{
    // Pure rules for Gary's tree planting: when he may go looking, and which spots near your bed are fair game.
    internal static class ReforestPolicy
    {
        internal const double MinFromBed=12,MinFromBuilding=8,PlantSeconds=2.5,GiveUpSeconds=20;
        // Time to look: planting on, you near your bed, his last tree long enough ago, and not too many of his saplings still growing
        // around home. He always has a free tree in him on the normal schedule; seeds he has gathered let him plant three times as
        // often and keep twice as many growing.
        internal const double SeededPace=1/3.0;internal const int SeededCapFactor=2;
        internal static bool Ready(bool enabled,double fromBed,double radius,double sinceLast,double interval,int saplings,int maxSaplings,bool haveSeeds=false)
        {
            if(!enabled||double.IsNaN(fromBed)||fromBed>radius)return false;
            bool free=!(sinceLast<interval)&&saplings<maxSaplings;
            bool seeded=haveSeeds&&!(sinceLast<interval*SeededPace)&&saplings<maxSaplings*SeededCapFactor;
            return free||seeded;
        }
        // Whether this planting must use a seed (it came early, or past the free limit).
        internal static bool NeedsSeed(double sinceLast,double interval,int saplings,int maxSaplings)=>sinceLast<interval||saplings>=maxSaplings;
        // A fair spot: around home but not in it, clear of buildings, on wild ground (not farmland or paths), dry and open to the sky.
        internal static bool Spot(double fromBed,double radius,double fromBuilding,bool inBase,bool cultivated,bool cleared,bool roofed,bool crowded,bool water)=>
            !double.IsNaN(fromBed)&&fromBed>=MinFromBed&&fromBed<=radius&&!(fromBuilding<MinFromBuilding)&&!inBase&&!cultivated&&!cleared&&!roofed&&!crowded&&!water;
        // The tree to plant from his seeds: the stump's kind if he has its seed, else any seed of his that grows here.
        internal static int ChooseSeeded(int stumpKind,bool[] grows,int[] seeds,double roll)
        {
            if(grows==null||seeds==null)return -1;
            var usable=new bool[grows.Length];
            for(int i=0;i<grows.Length;i++)usable[i]=grows[i]&&i<seeds.Length&&seeds[i]>0;
            return Choose(stumpKind,usable,roll);
        }
        // The tree to plant: the stump's own kind when he plants beside one, else one of those that grow here (by a 0–1 roll).
        internal static int Choose(int stumpKind,bool[] grows,double roll)
        {
            if(grows==null||grows.Length==0)return -1;
            if(stumpKind>=0&&stumpKind<grows.Length&&grows[stumpKind])return stumpKind;
            int count=0;foreach(bool g in grows)if(g)count++;
            if(count==0)return -1;
            int pick=(int)(Math.Max(0,Math.Min(0.999999,double.IsNaN(roll)?0:roll))*count);
            for(int i=0;i<grows.Length;i++)if(grows[i]&&pick--==0)return i;
            return -1;
        }
        // Which tree a stump came from, by its prefab name: Beech, Birch, Oak, Pine, Fir, else -1.
        internal static readonly string[] Saplings={"Beech_Sapling","Birch_Sapling","Oak_Sapling","PineTree_Sapling","FirTree_Sapling"};
        internal static readonly string[] Names={"beech","birch","oak","pine","fir"};
        internal static int StumpKind(string name)
        {
            if(string.IsNullOrEmpty(name)||name.IndexOf("stub",StringComparison.OrdinalIgnoreCase)<0)return -1;
            string n=name.ToLowerInvariant();
            if(n.StartsWith("beech"))return 0;
            if(n.StartsWith("birch"))return 1;
            if(n.StartsWith("oak"))return 2;
            if(n.StartsWith("pinetree"))return 3;
            if(n.StartsWith("firtree"))return 4;
            return -1;
        }
    }
}
