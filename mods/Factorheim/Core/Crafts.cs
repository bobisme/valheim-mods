using System;
using System.Collections.Generic;
using System.Linq;

namespace Factorheim
{
    // A machine recipe: what a kind of machine turns into what, how long it takes, and the research tier that unlocks it. Features list
    // theirs (Feature.Crafts); machines find theirs by Kind. Hand recipes (at the workbench or forge) are ItemDef costs, not these.
    internal sealed class Craft
    {
        internal string Id;                       // unique: "gear", "sci_red", "rocket_part"
        internal string Kind="assembler";         // which machines make it: "assembler", "chemical", "silo"...
        internal (string item,int n)[] In,Out;
        internal float Seconds=1f;                // at speed 1 (a basic assembler)
        internal int Tier;
        internal string Title=>Out!=null&&Out.Length>0?Ports.Title(Out[0].item)+(Out[0].n>1?" x"+Out[0].n:""):Id;
        internal bool Uses(string item)=>In.Any(i=>i.item==item);
        internal int Needs(string item)=>In.Where(i=>i.item==item).Sum(i=>i.n);

        internal static Craft Of(string id,string kind,float seconds,int tier,(string,int)[] input,params (string,int)[] output)=>
            new Craft{Id=id,Kind=kind,Seconds=seconds,Tier=tier,In=input,Out=output};
    }

    internal static class Crafts
    {
        internal static IEnumerable<Craft> All=>Features.All.SelectMany(f=>f.Crafts);
        internal static Craft Find(string id)=>All.FirstOrDefault(c=>c.Id==id);
        internal static IEnumerable<Craft> For(string kind)=>All.Where(c=>c.Kind==kind);
        internal static IEnumerable<Craft> Unlocked(string kind)=>For(kind).Where(c=>Tech.Unlocked(c.Tier));
    }
}
