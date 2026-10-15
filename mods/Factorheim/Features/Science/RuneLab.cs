using System.Linq;
using UnityEngine;

namespace Factorheim
{
    // The rune lab: put rune packs in (by hand or with inserters), and it reads them for the world's current research. A unit needs one of
    // each pack the tier asks for; they're used up when the unit is done. Runs on runes alone at half speed; power brings it to full.
    internal sealed class RuneLab:Machine
    {
        private bool _busy;          // (saved, so everyone sees the ring turn)
        private string _missing="";
        private Transform _ring;private Light _light;
        private float _spin,_shine;

        internal override float PowerUse=>60000f;
        internal override bool Wants=>_busy;
        protected override string MenuHint=>"Research";

        protected override void Setup()
        {
            Input=new Store("fh_in",10){Accepts=Research.IsPack};
            _ring=transform.Find("Model/ring");
            _light=GetComponentInChildren<Light>(true);
        }

        protected override void Work(float dt)
        {
            int tier=Research.Current;
            bool can=tier>0&&Research.Progress(tier)<Research.Cost(tier);
            _missing=can?string.Join(", ",Research.PacksFor(tier).Where(p=>Input.Count(p)<1).Select(Ports.Title)):"";
            can=can&&_missing.Length==0;
            if(can)
            {
                float speed=0.5f+0.5f*Powered;
                Progress+=dt*speed/Mathf.Max(0.5f,Research.Seconds?.Value??10f);
                if(Progress>=1f)
                {
                    Progress=0f;
                    foreach(string p in Research.PacksFor(tier))Input.Remove(p);
                    Research.Add(tier,1);
                }
                Dirty();
            }
            else if(Progress>0f&&tier==0){Progress=0f;Dirty();}
            if(can!=_busy){_busy=can;Dirty();}
        }
        protected override void SaveMore(ZDO z){if(z.GetBool("fh_busy",false)!=_busy)z.Set("fh_busy",_busy);}
        protected override void LoadMore(ZDO z){_busy=z.GetBool("fh_busy",false);}

        protected override string Status()
        {
            int tier=Research.Current;
            if(tier==0)return "Everything is researched";
            string what=$"Researching {Tech.Names[tier]}: {Research.Progress(tier)}/{Research.Cost(tier)}";
            if(_busy)return what+$" (unit {Mathf.RoundToInt(Progress*100)}%{(PowerUse>0&&Powered<0.99f?", half speed without power":"")})";
            return what+(Owner&&_missing.Length>0?$"\nNeeds {_missing}":"\nNeeds "+string.Join(", ",Research.PacksFor(tier).Select(Ports.Title)));
        }

        protected override bool Menu(Humanoid user){ResearchWindow.Open(this);return true;}

        // The rune ring turns and the light swells while it reads (on every game: from the saved busy flag).
        protected override void Update()
        {
            base.Update();
            _shine=Mathf.MoveTowards(_shine,_busy?1f:0.15f,Time.deltaTime*0.8f);
            _spin=Mathf.MoveTowards(_spin,_busy?40f:0f,Time.deltaTime*25f);
            if(_ring!=null&&_spin>0.01f)_ring.Rotate(0f,_spin*Time.deltaTime,0f,Space.Self);
            if(_light!=null){_light.intensity=_shine*(1.4f+0.25f*Mathf.Sin(Time.time*3.1f));_light.enabled=_shine>0.05f;}
        }

        // ---- the look: a round stone plinth, a reading table with a crystal of eitr, and eight rune stones on a ring above it ----
        internal static void Model(Transform t)
        {
            var b=new Look.Builder(t);
            Material stone=Look.Paint(new Color(0.38f,0.37f,0.35f),0f,0.12f),dark=Look.Paint(new Color(0.22f,0.21f,0.2f),0f,0.1f),
                bronze=Look.Paint(new Color(0.72f,0.45f,0.2f),0.85f,0.55f),wood=Look.Paint(new Color(0.36f,0.24f,0.14f),0f,0.2f);
            b.Tube("plinth",new Vector3(0,0.15f,0),1.7f,0.3f,stone,'y');
            b.Tube("step",new Vector3(0,0.35f,0),1.3f,0.1f,dark,'y');
            b.Tube("pillar",new Vector3(0,0.65f,0),0.35f,0.5f,stone,'y');
            b.Box("table",new Vector3(0,0.92f,0),new Vector3(0.9f,0.06f,0.9f),wood);
            b.Box("table rim",new Vector3(0,0.96f,0),new Vector3(0.95f,0.03f,0.95f),bronze);
            Material crystal=Look.Paint(new Color(0.55f,0.35f,1f),0.1f,0.9f,2.5f);
            b.Box("glow crystal",new Vector3(0,1.12f,0),new Vector3(0.14f,0.28f,0.14f),crystal,new Vector3(0,45f,0));
            // four posts holding the ring
            for(int i=0;i<4;i++)
            {
                float a=i*90f+45f;Vector3 dir=Quaternion.Euler(0,a,0)*Vector3.forward;
                b.Box("post",dir*0.72f+new Vector3(0,1.0f,0),new Vector3(0.09f,1.3f,0.09f),dark,new Vector3(0,a,0));
            }
            var ring=b.Group("ring",new Vector3(0,1.62f,0));
            b.Tube("ring band",new Vector3(0,1.62f,0),1.5f,0.04f,bronze,'y');
            Color[] colours={new Color(1f,0.25f,0.15f),new Color(0.3f,1f,0.35f),new Color(0.25f,0.6f,1f),new Color(0.75f,0.3f,1f),new Color(1f,0.85f,0.2f)};
            for(int i=0;i<8;i++)
            {
                float a=i*45f;Vector3 dir=Quaternion.Euler(0,a,0)*Vector3.forward;
                ring.Box("stone",dir*0.72f,new Vector3(0.16f,0.24f,0.06f),stone,new Vector3(0,a,0));
                Material g=Look.Paint(colours[i%colours.Length],0f,0.4f,3f);
                // (a rune: a staff and a branch, on the stone's outer face)
                ring.Box("glow rune",dir*0.755f,new Vector3(0.018f,0.17f,0.008f),g,new Vector3(0,a,0));
                ring.Box("glow branch",dir*0.755f+new Vector3(0,0.03f,0),new Vector3(0.016f,0.09f,0.008f),g,new Vector3(0,a,35f));
            }
            Look.AddLight(t,new Color(0.65f,0.45f,1f),5f,0f,new Vector3(0,1.3f,0));
        }
    }
}
