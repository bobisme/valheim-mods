using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WrongUniverse
{
    // How the laser pistol and rifle are held, done the way Quad's Guns does it (Aim.PoseHeld): after the animation, every frame, the
    // arms are bent (Core/Arms.cs) to hold the gun where it should be, and the gun points at what the crosshair is on. The pistol is held
    // Han Solo style, in the right hand with the left arm out to the side (with a shield, the shield arm is the game's); aiming (block)
    // straightens the arm to eye level. The rifle is always shouldered and sighted down the barrel. Sprinting either drops to a low
    // ready. A shot kicks it, a charging rifle trembles in the hands, an overheated gun is lowered and tipped to vent.
    // The others see it too: our state and pitch sit on our
    // player's ZDO and every game poses everyone near by from that (players without the mod see the game's crossbow carry).
    internal sealed class GunPose:Feature
    {
        internal override string Name=>"Gun poses";

        // Where a gun sits, per gun (tunable live: wu pose).
        internal sealed class Stance
        {
            internal Vector3 Grip;      // the right hand's joint in the model's space (the barrel +z, up +y, the top of the grip at the origin)
            internal Vector3 Support;   // the left hand's joint in the model's space
            internal Vector3 Euler;     // the right hand's turn in the model's space (the same turn as the pistol's tuned HandEuler)
            internal Vector3 Walk,Aim,Sprint;   // the grip from the right shoulder joint: (right, forward, up); the joint sits ~0.13 behind the chest
            internal bool OneHanded;    // held in the right hand alone, the left arm out to the side (Free)
            internal Vector3 Free;      // the empty left hand from the left shoulder joint: (out to the left, forward, up)
            internal float Drop;        // degrees the muzzle drops sprinting
            internal float Kick,Climb;  // a shot pushes it back this far (m) and the muzzle climbs this much (degrees)
        }
        // The pistol, Han Solo style: in the right hand alone, elbow bent at chest height walking, the arm straight out at eye level
        // aiming; the left arm out to the side.
        internal static readonly Stance PistolStance=new Stance{Grip=new Vector3(0,-0.06f,0),Support=new Vector3(-0.03f,-0.085f,0.005f),Euler=new Vector3(0,0,90),
            Walk=new Vector3(0f,0.50f,-0.24f),Aim=new Vector3(0f,0.70f,-0.06f),Sprint=new Vector3(0.02f,0.32f,-0.30f),
            OneHanded=true,Free=new Vector3(0.42f,0.08f,-0.30f),Drop=55f,Kick=0.03f,Climb=12f};
        // The rifle is always shouldered and sighted down the barrel (a little lower walking than aiming); only a sprint lowers it.
        // (the rifle's numbers tuned in the game with wu pose: the shoulder joint sits well back in the body, so the stock only reaches
        // the front of the shoulder with the grip ~0.58 ahead of it; the left hand on the power cell, as far forward as the arm reaches)
        internal static readonly Stance RifleStance=new Stance{Grip=new Vector3(0,-0.06f,0),Support=new Vector3(0,-0.04f,0.13f),Euler=new Vector3(0,0,90),
            Walk=new Vector3(-0.15f,0.56f,-0.08f),Aim=new Vector3(-0.15f,0.58f,-0.05f),Sprint=new Vector3(-0.12f,0.26f,-0.30f),
            Drop=35f,Kick=0.045f,Climb=4f};
        internal static Stance Of(ItemDef d)=>d==Lasers.Pistol?PistolStance:d==Lasers.Rifle?RifleStance:null;

        // One player's gun as the posing needs it: ours from our own game, the others' from their ZDO.
        private sealed class Carry
        {
            internal ItemDef Def;
            internal Vector3 Look;
            internal Vector3? Target;   // ours only: the spot under the crosshair, which the barrel points at
            internal bool Aiming,Sprinting,Vent,Charging,Shield,Arms=true;
            internal float AimT,SprintT,VentT,Recoil,ChargeT;
        }

        private static ConfigEntry<bool> PoseArms;
        private static readonly Carry Mine=new Carry();
        private static readonly Dictionary<Player,Carry> Others=new Dictionary<Player,Carry>();
        private static readonly List<Player> Gone=new List<Player>();
        private static readonly Dictionary<Transform,KeyValuePair<Vector3,Quaternion>> Bases=new Dictionary<Transform,KeyValuePair<Vector3,Quaternion>>();
        private static readonly AccessTools.FieldRef<Humanoid,ItemDrop.ItemData> LeftItem=AccessTools.FieldRefAccess<Humanoid,ItemDrop.ItemData>("m_leftItem");
        private static readonly AccessTools.FieldRef<Character,ZNetView> NView=AccessTools.FieldRefAccess<Character,ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<VisEquipment,GameObject> RightInstance=AccessTools.FieldRefAccess<VisEquipment,GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment,GameObject> LeftInstance=AccessTools.FieldRefAccess<VisEquipment,GameObject>("m_leftItemInstance");

        // What we share: Version*256 + flags (a version we don't know is not posed). Never removed, "none" when no gun is held
        // (a removed ZDO key stays on the other games).
        private const int Version=1,FAim=1,FSprint=2,FVent=4,FCharge=8,FArms=16,None=Version*256;
        private const float PoseRange=60f;
        private static readonly int StateKey="wu_gun_st".GetStableHashCode(),PitchKey="wu_gun_pitch".GetStableHashCode();
        private static readonly int PistolHash="WU_LaserPistol".GetStableHashCode(),RifleHash="WU_LaserRifle".GetStableHashCode();
        private static ZDO _zdo;
        private static int _state=-1;
        private static float _pitch=999f,_nextPitch,_nextSweep;

        // `wu pose hold aim|sprint|vent|charge|off`: a state forced on for pictures (nobody can hold block for a screenshot).
        private static string _hold;

        internal override void Bind(ConfigFile c)
        {
            PoseArms=c.Bind("1 - Lasers","PoseArms",true,"Bend your arms to hold the laser guns: the pistol in one hand with the other arm out to the side, the rifle shouldered and sighted down the barrel; both point at what the crosshair is on, drop low when you sprint and are lowered to vent when overheated. Off: the game's own crossbow carry. Your own setting.");
        }

        // ---- ours --------------------------------------------------------------------------------------------------

        internal static ZDO ZdoOf(Player p)
        {
            ZNetView nview=p!=null?NView(p):null;
            return nview!=null&&nview.IsValid()?nview.GetZDO():null;
        }
        // Something in the left hand (a shield, a torch): the pistol is held one-handed and block is the game's again.
        internal static bool OffHandBusy(Player p)
        {
            if(p==Player.m_localPlayer)return LeftItem(p)!=null;
            ZDO zdo=ZdoOf(p);
            return zdo!=null&&zdo.GetInt(ZDOVars.s_leftItem)!=0;
        }
        internal static bool Aiming;   // set by Lasers.Tick: block held with a gun that aims

        // A shot from our own gun (kind 0 pistol, 1 rifle, 2 a full charge), or from another player's (their RPC).
        internal static void Kick(int kind)=>Mine.Recoil=Mathf.Min(1.6f,Mine.Recoil+(kind==2?1.6f:1f));
        internal static void KickOther(long sender,int kind)
        {
            foreach(KeyValuePair<Player,Carry> kv in Others)
            {
                ZDO zdo=ZdoOf(kv.Key);
                if(zdo!=null&&zdo.GetOwner()==sender){kv.Value.Recoil=Mathf.Min(1.6f,kv.Value.Recoil+(kind==2?1.6f:1f));return;}
            }
        }

        internal override void Tick()=>Publish();

        private static void Publish()
        {
            Player p=Player.m_localPlayer;
            ZDO zdo=ZdoOf(p);
            if(zdo==null||!zdo.IsOwner())return;
            if(zdo!=_zdo){_zdo=zdo;_state=-1;_pitch=999f;}
            ItemDef d=Gun(Things.Held(p,out _));
            int state=None;
            float pitch=0f;
            if(d!=null&&!p.IsDead())
            {
                if(Aiming)state|=FAim;
                if(p.IsRunning())state|=FSprint;
                if(Lasers.Overheated)state|=FVent;
                if(Lasers.Charging)state|=FCharge;
                if(PoseArms.Value)state|=FArms;
                pitch=Mathf.Asin(Mathf.Clamp(p.GetLookDir().y,-1f,1f))*Mathf.Rad2Deg;
            }
            if(state!=_state){zdo.Set(StateKey,state);_state=state;}
            if(d!=null&&Mathf.Abs(pitch-_pitch)>2f&&Time.time>=_nextPitch){_nextPitch=Time.time+0.2f;_pitch=pitch;zdo.Set(PitchKey,pitch);}
        }
        private static ItemDef Gun(ItemDef d)=>d==Lasers.Pistol||d==Lasers.Rifle?d:null;

        // ---- every frame, after the animation ----------------------------------------------------------------------

        internal override void LateTick()
        {
            Player me=Player.m_localPlayer;
            if(me!=null)
            {
                ItemDef d=Gun(Things.Held(me,out _));
                Carry c=Mine;
                if(d!=c.Def){c.AimT=c.SprintT=c.VentT=c.ChargeT=0f;}
                c.Def=d;
                if(d!=null&&!me.IsDead())
                {
                    c.Look=_hold!=null?Vector3.ProjectOnPlane(me.transform.forward,Vector3.up).normalized:me.GetLookDir();   // (a held test state looks level)
                    c.Target=_hold!=null?(Vector3?)null:Crosshair(me);
                    c.Aiming=Aiming||_hold=="aim";
                    c.Sprinting=me.IsRunning()||_hold=="sprint";
                    c.Vent=Lasers.Overheated||_hold=="vent";
                    c.Charging=Lasers.Charging||_hold=="charge";
                    c.Shield=OffHandBusy(me)&&d==Lasers.Pistol;
                    c.Arms=PoseArms.Value;
                    PoseOn(me,c);
                }
            }
            Theirs(me);
            if(_film!=null&&me!=null){string f=_film;_film=null;Film(me,f);}
        }

        // `wu pose film <file>`: the upper body from five sides, rendered right after the pose (Claude Tools' view camera can render
        // before it, on a frame the animator ran, and shows the arms half posed).
        private static string _film;
        private static void Film(Player p,string file)
        {
            Transform spine=Utils.FindChild(p.transform,"Spine2");
            Vector3 c=(spine!=null?spine.position:p.transform.position+Vector3.up*1.3f)+p.transform.forward*0.15f;
            var go=new GameObject("WU_PoseCam");
            try
            {
                Camera cam=go.AddComponent<Camera>();
                cam.fieldOfView=40f;cam.nearClipPlane=0.05f;cam.farClipPlane=60f;cam.enabled=false;
                Camera main=Camera.main;
                if(main!=null)cam.cullingMask=main.cullingMask;
                const int S=480;
                var rt=new RenderTexture(S,S,24);
                var sheet=new Texture2D(S*5,S,TextureFormat.RGB24,false);
                Vector3 f=Vector3.ProjectOnPlane(p.transform.forward,Vector3.up).normalized,rgt=Vector3.Cross(Vector3.up,f);
                Vector3[] offs={rgt*1.6f,rgt*1.0f+f*1.3f,-rgt*1.6f+f*0.1f,f*1.8f+Vector3.up*0.15f,rgt*0.6f-f*1.5f+Vector3.up*0.5f};
                cam.targetTexture=rt;
                for(int i=0;i<offs.Length;i++)
                {
                    go.transform.position=c+offs[i];
                    go.transform.rotation=Quaternion.LookRotation(c-go.transform.position,Vector3.up);
                    cam.Render();
                    RenderTexture.active=rt;
                    sheet.ReadPixels(new Rect(0,0,S,S),i*S,0);
                }
                sheet.Apply();
                RenderTexture.active=null;cam.targetTexture=null;
                System.IO.File.WriteAllBytes(file,sheet.EncodeToPNG());
                UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(sheet);
            }
            finally{UnityEngine.Object.Destroy(go);}
        }

        private static void Theirs(Player me)
        {
            Vector3 eye=me!=null?me.transform.position:Camera.main!=null?Camera.main.transform.position:Vector3.zero;
            foreach(Player p in Player.GetAllPlayers())
            {
                if(p==null||p==me)continue;
                ZDO zdo=ZdoOf(p);
                ItemDef d=null;
                if(zdo!=null&&!p.IsDead()&&(p.transform.position-eye).sqrMagnitude<PoseRange*PoseRange)
                {
                    int r=zdo.GetInt(ZDOVars.s_rightItem),l=zdo.GetInt(ZDOVars.s_leftItem);
                    d=r==PistolHash?Lasers.Pistol:r==RifleHash||l==RifleHash?Lasers.Rifle:null;
                }
                if(d==null){Others.Remove(p);continue;}
                if(!Others.TryGetValue(p,out Carry c))Others[p]=c=new Carry();
                int state=zdo.GetInt(StateKey,0);
                bool shared=state/256==Version;
                c.Def=d;
                c.Aiming=shared&&(state&FAim)!=0;
                c.Sprinting=shared&&(state&FSprint)!=0;
                c.Vent=shared&&(state&FVent)!=0;
                c.Charging=shared&&(state&FCharge)!=0;
                c.Arms=!shared||(state&FArms)!=0;
                c.Shield=d==Lasers.Pistol&&zdo.GetInt(ZDOVars.s_leftItem)!=0;
                Vector3 flat=Vector3.ProjectOnPlane(p.transform.forward,Vector3.up).normalized;   // (their body faces where they look: AlwaysRotate below)
                float pitch=(shared?zdo.GetFloat(PitchKey,0f):0f)*Mathf.Deg2Rad;
                c.Look=flat*Mathf.Cos(pitch)+Vector3.up*Mathf.Sin(pitch);
                PoseOn(p,c);
            }
            if(Time.time<_nextSweep)return;
            _nextSweep=Time.time+2f;
            foreach(Player p in Others.Keys)if(p==null)Gone.Add(p);
            foreach(Player p in Gone)Others.Remove(p);
            Gone.Clear();
            Arms.Prune();
            if(Bases.Count>64){var dead=new List<Transform>();foreach(Transform t in Bases.Keys)if(t==null)dead.Add(t);foreach(Transform t in dead)Bases.Remove(t);}
        }

        // The spot under the crosshair (what the shot will hit), or null when there is nothing within reach or it is behind or right by us.
        private static readonly RaycastHit[] RayHits=new RaycastHit[16];
        private static Vector3? Crosshair(Player me)
        {
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:null;
            if(cam==null)return null;
            int n=Physics.RaycastNonAlloc(cam.position,cam.forward,RayHits,250f,Hits.Mask,QueryTriggerInteraction.Ignore);
            float best=250f;Vector3 at=cam.position+cam.forward*250f;
            for(int i=0;i<n;i++)
            {
                RaycastHit h=RayHits[i];
                if(h.distance>=best||h.collider==null||h.collider.GetComponentInParent<Character>()==me)continue;
                best=h.distance;at=h.point;
            }
            Vector3 to=at-me.transform.position;
            return to.magnitude<2.5f||Vector3.Dot(to,cam.forward)<=0f?(Vector3?)null:at;
        }

        // The model in the hand: the item's copy that VisEquipment made (the pistol in the right hand, the rifle, a bow, in the left).
        private static Transform Model(Player p)
        {
            VisEquipment ve=p.GetComponent<VisEquipment>();
            if(ve==null)return null;
            foreach(GameObject inst in new[]{RightInstance(ve),LeftInstance(ve)})
                if(inst!=null&&Lasers.Find(inst.transform,"wu_muzzle")!=null)return inst.transform;
            return null;
        }

        // The part's resting place, noted the first time it is seen, put back before each frame's pose.
        private static void Rest(Transform t)
        {
            if(!Bases.TryGetValue(t,out KeyValuePair<Vector3,Quaternion> b))Bases[t]=b=new KeyValuePair<Vector3,Quaternion>(t.localPosition,t.localRotation);
            t.localPosition=b.Key;t.localRotation=b.Value;
        }

        private static void PoseOn(Player p,Carry c)
        {
            Stance s=Of(c.Def);
            Transform model=Model(p);
            if(s==null||model==null)return;
            Rest(model);
            float dt=Time.deltaTime;
            c.Recoil=Mathf.Max(0f,c.Recoil-dt*7f);
            if(!c.Arms||p.InDodge()||p.IsSwimming()||p.IsAttached()&&!p.IsAttachedToShip()||p.InEmote()||c.Look.sqrMagnitude<0.0001f)return;   // (the animation has it)
            VisEquipment ve=p.GetComponent<VisEquipment>();
            if(ve==null||ve.m_rightHand==null||ve.m_leftHand==null)return;
            Transform ur=Arms.Upper(ve.m_rightHand),ul=Arms.Upper(ve.m_leftHand);
            if(ur==null||ul==null)return;
            Arms.FromAnimation(ve.m_rightHand);Arms.FromAnimation(ve.m_leftHand);
            Note(p,"anim",ve.m_rightHand.position);

            c.AimT=Mathf.MoveTowards(c.AimT,c.Aiming&&!c.Vent?1f:0f,dt*7f);
            c.SprintT=Mathf.MoveTowards(c.SprintT,c.Sprinting&&!c.Aiming?1f:0f,dt*5f);
            c.VentT=Mathf.MoveTowards(c.VentT,c.Vent?1f:0f,dt*(c.Vent?4f:2.5f));
            c.ChargeT=Mathf.MoveTowards(c.ChargeT,c.Charging?1f:0f,dt/Mathf.Max(0.2f,Lasers.ChargeSeconds));
            float aimT=Smooth(c.AimT),sprintT=Smooth(c.SprintT),ventT=Smooth(c.VentT);

            Vector3 offset=Vector3.Lerp(Vector3.Lerp(s.Walk,s.Aim,aimT),s.Sprint,sprintT);
            offset+=new Vector3(0.03f,-0.08f,-0.10f)*ventT;   // venting: lowered and drawn in
            // the barrel points at what the crosshair is on (from about where the gun is), or along the look for the others
            Vector3 fwd=c.Look.normalized;
            if(c.Target.HasValue)
            {
                Vector3 flat=Vector3.ProjectOnPlane(fwd,Vector3.up).normalized,sideFlat=Vector3.Cross(Vector3.up,flat).normalized;
                Vector3 from=ur.position+sideFlat*offset.x+flat*offset.y+Vector3.up*offset.z;
                Vector3 to=c.Target.Value-from;
                if(to.sqrMagnitude>1f)fwd=to.normalized;
            }
            Vector3 side=Vector3.Cross(Vector3.up,fwd).normalized;
            float kick=c.Recoil;
            // charging: a tremble that grows with the charge, and the gun pulled in tight
            Vector3 shake=Vector3.zero;
            if(c.ChargeT>0f)
            {
                float t=Time.time*38f,a=0.0045f*c.ChargeT*c.ChargeT;
                shake=new Vector3(Mathf.PerlinNoise(t,1.3f)-0.5f,Mathf.PerlinNoise(2.7f,t)-0.5f,0f)*2f*a;
                offset.y-=0.015f*c.ChargeT;
            }
            Quaternion rot=Quaternion.LookRotation(fwd,Vector3.up)
                *Quaternion.Euler(-kick*s.Climb+s.Drop*sprintT+28f*ventT,-14f*ventT,-38f*ventT);   // (venting: muzzle down, turned in and rolled to show the vents)
            Vector3 grip=ur.position+side*offset.x+fwd*offset.y+Vector3.up*offset.z+side*shake.x+Vector3.up*shake.y;
            grip+=rot*new Vector3(0f,0f,-kick*s.Kick);

            // the left hand on the gun (pulled back towards the body until it can reach), unless the gun is held in one hand
            bool twoHands=!s.OneHanded;
            if(twoHands)
            {
                float reach=Arms.Length(ve.m_leftHand)*0.96f;
                Vector3 start=grip;
                for(int i=0;i<3&&reach>0f;i++)
                {
                    float far=(grip+rot*(s.Support-s.Grip)-ul.position).magnitude;
                    if(far<=reach)break;
                    grip-=fwd*(far-reach)*0.8f;
                    if((start-grip).magnitude>0.18f){grip=start-fwd*0.18f;break;}
                }
            }
            // the right hand on the grip, turned the way the pistol's grip was tuned; the gun where the hand ended up (if it couldn't reach)
            Arms.Place(ve.m_rightHand,grip,rot*Quaternion.Inverse(Quaternion.Euler(s.Euler)),side*0.6f+Vector3.down);
            Vector3 at=ve.m_rightHand.position-rot*s.Grip;
            if(twoHands)Arms.Reach(ve.m_leftHand,at+rot*s.Support,-side*0.6f+Vector3.down);
            else if(!c.Shield)   // the free arm out to the side, elbow back and down (a shield's arm is the animation's: it blocks)
            {
                Vector3 flat=Vector3.ProjectOnPlane(fwd,Vector3.up).normalized,left=-Vector3.Cross(Vector3.up,flat).normalized;
                Vector3 free=s.Free+new Vector3(0.04f,0f,0.06f)*aimT-new Vector3(0.14f,0f,0.06f)*sprintT;   // (aiming: out a little further for balance; sprinting: in)
                Arms.Reach(ve.m_leftHand,ul.position+left*free.x+flat*free.y+Vector3.up*free.z,-flat+Vector3.down*0.5f);
            }
            model.rotation=rot;   // (last: the rifle, a bow to the game, hangs from the left hand, and bending that arm moved it)
            model.position=at;
            Arms.Leave(ve.m_rightHand);Arms.Leave(ve.m_leftHand);
            Note(p,"want",grip);Note(p,"got",ve.m_rightHand.position);Note(p,"model",model.position);
        }
        private static float Smooth(float t)=>t*t*(3f-2f*t);

        internal override void Stop()
        {
            ZDO zdo=ZdoOf(Player.m_localPlayer);
            if(zdo!=null&&zdo.IsOwner()&&zdo.GetInt(StateKey,None)!=None)zdo.Set(StateKey,None);
            _zdo=null;_state=-1;
            foreach(KeyValuePair<Transform,KeyValuePair<Vector3,Quaternion>> kv in Bases)
                if(kv.Key!=null){kv.Key.localPosition=kv.Value.Key;kv.Key.localRotation=kv.Value.Value;}
            Bases.Clear();Others.Clear();Arms.Clear();_hold=null;Aiming=false;
        }

        // ---- the test command --------------------------------------------------------------------------------------

        internal override string Command=>"pose";
        internal override string Help=>"info | trace [frames] | film [file] | hold aim|sprint|vent|charge|off | kick [2] | <pistol|rifle> <grip|support|euler|walk|aim|sprint|free> x y z | <pistol|rifle> <drop|kick|climb> v";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>2?args[2]:"info";
            var inv=CultureInfo.InvariantCulture;
            switch(sub)
            {
                case "info":output(new JObject{["pistol"]=Dump(PistolStance),["rifle"]=Dump(RifleStance),["hold"]=_hold,["aiming"]=Aiming,["held"]=Mine.Def?.Prefab,
                    ["aimT"]=Math.Round(Mine.AimT,2),["sprintT"]=Math.Round(Mine.SprintT,2),["ventT"]=Math.Round(Mine.VentT,2),["target"]=Mine.Target.HasValue?Tools.V(Mine.Target.Value):null,["others"]=Others.Count});return null;
                case "hold":_hold=args.Length>3&&args[3]!="off"?args[3]:null;output(new JObject{["hold"]=_hold});return null;
                case "trace":
                {
                    // Per frame: where the animation put the right hand (before our pose) and where it ended up, both from the player.
                    _trace=new JArray();
                    return Trace(args.Length>3?int.Parse(args[3]):12,output);
                }
                case "film":
                {
                    string file=args.Length>3?string.Join(" ",args,3,args.Length-3):System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"claude","shots","wu_pose.png");
                    _film=file;
                    return FilmWait(file,output);
                }
                case "kick":Kick(args.Length>3&&args[3]=="2"?2:0);output(new JObject{["recoil"]=Mine.Recoil});return null;
                case "pistol":case "rifle":
                {
                    Stance s=sub=="pistol"?PistolStance:RifleStance;
                    string field=args.Length>3?args[3]:"";
                    try
                    {
                        float F(int i)=>float.Parse(args[i],inv);
                        Vector3 V3()=>new Vector3(F(4),F(5),F(6));
                        switch(field)
                        {
                            case "grip":s.Grip=V3();break;case "support":s.Support=V3();break;case "euler":s.Euler=V3();break;
                            case "walk":s.Walk=V3();break;case "aim":s.Aim=V3();break;case "sprint":s.Sprint=V3();break;case "free":s.Free=V3();break;
                            case "drop":s.Drop=F(4);break;case "kick":s.Kick=F(4);break;case "climb":s.Climb=F(4);break;
                            default:error("wu pose "+sub+" <grip|support|euler|walk|aim|sprint|free> x y z | <drop|kick|climb> v");return null;
                        }
                    }
                    catch(Exception){error("wu pose "+sub+" "+field+": numbers, please");return null;}
                    output(Dump(s));return null;
                }
            }
            error("wu pose: "+Help);
            return null;
        }
        private static IEnumerator FilmWait(string file,Action<JObject> output)
        {
            for(int i=0;i<5&&_film!=null;i++)yield return null;
            output(new JObject{["file"]=file});
        }
        private static JArray _trace;
        private static IEnumerator Trace(int frames,Action<JObject> output)
        {
            for(int i=0;i<frames;i++)yield return null;
            JArray t=_trace;_trace=null;
            output(new JObject{["frames"]=t});
        }
        private static void Note(Player p,string what,Vector3 at)
        {
            if(_trace==null||p!=Player.m_localPlayer)return;
            Vector3 l=p.transform.InverseTransformPoint(at);
            _trace.Add($"{Time.frameCount} {what} {l.x:F3} {l.y:F3} {l.z:F3}");
        }
        private static JObject Dump(Stance s)=>new JObject{["grip"]=Tools.V(s.Grip),["support"]=Tools.V(s.Support),["euler"]=Tools.V(s.Euler),["walk"]=Tools.V(s.Walk),
            ["aim"]=Tools.V(s.Aim),["sprint"]=Tools.V(s.Sprint),["free"]=Tools.V(s.Free),["drop"]=s.Drop,["kick"]=s.Kick,["climb"]=s.Climb};
    }

    // Block with a laser gun aims (Lasers.Tick reads the button itself), so the game must not take it as blocking: that raises the
    // weapon across the chest. A pistol with a shield in the other hand blocks with the shield as ever. Block + jump still rolls.
    [HarmonyPatch(typeof(Player),nameof(Player.SetControls))]
    internal static class GunBlock
    {
        private static void Prefix(Player __instance,ref bool block,ref bool blockHold,bool jump)
        {
            if(__instance!=Player.m_localPlayer||jump&&blockHold)return;
            ItemDef d=Things.Of(__instance.GetCurrentWeapon());
            if(d!=Lasers.Rifle&&!(d==Lasers.Pistol&&!GunPose.OffHandBusy(__instance)))return;
            block=false;blockHold=false;
        }
    }

    // With a laser gun in hand the body faces where the camera looks (as while drawing a bow), so the gun points where you look.
    [HarmonyPatch(typeof(Player),"AlwaysRotateCamera")]
    internal static class GunFacing
    {
        private static void Postfix(Player __instance,ref bool __result)
        {
            if(__result||__instance!=Player.m_localPlayer||__instance.InDodge())return;
            ItemDef d=Things.Of(__instance.GetCurrentWeapon());
            if(d==Lasers.Pistol||d==Lasers.Rifle)__result=true;
        }
    }
}
