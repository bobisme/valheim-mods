using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // The jetpack: a utility-slot item worn on the back. Jump, then hold Jump in the air to fly up; move keys push you along, Run boosts.
    // Fuel burns while thrusting and refills on the ground. While it's worn you take no fall damage (a setting).
    //
    // Thrust is added after the game's own movement step (a postfix on Character.UpdateMotion), which otherwise sets the body's velocity
    // every physics step. Others see the flames from one small int on the player's ZDO, written only when it changes.
    // The pack hangs from the Spine2 bone (VisEquipment does that for everyone), but bones carry the skeleton's scale (~1/85), so every
    // frame it is sized back to metres and set square on the back from the body skin's own bones (Wear.Place).
    internal sealed class Jetpack:Feature
    {
        internal override string Name=>"Jetpack";
        internal static readonly ItemDef Pack=new ItemDef{Prefab="WU_Jetpack",Name="Jetpack",Base="BeltStrength",Attach="attach_Spine2",
            Cost="Iron:10,SurtlingCore:4,LeatherScraps:6,Resin:5",Station="forge",Level=1,Weight=8f,Model=Build,
            Description="Two rocket nozzles, a fuel tank and a very optimistic harness. Jump, then hold Jump to fly; move to steer, hold Run to go faster. Refuels on the ground. You will not die from falling. You may die from everything else."};
        internal override IEnumerable<ItemDef> Items=>new[]{Pack};

        internal static ConfigEntry<float> FuelSeconds,RefuelSeconds,Climb,Lift,Speed,BoostSpeed,BoostBurn;
        internal static ConfigEntry<bool> NoFallDamage;
        internal static float Fuel=1f;
        internal static int Thrust;            // 0 off, 1 flying, 2 boosting (local player)
        private static int _sent=-1;
        private static float _sentAt,_forceUntil;
        private static bool _forceBoost;
        internal static readonly int JetKey="wu_jet".GetStableHashCode();
        internal static Vector3 Offset=new Vector3(0f,0.02f,-0.15f);   // from Spine2, in the back's frame (up along the spine, -z behind)

        private static readonly AccessTools.FieldRef<Character,Rigidbody> Body=AccessTools.FieldRefAccess<Character,Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character,Vector3> MoveDir=AccessTools.FieldRefAccess<Character,Vector3>("m_moveDir");
        private static readonly AccessTools.FieldRef<Character,bool> Run=AccessTools.FieldRefAccess<Character,bool>("m_run");
        private static readonly AccessTools.FieldRef<Character,float> MaxAir=AccessTools.FieldRefAccess<Character,float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Humanoid,ItemDrop.ItemData> Utility=AccessTools.FieldRefAccess<Humanoid,ItemDrop.ItemData>("m_utilityItem");
        private static readonly AccessTools.FieldRef<VisEquipment,List<GameObject>> Worn=AccessTools.FieldRefAccess<VisEquipment,List<GameObject>>("m_utilityItemInstances");

        internal override void Bind(ConfigFile c)
        {
            const string s="3 - Jetpack";
            FuelSeconds=c.Bind(s,"FuelSeconds",8f,new ConfigDescription("Seconds of thrust in a full tank.",new AcceptableValueRange<float>(1f,120f)));
            RefuelSeconds=c.Bind(s,"RefuelSeconds",3f,new ConfigDescription("Seconds on the ground to refill an empty tank.",new AcceptableValueRange<float>(0.5f,60f)));
            Climb=c.Bind(s,"ClimbSpeed",10f,new ConfigDescription("How fast it lifts you (metres per second).",new AcceptableValueRange<float>(2f,40f)));
            Lift=c.Bind(s,"Acceleration",30f,new ConfigDescription("How quickly it reaches its speeds (metres per second, each second).",new AcceptableValueRange<float>(5f,100f)));
            Speed=c.Bind(s,"FlySpeed",14f,new ConfigDescription("How fast the move keys push you along in the air (metres per second).",new AcceptableValueRange<float>(2f,60f)));
            BoostSpeed=c.Bind(s,"BoostSpeed",28f,new ConfigDescription("The same holding Run.",new AcceptableValueRange<float>(2f,80f)));
            BoostBurn=c.Bind(s,"BoostFuelUse",2f,new ConfigDescription("How many times faster fuel burns while boosting.",new AcceptableValueRange<float>(1f,5f)));
            NoFallDamage=c.Bind(s,"NoFallDamage",true,"No fall damage while the jetpack is worn (with fuel or without).");
        }

        // Built under attach_Spine2: +z is the player's front, so the pack sits behind (-z), nozzles pointing down.
        private static void Build(Transform root)
        {
            var b=new Look.Builder(root);
            Material body=Look.Paint(Look.White,0.3f,0.6f),dark=Look.Paint(Look.Gunmetal,0.7f,0.5f),hazard=Look.Paint(Look.Hazard,0.2f,0.4f),
                chrome=Look.Paint(Look.Chrome,0.9f,0.8f),strap=Look.Paint(Look.Black,0.1f,0.2f),lamp=Look.Paint(Look.Cyan,0f,0.5f,2.5f);
            b.Box("plate",new Vector3(0,0,-0.03f),new Vector3(0.30f,0.42f,0.04f),dark);
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*0.1f;
                b.Capsule("tank",new Vector3(x,0.02f,-0.13f),0.15f,0.5f,body,'y');
                b.Tube("band",new Vector3(x,0.12f,-0.13f),0.158f,0.03f,hazard,'y');
                b.Tube("band",new Vector3(x,-0.08f,-0.13f),0.158f,0.03f,hazard,'y');
                b.Tube("nozzle",new Vector3(x,-0.27f,-0.13f),0.09f,0.1f,chrome,'y');
                b.Tube("bell",new Vector3(x,-0.33f,-0.13f),0.12f,0.04f,dark,'y');
                b.Box("strap",new Vector3(x,0.12f,0.04f),new Vector3(0.04f,0.02f,0.16f),strap);
            }
            b.Box("valve",new Vector3(0,0.22f,-0.12f),new Vector3(0.07f,0.05f,0.07f),chrome);
            b.Ball("lamp",new Vector3(0,0.12f,-0.2f),new Vector3(0.035f,0.035f,0.035f),lamp);
            // The flames: switched off in the item (so not on the ground or in the icon), lit by Tick on whoever thrusts.
            Look.Builder flames=b.Group("flames",Vector3.zero);
            flames.Root.gameObject.SetActive(false);
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*0.1f;
                flames.Capsule("flame",new Vector3(x,-0.55f,-0.13f),0.10f,0.42f,Look.Glow(new Color(1f,0.55f,0.15f)),'y');
                flames.Capsule("core",new Vector3(x,-0.45f,-0.13f),0.05f,0.22f,Look.Glow(new Color(1f,0.95f,0.8f)),'y');
            }
            Look.AddLight(flames.Root,new Color(1f,0.6f,0.25f),7f,2.5f,new Vector3(0,-0.6f,-0.13f));
        }
        internal override void Start()=>Fuel=1f;
        internal override void Stop()
        {
            foreach(var kv in Lit)if(kv.Value.Sound!=null)Object.Destroy(kv.Value.Sound.gameObject);
            Lit.Clear();
            SetThrust(Player.m_localPlayer,0,true);
        }

        internal static bool Wearing(Player p)=>p!=null&&Pack.Is(Utility(p));
        private static bool InputFree()=>(Chat.instance==null||!Chat.instance.HasFocus())&&!Console.IsVisible()&&!TextInput.IsVisible()&&!Menu.IsVisible()&&!InventoryGui.IsVisible()&&!Minimap.IsOpen();

        // After the game's movement for the step: thrust, steering, fuel, and no fall damage.
        [HarmonyPatch(typeof(Character),"UpdateMotion")]
        private static class Motion
        {
            private static void Postfix(Character __instance,float dt)
            {
                if(!(__instance is Player p)||p!=Player.m_localPlayer)return;
                try{Step(p,dt);}catch(Exception e){Plugin.Warn("Jetpack: "+e.Message);}
            }
        }
        private static void Step(Player p,float dt)
        {
            if(!Wearing(p)){if(Thrust!=0)SetThrust(p,0);Fuel=Mathf.MoveTowards(Fuel,1f,dt/RefuelSeconds.Value);return;}
            if(NoFallDamage.Value)MaxAir(p)=Mathf.Min(MaxAir(p),p.transform.position.y+3.5f);   // (the game hurts falls of more than 4 m)
            bool forced=Time.time<_forceUntil;
            bool held=forced||InputFree()&&(ZInput.GetButton("Jump")||ZInput.GetButton("JoyJump"));
            bool boost=forced?_forceBoost:Run(p);
            bool can=!p.IsDead()&&!p.IsSwimming()&&!p.IsAttached()&&!p.IsTeleporting()&&!p.IsStaggering()&&!p.InIntro();
            if(p.IsOnGround()||p.IsSwimming())Fuel=Mathf.MoveTowards(Fuel,1f,dt/RefuelSeconds.Value);
            int want=held&&can&&!p.IsOnGround()&&Fuel>0f?(boost?2:1):0;
            if(want!=Thrust)SetThrust(p,want);
            if(want==0)return;
            Fuel=Mathf.Max(0f,Fuel-dt/FuelSeconds.Value*(want==2?BoostBurn.Value:1f));
            Rigidbody rb=Body(p);
            if(rb==null)return;
            Vector3 v=rb.linearVelocity;
            // Up: towards the climb speed, plus what gravity will take off this step.
            v.y=Mathf.MoveTowards(v.y,Climb.Value*(want==2?1.3f:1f),Lift.Value*dt)-Physics.gravity.y*dt;
            Vector3 flat=new Vector3(v.x,0,v.z),move=MoveDir(p);move.y=0;
            Vector3 target=move.sqrMagnitude>0.01f?move.normalized*Mathf.Min(1f,move.magnitude)*(want==2?BoostSpeed.Value:Speed.Value):flat*0.98f;
            flat=Vector3.MoveTowards(flat,target,Lift.Value*dt*(want==2?1.5f:1f));
            rb.linearVelocity=new Vector3(flat.x,v.y,flat.z);
        }
        private static void SetThrust(Player p,int value,bool now=false)
        {
            Thrust=value;
            ZDO zdo=p!=null?p.GetComponent<ZNetView>()?.GetZDO():null;
            if(zdo==null||!zdo.IsOwner())return;
            if(value==_sent)return;
            // (a burst of taps would bump the player's ZDO every step: at most five changes a second, the last one always lands via Tick)
            if(!now&&Time.time-_sentAt<0.2f)return;
            zdo.Set(JetKey,value);_sent=value;_sentAt=Time.time;
        }

        // Every frame: square the packs on everyone's back, light the flames of whoever thrusts.
        private sealed class Lit_{internal Transform Flames;internal Light Light;internal AudioSource Sound;}
        private static readonly Dictionary<GameObject,Lit_> Lit=new Dictionary<GameObject,Lit_>();
        private static readonly List<GameObject> Gone=new List<GameObject>();
        internal override void Tick()
        {
            Player me=Player.m_localPlayer;
            if(me!=null&&Thrust!=_sent)SetThrust(me,Thrust);   // (a change held back by the rate limit)
        }
        internal override void LateTick()
        {
            Player me=Player.m_localPlayer;
            foreach(Player p in Player.GetAllPlayers())
            {
                if(p==null)continue;
                if(me!=null&&p!=me&&(p.transform.position-me.transform.position).sqrMagnitude>200f*200f)continue;
                VisEquipment vis=p.GetComponent<VisEquipment>();
                List<GameObject> worn=vis!=null?Worn(vis):null;
                if(worn==null)continue;
                foreach(GameObject inst in worn)
                {
                    if(inst==null||!inst.name.StartsWith(Pack.Attach))continue;
                    Wear.Place(vis,inst.transform,"Spine2","Spine2","Neck",Offset);
                    int thrust=p==me?Thrust:(p.GetComponent<ZNetView>()?.GetZDO()?.GetInt(JetKey)??0);
                    Flames(inst,thrust);
                }
            }
            Gone.Clear();
            foreach(GameObject g in Lit.Keys)if(g==null)Gone.Add(g);
            foreach(GameObject g in Gone)Lit.Remove(g);
        }
        private static void Flames(GameObject inst,int thrust)
        {
            if(!Lit.TryGetValue(inst,out Lit_ l))
            {
                Transform f=inst.transform.Find("flames");
                if(f==null)return;
                l=new Lit_{Flames=f,Light=f.GetComponentInChildren<Light>(true)};
                Lit[inst]=l;
            }
            bool on=thrust>0;
            if(l.Flames.gameObject.activeSelf!=on)l.Flames.gameObject.SetActive(on);
            if(on)
            {
                float flicker=0.85f+0.3f*Mathf.PerlinNoise(Time.time*25f,inst.GetInstanceID()*0.1f);
                float size=(thrust==2?1.5f:1f)*flicker;
                l.Flames.localScale=new Vector3(1f,size,1f);
                l.Flames.localPosition=new Vector3(0,-0.33f*(size-1f),0);   // (grows from the nozzles down)
                if(l.Light!=null)l.Light.intensity=2.5f*size;
                if(l.Sound==null)l.Sound=Sound.Loop(Roar(),inst.transform,45f);
            }
            if(l.Sound!=null)
            {
                float target=on?(thrust==2?1f:0.7f)*Plugin.Volume.Value:0f;
                l.Sound.volume=Mathf.MoveTowards(l.Sound.volume,target,Time.deltaTime*4f);
                l.Sound.pitch=Mathf.MoveTowards(l.Sound.pitch,thrust==2?1.25f:1f,Time.deltaTime);
            }
        }
        // A rocket's roar: low-passed noise with a rumble under it. Loops (noise has no seam to hear).
        private static float _lp1,_lp2;
        private static AudioClip Roar()=>Sound.Make("jet roar",2f,t=>
        {
            float n=Sound.Noise();
            _lp1+=(n-_lp1)*0.18f;_lp2+=(_lp1-_lp2)*0.3f;
            float rumble=Sound.Sine(t*55f)*0.25f+Sound.Sine(t*83f)*0.15f;
            return (_lp2*1.6f+n*0.08f+rumble*(0.7f+0.3f*Sound.Sine(t*7f)))*0.6f;
        });

        internal override void Gui()
        {
            Player p=Player.m_localPlayer;
            if(!Wearing(p)||Fuel>=0.999f&&Thrust==0)return;
            Hud.Bar(Fuel<=0f?"NO FUEL":"JET FUEL",Fuel,Fuel<0.2f?new Color(1f,0.3f,0.2f):new Color(1f,0.65f,0.15f),Fuel<0.2f);
        }

        internal override string Command=>"jet";
        internal override string Help=>"[info|thrust <seconds> [boost]|fuel <0-1>|offset <x y z>]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"info";
            float F(int i,float d)=>args.Length>i&&float.TryParse(args[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:d;
            switch(sub)
            {
                case "thrust":_forceUntil=Time.time+F(3,2f);_forceBoost=args.Length>4&&args[4]=="boost";break;
                case "fuel":Fuel=Mathf.Clamp01(F(3,1f));break;
                case "offset":Offset=new Vector3(F(3,Offset.x),F(4,Offset.y),F(5,Offset.z));break;
                case "info":break;
                default:error("wu jet: "+Help);return null;
            }
            Rigidbody rb=me!=null?Body(me):null;
            output(new JObject{["wearing"]=Wearing(me),["fuel"]=Math.Round(Fuel,3),["thrust"]=Thrust,["sent"]=_sent,["onGround"]=me!=null&&me.IsOnGround(),
                ["velocity"]=rb!=null?Tools.V(rb.linearVelocity):null,["position"]=me!=null?Tools.V(me.transform.position):null,["offset"]=Tools.V(Offset),
                ["forcedFor"]=Math.Round(Mathf.Max(0f,_forceUntil-Time.time),2),["packsLit"]=Lit.Count});
            return null;
        }
    }

    // Worn things the game hangs on a bone: put back to metres and square with the body, every frame (after the animation).
    internal static class Wear
    {
        private static readonly Dictionary<SkinnedMeshRenderer,Dictionary<string,Transform>> Bones=new Dictionary<SkinnedMeshRenderer,Dictionary<string,Transform>>();
        private static float _prune;
        // The body skin's own bone of that name (worn clothes carry copies of the skeleton that don't move).
        internal static Transform Bone(VisEquipment vis,string name)
        {
            SkinnedMeshRenderer skin=vis!=null?vis.m_bodyModel:null;
            if(skin==null)return null;
            if(Time.time>_prune){_prune=Time.time+30f;var dead=new List<SkinnedMeshRenderer>();foreach(var k in Bones.Keys)if(k==null)dead.Add(k);foreach(var k in dead)Bones.Remove(k);}
            if(!Bones.TryGetValue(skin,out var map)){map=new Dictionary<string,Transform>();Bones[skin]=map;}
            if(map.TryGetValue(name,out Transform t)&&t!=null)return t;
            t=null;
            foreach(Transform b in skin.bones)if(b!=null&&b.name==name){t=b;break;}
            map[name]=t;
            return t;
        }
        // At a bone, upright along upFrom -> upTo (a spine or neck's bend), facing the character's forward; offset in that frame (metres).
        internal static bool Place(VisEquipment vis,Transform item,string at,string upFrom,string upTo,Vector3 offset)
        {
            Transform a=Bone(vis,at),u0=Bone(vis,upFrom),u1=Bone(vis,upTo);
            if(a==null)return false;
            Vector3 up=u0!=null&&u1!=null?(u1.position-u0.position):vis.transform.up;
            if(up.sqrMagnitude<1e-6f)up=vis.transform.up;
            up.Normalize();
            Vector3 fwd=Vector3.ProjectOnPlane(vis.transform.forward,up);
            if(fwd.sqrMagnitude<1e-6f)fwd=Vector3.ProjectOnPlane(vis.transform.up,up);
            Quaternion rot=Quaternion.LookRotation(fwd,up);
            Transform parent=item.parent;
            if(parent!=null)
            {
                Vector3 s=parent.lossyScale;
                if(Mathf.Abs(s.x)>1e-5f&&Mathf.Abs(s.y)>1e-5f&&Mathf.Abs(s.z)>1e-5f)item.localScale=new Vector3(1f/s.x,1f/s.y,1f/s.z);
            }
            item.SetPositionAndRotation(a.position+rot*offset,rot);
            return true;
        }
    }
}
