using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // Laser beams: a bright core in a coloured glow from one point to another, fading out, with a flash where it ends. Fire() also hurts
    // what it hits, and everyone nearby sees the beam (one small RPC per shot). Pistol, rifle, drones and the ion cannon all use this.
    internal static class Beam
    {
        private const string Rpc="WU_Beam";
        private sealed class Live{internal GameObject Go;internal LineRenderer Core,Halo;internal Light Light;internal float Born,Life,Width;internal Color Color;}
        private static readonly List<Live> Lit=new List<Live>();
        private static readonly Stack<Live> Spare=new Stack<Live>();
        private static GameObject _root;
        private static bool _registered;

        // Shoots: hurts the first thing along the ray (within range) and shows the beam to everyone. Returns where the beam ended.
        internal static Vector3 Fire(Character attacker,Vector3 from,Vector3 dir,float range,HitData.DamageTypes damage,Color color,
            float width=0.05f,float push=10f,Skills.SkillType skill=Skills.SkillType.Crossbows,bool buildings=true,float life=0.15f)
        {
            dir.Normalize();
            Vector3 end=from+dir*range;
            if(Hits.Ray(attacker,from,dir,range,out RaycastHit hit,0.05f))
            {
                end=hit.point;
                Hits.Deal(attacker,hit.collider,hit.point,dir,damage,push,skill,buildings);
            }
            Show(from,end,color,width,life,true);
            return end;
        }

        // Just the look (share: also everyone else's game).
        internal static void Show(Vector3 from,Vector3 to,Color color,float width=0.05f,float life=0.15f,bool share=true)
        {
            Draw(from,to,color,width,life);
            if(!share)return;
            Init();
            var pkg=new ZPackage();
            pkg.Write(from);pkg.Write(to);
            pkg.Write(color.r);pkg.Write(color.g);pkg.Write(color.b);
            pkg.Write(width);pkg.Write(life);
            Net.Others(Rpc,pkg);
        }
        private static void Init()
        {
            if(_registered)return;
            _registered=true;
            Net.Register(Rpc,(sender,pkg)=>
            {
                Vector3 from=pkg.ReadVector3(),to=pkg.ReadVector3();
                var c=new Color(pkg.ReadSingle(),pkg.ReadSingle(),pkg.ReadSingle());
                float width=pkg.ReadSingle(),life=pkg.ReadSingle();
                Player me=Player.m_localPlayer;
                if(me!=null&&Vector3.Distance(me.transform.position,from)>250f&&Vector3.Distance(me.transform.position,to)>250f)return;
                Draw(from,to,c,width,life);
            });
        }
        internal static void Start()=>Init();

        private static void Draw(Vector3 from,Vector3 to,Color color,float width,float life)
        {
            if(_root==null){_root=new GameObject("WrongUniverseBeams");Object.DontDestroyOnLoad(_root);}
            Live b=Spare.Count>0?Spare.Pop():Make();
            b.Born=Time.time;b.Life=Mathf.Max(0.02f,life);b.Width=width;b.Color=color;
            b.Core.SetPosition(0,from);b.Core.SetPosition(1,to);
            b.Halo.SetPosition(0,from);b.Halo.SetPosition(1,to);
            b.Core.sharedMaterial=Look.Glow(Color.Lerp(color,Color.white,0.75f));
            b.Halo.sharedMaterial=Look.Glow(color);
            b.Light.transform.position=to-(to-from).normalized*0.3f;
            b.Light.color=color;
            b.Go.SetActive(true);
            Set(b,1f);
            Lit.Add(b);
        }
        private static Live Make()
        {
            var go=new GameObject("beam");
            go.transform.SetParent(_root.transform,false);
            var b=new Live{Go=go};
            b.Halo=Line(go,"halo");b.Core=Line(go,"core");
            b.Light=Look.AddLight(go.transform,Color.white,6f,2f);
            return b;
        }
        private static LineRenderer Line(GameObject parent,string name)
        {
            var go=new GameObject(name);go.transform.SetParent(parent.transform,false);
            LineRenderer l=go.AddComponent<LineRenderer>();
            l.positionCount=2;l.useWorldSpace=true;l.numCapVertices=4;
            l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;l.receiveShadows=false;
            return l;
        }
        private static void Set(Live b,float k)
        {
            b.Core.widthMultiplier=b.Width*0.35f*Mathf.Max(0.2f,k);
            b.Halo.widthMultiplier=b.Width*(1.6f-0.6f*k);
            Color h=b.Color;h.a=k;b.Halo.startColor=b.Halo.endColor=h;
            var c=new Color(1,1,1,k);b.Core.startColor=b.Core.endColor=c;
            b.Light.intensity=2.5f*k;
        }
        internal static void Tick()
        {
            for(int i=Lit.Count-1;i>=0;i--)
            {
                Live b=Lit[i];
                if(b.Go==null){Lit.RemoveAt(i);continue;}
                float k=1f-(Time.time-b.Born)/b.Life;
                if(k<=0f){b.Go.SetActive(false);Lit.RemoveAt(i);Spare.Push(b);continue;}
                Set(b,k*k);
            }
        }
        internal static void Clear()
        {
            if(_root!=null)Object.Destroy(_root);
            _root=null;Lit.Clear();Spare.Clear();_registered=false;
        }
    }
}
