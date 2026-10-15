using System.Collections.Generic;
using UnityEngine;

namespace Factorheim
{
    // The five rune packs (Factorio's science packs): stone tablets cut with a glowing rune, framed in the metal of their biome. A lab
    // reads them up one at a time.
    internal static class Packs
    {
        private sealed class Kind
        {
            internal string Prefab,Name,Text,Cost,Station,Rune;internal int Tier,Amount=1;internal Color Glow,Frame;
        }
        // Rune strokes on a 1 x 1.4 tablet face: (x, z) of the stroke's middle, its length, its turn (degrees).
        private static readonly Dictionary<string,(float x,float z,float len,float yaw)[]> Runes=new Dictionary<string,(float,float,float,float)[]>
        {
            {"fehu",new[]{(-0.18f,0f,1.1f,0f),(0.04f,0.3f,0.5f,-50f),(0.04f,0.02f,0.5f,-50f)}},
            {"uruz",new[]{(-0.25f,0f,1.1f,0f),(0.25f,-0.12f,0.85f,0f),(0f,0.4f,0.6f,-60f)}},
            {"thurisaz",new[]{(-0.2f,0f,1.1f,0f),(0.02f,0.12f,0.42f,-55f),(0.02f,-0.12f,0.42f,55f)}},
            {"ansuz",new[]{(-0.18f,0f,1.1f,0f),(0.04f,0.36f,0.5f,-50f),(0.04f,0.08f,0.5f,-50f)}},
            {"raidho",new[]{(-0.2f,0f,1.1f,0f),(0.02f,0.3f,0.42f,-55f),(0.02f,0.08f,0.42f,55f),(0.02f,-0.22f,0.55f,-45f)}},
        };
        private static readonly Kind[] Kinds=
        {
            new Kind{Prefab="FH_SciRed",Name="Bronze rune",Rune="fehu",Tier=0,Cost="Bronze:1, Wood:2",Station="piece_workbench",Glow=new Color(1f,0.25f,0.15f),Frame=new Color(0.72f,0.45f,0.2f),
                Text="A stone tablet cut with Fehu, the rune of wealth won by work, in a bronze frame. A rune lab reads it to research Automation."},
            new Kind{Prefab="FH_SciGreen",Name="Iron rune",Rune="uruz",Tier=1,Cost="FH_IronGear:1, FH_CopperWire:3",Station="forge",Glow=new Color(0.3f,1f,0.35f),Frame=new Color(0.45f,0.46f,0.48f),
                Text="A stone tablet cut with Uruz, the rune of strength, in an iron frame. Rune labs read it (with bronze runes) to research Logistics."},
            new Kind{Prefab="FH_SciBlue",Name="Silver rune",Rune="thurisaz",Tier=2,Cost="",Station="forge",Glow=new Color(0.25f,0.6f,1f),Frame=new Color(0.82f,0.85f,0.9f),Amount=2,
                Text="A stone tablet cut with Thurisaz, the thorn, in a silver frame. Made in an assembler. Rune labs read it to research Tar processing."},
            new Kind{Prefab="FH_SciPurple",Name="Eitr rune",Rune="ansuz",Tier=3,Cost="",Station="forge",Glow=new Color(0.75f,0.3f,1f),Frame=new Color(0.25f,0.2f,0.35f),Amount=3,
                Text="A stone tablet cut with Ansuz, the rune of the gods' breath, steeped in eitr. Made in an assembler. Rune labs read it to research the Eitr works."},
            new Kind{Prefab="FH_SciYellow",Name="Flametal rune",Rune="raidho",Tier=4,Cost="",Station="forge",Glow=new Color(1f,0.85f,0.2f),Frame=new Color(0.9f,0.45f,0.12f),Amount=3,
                Text="A stone tablet cut with Raidho, the rune of the journey, in a flametal frame. Made in an assembler. The last research: the road to Valhalla."},
        };

        internal static IEnumerable<ItemDef> Items()
        {
            foreach(Kind k in Kinds)
            {
                Kind kind=k;
                yield return new ItemDef
                {
                    Prefab=k.Prefab,Name=k.Name,Description=k.Text,Base="Coal",Cost=k.Cost,Station=k.Station,Level=1,Tier=k.Tier,Amount=1,
                    Weight=0.3f,Stack=100,Model=t=>Model(t,kind),IconLook=Quaternion.Euler(70f,0f,0f),
                };
            }
        }

        // The assembler's way to make each (DESIGN.md: blue, purple and yellow are assembler-only).
        internal static IEnumerable<Craft> Crafts()
        {
            yield return Craft.Of("sci_red","assembler",5f,0,new[]{("Bronze",1),("Wood",2)},("FH_SciRed",1));
            yield return Craft.Of("sci_green","assembler",6f,1,new[]{("FH_IronGear",1),("FH_CopperWire",3)},("FH_SciGreen",1));
            yield return Craft.Of("sci_blue","assembler",12f,2,new[]{("FH_Engine",1),("FH_SilverCircuit",1),("Silver",1)},("FH_SciBlue",2));
            yield return Craft.Of("sci_purple","assembler",20f,3,new[]{("FH_EitrUnit",1),("Eitr",2),("FH_IronGear",4)},("FH_SciPurple",3));
            // (FlametalNew: the Ashlands' flametal; the game's old "Flametal" can't be found any more)
            yield return Craft.Of("sci_yellow","assembler",25f,4,new[]{("FH_LowDensity",1),("FH_EitrUnit",1),("FlametalNew",1)},("FH_SciYellow",3));
        }

        internal static Color GlowOf(string pack)
        {
            foreach(Kind k in Kinds)if(k.Prefab==pack)return k.Glow;
            return Color.white;
        }

        // A tablet lying flat: 0.18 x 0.25 m, 3 cm thick, a raised frame round it and the rune glowing in its face.
        private static void Model(Transform t,Kind k)
        {
            var b=new Look.Builder(t);
            Material stone=Look.Paint(new Color(0.42f,0.41f,0.39f),0f,0.15f),frame=Look.Paint(k.Frame,0.85f,0.55f),glow=Look.Paint(k.Glow,0f,0.4f,3f);
            const float w=0.18f,l=0.25f,h=0.03f;
            b.Box("slab",new Vector3(0,h/2,0),new Vector3(w,h,l),stone);
            b.Box("frame n",new Vector3(0,h*0.6f,l/2),new Vector3(w+0.016f,h*1.2f,0.016f),frame);
            b.Box("frame s",new Vector3(0,h*0.6f,-l/2),new Vector3(w+0.016f,h*1.2f,0.016f),frame);
            b.Box("frame e",new Vector3(w/2,h*0.6f,0),new Vector3(0.016f,h*1.2f,l),frame);
            b.Box("frame w",new Vector3(-w/2,h*0.6f,0),new Vector3(0.016f,h*1.2f,l),frame);
            const float s=0.13f;   // (the rune's height on the face)
            foreach(var (x,z,len,yaw) in Runes[k.Rune])
                b.Box("rune",new Vector3(x*s,h+0.002f,z*s),new Vector3(0.014f,0.006f,len*s),glow,new Vector3(0,yaw,0));
        }
    }
}
