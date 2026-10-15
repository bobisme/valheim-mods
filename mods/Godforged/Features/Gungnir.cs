using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;

namespace Godforged
{
    // Gungnir, Odin's spear, made by the sons of Ivaldi: a spear (a copy of the Carapace Spear, so the game's spear thrusts, throw and skill).
    // Thrown, it picks the foe nearest the crosshair and turns in flight to reach it (it never misses), then comes back to the hand (Throws).
    internal sealed class Gungnir:Feature
    {
        internal override string Name=>"Gungnir";
        internal static readonly ItemDef Spear=new ItemDef{Prefab="GF_Gungnir",Name="Gungnir",Base="SpearCarapace",HandEuler=new Vector3(-90,0,0),
            Description="Odin's spear, made by the sons of Ivaldi. Thrown, it finds the foe you look at, whatever it has to do to get there, and comes back to the hand that threw it.",
            Cost="YggdrasilWood:20, BlackMetal:16, Eitr:8, Feathers:10",Station="blackforge",Level=1,Weight=3f,
            Model=t=>Meshes.AddAll(t,"gungnir"),Stats=SpearStats,Returns=true,SeekCone=10f,SeekRange=90f};
        internal override IEnumerable<ItemDef> Items=>new[]{Spear};

        internal override void Register(ZNetScene scene)=>Throws.Projectile(scene,Spear,"SpearCarapace",t=>Meshes.AddAll(t,"gungnir"),sound:"wind");

        // In flight: wind (0.2.48). White noise sounded raspy however it was filtered, so these start from pink noise (softer at the top,
        // as real wind is), and four ways to shape it can be tried (gf sound wind1..4; the WindSound setting picks the thrown one's):
        //   1 airy: a band near 2.5 kHz drifting 1.8-3.2 kHz;  2 sweep: a band rising 600 Hz to 2.4 kHz and falling, once a loop;
        //   3 breath: pink noise rolled off above 4 kHz;  4 layered: a low pink body and a quiet smooth air layer near 5 kHz;
        //   5 (the default) 4 whooshing: its cutoff opening 600 Hz to 2.4 kHz and its level swelling, once a loop;  6 the same, gentler.
        // Each loops clean in two seconds (every sweep is a whole cycle), all at about the same, quieter level.
        private static ConfigEntry<int> _wind;
        internal override void Bind(ConfigFile c)=>_wind=c.Bind("3 - Gungnir","WindSound",5,new ConfigDescription("Which wind a thrown Gungnir makes (1 airy, 2 sweep, 3 breath, 4 layered, 5 layered with a whoosh, 6 a gentler whoosh: gf sound wind1..6 plays each).",new AcceptableValueRange<int>(1,6)));
        internal static AudioClip Wind()=>Wind(_wind!=null?_wind.Value:5);
        internal static AudioClip Wind(int which)
        {
            float b0=0,b1=0,b2=0,b3=0,b4=0,b5=0,b6=0;            // (Paul Kellet's pink noise filter)
            float lo=0,band=0,lo2=0,band2=0,o1=0,o2=0;            // (state-variable filters, and smoothing)
            float Pink()
            {
                float w=Sound.Noise();
                b0=0.99886f*b0+w*0.0555179f;b1=0.99332f*b1+w*0.0750759f;b2=0.96900f*b2+w*0.1538520f;b3=0.86650f*b3+w*0.3104856f;
                b4=0.55000f*b4+w*0.5329522f;b5=-0.7616f*b5-w*0.0168980f;float p=b0+b1+b2+b3+b4+b5+b6+w*0.5362f;b6=w*0.115926f;
                return p*0.11f;
            }
            float Band(float x,float hz,float q,ref float l,ref float bp)   // (Chamberlin state-variable band-pass)
            {
                float f=2f*Mathf.Sin(Mathf.PI*Mathf.Min(hz,8000f)/Sound.Rate);
                l+=f*bp;float h=x-l-q*bp;bp+=f*h;return bp;
            }
            return Sound.Make("gungnir wind "+which,2f,t=>
            {
                float s=Mathf.Sin(Mathf.PI*t),drift=0.85f+0.15f*s;   // (one slow cycle per loop)
                float p=Pink(),v;
                switch(which)
                {
                    case 1: v=Band(p,2500f+700f*s,0.9f,ref lo,ref band)*2.95f;break;
                    case 2: v=Band(p,1500f-900f*Mathf.Cos(Mathf.PI*t),1.1f,ref lo,ref band)*3.6f;break;
                    case 4: o1+=(p-o1)*0.12f;v=o1*1.5f+Band(p,5000f,1.4f,ref lo2,ref band2)*0.5f;break;
                    case 5: case 6:   // 4's wind, whooshing as 2 did: its body opens up and swells, and its air with it, once a loop
                    {
                        float sw=0.5f-0.5f*Mathf.Cos(Mathf.PI*t);              // (0 to 1 and back over the loop)
                        float depth=which==5?1f:0.55f;
                        float hz=600f+1800f*sw*depth,k=1f-Mathf.Exp(-2f*Mathf.PI*hz/Sound.Rate);
                        o1+=(p-o1)*k;o2+=(o1-o2)*k;                            // (two poles: a soft rolling cutoff)
                        float swell=(1f-0.45f*depth)+0.45f*depth*sw;
                        v=(o2*1.5f+Band(p,5000f,1.4f,ref lo2,ref band2)*0.5f*(0.4f+0.6f*sw))*swell*(which==5?1.33f:1.13f);   // (as loud as 4)
                        break;
                    }
                    default: o1+=(p-o1)*0.45f;o2+=(o1-o2)*0.45f;v=o2*1.4f;break;   // (two gentle poles near 4 kHz)
                }
                return v*drift*0.55f;
            });
        }

        private static void SpearStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_damages=new HitData.DamageTypes{m_pierce=120f,m_spirit=30f};
            s.m_maxDurability=600f;s.m_durabilityPerLevel=0f;
        }
    }
}
