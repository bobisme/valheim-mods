using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // Infrared goggles: a helmet. Worn and switched on (a hotkey), the world goes cold and dim blue, and anything with a pulse glows
    // through walls and hills: creatures orange, players white-hot, bosses red, the undead barely at all (they're cold). The night is
    // brighter too. No shader assets: a command buffer on the game camera, after its image effects, multiplies the picture with a cold
    // tint, then draws each warm body's own renderers again, additive, ignoring depth (Unity's built-in Hidden/Internal-Colored shader).
    internal sealed class Goggles:Feature
    {
        internal override string Name=>"Goggles";
        internal static readonly ItemDef Item=new ItemDef{Prefab="WU_InfraredGoggles",Name="Infrared Goggles",Base="HelmetLeather",
            Cost="Bronze:4,SurtlingCore:1,LeatherScraps:3,Resin:2",Station="forge",Level=1,Weight=1f,Model=Build,
            Stats=s=>{s.m_armor=2f;s.m_armorPerLevel=0f;s.m_helmetHideHair=ItemDrop.ItemData.HelmetHairType.Default;s.m_helmetHideBeard=ItemDrop.ItemData.HelmetHairType.Default;},
            Description="Two red lenses and a battery pack held on with a strap. Everything with a pulse glows, through walls, through hills, at night. Toggle with the goggles key (H). The undead don't show up much. Think about that."};
        internal override IEnumerable<ItemDef> Items=>new[]{Item};

        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<float> Range,Brighten,Dim;
        internal static ConfigEntry<bool> StartOn,ShowSelf;
        internal static bool On=true;
        internal static Vector3 Offset=new Vector3(0f,0.26f,0.11f);   // from the Head bone (up the head, forward): lenses over the eyes, set in game
        internal static CameraEvent Stage=CameraEvent.AfterImageEffects;

        private static readonly AccessTools.FieldRef<Humanoid,ItemDrop.ItemData> Helmet=AccessTools.FieldRefAccess<Humanoid,ItemDrop.ItemData>("m_helmetItem");
        private static readonly AccessTools.FieldRef<VisEquipment,GameObject> HelmetInst=AccessTools.FieldRefAccess<VisEquipment,GameObject>("m_helmetItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment,int> HelmetHash=AccessTools.FieldRefAccess<VisEquipment,int>("m_currentHelmetItemHash");
        private static readonly int AmbientId=Shader.PropertyToID("_AmbientColor");

        internal override void Bind(ConfigFile c)
        {
            const string s="4 - Goggles";
            Key=c.Bind(s,"ToggleKey",KeyCode.H,"Switches the goggles' thermal view on and off while you wear them.");
            StartOn=c.Bind(s,"OnWhenPutOn",true,"The thermal view is on when you put the goggles on.");
            Range=c.Bind(s,"Range",80f,new ConfigDescription("How far away warm bodies show (metres).",new AcceptableValueRange<float>(10f,300f)));
            Dim=c.Bind(s,"WorldDim",0.55f,new ConfigDescription("How bright the cold world stays (0 black, 1 as it is).",new AcceptableValueRange<float>(0.1f,1f)));
            Brighten=c.Bind(s,"NightBoost",2.5f,new ConfigDescription("How much brighter the night's ambient light is (1: not at all).",new AcceptableValueRange<float>(1f,8f)));
            ShowSelf=c.Bind(s,"ShowYourself",true,"Your own body glows too (seen in third person).");
        }

        // Built under "attach" on the helmet joint; placed every frame by Wear.Place: +z forward, +y up the head.
        // The look is Models/goggles.py (Blender): barrels, red lenses, battery, side dial, antenna and strap in one "body" part.
        // Without the model file, the old primitive pair stands in.
        private static void Build(Transform root)
        {
            if(Meshes.Has("goggles")){Meshes.AddAll(root,"goggles");return;}
            var b=new Look.Builder(root);
            Material frame=Look.Paint(Look.Gunmetal,0.7f,0.5f),strap=Look.Paint(Look.Black,0.1f,0.2f),lens=Look.Paint(new Color(0.9f,0.08f,0.05f),0.2f,0.95f,1.8f),
                chrome=Look.Paint(Look.Chrome,0.9f,0.8f);
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*0.042f;
                b.Tube("barrel",new Vector3(x,0,0.02f),0.05f,0.05f,frame);
                b.Tube("lens",new Vector3(x,0,0.047f),0.042f,0.006f,lens);
                b.Tube("rim",new Vector3(x,0,0.045f),0.054f,0.008f,chrome);
            }
            b.Box("bridge",new Vector3(0,0.005f,0.02f),new Vector3(0.03f,0.018f,0.03f),frame);
            b.Box("battery",new Vector3(0,0.04f,0.0f),new Vector3(0.05f,0.025f,0.045f),frame);
            b.Ball("led",new Vector3(0.018f,0.055f,0.0f),new Vector3(0.008f,0.008f,0.008f),Look.Paint(Look.Green,0f,0.5f,3f));
            // the strap round the back of the head
            b.Box("strap",new Vector3(0,0,-0.07f),new Vector3(0.16f,0.03f,0.012f),strap);
            for(int side=-1;side<=1;side+=2)b.Box("strap",new Vector3(side*0.08f,0,-0.02f),new Vector3(0.012f,0.03f,0.11f),strap);
        }

        internal static bool Wearing(Player p)=>p!=null&&Item.Is(Helmet(p));
        internal static bool Active=>On&&Wearing(Player.m_localPlayer);
        private static bool _wasWearing;

        internal override void Tick()
        {
            Player me=Player.m_localPlayer;
            bool wearing=Wearing(me);
            if(wearing&&!_wasWearing)On=StartOn.Value;
            _wasWearing=wearing;
            if(wearing&&Input.GetKeyDown(Key.Value)&&InputFree())
            {
                On=!On;
                me.Message(MessageHud.MessageType.TopLeft,On?"Infrared on":"Infrared off");
                Sound.Play(Click(),me.transform.position+Vector3.up*1.7f,0.6f,On?1.2f:0.9f,10f);
            }
        }
        private static bool InputFree()=>(Chat.instance==null||!Chat.instance.HasFocus())&&!Console.IsVisible()&&!TextInput.IsVisible()&&!Menu.IsVisible();
        private static AudioClip Click()=>Sound.Make("goggles click",0.25f,t=>(Sound.Sine(t*1800f)*0.5f+Sound.Noise()*0.2f)*Sound.Env(t,0.004f,0.08f)+Sound.Sine(Sound.SweepPhase(t,300f,1400f,0.25f))*0.25f*Sound.Env(t,0.02f,0.25f));

        internal override void LateTick()
        {
            // Square the goggles on every face that wears them (remote players too: VisEquipment knows what each one wears).
            Player me=Player.m_localPlayer;
            int hash=Item.Prefab.GetStableHashCode();
            foreach(Player p in Player.GetAllPlayers())
            {
                if(p==null)continue;
                if(me!=null&&p!=me&&(p.transform.position-me.transform.position).sqrMagnitude>200f*200f)continue;
                VisEquipment vis=p.GetComponent<VisEquipment>();
                if(vis==null||HelmetHash(vis)!=hash)continue;
                GameObject inst=HelmetInst(vis);
                if(inst!=null)Wear.Place(vis,inst.transform,"Head","Neck","Head",Offset);
            }
            Thermal();
        }

        // ---- the thermal view ----
        private static Camera _cam;
        private static CommandBuffer _cmd;
        private static CameraEvent _stageOn;
        private static Mesh _quad;
        private static Material _tint,_cold;
        private static readonly Dictionary<int,Material> Heat=new Dictionary<int,Material>();
        private sealed class Body{internal Renderer[] Renderers;internal float Refresh;}
        private static readonly Dictionary<Character,Body> Bodies=new Dictionary<Character,Body>();
        private static Shader _shader;
        private static Color _ambBase,_ambSet;
        private static int _drawn;

        private static void Thermal()
        {
            Camera cam=GameCamera.instance!=null?GameCamera.instance.GetComponent<Camera>():null;
            if(!Active||cam==null){Detach();return;}
            if(_shader==null)_shader=Shader.Find("Hidden/Internal-Colored");
            if(_shader==null){Detach();return;}
            if(_cmd==null)_cmd=new CommandBuffer{name="WrongUniverse thermal"};
            if(_cam!=cam||_stageOn!=Stage){Detach();cam.AddCommandBuffer(Stage,_cmd);_cam=cam;_stageOn=Stage;}
            Night();
            Fill(cam);
        }
        // Brighter nights: the game sets the ambient light every physics frame; scale what it last set (not what we set).
        private static void Night()
        {
            if(RenderSettings.ambientLight!=_ambSet)_ambBase=RenderSettings.ambientLight;
            float boost=Brighten.Value;
            Color c=_ambBase*boost;
            float floor=0.12f*(boost-1f);   // (a moonless night's ambient is nearly black: give it something to multiply)
            c=new Color(Mathf.Max(c.r,floor*0.7f),Mathf.Max(c.g,floor*0.8f),Mathf.Max(c.b,floor),_ambBase.a);
            RenderSettings.ambientLight=c;_ambSet=c;
            Shader.SetGlobalColor(AmbientId,c);
        }
        private static Material Mat(Color color,BlendMode src,BlendMode dst)
        {
            var m=Look.Own(new Material(_shader){hideFlags=HideFlags.HideAndDontSave});
            m.SetInt("_SrcBlend",(int)src);m.SetInt("_DstBlend",(int)dst);
            m.SetInt("_ZWrite",0);m.SetInt("_ZTest",(int)CompareFunction.Always);m.SetInt("_Cull",(int)CullMode.Off);
            m.SetColor("_Color",color);
            return m;
        }
        // Heat colours: kind 0 creature, 1 player, 2 boss, 3 undead; level 0..7 by distance.
        private static Material HeatMat(int kind,int level)
        {
            int key=kind*16+level;
            if(Heat.TryGetValue(key,out Material m)&&m!=null)return m;
            Color c=kind==1?new Color(1f,0.95f,0.75f):kind==2?new Color(1f,0.25f,0.1f):kind==3?new Color(0.25f,0.45f,0.7f):new Color(1f,0.55f,0.15f);
            float k=Mathf.Lerp(0.35f,1f,level/7f)*(kind==3?0.35f:1f);
            return Heat[key]=Mat(c*k,BlendMode.One,BlendMode.One);
        }
        private static void Fill(Camera cam)
        {
            _cmd.Clear();_drawn=0;
            if(_quad==null)
            {
                _quad=Look.Own(new Mesh{hideFlags=HideFlags.HideAndDontSave});
                _quad.vertices=new[]{new Vector3(-1,-1,0.5f),new Vector3(1,-1,0.5f),new Vector3(1,1,0.5f),new Vector3(-1,1,0.5f)};
                _quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
                _quad.triangles=new[]{0,1,2,0,2,3};
            }
            float dim=Dim.Value;
            if(_tint==null)_tint=Mat(Color.white,BlendMode.DstColor,BlendMode.Zero);
            _tint.SetColor("_Color",new Color(0.55f*dim,0.75f*dim,1f*dim,1f));
            if(_cold==null)_cold=Mat(new Color(0f,0.015f,0.05f),BlendMode.One,BlendMode.One);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
            _cmd.DrawMesh(_quad,Matrix4x4.identity,_tint);
            _cmd.DrawMesh(_quad,Matrix4x4.identity,_cold);
            _cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix,cam.projectionMatrix);

            Player me=Player.m_localPlayer;
            Vector3 eye=cam.transform.position;
            float range=Range.Value;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c.IsDead())continue;
                if(c==me&&(!ShowSelf.Value||Vector3.Distance(eye,c.transform.position)<1.2f))continue;   // (first person: your own arms would fill the view)
                float d=Vector3.Distance(eye,c.transform.position);
                if(d>range)continue;
                if(!Bodies.TryGetValue(c,out Body body)||Time.time>body.Refresh)
                {
                    var list=new List<Renderer>();
                    foreach(Renderer r in c.GetComponentsInChildren<Renderer>(false))
                        if(r is SkinnedMeshRenderer||r is MeshRenderer)list.Add(r);
                    body=new Body{Renderers=list.ToArray(),Refresh=Time.time+1f+UnityEngine.Random.value};   // (equipment changes: look again now and then)
                    Bodies[c]=body;
                }
                int kind=c.IsPlayer()?1:c.IsBoss()?2:c.m_faction==Character.Faction.Undead?3:0;
                int level=c==me?0:Mathf.Clamp(Mathf.RoundToInt((1f-d/range)*7f),0,7);   // (yourself only faintly: the camera is close, and full heat hid you and your gear)
                Material m=HeatMat(kind,level);
                foreach(Renderer r in body.Renderers)
                {
                    if(r==null||!r.enabled||!r.gameObject.activeInHierarchy||!r.isVisible)continue;   // (isVisible: the shown LOD only)
                    Mesh mesh=r is SkinnedMeshRenderer s?s.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh;
                    if(mesh==null)continue;
                    for(int i=0;i<mesh.subMeshCount;i++)_cmd.DrawRenderer(r,m,i,0);
                    _drawn++;
                }
            }
            if(Time.frameCount%300==0){var dead=new List<Character>();foreach(var k in Bodies.Keys)if(k==null)dead.Add(k);foreach(var k in dead)Bodies.Remove(k);}
        }
        private static void Detach()
        {
            if(_cam!=null&&_cmd!=null)_cam.RemoveCommandBuffer(_stageOn,_cmd);
            _cam=null;
            if(_ambSet!=default&&RenderSettings.ambientLight==_ambSet){RenderSettings.ambientLight=_ambBase;Shader.SetGlobalColor(AmbientId,_ambBase);}
            _ambSet=default;
        }

        internal override void Stop()
        {
            Detach();
            _cmd?.Release();_cmd=null;
            Heat.Clear();Bodies.Clear();_tint=null;_cold=null;_quad=null;   // (Look.Clear destroys them)
        }

        internal override string Command=>"goggles";
        internal override string Help=>"[info|on|off|offset <x y z>|stage post|alpha|opaque]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>2?args[2]:"info";
            float F(int i,float d)=>args.Length>i&&float.TryParse(args[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:d;
            switch(sub)
            {
                case "on":On=true;break;
                case "off":On=false;break;
                case "offset":Offset=new Vector3(F(3,Offset.x),F(4,Offset.y),F(5,Offset.z));break;
                case "stage":
                {
                    string w=args.Length>3?args[3]:"post";
                    Stage=w=="alpha"?CameraEvent.AfterForwardAlpha:w=="opaque"?CameraEvent.AfterForwardOpaque:CameraEvent.AfterImageEffects;
                    break;
                }
                case "info":break;
                default:error("wu goggles: "+Help);return null;
            }
            // Where the worn goggles really are (they once showed as a huge halo): the instance, its joint, the head bone, world scale.
            var placed=new JObject();
            Player me=Player.m_localPlayer;
            VisEquipment vis=me!=null?me.GetComponent<VisEquipment>():null;
            GameObject inst=vis!=null?HelmetInst(vis):null;
            if(inst!=null)
            {
                Transform head=Wear.Bone(vis,"Head"),neck=Wear.Bone(vis,"Neck");
                placed["joint"]=inst.transform.parent!=null?inst.transform.parent.name:null;
                placed["jointScale"]=inst.transform.parent!=null?Tools.V(inst.transform.parent.lossyScale):null;
                placed["worldScale"]=Tools.V(inst.transform.lossyScale);
                placed["at"]=Tools.V(inst.transform.position);
                placed["head"]=head!=null?Tools.V(head.position):null;
                placed["neckFound"]=neck!=null;
                placed["hashMatches"]=HelmetHash(vis)==Item.Prefab.GetStableHashCode();
                placed["renderers"]=inst.GetComponentsInChildren<Renderer>(true).Length;
            }
            output(new JObject{["placed"]=placed,["wearing"]=Wearing(Player.m_localPlayer),["on"]=On,["active"]=Active,["shader"]=_shader!=null||Shader.Find("Hidden/Internal-Colored")!=null,
                ["attached"]=_cam!=null,["stage"]=Stage.ToString(),["renderersDrawn"]=_drawn,["bodies"]=Bodies.Count,["offset"]=Tools.V(Offset),
                ["ambient"]=RenderSettings.ambientLight.ToString()});
            return null;
        }
    }
}
