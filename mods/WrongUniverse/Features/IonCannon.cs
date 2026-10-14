using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;
using Random=UnityEngine.Random;

namespace WrongUniverse
{
    // The Orbital Uplink: point it at the ground and pull the trigger. A red ring paints the spot and something far above starts
    // charging; a few seconds later a beam the width of a house comes down. Huge damage in a circle, everything thrown about and set
    // burning, trees felled and rocks broken, a crater, a shockwave and a long cooldown. One RPC tells everyone (they see and hear it);
    // only the firer's game does the damage and the crater, so nothing happens twice.
    internal sealed class IonCannon:Feature
    {
        internal override string Name=>"Ion Cannon";
        private const string Rpc="WU_IonStrike",CraterPrefab="WU_IonCrater";
        internal static readonly ItemDef Uplink=new ItemDef{Prefab="WU_OrbitalUplink",Name="Orbital Uplink",Base="Club",
            Cost="Iron:20, Silver:10, SurtlingCore:8, Crystal:5",Station="forge",Level=2,Weight=2.5f,
            Description="A heavy handset with one red button, a long antenna and a sticker that says DO NOT. Point it at the ground and press attack: somebody in low orbit paints the spot, charges up and puts an ion beam through it. Stand well back. Then wait for it to charge again.",
            Model=BuildModel,IconLook=Quaternion.Euler(0,90,45),
            Stats=s=>{s.m_damages=new HitData.DamageTypes();s.m_attackForce=0f;s.m_blockPower=2f;s.m_useDurability=false;s.m_attack.m_attackStamina=0f;}};
        internal override IEnumerable<ItemDef> Items=>new[]{Uplink};

        internal static ConfigEntry<float> Range,Radius,Damage,ChopMine,Push,Delay,Cooldown,CraterDepth;
        internal static ConfigEntry<bool> Crater,HurtBuildings,CraterNearBuildings;
        private static GameObject _crater;
        private static float _readyAt,_nagAt;
        private static GameObject _dot;
        private static Light _dotLight;

        internal override void Bind(ConfigFile c)
        {
            const string S="6 - Ion Cannon";
            Range=c.Bind(S,"Range",300f,new ConfigDescription("How far away you can paint a target, in metres.",new AcceptableValueRange<float>(20,1000)));
            Radius=c.Bind(S,"Radius",12f,new ConfigDescription("Radius of the strike, in metres.",new AcceptableValueRange<float>(2,40)));
            Damage=c.Bind(S,"Damage",600f,new ConfigDescription("Damage at the centre (half lightning, three tenths blunt, a fifth fire; it sets things burning). A third of it reaches the rim.",new AcceptableValueRange<float>(0,10000)));
            ChopMine=c.Bind(S,"ChopAndMine",400f,new ConfigDescription("Chopping and mining damage too, so trees fall and rocks break. 0 leaves them be.",new AcceptableValueRange<float>(0,10000)));
            Push=c.Bind(S,"Knockback",250f,new ConfigDescription("How hard it throws things.",new AcceptableValueRange<float>(0,2000)));
            Delay=c.Bind(S,"ChargeSeconds",3f,new ConfigDescription("Seconds from painting the target to the beam.",new AcceptableValueRange<float>(0.5f,15)));
            Cooldown=c.Bind(S,"CooldownSeconds",60f,new ConfigDescription("Seconds before the uplink can call another strike.",new AcceptableValueRange<float>(0,3600)));
            Crater=c.Bind(S,"Crater",true,"The strike blasts a crater in the ground.");
            CraterDepth=c.Bind(S,"CraterDepth",4f,new ConfigDescription("How deep the crater is at the middle, in metres.",new AcceptableValueRange<float>(0.5f,8)));
            HurtBuildings=c.Bind(S,"HurtBuildings",false,"The strike also damages built pieces (walls, floors, roofs...). Off by default: it would flatten a base.");
            CraterNearBuildings=c.Bind(S,"CraterNearBuildings",false,"Make a crater even inside a ward's area or near built pieces. Off by default.");
            Uplink.Primary=Fire;
            Uplink.Secondary=(p,i)=>true;   // (no club swing with the middle button)
        }

        internal override void Register(ZNetScene scene)
        {
            // The crater is the game's own terrain operation (TerrainOp), a prefab of ours: the ground's owner looks it up in ObjectDB by
            // name when it applies the dig, so it must be listed there in every game (all players have the mod; one without it cancels).
            if(_crater==null)
            {
                _crater=new GameObject(CraterPrefab);
                _crater.transform.SetParent(Things.Holder,false);
                _crater.AddComponent<TerrainOp>();
            }
            ObjectDB db=ObjectDB.instance;
            if(db==null)return;
            TerrainOp op=_crater.GetComponent<TerrainOp>();
            db.m_terrainOps.RemoveAll(o=>o==null||o!=op&&o.name==CraterPrefab);
            if(!db.m_terrainOps.Contains(op))db.m_terrainOps.Add(op);
            db.m_terrainOpsByHash[CraterPrefab.GetStableHashCode()]=op;
        }

        internal override void Start()
        {
            Net.Register(Rpc,(sender,pkg)=>
            {
                Vector3 at=pkg.ReadVector3();
                float radius=pkg.ReadSingle(),delay=pkg.ReadSingle();
                long firer=pkg.ReadLong();
                Player me=Player.m_localPlayer;
                bool mine=me!=null&&me.GetPlayerID()==firer&&Net.Mine(sender);
                if(!mine&&(me==null||Vector3.Distance(me.transform.position,at)>600f))return;
                var go=new GameObject("WU_IonStrike");
                go.transform.position=at;
                go.AddComponent<IonStrike>().Begin(radius,delay,mine);
            },skipOwn:false);
        }

        // The trigger: paint whatever the crosshair is on (the game asks every physics step while held; the cooldown answers the rest).
        private static bool Fire(Player p,ItemDrop.ItemData item)
        {
            if(Time.time<_readyAt)
            {
                if(Time.time>=_nagAt){_nagAt=Time.time+1.5f;p.Message(MessageHud.MessageType.Center,$"Uplink charging: {Mathf.CeilToInt(_readyAt-Time.time)} s");}
                return true;
            }
            if(!Aim(p,out Vector3 at)){if(Time.time>=_nagAt){_nagAt=Time.time+1f;p.Message(MessageHud.MessageType.Center,"No target in range");}return true;}
            Strike(p,at);
            return true;
        }
        internal static void Strike(Player p,Vector3 at)
        {
            _readyAt=Time.time+Cooldown.Value;
            var pkg=new ZPackage();
            pkg.Write(at);pkg.Write(Radius.Value);pkg.Write(Delay.Value);pkg.Write(p.GetPlayerID());
            if(ZRoutedRpc.instance!=null)Net.Everyone(Rpc,pkg);
            p.Message(MessageHud.MessageType.Center,"<color=#ff4040>TARGET PAINTED</color>");
        }
        private static bool Aim(Player p,out Vector3 at)
        {
            at=default;
            GameCamera cam=GameCamera.instance;
            if(cam==null)return false;
            Transform t=cam.transform;
            if(!Hits.Ray(p,t.position,t.forward,Range.Value,out RaycastHit hit))return false;
            at=hit.point;
            return true;
        }

        // The firer's game: the damage and the crater, at the moment of the beam.
        internal static void Impact(Vector3 at,float radius)
        {
            Player me=Player.m_localPlayer;
            float d=Damage.Value,tools=ChopMine.Value;
            var damage=new HitData.DamageTypes{m_lightning=d*0.5f,m_blunt=d*0.3f,m_fire=d*0.2f,m_chop=tools,m_pickaxe=tools};
            Hits.Blast(me,at,radius,damage,Push.Value,HurtBuildings.Value,Skills.SkillType.None,0.35f,10);
            if(Crater.Value&&_crater!=null&&(CraterNearBuildings.Value||!Built(at,radius)))
            {
                TerrainOp.Settings s=_crater.GetComponent<TerrainOp>().m_settings;
                s.m_level=false;s.m_square=false;
                s.m_raise=true;s.m_raiseRadius=radius*0.75f;s.m_raiseDelta=-CraterDepth.Value;s.m_raisePower=0.7f;
                s.m_smooth=true;s.m_smoothRadius=radius*0.95f;s.m_smoothPower=3f;
                s.m_paintCleared=true;s.m_paintType=TerrainModifier.PaintType.Dirt;s.m_paintRadius=radius*0.9f;s.m_paintHeightCheck=false;
                Object.Instantiate(_crater,at,Quaternion.identity);   // (applies itself in Awake, sending the dig to each piece of ground's owner, then goes)
            }
        }
        // Inside a ward's area, or near a piece somebody built.
        private static bool Built(Vector3 at,float radius)
        {
            var areas=(List<PrivateArea>)AccessTools.Field(typeof(PrivateArea),"m_allAreas").GetValue(null);
            foreach(PrivateArea a in areas)
                if(a!=null&&Utils.DistanceXZ(a.transform.position,at)<a.m_radius+radius)return true;
            var pieces=new List<Piece>();
            Piece.GetAllPiecesInRadius(at,radius+6f,pieces);
            return pieces.Any(x=>x!=null&&x.IsPlacedByPlayer());
        }

        // Holding the uplink: a red dot where it points and the cooldown gauge.
        internal override void Tick()
        {
            Player p=Player.m_localPlayer;
            bool held=p!=null&&!p.IsDead()&&Things.Held(p,out _)==Uplink;
            Vector3 at=default;
            bool aimed=held&&Aim(p,out at);
            if(!aimed){if(_dot!=null)_dot.SetActive(false);return;}
            if(_dot==null)
            {
                _dot=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(_dot.GetComponent<Collider>());
                _dot.name="WU_UplinkDot";
                _dot.GetComponent<Renderer>().sharedMaterial=Look.Glow(Look.Red);
                _dot.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                _dotLight=Look.AddLight(_dot.transform,Look.Red,3f,1.5f);
            }
            _dot.SetActive(true);
            _dot.transform.position=at;
            float far=Vector3.Distance(GameCamera.instance.transform.position,at);
            _dot.transform.localScale=Vector3.one*Mathf.Clamp(far*0.008f,0.08f,1.5f);
            _dotLight.intensity=1.2f+0.6f*Mathf.Sin(Time.time*12f);
        }
        internal override void Gui()
        {
            Player p=Player.m_localPlayer;
            if(p==null||Things.Held(p,out _)!=Uplink)return;
            float cd=Mathf.Max(0.01f,Cooldown.Value);
            float ready=Time.time>=_readyAt?1f:1f-(_readyAt-Time.time)/cd;
            Hud.Bar(ready>=1f?"Uplink ready":"Uplink charging",ready,ready>=1f?Look.Cyan:Look.Violet);
        }
        internal override void Stop()
        {
            foreach(IonStrike s in IonStrike.All.ToList())if(s!=null)Object.Destroy(s.gameObject);
            IonStrike.All.Clear();
            if(_dot!=null)Object.Destroy(_dot);
            _dot=null;
            if(ObjectDB.instance!=null&&_crater!=null)
            {
                ObjectDB.instance.m_terrainOps.Remove(_crater.GetComponent<TerrainOp>());
                ObjectDB.instance.m_terrainOpsByHash.Remove(CraterPrefab.GetStableHashCode());
            }
            _crater=null;
        }

        internal override string Command=>"ion";
        internal override string Help=>"[info|strike <x> <z> [y]|ready]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"info";
            var inv=System.Globalization.CultureInfo.InvariantCulture;
            switch(sub)
            {
                case "strike":
                {
                    if(args.Length<5){error("ion strike x z [y]");return null;}
                    float x=float.Parse(args[3],inv),z=float.Parse(args[4],inv);
                    float y=args.Length>5?float.Parse(args[5],inv):ZoneSystem.instance!=null&&ZoneSystem.instance.GetSolidHeight(new Vector3(x,0,z),out float h,1000)?h:me.transform.position.y;
                    var at=new Vector3(x,y,z);
                    if(Vector3.Distance(me.transform.position,at)<Radius.Value+3f){error("ion strike: that's on top of you (move or pick a spot further than the radius)");return null;}
                    Strike(me,at);
                    output(new JObject{["at"]=Tools.V(at),["radius"]=Radius.Value,["delay"]=Delay.Value,["crater"]=Crater.Value&&(CraterNearBuildings.Value||!Built(at,Radius.Value)),["registered"]=ObjectDB.instance!=null&&ObjectDB.instance.TryGetTerrainOp(CraterPrefab,out _)});
                    return null;
                }
                case "ready":_readyAt=0;output(new JObject{["ready"]=true});return null;
                default:
                {
                    bool aimed=Aim(me,out Vector3 at);
                    output(new JObject{["held"]=Things.Held(me,out _)==Uplink,["aim"]=aimed?Tools.V(at):null,["readyIn"]=Math.Round(Mathf.Max(0,_readyAt-Time.time),1),
                        ["strikes"]=IonStrike.All.Count,["craterOp"]=ObjectDB.instance!=null&&ObjectDB.instance.TryGetTerrainOp(CraterPrefab,out _)});
                    return null;
                }
            }
        }

        // A chunky handset held upright: grip at the hand (the origin), a screen, a red button, and a long antenna with a red emitter on top.
        private static void BuildModel(Transform root)
        {
            var b=new Look.Builder(root);
            Material body=Look.Paint(Look.Gunmetal,0.5f,0.55f),dark=Look.Paint(Look.Black,0.3f,0.4f),hazard=Look.Paint(Look.Hazard,0.2f,0.5f);
            b.Box("grip",new Vector3(0,0,0),new Vector3(0.05f,0.14f,0.045f),dark);
            b.Box("case",new Vector3(0,0.15f,0),new Vector3(0.085f,0.18f,0.055f),body);
            b.Box("screen",new Vector3(0,0.17f,0.029f),new Vector3(0.06f,0.06f,0.004f),Look.Glow(Look.Cyan,0.8f));
            b.Box("stripe",new Vector3(0,0.085f,0.029f),new Vector3(0.087f,0.012f,0.003f),hazard);
            b.Tube("button",new Vector3(0,0.115f,0.03f),0.022f,0.012f,Look.Paint(Look.Red,0.1f,0.6f,1.5f),'z');
            b.Tube("antenna",new Vector3(0.025f,0.36f,-0.01f),0.008f,0.25f,Look.Paint(Look.Chrome,0.9f,0.8f),'y');
            b.Ball("tip",new Vector3(0.025f,0.49f,-0.01f),Vector3.one*0.025f,Look.Glow(Look.Red));
            b.Box("dish",new Vector3(-0.02f,0.26f,-0.01f),new Vector3(0.05f,0.02f,0.04f),body);
        }
    }

    // One strike as every game sees it: the painted ring and the charge, then the beam, the flash, the shockwave and flying debris.
    internal sealed class IonStrike:MonoBehaviour
    {
        internal static readonly List<IonStrike> All=new List<IonStrike>();
        private float _radius,_delay,_born;
        private bool _mine,_struck;
        private LineRenderer _ring,_inner,_paint,_wave;
        private Light _light;
        private AudioSource _whine;
        private readonly List<(Transform t,Vector3 v,float spin)> _debris=new List<(Transform,Vector3,float)>();
        private readonly List<Material> _own=new List<Material>();
        private GameObject _ball;

        private void Awake()=>All.Add(this);
        private void OnDestroy()
        {
            All.Remove(this);
            foreach(Material m in _own)if(m!=null)Destroy(m);
        }

        internal void Begin(float radius,float delay,bool mine)
        {
            _radius=radius;_delay=delay;_mine=mine;_born=Time.time;
            Vector3 at=transform.position;
            _ring=Circle("ring",Look.Red,0.35f);
            _inner=Circle("inner",Look.Red,0.15f);
            _paint=Line("paint",Look.Red,0.12f);
            _paint.SetPosition(0,at+Vector3.up*500f);_paint.SetPosition(1,at);
            _light=Look.AddLight(transform,Look.Red,radius*1.5f,0f,Vector3.up*2f);
            _whine=Sound.Play(WhineClip(delay),at,0.9f,1f,160f);
        }

        private LineRenderer Line(string name,Color color,float width)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            LineRenderer l=go.AddComponent<LineRenderer>();
            l.useWorldSpace=true;l.positionCount=2;l.widthMultiplier=width;l.numCapVertices=2;
            l.sharedMaterial=Look.Glow(color);
            l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;l.receiveShadows=false;
            return l;
        }
        private LineRenderer Circle(string name,Color color,float width)
        {
            LineRenderer l=Line(name,color,width);
            l.loop=true;l.positionCount=64;
            return l;
        }
        // A circle lying on the ground (each point dropped onto whatever is below it), turned by spin degrees.
        private void Lay(LineRenderer l,float r,float spin,float lift=0.25f)
        {
            Vector3 c=transform.position;
            for(int i=0;i<l.positionCount;i++)
            {
                float a=(i/(float)l.positionCount)*Mathf.PI*2f+spin*Mathf.Deg2Rad;
                Vector3 p=c+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);
                if(Physics.Raycast(p+Vector3.up*30f,Vector3.down,out RaycastHit hit,60f,Hits.Solid,QueryTriggerInteraction.Ignore))p.y=hit.point.y;
                l.SetPosition(i,p+Vector3.up*lift);
            }
        }

        private void Update()
        {
            float t=Time.time-_born;
            if(!_struck)
            {
                float k=Mathf.Clamp01(t/_delay);
                float pulse=0.5f+0.5f*Mathf.Sin(t*(8f+20f*k));
                if(_ring!=null&&(Time.frameCount&1)==0)Lay(_ring,_radius,t*20f);
                if(_inner!=null&&(Time.frameCount&1)==1)Lay(_inner,_radius*(1f-k*0.85f),-t*90f);
                Color c=Look.Red;c.a=0.4f+0.6f*pulse;
                _ring.startColor=_ring.endColor=c;_inner.startColor=_inner.endColor=c;
                _paint.widthMultiplier=0.06f+0.25f*k*pulse;
                _light.intensity=(1f+3f*k)*pulse;
                if(t>=_delay)Strike();
                return;
            }
            float s=t-_delay;
            // The flash and the fireball fade; the shockwave runs out; the debris flies and falls.
            _light.intensity=Mathf.Max(0f,12f*(1f-s/1.6f));
            if(_ball!=null)
            {
                float g=Mathf.Clamp01(s/1.2f);
                _ball.transform.localScale=Vector3.one*_radius*2.2f*Mathf.Sqrt(g);
                Material m=_ball.GetComponent<Renderer>().sharedMaterial;
                var col=new Color(0.6f,0.85f,1f,1f-g);
                if(m.HasProperty("_TintColor"))m.SetColor("_TintColor",col);
                if(m.HasProperty("_Color"))m.SetColor("_Color",col);
                if(g>=1f)Destroy(_ball);
            }
            if(_wave!=null&&s<0.95f)
            {
                float w=Mathf.Clamp01(s/0.9f);
                Lay(_wave,Mathf.Lerp(1f,_radius*2.6f,w),0f,0.6f);
                var col=new Color(0.6f,0.9f,1f,1f-w);_wave.startColor=_wave.endColor=col;
                _wave.widthMultiplier=1.2f*(1f-w)+0.1f;
            }
            float dt=Time.deltaTime;
            for(int i=0;i<_debris.Count;i++)
            {
                var d=_debris[i];
                if(d.t==null)continue;
                d.v+=Physics.gravity*dt;
                d.t.position+=d.v*dt;
                d.t.Rotate(d.spin*dt,d.spin*0.7f*dt,0f);
                _debris[i]=d;
            }
            if(s>4.5f)Destroy(gameObject);
        }

        private void Strike()
        {
            _struck=true;
            Vector3 at=transform.position;
            if(_whine!=null)Destroy(_whine.gameObject);
            if(_ring!=null)Destroy(_ring.gameObject);
            if(_inner!=null)Destroy(_inner.gameObject);
            if(_paint!=null)Destroy(_paint.gameObject);
            // The beam: a wide blue pillar with a white-hot core, straight down from orbit (each game draws it: the RPC was the share).
            Beam.Show(at+Vector3.up*700f,at,new Color(0.35f,0.65f,1f),_radius*0.9f,1.6f,false);
            Beam.Show(at+Vector3.up*700f,at,Color.white,_radius*0.35f,2.2f,false);
            _light.color=new Color(0.7f,0.85f,1f);_light.range=_radius*6f;_light.intensity=12f;
            _light.transform.position=at+Vector3.up*6f;
            _ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(_ball.GetComponent<Collider>());
            _ball.transform.SetParent(transform,false);
            var mat=new Material(Look.Glow(new Color(0.6f,0.85f,1f)));_own.Add(mat);
            _ball.GetComponent<Renderer>().sharedMaterial=mat;
            _ball.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            _wave=Circle("wave",new Color(0.6f,0.9f,1f),1.2f);
            Material rock=Look.Paint(new Color(0.22f,0.2f,0.18f),0f,0.1f),ember=Look.Glow(new Color(1f,0.55f,0.15f));
            for(int i=0;i<36;i++)
            {
                bool hot=i%3==0;
                GameObject chunk=GameObject.CreatePrimitive(hot?PrimitiveType.Sphere:PrimitiveType.Cube);
                Destroy(chunk.GetComponent<Collider>());
                chunk.transform.SetParent(transform,false);
                chunk.transform.position=at+Random.insideUnitSphere*_radius*0.3f+Vector3.up*0.5f;
                chunk.transform.localScale=Vector3.one*(hot?Random.Range(0.15f,0.35f):Random.Range(0.25f,0.8f));
                Renderer r=chunk.GetComponent<Renderer>();r.sharedMaterial=hot?ember:rock;
                if(hot)r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 flat=Random.insideUnitCircle.normalized;
                Vector3 v=new Vector3(flat.x,0,flat.y)*Random.Range(4f,14f)+Vector3.up*Random.Range(8f,22f);
                _debris.Add((chunk.transform,v,Random.Range(-400f,400f)));
            }
            Sound.Play(BoomClip(),at,1f,Random.Range(0.95f,1.05f),450f);
            GameCamera.instance?.AddShake(at,_radius*12f,8f,false);
            if(_mine)Plugin.Safe("ion impact",()=>IonCannon.Impact(at,_radius));
        }

        // A whine climbing to a scream with beeps getting faster, over the charge time.
        private static AudioClip WhineClip(float seconds)=>Sound.Make("ion_whine_"+seconds.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),seconds,t=>
        {
            float k=t/seconds;
            float whine=0.25f*Sound.Saw(Sound.SweepPhase(t,180f,2600f,seconds))+0.2f*Sound.Sine(Sound.SweepPhase(t,360f,5200f,seconds));
            float beepRate=2f+10f*k*k;
            float beepPhase=Mathf.Repeat(t*beepRate,1f);
            float beep=beepPhase<0.25f?0.25f*Sound.Square(t*1800f):0f;
            return (whine*(0.3f+0.7f*k)+beep)*Mathf.Clamp01((seconds-t)*20f);
        });
        // Thunder: a sub-bass drop, a crack of white noise and a long rumble through a low-pass filter.
        private static AudioClip BoomClip()
        {
            float low=0f,low2=0f;
            return Sound.Make("ion_boom",5f,t=>
            {
                float n=Sound.Noise();
                low+=(n-low)*0.03f;low2+=(low-low2)*0.05f;
                float crack=n*Mathf.Exp(-t*14f)*0.9f;
                float rumble=low2*6f*Mathf.Exp(-t*0.7f)*(0.7f+0.3f*Sound.Sine(t*3.1f));
                float bass=Sound.Sine(Sound.SweepPhase(t,70f,22f,5f))*Mathf.Exp(-t*0.9f)*0.9f;
                float a=Mathf.Clamp01(t*400f);
                return a*Mathf.Clamp(crack+rumble+bass,-1f,1f);
            });
        }
    }
}
