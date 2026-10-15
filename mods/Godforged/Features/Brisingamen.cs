using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Godforged
{
    // Brísingamen, Freyja's necklace, made by four dwarves (the Brísings): carried (the game has no necklace slot), when its bearer is
    // healed, by a mead, a healing effect, anyone's magic, friends nearby are healed too, half as much; everyone sees a golden glint on
    // each. Not the steady regeneration from food (that would be a constant trickle), and never a heal that came from someone else's
    // Brísingamen or across the network (no echo between two bearers).
    internal sealed class Brisingamen:Feature
    {
        internal override string Name=>"Brísingamen";
        internal static readonly ItemDef Necklace=new ItemDef{Prefab="GF_Brisingamen",Name="Brísingamen",Base="SilverNecklace",
            Description="Freyja's necklace, made by the four Brísings. Carry it, and when you are healed, your companions near you are healed too.",
            Cost="Silver:8, Amber:10, AmberPearl:4, Ruby:2",Station="forge",Level=3,Weight=0.4f,Model=t=>Meshes.AddAll(t,"brisingamen")};
        internal override IEnumerable<ItemDef> Items=>new[]{Necklace};

        private const string GlintRpc="GF_Glint";
        private static ConfigEntry<float> _share,_range;
        internal static bool InRegen;

        internal override void Bind(ConfigFile c)
        {
            const string s="5 - Brísingamen";
            _share=c.Bind(s,"Share",0.5f,new ConfigDescription("How much of a heal friends nearby get.",new AcceptableValueRange<float>(0f,1f)));
            _range=c.Bind(s,"Range",20f,"How near, in metres.");
        }
        internal override void Start()=>Net.Register(GlintRpc,(sender,pkg)=>{int n=pkg.ReadInt();for(int i=0;i<n;i++)Glint(pkg.ReadVector3());});

        internal static void Shared(Player p,float hp)
        {
            if(_share==null||_share.Value<=0f||!p.GetInventory().GetAllItems().Exists(Necklace.Is))return;
            float amount=hp*_share.Value;
            var at=new List<Vector3>();
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c==p||c.IsDead()||!(c is Player||c.IsTamed())||!Hits.Friendly(p,c))continue;
                if(Vector3.Distance(c.transform.position,p.transform.position)>_range.Value)continue;
                if(c.GetHealth()>=c.GetMaxHealth())continue;
                c.Heal(amount,true);   // (to its owner's game: arrives there as a network heal, which is never shared again)
                at.Add(c.GetCenterPoint());
            }
            if(at.Count==0)return;
            at.Add(p.GetCenterPoint());
            foreach(Vector3 v in at)Glint(v);
            var pkg=new ZPackage();pkg.Write(at.Count);foreach(Vector3 v in at)pkg.Write(v);
            Net.Others(GlintRpc,pkg);
        }

        private static void Glint(Vector3 at)=>new GameObject("GF_Glint").AddComponent<GlintFx>().transform.position=at;
    }

    // A golden glow that swells and fades in two thirds of a second.
    internal sealed class GlintFx:MonoBehaviour
    {
        private Light _l;private float _t;
        private void Start()
        {
            _l=gameObject.AddComponent<Light>();
            _l.type=LightType.Point;_l.color=new Color(1f,0.75f,0.3f);_l.range=4f;_l.shadows=LightShadows.None;_l.intensity=0f;
        }
        private void Update()
        {
            _t+=Time.deltaTime;
            if(_t>=0.66f){Destroy(gameObject);return;}
            _l.intensity=3.5f*Mathf.Sin(Mathf.PI*_t/0.66f);
        }
    }

    // Food regeneration heals through Character.Heal too: marked while it runs, so it isn't shared.
    [HarmonyPatch(typeof(Player),"UpdateFood")]
    internal static class BrisingamenRegen
    {
        private static void Prefix()=>Brisingamen.InRegen=true;
        private static void Finalizer()=>Brisingamen.InRegen=false;
    }
    // Heals land in RPC_Heal on the healed character's own game: sender 0 is a heal from this game itself (a mead, an effect), anything
    // else came over the network (someone's staff, someone's Brísingamen).
    [HarmonyPatch(typeof(Character),"RPC_Heal")]
    internal static class BrisingamenHeal
    {
        private static void Prefix(Character __instance,long sender,float hp)
        {
            if(sender!=0L||Brisingamen.InRegen||hp<=0f)return;
            if(!(__instance is Player p)||p!=Player.m_localPlayer||p.IsDead())return;
            Plugin.Safe("Brísingamen",()=>Brisingamen.Shared(p,hp));
        }
    }
}
