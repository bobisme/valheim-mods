using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Factorheim
{
    // What lies under a drill, round a woodcutter, beside a pumpjack: read from the game's own drop tables (what you'd get by hitting it),
    // never changed. Deposits are not used up in 0.1.
    internal static class Deposits
    {
        // Minerals a drill can bring up, best first: when a rock drops several things (copper and stone) the drill gets the better one.
        internal static readonly string[] Minerals=
        {
            "FlametalOreNew","FlametalOre","Softtissue","BlackMarble","SilverOre","Obsidian","IronScrap","CopperScrap","TinOre","CopperOre",
            "GemstoneRed","GemstoneBlue","GemstoneGreen","Crystal","Flint","Stone",
        };
        internal static readonly HashSet<string> Woods=new HashSet<string>{"Wood","FineWood","RoundLog","ElderBark","YggdrasilWood","Blackwood"};
        private static readonly Collider[] Hits=new Collider[64];

        // The thing a drill at `at` would mine, and what it is ("copper deposit"); null when there's nothing but plain ground.
        internal static string Mineral(Vector3 at,float radius,out string what)
        {
            what=null;string best=null;int bestRank=int.MaxValue;
            int n=Physics.OverlapSphereNonAlloc(at,radius,Hits,~0,QueryTriggerInteraction.Ignore);
            var seen=new HashSet<Component>();
            for(int i=0;i<n;i++)
            {
                Collider c=Hits[i];if(c==null)continue;
                Component source;List<string> drops=Drops(c,out source);
                if(source==null||!seen.Add(source)||drops==null)continue;
                foreach(string d in drops)
                {
                    int rank=System.Array.IndexOf(Minerals,d);
                    if(rank<0||rank>=bestRank)continue;
                    best=d;bestRank=rank;what=Localize(source);
                }
            }
            return best;
        }
        // Plain ground or rock with nothing to mine: stone (a little slower, see Drill).
        internal static bool Ground(Vector3 at)=>ZoneSystem.instance!=null&&ZoneSystem.instance.GetGroundHeight(at,out float h)&&Mathf.Abs(h-at.y)<1.5f;

        // Standing trees around a woodcutter: each one's wood (by its drop table) once per tree.
        internal static List<string> Trees(Vector3 at,float radius)
        {
            var woods=new List<string>();var seen=new HashSet<TreeBase>();
            int n=Physics.OverlapSphereNonAlloc(at,radius,Hits,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)
            {
                TreeBase t=Hits[i]!=null?Hits[i].GetComponentInParent<TreeBase>():null;
                if(t==null||!seen.Add(t)||t.m_dropWhenDestroyed==null)continue;
                // (the best wood it gives: birch and oak give fine wood, pines round logs (core wood), the rest plain wood)
                string wood=t.m_dropWhenDestroyed.m_drops.Where(d=>d.m_item!=null&&Woods.Contains(d.m_item.name)).Select(d=>d.m_item.name)
                    .OrderBy(w=>w=="Wood"?1:0).FirstOrDefault();
                if(wood!=null)woods.Add(wood);
            }
            return woods;
        }

        // A tar pit's liquid near a pumpjack.
        internal static bool Tar(Vector3 at,float radius)
        {
            int n=Physics.OverlapSphereNonAlloc(at,radius,Hits,~0,QueryTriggerInteraction.Collide);
            for(int i=0;i<n;i++)
            {
                if(Hits[i]==null)continue;
                LiquidVolume l=Hits[i].GetComponentInParent<LiquidVolume>();
                if(l!=null&&l.m_liquidType==LiquidType.Tar)return true;
                if(Hits[i].GetComponentInParent<LiquidSurface>() is LiquidSurface s&&s.GetLiquidType()==LiquidType.Tar)return true;
            }
            return false;
        }

        // Everything mineable round a point (fh prod deposits).
        internal static List<(string what,string drops,float distance)> Near(Vector3 at,float radius)
        {
            var list=new List<(string,string,float)>();var seen=new HashSet<Component>();
            int n=Physics.OverlapSphereNonAlloc(at,radius,Hits,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)
            {
                Collider c=Hits[i];if(c==null)continue;
                List<string> drops=Drops(c,out Component source);
                if(source==null||!seen.Add(source)||drops==null||!drops.Any(d=>Minerals.Contains(d)))continue;
                list.Add((Localize(source),string.Join(", ",drops.Distinct()),Vector3.Distance(at,c.ClosestPoint(at))));
            }
            return list.OrderBy(x=>x.Item3).ToList();
        }

        private static List<string> Drops(Collider c,out Component source)
        {
            source=null;
            if(c.GetComponentInParent<TreeBase>()!=null||c.GetComponentInParent<TreeLog>()!=null||c.GetComponentInParent<Piece>()!=null)return null;
            if(c.GetComponentInParent<MineRock5>() is MineRock5 r5){source=r5;return Items(r5.m_dropItems);}
            if(c.GetComponentInParent<MineRock>() is MineRock r){source=r;return Items(r.m_dropItems);}
            if(c.GetComponentInParent<DropOnDestroyed>() is DropOnDestroyed d&&d.GetComponent<Destructible>()!=null){source=d;return Items(d.m_dropWhenDestroyed);}
            return null;
        }
        private static List<string> Items(DropTable t)=>t?.m_drops?.Where(d=>d.m_item!=null).Select(d=>d.m_item.name).ToList();
        private static string Localize(Component c)
        {
            HoverText h=c.GetComponentInParent<HoverText>();
            string n=h!=null?h.m_text:c.GetComponentInParent<MineRock5>()?.m_name??c.GetComponentInParent<MineRock>()?.m_name??c.gameObject.name.Replace("(Clone)","");
            return Localization.instance!=null?Localization.instance.Localize(n):n;
        }
    }
}
