using System;
using System.Collections.Generic;
using UnityEngine;

namespace WrongUniverse
{
    // The Hover Speeder in the world. How riding is handed over is Quad's (Shared/GroundVehicle.cs in HardHeadHackerHead/valheim-mods),
    // the way the game's saddles do it: E asks the owner, the owner names the rider and hands them the ZDO, so the rider's game runs the
    // physics and everyone else sees ZSyncTransform. The flying is our own: no wheels, rays down to the ground or the water.
    //
    // The body never tips by physics (rotation is frozen and set here): it follows the ground's slope, banks into turns and can't flip.
    // Height: gravity is cancelled near the ground and a spring holds the hull HoverHeight above the highest surface under its pads (or
    // under where it is about to be, so it rises before a slope instead of hitting it). Water counts as ground. Off a cliff it falls.
    // Fields are read in Awake (a hot reload swaps this component on speeders standing in the world: prefab fields would be lost).
    internal sealed class SpeederCraft:MonoBehaviour,Interactable,Hoverable,IDoodadController
    {
        internal static readonly List<SpeederCraft> All=new List<SpeederCraft>();
        private const string UserKey="wu_speeder_user";
        private static int _mask=-1;
        private static string _pose;

        private ZNetView _nview;
        private Rigidbody _body;
        private Transform _seat,_thrust;
        private Transform[] _pads;
        private Light[] _padLights;
        private AudioSource _hum;

        // the rider's keys (sent to the owner until it is theirs) and the owner's flight state
        private float _throttle,_steer,_inputAt=-99f,_sentAt,_sentThrottle,_sentSteer;
        private bool _run,_sentRun,_validUser,_wasOwner;
        private float _userCheckAt,_yaw,_bank,_hopAt,_areaCheckAt,_groundCheckAt;
        private bool _areaReady;
        private readonly Dictionary<Character,float> _rammed=new Dictionary<Character,float>();

        // every game: how fast it is going (for the hum, the glow and the gauge)
        private Vector3 _lastPos;
        internal float Speed{get;private set;}
        internal float Height{get;private set;}=-1f;   // the owner's: hull above the surface under it (-1: none within reach)
        internal bool Contact{get;private set;}
        internal bool Kinematic=>_body!=null&&_body.isKinematic;
        internal bool AreaReady=>_areaReady;
        internal ZNetView View=>_nview;
        // Test drive (wu speeder drive): keys held for the rider until then.
        internal float TestUntil,TestThrottle,TestSteer;
        internal bool TestRun;

        private void Awake()
        {
            _nview=GetComponent<ZNetView>();
            _body=GetComponent<Rigidbody>();
            if(_nview==null||_body==null||_nview.GetZDO()==null){enabled=false;return;}   // (the placement ghost)
            _nview.Register<long>("WU_SpeederRequest",RPC_Request);
            _nview.Register<long>("WU_SpeederRelease",RPC_Release);
            _nview.Register<bool>("WU_SpeederResponse",RPC_Response);
            _nview.Register<float,float,bool>("WU_SpeederControls",RPC_Controls);
            _nview.Register("WU_SpeederHop",RPC_Hop);
            _seat=Find(transform,"seat");_thrust=Find(transform,"thrust");
            var pads=new List<Transform>();var lights=new List<Light>();
            foreach(Transform t in GetComponentsInChildren<Transform>(true))if(t.name.StartsWith("pad")&&t.name!="padlight")pads.Add(t);
            foreach(Light l in GetComponentsInChildren<Light>(true))if(l.name=="padlight")lights.Add(l);
            _pads=pads.ToArray();_padLights=lights.ToArray();
            if(_mask<0)_mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain");
            if(_pose==null)_pose=RiderPose();
            Piece piece=GetComponent<Piece>();
            if(piece!=null)piece.m_canBeRemoved=true;
            _body.mass=300f;_body.linearDamping=0.05f;_body.angularDamping=5f;
            _body.freezeRotation=true;   // (it is turned by hand below, never knocked over)
            _body.interpolation=RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            _body.centerOfMass=new Vector3(0f,0.4f,0f);
            _yaw=Heading();
            _lastPos=transform.position;
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)_hum=Sound.Loop(Speeder.HumClip(),transform,45f);
            All.Add(this);
        }
        private void OnDestroy()
        {
            All.Remove(this);
            if(_nview!=null&&_nview.GetZDO()!=null)
                foreach(string r in new[]{"WU_SpeederRequest","WU_SpeederRelease","WU_SpeederResponse","WU_SpeederControls","WU_SpeederHop"})_nview.Unregister(r);
        }
        private static Transform Find(Transform t,string name)
        {
            if(t.name==name)return t;
            foreach(Transform c in t){Transform f=Find(c,name);if(f!=null)return f;}
            return null;
        }
        // Astride, as on a lox (its saddle's own pose), or sitting as in a chair if the game has no lox.
        private static string RiderPose()
        {
            string set=Speeder.CPose!=null?Speeder.CPose.Value:"";
            if(!string.IsNullOrEmpty(set))return set;
            Sadle s=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("Lox")?.GetComponentInChildren<Sadle>(true):null;
            return s!=null&&!string.IsNullOrEmpty(s.m_attachAnimation)?s.m_attachAnimation:"attach_chair";
        }
        private float Heading()
        {
            Vector3 f=Vector3.ProjectOnPlane(transform.forward,Vector3.up);
            return f.sqrMagnitude<1e-4f?transform.eulerAngles.y:Mathf.Atan2(f.x,f.z)*Mathf.Rad2Deg;
        }

        // ---- getting on and off ----------------------------------------------------------------------------------------------

        public string GetHoverName()=>"Hover Speeder";
        public string GetHoverText()
        {
            Player me=Player.m_localPlayer;
            if(me==null||_seat==null)return "";
            if(me.GetDoodadController() as SpeederCraft==this)return "";
            if(Vector3.Distance(me.transform.position,_seat.position)>4f)return Localization.instance.Localize("Hover Speeder\n<color=#888888>$piece_toofar</color>");
            return Localization.instance.Localize("Hover Speeder\n[<color=yellow><b>$KEY_Use</b></color>] Ride");
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        public float GetHoverOffset()=>0f;
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||!_nview.IsValid()||!(user is Player p))return false;
            if(p.GetDoodadController() as SpeederCraft==this){p.StopDoodadControl();return true;}   // (E on it while riding: get off)
            if(p.IsAttached())return false;
            Mount(p,false);
            return true;
        }
        internal void Mount(Player p,bool anyDistance)
        {
            if(p==null||p.IsAttached()||!_nview.IsValid()||_seat==null)return;
            if(p.IsEncumbered()){p.Message(MessageHud.MessageType.Center,"$se_encumbered_start");return;}
            if(!anyDistance&&Vector3.Distance(p.transform.position,_seat.position)>4f)return;
            if(!PrivateArea.CheckAccess(transform.position))return;
            _nview.InvokeRPC("WU_SpeederRequest",p.GetZDOID().UserID);
        }
        // The owner's game: free (or theirs already)? Then it's theirs, physics and all.
        private void RPC_Request(long sender,long playerId)
        {
            if(!_nview.IsValid()||!_nview.IsOwner())return;
            CheckUser();
            if(GetUser()==playerId||!_validUser)
            {
                _nview.GetZDO().Set(UserKey,playerId);
                _throttle=_steer=0f;_run=false;
                _nview.InvokeRPC(sender,"WU_SpeederResponse",true);
                _nview.GetZDO().SetOwner(sender);
            }
            else _nview.InvokeRPC(sender,"WU_SpeederResponse",false);
        }
        private void RPC_Release(long sender,long playerId)
        {
            if(_nview.IsValid()&&_nview.IsOwner()&&GetUser()==playerId){_nview.GetZDO().Set(UserKey,0L);_throttle=_steer=0f;_run=false;}
        }
        private void RPC_Response(long sender,bool granted)
        {
            Player me=Player.m_localPlayer;
            if(me==null)return;
            if(!granted){me.Message(MessageHud.MessageType.Center,"$msg_inuse");return;}
            me.StartDoodadControl(this);
            me.AttachStart(_seat,gameObject,hideWeapons:true,isBed:false,onShip:false,_pose,new Vector3(0.9f,0.3f,0f));   // (off to the side)
            if(!Speeder.Helped){Speeder.Helped=true;me.Message(MessageHud.MessageType.TopLeft,"W/S throttle, A/D steer, Shift boost, Jump hops, E gets off");}
        }
        public void OnUseStop(Player player)
        {
            if(!_nview.IsValid())return;
            _nview.InvokeRPC("WU_SpeederRelease",player.GetZDOID().UserID);
            player.AttachStop();
        }
        public bool IsValid()=>this!=null;
        public Component GetControlledComponent()=>this;
        public Vector3 GetPosition()=>_seat!=null?_seat.position:transform.position;
        internal long GetUser()=>_nview.IsValid()?_nview.GetZDO().GetLong(UserKey,0L):0L;
        // The rider is a player still near it (not logged out or far off).
        private void CheckUser()
        {
            _validUser=false;
            long user=GetUser();
            if(user==0L||ZNet.instance==null)return;
            foreach(ZDO z in ZNet.instance.GetAllCharacterZDOS())
                if(z.m_uid.UserID==user){_validUser=Vector3.Distance(z.GetPosition(),transform.position)<12f;break;}
        }
        internal Player Rider()
        {
            long user=GetUser();
            if(user==0L)return null;
            foreach(Player p in Player.GetAllPlayers())if(p!=null&&p.GetZDOID().UserID==user)return p;
            return null;
        }

        // ---- the keys (the rider's game, every physics step) -------------------------------------------------------------------

        public void ApplyControlls(Vector3 moveDir,Vector3 lookDir,bool run,bool autoRun,bool block)
        {
            _inputAt=Time.time;
            _throttle=Mathf.Clamp(moveDir.z,-1f,1f);_steer=Mathf.Clamp(moveDir.x,-1f,1f);_run=run;
            if(_nview.IsValid()&&!_nview.IsOwner()&&(Time.time-_sentAt>0.5f||Time.time-_sentAt>0.08f&&(Mathf.Abs(_throttle-_sentThrottle)>0.05f||Mathf.Abs(_steer-_sentSteer)>0.05f||_run!=_sentRun)))
            {
                _sentAt=Time.time;_sentThrottle=_throttle;_sentSteer=_steer;_sentRun=_run;
                _nview.InvokeRPC("WU_SpeederControls",_throttle,_steer,_run);   // (until the ownership arrives: then it's all local)
            }
        }
        private void RPC_Controls(long sender,float throttle,float steer,bool run)
        {
            if(!_nview.IsValid()||!_nview.IsOwner())return;
            _inputAt=Time.time;_throttle=Mathf.Clamp(throttle,-1f,1f);_steer=Mathf.Clamp(steer,-1f,1f);_run=run;
        }
        // Jump while riding: a hop (Speeder's patch keeps the game from taking it as "get off" while moving).
        internal void Hop(){if(_nview.IsValid())_nview.InvokeRPC("WU_SpeederHop");}
        private void RPC_Hop(long sender)
        {
            if(!_nview.IsValid()||!_nview.IsOwner()||!Contact||Time.time<_hopAt)return;
            _hopAt=Time.time+1.1f;
            Vector3 v=_body.linearVelocity;v.y=Mathf.Max(v.y,0f)+Speeder.CHop.Value;
            _body.linearVelocity=v;
            Sound.Play(Speeder.HopClip(),transform.position,0.8f,1f,30f);
        }

        // ---- the owner's game: flying ------------------------------------------------------------------------------------------

        private void FixedUpdate()
        {
            if(!_nview.IsValid())return;
            bool owner=_nview.IsOwner();
            if(owner&&!_wasOwner){_yaw=Heading();_bank=0f;_areaReady=false;}   // (just handed over: start from where it is)
            _wasOwner=owner;
            if(!owner)return;
            float dt=Time.fixedDeltaTime;
            Vector3 pos=_body.position;

            // Not before the ground under it is loaded (it would fall through the world), and never left under the terrain.
            if(Time.time>=_areaCheckAt)
            {
                _areaCheckAt=Time.time+(_areaReady?1f:0.25f);
                _areaReady=ZNetScene.instance!=null&&ZNetScene.instance.IsAreaReady(pos);
            }
            if(!_areaReady){_body.isKinematic=true;return;}
            if(_body.isKinematic){_body.isKinematic=false;_body.linearVelocity=Vector3.zero;}
            if(Time.time>=_groundCheckAt&&pos.y<4000f&&ZoneSystem.instance!=null)   // (dungeons and caves sit far up: no terrain there)
            {
                _groundCheckAt=Time.time+0.5f;
                if(ZoneSystem.instance.GetGroundHeight(pos,out float ground)&&pos.y<ground-1.5f)
                {
                    pos.y=ground+Speeder.CHeight.Value+0.5f;
                    _body.position=pos;_body.linearVelocity=Vector3.zero;
                    transform.position=pos;
                }
            }

            if(Time.time>=_userCheckAt){_userCheckAt=Time.time+0.5f;CheckUser();}
            bool test=Time.time<TestUntil;
            bool ridden=_validUser&&Time.time-_inputAt<0.4f||test;
            float throttle=test?TestThrottle:ridden?_throttle:0f,steer=test?TestSteer:ridden?_steer:0f;
            bool run=test?TestRun:ridden&&_run;

            Vector3 fwd=Quaternion.Euler(0f,_yaw,0f)*Vector3.forward,right=Vector3.Cross(Vector3.up,fwd);
            Vector3 v=_body.linearVelocity;
            Vector3 flat=new Vector3(v.x,0f,v.z);

            // the surface: under each pad, and where it will be in a moment (so it rises before a slope)
            float hover=Speeder.CHeight.Value,top=float.NegativeInfinity;
            Vector3 normal=Vector3.zero;int hits=0;
            foreach(Transform pad in _pads)
                if(Surface(new Vector3(pad.position.x,pos.y,pad.position.z),out float y,out Vector3 n)){top=Mathf.Max(top,y);normal+=n;hits++;}
            Vector3 ahead=pos+flat*0.3f+fwd*1.2f;
            if(Surface(ahead,out float yAhead,out Vector3 nAhead)){top=Mathf.Max(top,yAhead-0.25f);normal+=nAhead*0.5f;}
            Contact=hits>0&&pos.y-top<hover+1.6f;
            Height=hits>0?pos.y-top:-1f;
            if(Contact)
            {
                // gravity cancelled, then a damped spring to the hover height (well damped: it settles, it doesn't bob)
                float err=top+hover-pos.y;
                float a=Mathf.Clamp(-Physics.gravity.y+40f*err-9f*v.y,0f,45f);
                _body.AddForce(Vector3.up*a,ForceMode.Acceleration);
            }
            else if(v.y>0f&&Height>25f)_body.AddForce(Vector3.down*10f,ForceMode.Acceleration);   // (no flying off: it falls back)

            // along: throttle toward cruise or boost, brake harder than it speeds up, coast slowly; sideways: it grips, with a little slide
            float cruise=Speeder.CCruise.Value,boost=Speeder.CBoost.Value;
            float along=Vector3.Dot(flat,fwd),side=Vector3.Dot(flat,right);
            float target,rate;
            if(!ridden){target=0f;rate=10f;}
            else if(throttle>0.1f){target=(run?boost:cruise)*throttle;rate=along<target?Speeder.CAccel.Value*(run?1.4f:1f):Speeder.CAccel.Value*1.5f;}
            else if(throttle<-0.1f){target=along>1f?0f:-Speeder.CReverse.Value;rate=along>0f?Speeder.CAccel.Value*2f:Speeder.CAccel.Value*0.6f;}
            else{target=0f;rate=3f;}
            if(!Contact)rate*=0.15f;   // (in the air it barely steers its speed)
            if(Contact&&normal.sqrMagnitude>0f)
            {
                // too steep to climb: no more push up it (cliffs stop it, hills don't)
                Vector3 nn=normal.normalized;
                float uphill=-Vector3.Dot(new Vector3(nn.x,0f,nn.z),fwd*Mathf.Sign(target==0f?1f:target));
                if(nn.y<0.6f&&uphill>0.2f&&Mathf.Abs(target)>Mathf.Abs(along))target=along;
            }
            along=Mathf.MoveTowards(along,target,rate*dt);
            side*=Mathf.Exp(-(Contact?Speeder.CGrip.Value:0.3f)*dt);
            Vector3 newFlat=fwd*along+right*side;
            _body.linearVelocity=new Vector3(newFlat.x,Mathf.Min(v.y,16f),newFlat.z);   // (climbs steep hills, never launches)

            // turning: slower turns at speed; banking into them
            float speed01=Mathf.Clamp01(Mathf.Abs(along)/Mathf.Max(1f,boost));
            float turn=steer*Speeder.CTurn.Value*Mathf.Lerp(1f,0.55f,speed01)*(ridden?1f:0f);
            _yaw+=turn*dt;
            _bank=Mathf.Lerp(_bank,-steer*Mathf.Clamp01(Mathf.Abs(along)/12f)*28f,1f-Mathf.Exp(-5f*dt));
            Vector3 up=Contact&&normal.sqrMagnitude>0f?Vector3.Slerp(Vector3.up,normal.normalized,0.65f):Vector3.up;
            Quaternion goal=Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0f,_yaw,0f)*Quaternion.Euler(0f,0f,_bank);
            _body.MoveRotation(Quaternion.Slerp(_body.rotation,goal,1f-Mathf.Exp(-10f*dt)));
            _body.angularVelocity=Vector3.zero;

            if(ridden&&Mathf.Abs(along)>8f)Ram(Mathf.Abs(along),along>0f,along>0f?fwd:-fwd);
        }

        // The highest ground or water below a point (within reach), and the ground's slope there.
        private bool Surface(Vector3 at,out float y,out Vector3 normal)
        {
            y=float.NegativeInfinity;normal=Vector3.up;
            Vector3 origin=at+Vector3.up*1.5f;
            bool found=false;
            RaycastHit[] hits=Physics.RaycastAll(origin,Vector3.down,1.5f+Speeder.CHeight.Value+3f,_mask,QueryTriggerInteraction.Ignore);
            float best=float.MaxValue;
            foreach(RaycastHit h in hits)
            {
                if(h.collider.attachedRigidbody==_body||h.distance>=best)continue;
                best=h.distance;y=h.point.y;normal=h.normal;found=true;
            }
            if(at.y<4000f)
            {
                float water=Floating.GetLiquidLevel(at);
                if(water<-1000f&&ZoneSystem.instance!=null)water=ZoneSystem.instance.m_waterLevel;   // (the sea: the same everywhere, waves aside)
                if(water>y&&at.y-water<1.5f+Speeder.CHeight.Value+3f&&at.y-water>-6f){y=water;normal=Vector3.up;found=true;}
            }
            return found;
        }

        // Running into enemies at speed: blunt, from the rider, each one once a second.
        private void Ram(float speed,bool forward,Vector3 dir)
        {
            if(Speeder.CRam.Value<=0f)return;
            Player rider=Rider();
            Vector3 centre=transform.TransformPoint(new Vector3(0f,0.5f,forward?1.6f:-1.6f));
            foreach(Collider c in Physics.OverlapBox(centre,new Vector3(0.7f,0.7f,0.7f),transform.rotation,Hits.Mask,QueryTriggerInteraction.Ignore))
            {
                Character ch=c.GetComponentInParent<Character>();
                if(ch==null||ch==rider||rider==null&&(ch.IsPlayer()||ch.IsTamed()))continue;   // (no rider, no friend to judge by: only wild things)
                if(_rammed.TryGetValue(ch,out float t)&&Time.time<t)continue;
                _rammed[ch]=Time.time+1f;
                var dmg=new HitData.DamageTypes{m_blunt=(speed-6f)*Speeder.CRam.Value};
                if(Hits.Deal(rider,c,c.ClosestPoint(centre),dir+Vector3.up*0.3f,dmg,speed*6f,Skills.SkillType.None,buildings:false,ranged:false))
                    _body.linearVelocity*=0.85f;
            }
        }

        // ---- every game: the look and sound of it ------------------------------------------------------------------------------

        private void Update()
        {
            float dt=Mathf.Max(Time.deltaTime,1e-4f);
            Vector3 p=transform.position;
            Vector3 d=p-_lastPos;d.y=0f;_lastPos=p;
            Speed=Mathf.Lerp(Speed,d.magnitude/dt,1f-Mathf.Exp(-6f*dt));
            float k=Mathf.Clamp01(Speed/Mathf.Max(1f,Speeder.CBoost.Value));
            if(_hum!=null){_hum.volume=(0.18f+0.45f*k)*Plugin.Volume.Value;_hum.pitch=0.75f+1.1f*k;}
            float pulse=0.85f+0.15f*Mathf.Sin(Time.time*9f);
            foreach(Light l in _padLights)if(l!=null)l.intensity=(1.2f+1.2f*k)*pulse;
            if(_thrust!=null){float s=0.4f+1.4f*k;_thrust.localScale=new Vector3(1f,1f,s);}
        }
    }

    // The seat behind the rider: sit with E, move to get off. (The game's Chair, but its rider ignores the hull's colliders.)
    internal sealed class SpeederPillion:MonoBehaviour,Interactable,Hoverable
    {
        private Transform _point,_root;
        private void Awake(){_point=transform.parent!=null?transform.parent.Find("passenger"):null;_root=GetComponentInParent<SpeederCraft>()?.transform;}
        public string GetHoverName()=>"Pillion seat";
        public string GetHoverText()
        {
            Player me=Player.m_localPlayer;
            if(me==null||_point==null)return "";
            if(Vector3.Distance(me.transform.position,_point.position)>4f)return Localization.instance.Localize("Pillion seat\n<color=#888888>$piece_toofar</color>");
            return Localization.instance.Localize("Pillion seat\n[<color=yellow><b>$KEY_Use</b></color>] Sit");
        }
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||_point==null||!(user is Player p)||p.IsAttached()||p.IsEncumbered())return false;
            if(Vector3.Distance(p.transform.position,_point.position)>4f)return false;
            if(Player.GetClosestPlayer(_point.position,0.1f)!=null){p.Message(MessageHud.MessageType.Center,"$msg_blocked");return false;}
            p.AttachStart(_point,_root!=null?_root.gameObject:null,hideWeapons:false,isBed:false,onShip:false,"attach_chair",new Vector3(-0.9f,0.3f,0f));
            return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        public float GetHoverOffset()=>0f;
    }
}
