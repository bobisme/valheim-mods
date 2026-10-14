using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // One drone in the world. Every game spins its rotors, bobs it and plays its buzz and zaps; the game that launched it (and owns its
    // ZDO) also flies it and picks targets. Nothing big on its ZDO: the synced transform and nothing else.
    internal sealed class DroneBot:MonoBehaviour
    {
        internal static readonly List<DroneBot> All=new List<DroneBot>();
        private const string ZapRpc="WU_DroneZap";
        private ZNetView _nview;
        private Transform _model,_eye;
        private readonly List<Transform> _rotors=new List<Transform>();
        private AudioSource _buzz;
        // The brain (owner only).
        private Player _owner;
        internal ItemDrop.ItemData Item;
        internal int Index;
        internal float Battery=1f;
        private Character _target;
        private float _nextScan,_nextShot,_orphanFor,_launched;
        private Vector3 _vel;
        internal Vector3 Eye=>_eye!=null?_eye.position:transform.position;
        internal string TargetName=>_target!=null?_target.m_name:null;

        private void Awake()
        {
            _nview=GetComponent<ZNetView>();
            All.Add(this);
            _model=transform.Find("model");
            if(_model!=null)
            {
                _eye=_model.Find("eye");
                foreach(Transform t in _model.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith("rotor"))_rotors.Add(t);
            }
            if(_nview!=null&&_nview.IsValid()){_nview.Unregister(ZapRpc);_nview.Register(ZapRpc,RPC_Zap);}   // (an older copy's handler may still be there)
            _buzz=Sound.Loop(BuzzClip(),transform,22f);
            if(_buzz!=null){_buzz.volume=0.22f*Plugin.Volume.Value;_buzz.pitch=Random.Range(0.94f,1.06f);}
        }
        private void OnDestroy()
        {
            All.Remove(this);
            if(_nview!=null&&_nview.IsValid())_nview.Unregister(ZapRpc);
        }

        // A new copy of the mod is loading: this copy's code must stop. Ours go home; anyone else's keep flying with no brain here.
        internal void Unload()
        {
            if(_nview!=null&&_nview.IsValid()&&_nview.IsOwner()&&_owner!=null)Despawn();
            else{if(_buzz!=null)Object.Destroy(_buzz.gameObject);Object.Destroy(this);}
        }

        internal void Launch(Player owner,ItemDrop.ItemData item,int index,float battery)
        {
            _owner=owner;Item=item;Index=index;Battery=battery;_launched=Time.time;
            _nextShot=Time.time+0.8f;
            if(_nview!=null&&_nview.IsValid())_nview.GetZDO().Set("wu_owner",owner.GetPlayerID());
            Sound.Play(UpClip(),transform.position,0.6f,1f,25f);
        }
        internal void Despawn()
        {
            if(_owner!=null)Sound.Play(UpClip(),transform.position,0.5f,0.7f,25f);
            _owner=null;
            if(_nview!=null&&_nview.IsValid())
            {
                if(!_nview.IsOwner())_nview.ClaimOwnership();
                ZNetScene.instance.Destroy(gameObject);
            }
            else Object.Destroy(gameObject);
        }

        private void Update()
        {
            float dt=Time.deltaTime;
            foreach(Transform r in _rotors)r.Rotate(0f,2400f*dt,0f,Space.Self);
            if(_model!=null)_model.localPosition=Vector3.up*Mathf.Sin((Time.time+Index)*2.3f)*0.04f;
            if(_buzz!=null)_buzz.volume=0.22f*Plugin.Volume.Value;
            if(_nview==null||!_nview.IsValid()||!_nview.IsOwner())return;
            if(_owner==null)
            {
                // Owned here but not launched here (the owner left and the zone came to us, or a copy before a reload): it goes.
                _orphanFor+=dt;
                if(_orphanFor>5f)Despawn();
                return;
            }
            Brain(dt);
        }

        private void Brain(float dt)
        {
            Battery-=dt/(Drones.FlightMinutes.Value*60f);
            Vector3 home=_owner.transform.position;
            if(Time.time>=_nextScan){_nextScan=Time.time+0.4f;_target=Pick(home);}
            else if(_target!=null&&(_target.IsDead()||!_target.isActiveAndEnabled))_target=null;

            // Where to be: circling over the shoulder, or out on the side towards the fight (on a short leash).
            float a=Index*2.094f+Time.time*0.55f;
            Vector3 want=home+Vector3.up*(2.2f+0.18f*Index)+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*1.4f;
            if(_target!=null)
            {
                Vector3 to=_target.transform.position-home;to.y=0;
                want+=to.normalized*Mathf.Min(2.5f,to.magnitude*0.3f)+Vector3.up*0.6f;
            }
            if(Vector3.Distance(transform.position,want)>40f){transform.position=want;_vel=Vector3.zero;}   // (the owner teleported)
            Vector3 was=transform.position;
            transform.position=Vector3.SmoothDamp(was,want,ref _vel,0.35f,25f,dt);

            // Face the target (or where the owner faces), tipped into its motion like a real quadcopter.
            Vector3 face=_target!=null?_target.GetCenterPoint()-transform.position:_owner.transform.forward;
            face.y=0;if(face.sqrMagnitude<0.001f)face=transform.forward;
            Quaternion yaw=Quaternion.LookRotation(face.normalized);
            Vector3 local=Quaternion.Inverse(yaw)*_vel;
            Quaternion tilt=Quaternion.Euler(Mathf.Clamp(local.z*4f,-25f,25f),0f,Mathf.Clamp(-local.x*4f,-25f,25f));
            transform.rotation=Quaternion.Slerp(transform.rotation,yaw*tilt,1f-Mathf.Exp(-6f*dt));

            if(_target!=null&&Time.time>=_nextShot&&Time.time-_launched>0.8f)
            {
                _nextShot=Time.time+Drones.FireInterval.Value*Random.Range(0.85f,1.15f);
                Vector3 aim=_target.GetCenterPoint()+Random.insideUnitSphere*0.15f;
                if(!Physics.Linecast(Eye,aim,Hits.Solid,QueryTriggerInteraction.Ignore))Zap(aim);
            }
        }

        // The nearest enemy that means harm (an alerted monster, or anything hostile close by) the drone can see.
        private Character Pick(Vector3 home)
        {
            float range=Drones.Range.Value,best=float.MaxValue;
            Character pick=null;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c==_owner||c.IsDead())continue;
                float d=Vector3.Distance(home,c.transform.position);
                if(d>range||d>=best)continue;
                if(Hits.Friendly(_owner,c))continue;
                BaseAI ai=c.GetBaseAI();
                bool menace=c.IsPlayer()||ai is MonsterAI m&&m.IsAlerted()||d<8f;
                if(!menace)continue;
                if(Physics.Linecast(Eye,c.GetCenterPoint(),Hits.Solid,QueryTriggerInteraction.Ignore))continue;
                best=d;pick=c;
            }
            return pick;
        }

        internal void Zap(Vector3 at)
        {
            if(_owner==null)return;
            float dmg=Drones.Damage.Value;
            var damage=new HitData.DamageTypes{m_pierce=dmg*0.5f,m_lightning=dmg*0.5f};
            Vector3 dir=at-Eye;
            Beam.Fire(_owner,Eye,dir,dir.magnitude+3f,damage,Look.Red,0.035f,4f,Skills.SkillType.None,false,0.12f);
            Battery-=Drones.ShotCost.Value;
            _nview.InvokeRPC(ZNetView.Everybody,ZapRpc);
        }
        private void RPC_Zap(long sender)
        {
            Sound.Play(ZapClip(),Eye,0.5f,Random.Range(0.9f,1.12f),30f);
        }

        // Sounds: a buzz from four small rotors beating against each other, a sharp falling zap, a whirring spin-up.
        private static AudioClip BuzzClip()=>Sound.Make("drone_buzz",1f,t=>
            0.28f*Sound.Saw(t*190f)+0.22f*Sound.Saw(t*197f)+0.16f*Sound.Sine(t*380f)*(0.6f+0.4f*Sound.Sine(t*7f))+0.05f*Sound.Noise());
        private static AudioClip ZapClip()=>Sound.Make("drone_zap",0.16f,t=>
            Sound.Env(t,0.004f,0.16f)*(0.6f*Sound.Square(Sound.SweepPhase(t,2600f,500f,0.16f))*0.5f+0.3f*Sound.Sine(Sound.SweepPhase(t,1800f,300f,0.16f))));
        private static AudioClip UpClip()=>Sound.Make("drone_up",0.6f,t=>
            Sound.Env(t,0.05f,0.6f)*(0.4f*Sound.Saw(Sound.SweepPhase(t,90f,260f,0.6f))+0.08f*Sound.Noise()));

        // The model: a squat body with a red eye at the front (+z), four arms with spinning rotors, and skids. scale 1 = 0.5 m across.
        internal static void BuildModel(Transform root,float scale,bool flying)
        {
            var b=new Look.Builder(root);
            Material body=Look.Paint(Look.Gunmetal,0.6f,0.6f),dark=Look.Paint(Look.Black,0.3f,0.3f),stripe=Look.Paint(Look.Hazard,0.2f,0.5f);
            float s=scale;
            b.Box("body",Vector3.zero,new Vector3(0.22f,0.07f,0.26f)*s,body);
            b.Ball("dome",new Vector3(0,0.04f,-0.01f)*s,new Vector3(0.17f,0.08f,0.19f)*s,dark);
            b.Box("stripeL",new Vector3(-0.112f,0,0.02f)*s,new Vector3(0.005f,0.03f,0.18f)*s,stripe);
            b.Box("stripeR",new Vector3(0.112f,0,0.02f)*s,new Vector3(0.005f,0.03f,0.18f)*s,stripe);
            b.Box("visor",new Vector3(0,0,0.13f)*s,new Vector3(0.1f,0.05f,0.02f)*s,dark);
            b.Ball("lens",new Vector3(0,0,0.142f)*s,Vector3.one*0.04f*s,Look.Glow(Look.Red));
            Transform eye=b.Point("eye",new Vector3(0,0,0.17f)*s);
            if(flying)Look.AddLight(eye,Look.Red,2.5f,1.2f);
            for(int i=0;i<4;i++)
            {
                float ang=45f+90f*i;
                Vector3 dir=Quaternion.Euler(0,ang,0)*Vector3.forward;
                Vector3 tip=dir*0.24f*s;
                b.Box("arm"+i,dir*0.13f*s,new Vector3(0.025f,0.02f,0.2f)*s,body,new Vector3(0,ang,0));
                b.Tube("motor"+i,tip+Vector3.up*0.015f*s,0.045f*s,0.05f*s,dark,'y');
                Look.Builder rotor=b.Group("rotor"+i,tip+Vector3.up*0.045f*s,new Vector3(0,i*37f,0));
                rotor.Box("bladeA",Vector3.zero,new Vector3(0.2f,0.004f,0.025f)*s,dark);
                rotor.Box("bladeB",Vector3.zero,new Vector3(0.025f,0.004f,0.2f)*s,dark);
                if(flying)rotor.Tube("blur",Vector3.zero,0.21f*s,0.002f*s,Look.Glow(Color.white,0.12f),'y');
                b.Ball("tipLight"+i,tip+Vector3.down*0.012f*s,Vector3.one*0.018f*s,Look.Glow(i<2?Look.Green:Look.Red));
            }
            b.Tube("skidL",new Vector3(-0.07f,-0.07f,0)*s,0.012f*s,0.24f*s,dark);
            b.Tube("skidR",new Vector3(0.07f,-0.07f,0)*s,0.012f*s,0.24f*s,dark);
            b.Box("legL",new Vector3(-0.07f,-0.045f,0)*s,new Vector3(0.01f,0.05f,0.1f)*s,dark);
            b.Box("legR",new Vector3(0.07f,-0.045f,0)*s,new Vector3(0.01f,0.05f,0.1f)*s,dark);
        }
    }
}
