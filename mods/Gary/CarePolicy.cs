using System;
using System.Collections.Generic;
using System.Linq;

namespace Gary
{
    // Pure rules for Gary's caretaking: his seed pouch and carrying basket, what the trees give him, how he feels about omens and
    // trophies, and when each chore is due. No Unity or game types.
    internal static class CarePolicy
    {
        // ---- the seed pouch: one count per tree kind, in ReforestPolicy.Saplings order ----
        internal static readonly string[] Seeds={"BeechSeeds","BirchSeeds","Acorn","PineCone","FirCone"};
        internal const int SeedCapacity=10;
        internal static int SeedKind(string prefab)=>Array.IndexOf(Seeds,prefab);

        // ---- the basket: what he carries back for you, "prefab:count" pairs ----
        internal const int BasketCapacity=30;
        internal static Dictionary<string,int> Basket(string saved)
        {
            var basket=new Dictionary<string,int>();
            foreach(string part in (saved??"").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
            {
                int colon=part.LastIndexOf(':');
                if(colon<=0||!int.TryParse(part.Substring(colon+1),out int count)||count<=0)continue;
                string name=part.Substring(0,colon);
                if(name.IndexOfAny(new[]{';',':'})>=0)continue;
                basket[name]=basket.TryGetValue(name,out int had)?Math.Min(BasketCapacity,had+count):Math.Min(BasketCapacity,count);
            }
            return basket;
        }
        internal static string Save(Dictionary<string,int> basket)=>string.Join(";",basket.Where(kv=>kv.Value>0&&!string.IsNullOrEmpty(kv.Key)).OrderBy(kv=>kv.Key,StringComparer.Ordinal).Select(kv=>kv.Key+":"+kv.Value));
        internal static int Count(Dictionary<string,int> basket)=>basket.Values.Sum();
        // He comes back with it once it is heavy enough, or once he has carried it a while.
        internal static bool Deliver(int count,double carriedSeconds)=>count>0&&(count>=12||carriedSeconds>=40);

        // ---- deadfall and the harvest: what he picks up off the ground near home ----
        internal static readonly HashSet<string> Deadfall=new HashSet<string>{"Pickable_Branch","Pickable_Stone"};
        internal static readonly HashSet<string> Harvest=new HashSet<string>{"Carrot","Turnip","Onion","Barley","Flax","CarrotSeeds","TurnipSeeds","OnionSeeds",
            "Wood","Stone","Resin","Feathers","Raspberry","Blueberries","Mushroom","Cloudberry"};

        // ---- talking to trees ----
        internal enum TreeGift{None,Resin,Seed,Feather}
        internal static TreeGift Listen(double roll)
        {
            double r=double.IsNaN(roll)?1:roll;
            return r<0.35?TreeGift.Resin:r<0.6?TreeGift.Seed:r<0.7?TreeGift.Feather:TreeGift.None;
        }
        // Which tree a tree is, by its prefab name, in ReforestPolicy.Saplings order.
        internal static int TreeKind(string name)
        {
            if(string.IsNullOrEmpty(name))return -1;
            string n=name.ToLowerInvariant();
            if(n.Contains("stub")||n.Contains("log")||n.Contains("sapling"))return -1;
            if(n.StartsWith("beech"))return 0;
            if(n.StartsWith("birch"))return 1;
            if(n.StartsWith("oak"))return 2;
            if(n.StartsWith("pinetree"))return 3;
            if(n.StartsWith("firtree"))return 4;
            return -1;
        }

        // ---- how he feels about the omens (Omens' own kind numbers; that mod is not referenced) ----
        private static readonly HashSet<int> GoodOmens=new HashSet<int>{1,5,7,8,9,10,17,18};
        internal static int OmenFeeling(int kind)=>kind<0||kind>40?0:GoodOmens.Contains(kind)?1:-1; // 1 happy, -1 uneasy

        // ---- how he feels about a trophy ----
        internal enum Feeling{Admire,Growl,Sad}
        internal static Feeling Trophy(string prefab)
        {
            string n=(prefab??"").ToLowerInvariant();
            if(n.Contains("troll"))return Feeling.Growl;
            if(n.Contains("greydwarf")||n.Contains("greyling"))return Feeling.Sad; // one of his own
            return Feeling.Admire;
        }

        // ---- cadence ----
        internal static bool Due(double now,double next)=>!(now<next);
    }
}
