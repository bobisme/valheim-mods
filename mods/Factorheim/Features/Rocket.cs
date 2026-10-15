using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Factorheim
{
    // The end of the tree: the rocket silo (tier 5), the parts it builds from low density structures, rocket control units and rocket fuel,
    // and the launch: the player who boards rides it through the clouds to Valhalla (Flight), and everyone around watches it go (Silo).
    // Coming back: the Einherjar's horn, a world global key "fh_valhalla", and an empty silo ready for the next rocket.
    internal sealed class Rocket:Feature
    {
        internal override string Name=>"Rocket";
        internal static ConfigEntry<int> PartsNeeded;
        internal const string Trophy="FH_ValhallaTrophy";

        internal override void Bind(ConfigFile config)
        {
            PartsNeeded=config.Bind("5 - Rocket","Rocket parts needed",100,new ConfigDescription("Rocket parts the silo builds before the rocket can launch (Factorio: 100).",
                new AcceptableValueRange<int>(1,1000)));
        }

        // ---- items ----
        private static readonly ItemDef[] Defs=
        {
            new ItemDef{Prefab="FH_RocketFuel",Name="Rocket fuel",Base="Coal",Cost="",Weight=2f,Stack=20,Tier=4,
                Description="Solid fuel soaked in eitr and sealed in an iron cask. It wants very badly to go up.",Model=FuelModel},
            new ItemDef{Prefab="FH_LowDensity",Name="Low density structure",Base="Iron",Cost="",Weight=1f,Stack=20,Tier=5,
                Description="A flametal lattice skinned in copper and black resin: as strong as a longship's keel and light as a feather.",Model=LatticeModel},
            new ItemDef{Prefab="FH_RocketControl",Name="Rocket control unit",Base="Iron",Cost="",Weight=1f,Stack=20,Tier=5,
                Description="Rune circuits that know where Valhalla is. Nobody is sure how.",Model=ControlModel},
            new ItemDef{Prefab="FH_RocketPart",Name="Rocket part",Base="Iron",Cost="",Weight=10f,Stack=10,Tier=5,
                Description="One section of a rocket's hull. The silo builds them; you shouldn't need to carry one.",Model=PartModel},
            new ItemDef{Prefab=Trophy,Name="Einherjar's horn",Base="TrophyDeer",Cost="",Weight=1f,Stack=1,
                Description="Poured for you in Odin's hall, the day you rode fire to Valhalla. It's still full.",Model=HornModel,
                IconLook=Quaternion.Euler(0,90,0)},
        };
        internal override IEnumerable<ItemDef> Items=>Defs;

        internal override IEnumerable<Craft> Crafts=>new[]
        {
            Craft.Of("rocket_fuel","chemical",15f,4,new[]{("FH_SolidFuel",5),("Eitr",1)},("FH_RocketFuel",1)),
            Craft.Of("low_density","assembler",15f,5,new[]{("FlametalNew",1),("Copper",5),("FH_Plastic",5)},("FH_LowDensity",1)),
            Craft.Of("rocket_control","assembler",15f,5,new[]{("FH_EitrUnit",1),("FH_SilverCircuit",1)},("FH_RocketControl",1)),
            Craft.Of("rocket_part","silo",3f,5,new[]{("FH_LowDensity",1),("FH_RocketControl",1),("FH_RocketFuel",1)},("FH_RocketPart",1)),
        };

        // ---- the silo ----
        private static readonly PieceDef SiloDef=new PieceDef
        {
            Prefab="FH_RocketSilo",Name="Rocket silo",Tier=5,Station="blackforge",Health=8000f,BoxCollider=false,
            Cost="Stone:150, BlackMetal:50, FlametalNew:30, FH_EitrUnit:10",
            Description="A launch pad for one rocket. Feed it low density structures, rocket control units and rocket fuel (4 MW): it builds the "+
                "rocket part by part. When the rocket stands complete, board it.",
            Model=SiloModel,Setup=SiloSetup,IconView=new Vector3(0.6f,0.35f,-1f),
        };
        internal override IEnumerable<PieceDef> Pieces=>new[]{SiloDef};

        private static void SiloModel(Transform root)
        {
            Meshes.Add(root,"rocket","pad");
            var rocket=new GameObject("Rocket"){layer=root.gameObject.layer};
            rocket.transform.SetParent(root,false);
            for(int i=0;i<Silo.Sections;i++)Meshes.Add(rocket.transform,"rocket","sec"+i);
        }
        private static void SiloSetup(GameObject go)
        {
            int layer=go.layer;
            BoxCollider Box(Transform on,Vector3 center,Vector3 size){var b=on.gameObject.AddComponent<BoxCollider>();b.center=center;b.size=size;return b;}
            // the pad (its top is 0.8 m up), the steps at the front, the gantry at the back
            var pad=new GameObject("padcollider"){layer=layer};pad.transform.SetParent(go.transform,false);
            Box(pad.transform,new Vector3(0,0.4f,0),new Vector3(9,0.8f,9));
            Box(pad.transform,new Vector3(0,0.4f,5.1f),new Vector3(3,0.5f,1.2f));
            Box(pad.transform,new Vector3(0,11.4f,-3.4f),new Vector3(1.6f,21.2f,1.6f));
            // the rocket's own, which flies with it
            Transform rocket=go.transform.Find("Model/Rocket");
            if(rocket!=null)
            {
                var cap=rocket.gameObject.AddComponent<CapsuleCollider>();
                cap.direction=1;cap.radius=1.6f;cap.height=22f;cap.center=new Vector3(0,12f,0);
            }
            SnapPoints.Box(go,new Vector3(-4.5f,0,-4.5f),new Vector3(4.5f,0,4.5f));
            go.AddComponent<Silo>();
        }

        // ---- item models (Look.Builder, in the attach child) ----
        private static Material Gold=>Look.Paint(new Color(0.85f,0.66f,0.24f),0.2f,0.55f);
        private static Material Iron=>Look.Paint(new Color(0.18f,0.19f,0.2f),0.45f,0.4f);
        private static void FuelModel(Transform t)
        {
            var b=new Look.Builder(t);
            b.Tube("cask",new Vector3(0,0.13f,0),0.18f,0.26f,Look.Paint(new Color(0.55f,0.1f,0.07f),0.2f,0.45f),'y');
            foreach(float y in new[]{0.03f,0.13f,0.23f})b.Tube("hoop"+y,new Vector3(0,y,0),0.19f,0.025f,Gold,'y');
            b.Tube("cap",new Vector3(0,0.275f,0),0.07f,0.03f,Look.Slot("emit_orange"),'y');
        }
        private static void LatticeModel(Transform t)
        {
            var b=new Look.Builder(t);
            b.Box("skin",new Vector3(0,0.01f,0),new Vector3(0.34f,0.02f,0.34f),Look.Slot("copper"));
            for(int i=-2;i<=2;i++)
            {
                b.Box("x"+i,new Vector3(i*0.08f,0.035f,0),new Vector3(0.02f,0.03f,0.34f),Look.Paint(new Color(0.75f,0.3f,0.12f),0.5f,0.6f));
                b.Box("z"+i,new Vector3(0,0.035f,i*0.08f),new Vector3(0.34f,0.03f,0.02f),Look.Paint(new Color(0.75f,0.3f,0.12f),0.5f,0.6f));
            }
        }
        private static void ControlModel(Transform t)
        {
            var b=new Look.Builder(t);
            b.Box("case",new Vector3(0,0.06f,0),new Vector3(0.26f,0.12f,0.2f),Iron);
            b.Box("trim",new Vector3(0,0.121f,0),new Vector3(0.27f,0.012f,0.21f),Gold);
            b.Box("screen",new Vector3(0,0.13f,0),new Vector3(0.18f,0.01f,0.12f),Look.Slot("emit_cyan"));
            b.Box("rune1",new Vector3(-0.04f,0.137f,0),new Vector3(0.012f,0.006f,0.09f),Look.Slot("emit_white"));
            b.Box("rune2",new Vector3(0.0f,0.137f,0.02f),new Vector3(0.06f,0.006f,0.012f),Look.Slot("emit_white"),new Vector3(0,35,0));
        }
        private static void PartModel(Transform t)
        {
            var b=new Look.Builder(t);
            b.Tube("hull",new Vector3(0,0.15f,0),0.36f,0.3f,Iron,'y');
            b.Tube("band",new Vector3(0,0.29f,0),0.375f,0.04f,Gold,'y');
            b.Box("rune",new Vector3(0,0.15f,0.182f),new Vector3(0.025f,0.14f,0.01f),Look.Slot("emit_cyan"));
        }
        // A drinking horn curling up from its mouth, gold bands, a gold tip.
        private static void HornModel(Transform t)
        {
            var b=new Look.Builder(t);
            Material horn=Look.Paint(new Color(0.78f,0.68f,0.48f),0.05f,0.5f);
            Vector3 p=Vector3.zero;
            for(int i=0;i<9;i++)
            {
                float th=i*13f*Mathf.Deg2Rad,len=0.06f,d=0.12f-0.011f*i;
                var dir=new Vector3(0,Mathf.Sin(th),Mathf.Cos(th));
                b.Tube("seg"+i,p+dir*len*0.5f,d,len+0.01f,i%3==0?Gold:horn,'z',new Vector3(-i*13f,0,0));
                p+=dir*len;
            }
            b.Tube("rim",new Vector3(0,0,-0.005f),0.135f,0.02f,Gold,'z');
            b.Ball("tip",p,Vector3.one*0.035f,Gold);
        }

        // ---- running ----
        internal override void Start()
        {
            Net.Register("FH_Valhalla",(sender,pkg)=>
            {
                string who=pkg.ReadString();
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,who+" rode to Valhalla!");
            },skipOwn:false);
            Flight.Start();
        }
        internal override void Tick()=>Flight.Tick();
        internal override void LateTick()=>Flight.LateTick();
        internal override void Gui()=>Flight.Gui();
        internal override bool WindowOpen=>Flight.Active;
        internal override void CloseWindow()=>Flight.Skip();
        internal override void Stop()=>Flight.Stop();

        internal static Silo Nearest(Vector3 at,float range=40f)=>
            Live<Machine>.All.OfType<Silo>().Where(s=>s!=null&&Vector3.Distance(s.transform.position,at)<range).OrderBy(s=>Vector3.Distance(s.transform.position,at)).FirstOrDefault();

        // ---- fh silo | fh silo fill [n] | fh silo launch [skip] ----
        internal override string Command=>"silo";
        internal override string Help=>"[fill [n] | launch [skip]]: the nearest rocket silo's state; fill completes its rocket (or sets n parts); launch boards it (completing it first); launch skip jumps to the end of the flight";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"";
            if(sub=="launch"&&args.Length>3&&args[3]=="skip"){if(!Flight.Active)error("fh silo launch skip: not flying");else{Flight.Skip();output(new JObject{["skipped"]=true});}return null;}
            Silo s=Nearest(me.transform.position);
            if(s==null){error("fh silo: no rocket silo within 40 m (fh place FH_RocketSilo 12)");return null;}
            switch(sub)
            {
                case "fill":
                    s.Fill(args.Length>3&&int.TryParse(args[3],out int n)?n:PartsNeeded.Value);
                    break;
                case "launch":
                    if(!s.Board(me,force:true)){error("fh silo launch: can't board ("+s.Why+")");return null;}
                    break;
            }
            output(s.State());
            return null;
        }
    }
}
