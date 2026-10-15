using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Factorheim
{
    // The rocket silo: takes low density structures, rocket control units and rocket fuel (inserters or by hand), and with 4 MW builds
    // rocket parts into the rocket itself (a count, not an output). The rocket shows section by section as the count rises. When it is
    // complete, Shift+E boards it.
    //
    // The launch is one number on the silo's ZDO: "fh_launch", the world time (ms) it started, plus who rides ("fh_rider"). Every game
    // that has the silo loaded plays the same flight from that time (Flight.Height): the rocket rises, fire and smoke under it, the
    // rumble. The owner empties the silo once the rocket is out of sight. Only the rider's own game runs the cinematic (Flight).
    internal sealed class Silo:Machine
    {
        internal const int Sections=8;
        private int _parts;private long _launch;private long _riderId;private string _rider="";
        private Transform _rocket;private readonly Transform[] _sections=new Transform[Sections];
        private GameObject _exhaust;private Transform _flame,_core;private Light _light;private ParticleSystem _smoke;
        private AudioSource _rumble;private int _beeped=-1;
        internal string Why="";

        internal override string Title=>"Rocket silo";
        internal static int Needed=>Rocket.PartsNeeded?.Value??100;
        internal int Parts=>_parts;
        internal bool Complete=>_parts>=Needed;
        internal bool Launching=>_launch>0;
        internal double Since=>_launch>0&&ZNet.instance!=null?ZNet.instance.GetTimeSeconds()-_launch/1000.0:-1;
        private static Craft Recipe=>Crafts.Find("rocket_part");

        protected override void Setup()
        {
            Input=new Store("fh_in",10);
            Input.Accepts=item=>Recipe!=null&&Recipe.Uses(item);
        }
        protected override void Awake()
        {
            base.Awake();
            _rocket=transform.Find("Model/Rocket");
            if(_rocket!=null)for(int i=0;i<Sections;i++)_sections[i]=_rocket.Find("sec"+i);
            Show();
        }
        protected override void OnDestroy()
        {
            if(_exhaust!=null)Destroy(_exhaust);
            base.OnDestroy();
        }

        internal override float PowerUse=>4e6f;
        internal override bool Wants=>!Launching&&!Complete&&HasInputs;
        private bool HasInputs=>Recipe!=null&&Recipe.In.All(i=>Input.Count(i.item)>=i.n);

        protected override void Work(float dt)
        {
            if(Launching)
            {
                // the rocket is out of sight: an empty silo again
                if(Since>Flight.Gone){_parts=0;_launch=0;_riderId=0;_rider="";Progress=0;Dirty();}
                return;
            }
            if(Complete||!HasInputs){if(Progress!=0){Progress=0;Dirty();}return;}
            float speed=Powered;
            if(speed<=0f)return;
            Progress+=dt*speed/Mathf.Max(0.1f,Recipe.Seconds);Dirty();
            if(Progress<1f)return;
            Progress=0f;
            foreach(var (item,n) in Recipe.In)Input.Remove(item,n);
            _parts++;
        }
        protected override void SaveMore(ZDO z)
        {
            if(z.GetInt("fh_parts",0)!=_parts)z.Set("fh_parts",_parts);
            if(z.GetLong("fh_launch",0L)!=_launch)z.Set("fh_launch",_launch);
            if(z.GetLong("fh_riderid",0L)!=_riderId)z.Set("fh_riderid",_riderId);
            Set(z,"fh_rider",_rider);
        }
        protected override void LoadMore(ZDO z)
        {
            _parts=z.GetInt("fh_parts",0);_launch=z.GetLong("fh_launch",0L);_riderId=z.GetLong("fh_riderid",0L);_rider=z.GetString("fh_rider","");
        }

        protected override string Status()
        {
            if(Launching)return Since<Flight.Liftoff?$"Launch in {Mathf.CeilToInt((float)(Flight.Liftoff-Since))}...":$"{_rider} is on the way to Valhalla";
            if(Complete)return "The rocket is ready. Board it!";
            string s=$"Rocket parts: {_parts}/{Needed}";
            if(!HasInputs)return s+" (needs "+string.Join(", ",Recipe?.In.Select(i=>Ports.Title(i.item))??new string[0])+")";
            if(Powered<=0f)return s+" (no power)";
            return s+$" (building {Mathf.RoundToInt(Progress*100)}%)";
        }
        protected override string MenuHint=>Complete&&!Launching?"Board the rocket":null;
        protected override bool Menu(Humanoid user)=>user is Player p&&Board(p,false);

        // The player gets in and the countdown starts. force: test command (completes the rocket first).
        internal bool Board(Player p,bool force)
        {
            Why="";
            if(p==null||p!=Player.m_localPlayer){Why="not the local player";return false;}
            if(View==null||!View.IsValid()){Why="not loaded";return false;}
            if(!Owner){View.ClaimOwnership();Load();}
            if(Launching){Why="already launching";p.Message(MessageHud.MessageType.Center,"The rocket is already launching");return false;}
            if(Flight.Active){Why="already flying";return false;}
            if(force&&!Complete)_parts=Needed;
            if(!Complete){Why="not complete";return false;}
            _launch=(long)(ZNet.instance.GetTimeSeconds()*1000);_riderId=p.GetPlayerID();_rider=p.GetPlayerName();
            Save();
            Flight.Begin(this,_launch/1000.0);
            return true;
        }
        internal void Fill(int parts)
        {
            if(!Owner){View.ClaimOwnership();Load();}
            _parts=Mathf.Clamp(parts,0,Needed);_launch=0;Save();Show();
        }
        internal JObject State()=>new JObject{["parts"]=_parts,["needed"]=Needed,["complete"]=Complete,["launching"]=Launching,
            ["since"]=System.Math.Round(Since,1),["rider"]=_rider,["owner"]=Owner,["powered"]=Powered,["input"]=Machine.List(Input),
            ["flying"]=Flight.Active,["at"]=Tools.V(transform.position)};

        protected override void Update()
        {
            base.Update();
            Show();
        }

        // What everyone sees: the rocket by sections while it's built, and its flight.
        private void Show()
        {
            if(_rocket==null||View==null||!View.IsValid())return;
            double t=Since;
            if(t<0||t>Flight.Gone)
            {
                int shown=_parts<=0?0:Mathf.Clamp(Mathf.CeilToInt(_parts*(float)Sections/Needed),1,Sections);
                for(int i=0;i<Sections;i++)if(_sections[i]!=null&&_sections[i].gameObject.activeSelf!=(i<shown))_sections[i].gameObject.SetActive(i<shown);
                _rocket.localPosition=Vector3.zero;
                if(_exhaust!=null)Destroy(_exhaust);
                _exhaust=null;_beeped=-1;
                return;
            }
            for(int i=0;i<Sections;i++)if(_sections[i]!=null&&!_sections[i].gameObject.activeSelf)_sections[i].gameObject.SetActive(true);
            float h=Flight.Height(t);
            _rocket.localPosition=new Vector3(0,h,0);
            if(_exhaust==null)MakeExhaust();
            // countdown beeps for everyone around
            int second=Mathf.FloorToInt((float)t);
            if(second!=_beeped&&second<Flight.Liftoff&&second>=1){_beeped=second;Sound.Play(Fx.Beep(second>=Flight.Liftoff-1),transform.position+Vector3.up*3,0.8f,1f,60f);}
            _exhaust.transform.position=_rocket.position;
            float burn=Mathf.Clamp01((float)(t-Flight.Ignite)*1.5f)*(t>Flight.Gone-2?Mathf.Clamp01((float)(Flight.Gone-t)/2):1f);
            float flick=0.85f+0.3f*Mathf.PerlinNoise(Time.time*12f,0.3f);
            float stretch=burn*(t<Flight.Liftoff?0.5f:1f+Mathf.Clamp01((float)(t-Flight.Liftoff)/6f));
            _flame.gameObject.SetActive(burn>0.01f);_core.gameObject.SetActive(burn>0.01f);
            _flame.localScale=new Vector3(flick,stretch*flick,flick);
            _core.localScale=new Vector3(flick,stretch*(2f-flick),flick);
            _light.intensity=burn*flick*4f;
            var em=_smoke.emission;em.rateOverTime=burn>0?(t<Flight.Liftoff+4?90f:35f):0f;
            if(_rumble!=null)_rumble.volume=burn*Plugin.Volume.Value*Mathf.Clamp01(1f-(float)(t-Flight.Liftoff-12)/12f);
        }
        private void MakeExhaust()
        {
            _exhaust=new GameObject("FH_RocketExhaust");
            _exhaust.transform.position=_rocket.position;_exhaust.transform.rotation=transform.rotation;
            GameObject f=Meshes.Add(_exhaust.transform,"rocket","flame"),c=Meshes.Add(_exhaust.transform,"rocket","flamecore");
            _flame=f!=null?f.transform:new GameObject("flame").transform;_core=c!=null?c.transform:new GameObject("core").transform;
            if(f==null)_flame.SetParent(_exhaust.transform,false);
            if(c==null)_core.SetParent(_exhaust.transform,false);
            _light=Look.AddLight(_exhaust.transform,new Color(1f,0.55f,0.2f),45f,0f,new Vector3(0,-1f,0));
            _smoke=Fx.Smoke(_exhaust.transform,new Vector3(0,0.2f,0));
            _rumble=Sound.Loop(Fx.Rumble(),_exhaust.transform,350f);
        }
    }
}
