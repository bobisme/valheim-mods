using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WrongUniverse
{
    // The laser sword: a copy of the iron sword (so the swings, combos and Swords skill are the game's) with a hilt and a blade of light.
    // The model is the item's "attach" child, which VisEquipment shows on every player holding it; "attach_back" is the bare hilt shown
    // on the back when sheathed. Every saber in anyone's hand is noticed when VisEquipment puts it there (a postfix, so remote players'
    // too) and kept here: the blade grows out on ignition, hums louder and higher the faster its tip moves, and streaks while swinging.
    // Nothing of ours is on the item prefab itself (ItemReload: item prefabs stay plain).
    internal sealed class Saber:Feature
    {
        internal override string Name=>"Saber";

        private static ConfigEntry<float> Slash,Lightning,Chop,Mine,Block;
        private static ConfigEntry<int> Tier;
        private static ConfigEntry<string> BladeColor;

        internal static readonly ItemDef Sword=new ItemDef{Prefab="WU_LaserSword",Name="Laser Sword",Base="SwordIron",
            Description="An elegant weapon from a more civilised universe. A blade of pure light that hums when it moves: it cuts through trolls, trees and copper ore alike, and turns aside almost anything that is swung at you.",
            Cost="Iron:12, SurtlingCore:4, Crystal:4",Station="forge",Level=2,Weight=1.2f,Model=SwordModel,Stats=SwordStats};
        internal override IEnumerable<ItemDef> Items=>new[]{Sword};

        internal override void Bind(ConfigFile c)
        {
            const string S="2 - Laser Sword";
            Slash=c.Bind(S,"SlashDamage",110f,new ConfigDescription("Slash damage of a swing.",new AcceptableValueRange<float>(0,2000)));
            Lightning=c.Bind(S,"LightningDamage",40f,new ConfigDescription("Lightning damage of a swing, on top.",new AcceptableValueRange<float>(0,2000)));
            Chop=c.Bind(S,"ChopDamage",150f,new ConfigDescription("Damage to trees and logs.",new AcceptableValueRange<float>(0,2000)));
            Mine=c.Bind(S,"PickaxeDamage",90f,new ConfigDescription("Damage to rocks and ore.",new AcceptableValueRange<float>(0,2000)));
            Tier=c.Bind(S,"ToolTier",4,new ConfigDescription("The hardest trees and ores it can cut (4 cuts everything a black metal pickaxe or axe can).",new AcceptableValueRange<int>(0,10)));
            Block=c.Bind(S,"BlockPower",90f,new ConfigDescription("Block power.",new AcceptableValueRange<float>(0,1000)));
            BladeColor=c.Bind(S,"BladeColor","Blue",new ConfigDescription("The blade's colour. Needs a restart.",new AcceptableValueList<string>("Blue","Green","Red","Violet","Yellow","White","Cyan")));
        }

        private static Color Colour()
        {
            switch(BladeColor?.Value)
            {
                case "Green":return Look.Green;
                case "Red":return Look.Red;
                case "Violet":return Look.Violet;
                case "Yellow":return new Color(1f,0.85f,0.15f);
                case "White":return new Color(0.9f,0.93f,1f);
                case "Cyan":return Look.Cyan;
                default:return Look.Blue;
            }
        }

        private static void SwordStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_damages=new HitData.DamageTypes{m_slash=Slash.Value,m_lightning=Lightning.Value,m_chop=Chop.Value,m_pickaxe=Mine.Value};
            s.m_toolTier=Tier.Value;
            s.m_blockPower=Block.Value;s.m_deflectionForce=60f;s.m_timedBlockBonus=3f;
            s.m_useDurability=false;
            s.m_attackForce=60f;
        }

        // The hilt in the hand at the origin, the blade out along -Y (as the Meadow golf club's shaft is, in a one-handed weapon's hand).
        private static void SwordModel(Transform root)
        {
            Color col=Colour();
            Hilt(root);
            var b=new Look.Builder(root);
            Look.Builder blade=b.Group("wu_blade",new Vector3(0,-0.15f,0));
            blade.Capsule("core",new Vector3(0,-0.52f,0),0.028f,1.04f,Look.Glow(Color.Lerp(col,Color.white,0.7f)),'y');
            blade.Capsule("halo",new Vector3(0,-0.52f,0),0.07f,1.08f,Look.Glow(col,0.55f),'y');
            Look.AddLight(blade.Root,col,4.5f,1.6f,new Vector3(0,-0.5f,0));
            Transform tip=blade.Point("wu_tip",new Vector3(0,-1.02f,0));
            // the swing streak: drawn only while the tip moves fast (Tick switches it), on everyone's copy
            TrailRenderer trail=tip.gameObject.AddComponent<TrailRenderer>();
            trail.time=0.09f;trail.minVertexDistance=0.04f;trail.widthMultiplier=0.22f;
            trail.widthCurve=new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0));
            trail.startColor=new Color(col.r,col.g,col.b,0.8f);trail.endColor=new Color(col.r,col.g,col.b,0f);
            trail.sharedMaterial=Look.Glow(col);trail.emitting=false;
            trail.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            // the bare hilt for the back, a sibling of attach (the game looks for "attach_back" when the sword is sheathed)
            var back=new GameObject("attach_back"){layer=root.gameObject.layer};
            back.transform.SetParent(root.parent,false);
            back.transform.localPosition=root.localPosition;back.transform.localRotation=root.localRotation;
            Hilt(back.transform);
        }
        private static void Hilt(Transform root)
        {
            var b=new Look.Builder(root);
            Material chrome=Look.Paint(Look.Chrome,0.9f,0.8f),black=Look.Paint(Look.Black,0.3f,0.4f),metal=Look.Paint(Look.Gunmetal,0.8f,0.6f),red=Look.Glow(Look.Red);
            b.Tube("hilt",new Vector3(0,0.02f,0),0.038f,0.26f,chrome,'y');
            for(int i=0;i<5;i++)b.Tube("grip ridge",new Vector3(0,-0.04f+i*0.035f,0),0.043f,0.012f,black,'y');
            b.Tube("emitter",new Vector3(0,-0.125f,0),0.05f,0.05f,metal,'y');
            b.Tube("emitter lip",new Vector3(0,-0.152f,0),0.056f,0.008f,chrome,'y');
            b.Tube("pommel",new Vector3(0,0.165f,0),0.046f,0.03f,black,'y');
            b.Box("switch",new Vector3(0.021f,-0.085f,0),new Vector3(0.01f,0.02f,0.012f),red);
        }

        // ---- every saber in anyone's hand -----------------------------------------------------------------------------

        private sealed class Lit
        {
            internal GameObject Root;internal Transform Blade,Tip;internal TrailRenderer Trail;internal AudioSource Hum;
            internal Vector3 LastTip,LastPos;internal float Born,Speed,NextWhoosh;
        }
        private static readonly List<Lit> Sabers=new List<Lit>();
        private static AudioClip _hum,_ignite,_retract,_whoosh;
        private static readonly AccessTools.FieldRef<VisEquipment,GameObject> RightInstance=AccessTools.FieldRefAccess<VisEquipment,GameObject>("m_rightItemInstance");

        // From the postfix on VisEquipment.SetRightHandEquipped: a new thing in someone's right hand.
        internal static void Equipped(VisEquipment vis)
        {
            GameObject inst=RightInstance(vis);
            if(inst==null)return;
            foreach(Lit s in Sabers)if(s.Root==inst)return;
            Transform blade=Lasers.Find(inst.transform,"wu_blade");
            if(blade==null)return;
            Transform tip=Lasers.Find(blade,"wu_tip");
            var lit=new Lit{Root=inst,Blade=blade,Tip=tip,Trail=tip!=null?tip.GetComponent<TrailRenderer>():null,Born=Time.time,
                LastTip=tip!=null?tip.position:inst.transform.position,LastPos=inst.transform.position};
            lit.Hum=Sound.Loop(Hum(),blade,25f);
            blade.localScale=new Vector3(1f,0.01f,1f);
            Sabers.Add(lit);
            Sound.Play(Ignite(),inst.transform.position,0.7f,1f,25f);
        }

        internal override void Start()
        {
            // a hot reload: sabers already drawn
            foreach(Player p in Player.GetAllPlayers())
            {
                VisEquipment vis=p!=null?p.GetComponent<VisEquipment>():null;
                if(vis!=null)Equipped(vis);
            }
        }
        internal override void Tick()
        {
            float dt=Mathf.Max(0.001f,Time.deltaTime);
            Vector3 cam=GameCamera.instance!=null?GameCamera.instance.transform.position:Vector3.zero;
            for(int i=Sabers.Count-1;i>=0;i--)
            {
                Lit s=Sabers[i];
                if(s.Root==null||!s.Root.activeInHierarchy)
                {
                    if(s.Root==null)Sound.Play(Retract(),s.LastPos,0.6f,1f,25f);   // (put away: the instance is gone)
                    Sabers.RemoveAt(i);continue;
                }
                s.LastPos=s.Root.transform.position;
                float grow=Mathf.Clamp01((Time.time-s.Born)/0.22f);
                if(grow<1f||s.Blade.localScale.y<1f)s.Blade.localScale=new Vector3(1f,Mathf.Max(0.01f,grow*grow*(3f-2f*grow)),1f);
                if(s.Tip==null||(s.LastPos-cam).sqrMagnitude>60f*60f){if(s.Hum!=null)s.Hum.volume=0f;continue;}
                Vector3 tip=s.Tip.position;
                float speed=(tip-s.LastTip).magnitude/dt;
                s.LastTip=tip;
                s.Speed=Mathf.Lerp(s.Speed,Mathf.Min(speed,40f),1f-Mathf.Exp(-dt*18f));
                float k=Mathf.Clamp01(s.Speed/14f);
                if(s.Hum!=null){s.Hum.volume=(0.16f+0.6f*k)*Plugin.Volume.Value;s.Hum.pitch=0.95f+0.3f*k;}
                if(s.Trail!=null)s.Trail.emitting=s.Speed>4.5f;
                if(speed>9f&&Time.time>s.NextWhoosh)
                {
                    s.NextWhoosh=Time.time+0.3f;
                    Sound.Play(Whoosh(),tip,0.5f,UnityEngine.Random.Range(0.9f,1.15f),20f);
                }
            }
        }
        internal override void Stop()
        {
            foreach(Lit s in Sabers)
            {
                if(s.Hum!=null)UnityEngine.Object.Destroy(s.Hum.gameObject);
                if(s.Blade!=null)s.Blade.localScale=Vector3.one;
            }
            Sabers.Clear();
        }

        // ---- sounds -------------------------------------------------------------------------------------------------

        // The hum: a low buzzing chord, 2 s long and whole cycles of every partial, so it loops without a click.
        private static AudioClip Hum()=>_hum!=null?_hum:_hum=Sound.Make("saberhum",2f,t=>
            0.35f*Sound.Saw(t*90f)+0.25f*Sound.Sine(t*91.5f)+0.12f*Sound.Sine(t*180f)+0.06f*Sound.Square(t*45f)+0.04f*Sound.Noise());
        // Ignition: a snap and a rising buzz.
        private static AudioClip Ignite()=>_ignite!=null?_ignite:_ignite=Sound.Make("saberon",0.6f,t=>
        {
            float ph=Sound.SweepPhase(t,40f,95f,0.6f);
            return (0.5f*Sound.Saw(ph)+0.3f*Sound.Sine(ph*2f)+0.6f*Sound.Noise()*Mathf.Exp(-t*30f))*Sound.Env(t,0.005f,0.6f);
        });
        // Put away: the buzz falling into nothing.
        private static AudioClip Retract()=>_retract!=null?_retract:_retract=Sound.Make("saberoff",0.5f,t=>
        {
            float ph=Sound.SweepPhase(t,95f,30f,0.5f);
            return (0.5f*Sound.Saw(ph)+0.3f*Sound.Sine(ph*2f))*Sound.Env(t,0.01f,0.5f);
        });
        // A fast swing: a rising then falling buzz with air in it.
        private static AudioClip Whoosh()=>_whoosh!=null?_whoosh:_whoosh=Sound.Make("saberwhoosh",0.4f,t=>
        {
            float bell=Mathf.Sin(Mathf.PI*Mathf.Clamp01(t/0.4f));
            float ph=Sound.SweepPhase(t,110f,80f,0.4f);
            return (0.45f*Sound.Saw(ph)+0.3f*Sound.Sine(ph*3f)+0.2f*Sound.Noise())*bell*bell;
        });

        // ---- the test command ----------------------------------------------------------------------------------------

        internal override string Command=>"saber";
        internal override string Help=>"info";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            var list=new JArray();
            foreach(Lit s in Sabers)
                if(s.Root!=null)list.Add(new JObject{["at"]=Tools.V(s.Root.transform.position),["blade"]=Math.Round(s.Blade.localScale.y,2),["tipSpeed"]=Math.Round(s.Speed,1),
                    ["hum"]=s.Hum!=null?Math.Round(s.Hum.volume,2):-1,["trail"]=s.Trail!=null&&s.Trail.emitting});
            output(new JObject{["sabers"]=list,["color"]=BladeColor.Value});
            return null;
        }
    }

    [HarmonyPatch(typeof(VisEquipment),"SetRightHandEquipped")]
    internal static class SaberDrawn
    {
        private static void Postfix(VisEquipment __instance,bool __result)
        {
            if(!__result)return;
            try{Saber.Equipped(__instance);}catch(Exception e){Plugin.Warn("Laser sword: "+e.Message);}
        }
    }
}
