using System;

namespace Gary
{
    // Pure rules for Gary's tree planting: when he may go looking, and which spots near your bed are fair game.
    internal static class ReforestPolicy
    {
        internal const double MinFromBed=12,MinFromBuilding=8,PlantSeconds=2.5,GiveUpSeconds=20;
        // Time to look: planting on, you near your bed, nothing else on his mind, his last tree long enough ago, and not too many
        // of his saplings already growing around home.
        internal static bool Ready(bool enabled,double fromBed,double radius,double sinceLast,double interval,int saplings,int maxSaplings)=>
            enabled&&!double.IsNaN(fromBed)&&fromBed<=radius&&!(sinceLast<interval)&&saplings<maxSaplings;
        // A fair spot: around home but not in it, clear of buildings, on wild ground (not farmland or paths), dry and open to the sky.
        internal static bool Spot(double fromBed,double radius,double fromBuilding,bool inBase,bool cultivated,bool cleared,bool roofed,bool crowded,bool water)=>
            !double.IsNaN(fromBed)&&fromBed>=MinFromBed&&fromBed<=radius&&!(fromBuilding<MinFromBuilding)&&!inBase&&!cultivated&&!cleared&&!roofed&&!crowded&&!water;
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
