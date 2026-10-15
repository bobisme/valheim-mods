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

        internal override void Register(ZNetScene scene)=>Throws.Projectile(scene,Spear,"SpearCarapace",t=>Meshes.AddAll(t,"gungnir"),sound:Wind);

        // In flight: a sharp rushing wind, rising and falling twice a second, with a thin whistle over it. Loops clean in two seconds.
        internal static AudioClip Wind()
        {
            float lo=0f,lo2=0f;
            return Sound.Make("gungnir wind",2f,t=>
            {
                float n=Sound.Noise();
                float sweep=0.5f+0.5f*Mathf.Sin(2f*Mathf.PI*2f*t);   // (how bright the rush is)
                lo+=(n-lo)*(0.15f+0.5f*sweep);lo2+=(lo-lo2)*0.05f;
                float rush=(lo-lo2)*(0.6f+0.6f*sweep);   // (band-passed: no rumble, no hiss)
                float whistle=Sound.Sine((1600f+400f*sweep)*t)*0.06f*sweep;
                return rush*1.4f+whistle;
            });
        }

        private static void SpearStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_damages=new HitData.DamageTypes{m_pierce=120f,m_spirit=30f};
            s.m_maxDurability=600f;s.m_durabilityPerLevel=0f;
        }
    }
}
