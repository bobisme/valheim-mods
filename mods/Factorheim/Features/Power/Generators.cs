using System.Text;
using UnityEngine;

namespace Factorheim
{
    // Something that makes electricity from fuel. The solver sets Draw (0..1: how much of MaxWatts the network draws) on every game;
    // the owner burns fuel at that rate (an idle engine burns nothing, a half-loaded one half). Parts named "flywheel" (turns),
    // "steam"/"smoke" (particles) and "light" under the model come alive while it runs.
    internal abstract class PowerSource:Machine
    {
        internal float Draw;
        internal abstract float MaxWatts{get;}
        protected abstract float SecondsOf(string item);   // seconds at full load one of this fuel lasts; 0: not its fuel
        protected virtual string FuelWords=>"fuel";
        internal virtual bool CanRun=>Burn>0f||FuelStore!=null&&FuelStore.First(x=>SecondsOf(x)>0f)!=null;
        internal float Capacity=>CanRun?MaxWatts:0f;
        internal bool Running=>Draw>0.005f&&CanRun;

        private Transform _wheel;private ParticleSystem[] _puffs;private Light _light;private bool _shown;

        protected override void Awake()
        {
            base.Awake();
            Transform model=transform.Find("Model");
            if(model!=null)
            {
                _wheel=Find(model,"flywheel");
                _puffs=model.GetComponentsInChildren<ParticleSystem>(true);
                _light=model.GetComponentInChildren<Light>(true);
            }
            if(_puffs!=null)foreach(ParticleSystem p in _puffs){var em=p.emission;em.enabled=false;p.Play();}
            if(_light!=null)_light.intensity=0f;
        }
        private static Transform Find(Transform root,string name)
        {
            foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;
            return null;
        }
        protected override void Work(float dt)
        {
            if(Draw<=0.001f)return;
            float use=dt*Draw;
            if(Burn<=0f&&FuelStore!=null)
            {
                string f=FuelStore.First(x=>SecondsOf(x)>0f);
                if(f!=null&&FuelStore.Remove(f))Burn+=SecondsOf(f);
            }
            if(Burn<=0f)return;
            Burn=Mathf.Max(0f,Burn-use);
            Dirty();
        }
        protected override void Update()
        {
            base.Update();
            bool on=Running;
            if(_wheel!=null&&on)_wheel.Rotate(0f,(90f+270f*Draw)*Time.deltaTime,0f,Space.Self);
            if(on!=_shown)
            {
                _shown=on;
                if(_puffs!=null)foreach(ParticleSystem p in _puffs){if(p==null)continue;var em=p.emission;em.enabled=on;}
            }
            if(_light!=null)_light.intensity=Mathf.MoveTowards(_light.intensity,on?0.6f+2.4f*Draw:0f,Time.deltaTime*2f);
        }

        // ---- fuel only (no ore, no output) ----
        public override bool CanPut(string item)=>FuelStore!=null&&SecondsOf(item)>0f&&FuelStore.CanAdd(item);
        public override bool Put(string item){if(!CanPut(item))return false;FuelStore.Add(item);Dirty();return true;}

        protected virtual string Missing()=>null;   // "No water nearby" and the like
        protected override string Status()
        {
            string missing=Missing();
            if(missing!=null)return missing;
            if(!CanRun)return "No "+FuelWords;
            if(PowerGrid.Of(this)==null)return "Not on a network: put a power pole next to it";
            return Running?$"Running: {Watts(MaxWatts*Draw)} of {Watts(MaxWatts)}":"Idle: nothing is drawing power";
        }
        public override string GetHoverText()
        {
            var sb=new StringBuilder(Title);
            sb.Append("\n<color=#ffd27a>").Append(Status()).Append("</color>");
            if(FuelStore!=null)sb.Append("\nFuel: ").Append(List(FuelStore)).Append(Burn>0?$" ({Burn:0}s at full load)":"");
            sb.Append($"\n[<color=yellow><b>1-8</b></color>] Put in {FuelWords}");
            return Localization.instance.Localize(sb.ToString());
        }
    }

    // Boiler and steam engine in one: burns wood, coal or solid fuel, and needs water close by for its boiler.
    internal sealed class SteamEngine:PowerSource
    {
        private bool _water;private float _checkAt;
        internal override float MaxWatts=>900e3f;
        protected override float SecondsOf(string item)=>Fuel.Is(item)?Fuel.Seconds[item]:0f;
        protected override void Setup(){FuelStore=new Store("fh_fuel",50){Total=50,Accepts=Fuel.Is};}
        internal override bool CanRun=>HasWater&&base.CanRun;
        protected override string Missing()=>HasWater?null:"No water nearby: build it within 12 m of a lake or the sea";
        // The sea and lakes are at the world's water level: water nearby if the ground dips under it somewhere within 12 m.
        internal bool HasWater
        {
            get
            {
                if(Time.time<_checkAt)return _water;
                _checkAt=Time.time+20f;
                _water=false;
                if(ZoneSystem.instance==null)return false;
                float level=ZoneSystem.instance.m_waterLevel-0.3f;
                Vector3 c=transform.position;
                for(int r=0;r<=12&&!_water;r+=3)
                    for(int a=0;a<(r==0?1:16)&&!_water;a++)
                    {
                        float ang=a*Mathf.PI/8f;
                        Vector3 p=c+new Vector3(Mathf.Cos(ang)*r,0f,Mathf.Sin(ang)*r);
                        if(ZoneSystem.instance.GetGroundHeight(p,out float h)&&h<level)_water=true;
                    }
                return _water;
            }
        }
    }

    // Burns Surtling cores: a fistful of fire in a cage.
    internal sealed class SurtlingGenerator:PowerSource
    {
        internal override float MaxWatts=>3e6f;
        protected override string FuelWords=>"Surtling cores";
        protected override float SecondsOf(string item)=>item=="SurtlingCore"?120f:0f;
        protected override void Setup(){FuelStore=new Store("fh_fuel",20){Total=20,Accepts=x=>x=="SurtlingCore"};}
    }

    // Burns eitr: ten megawatts and a violet glow.
    internal sealed class EitrReactor:PowerSource
    {
        internal override float MaxWatts=>10e6f;
        protected override string FuelWords=>"eitr";
        protected override float SecondsOf(string item)=>item=="Eitr"?300f:0f;
        protected override void Setup(){FuelStore=new Store("fh_fuel",30){Total=30,Accepts=x=>x=="Eitr"};}
    }

    // Stores what generators make and nobody uses, and gives it back when they can't keep up. Its charge is kept in its ZDO by its
    // owner (once a second at most, and only when it moved by 1%).
    internal sealed class Accumulator:Machine
    {
        internal const float Max=5e6f,Rate=300e3f;
        internal float Charge;
        private Transform _gauge;
        protected override void Setup(){}
        protected override void Work(float dt){}
        protected override void Awake()
        {
            base.Awake();
            Transform model=transform.Find("Model");
            if(model!=null)foreach(Transform t in model.GetComponentsInChildren<Transform>(true))if(t.name=="gauge")_gauge=t;
        }
        internal void Shift(float joules)
        {
            float before=Charge;
            Charge=Mathf.Clamp(Charge+joules,0f,Max);
            if(Owner&&Charge!=before)Dirty();
        }
        protected override void SaveMore(ZDO z){if(Mathf.Abs(z.GetFloat("fh_charge",0f)-Charge)>Max*0.01f||Charge<=0f||Charge>=Max)if(z.GetFloat("fh_charge",-1f)!=Charge)z.Set("fh_charge",Charge);}
        protected override void LoadMore(ZDO z){Charge=z.GetFloat("fh_charge",0f);}
        protected override void Update()
        {
            base.Update();
            if(_gauge!=null){Vector3 s=_gauge.localScale;float f=Mathf.Max(0.02f,Charge/Max);if(Mathf.Abs(s.y-f)>0.005f)_gauge.localScale=new Vector3(s.x,f,s.z);}
        }
        public override bool CanPut(string item)=>false;
        public override bool Put(string item)=>false;
        public override string GetHoverText()
        {
            PowerNet net=PowerGrid.Of(this);
            string state=net==null?"Not on a network: put a power pole next to it":net.Flow>1f?"Charging":net.Flow<-1f?"Draining":"Holding";
            return Localization.instance.Localize($"{Title}\n<color=#ffd27a>{state}</color>\nStored: {Pole.Joules(Charge)} of {Pole.Joules(Max)}\nCharges and gives back up to {Watts(Rate)}");
        }
    }
}
