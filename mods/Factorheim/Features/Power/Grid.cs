using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // One electric network: poles joined by wires, and the machines, generators and accumulators within a pole's supply reach.
    internal sealed class PowerNet
    {
        internal readonly List<Pole> Poles=new List<Pole>();
        internal readonly List<Machine> Users=new List<Machine>();
        internal readonly List<PowerSource> Sources=new List<PowerSource>();
        internal readonly List<Accumulator> Banks=new List<Accumulator>();
        internal float Capacity,Demand,Supplied,Satisfaction,Stored,StoredMax,Flow;   // W, W, W, 0..1, J, J, W into (+) or out of (-) the banks
    }

    // The solver. Every game works it out for itself from the pieces it has loaded (no network traffic): poles wire themselves to their
    // nearest neighbours in reach, a machine joins the network of the first pole whose supply area touches it, and twice a second each
    // network shares its generators' output among the machines that want to work (Machine.Powered = supplied / wanted). Generators burn
    // fuel in proportion to their load, on their owner's game only (Work). The graph is rebuilt only when poles or machines come or go.
    internal static class PowerGrid
    {
        internal const float Interval=0.5f;
        internal static readonly List<PowerNet> Nets=new List<PowerNet>();
        private static readonly Dictionary<Pole,PowerNet> PoleNet=new Dictionary<Pole,PowerNet>();
        private static readonly Dictionary<Machine,PowerNet> MachineNet=new Dictionary<Machine,PowerNet>();
        private static readonly List<PowerSource> Loose=new List<PowerSource>();
        private static readonly List<(Pole a,Pole b)> Edges=new List<(Pole,Pole)>();
        private static int _signature=int.MinValue;private static float _next,_last;
        internal static float RebuildMs;internal static int Rebuilds;

        internal static PowerNet Of(Pole p)=>p!=null&&PoleNet.TryGetValue(p,out PowerNet n)?n:null;
        internal static PowerNet Of(Machine m)=>m!=null&&MachineNet.TryGetValue(m,out PowerNet n)?n:null;
        internal static int Wires=>Edges.Count;

        internal static void Tick()
        {
            if(Time.time<_next)return;
            _next=Time.time+Interval;
            float dt=_last>0?Mathf.Clamp(Time.time-_last,0.05f,2f):Interval;
            _last=Time.time;
            List<Pole> poles=Live<Pole>.All;List<Machine> machines=Live<Machine>.All;
            int sig=Signature(poles,machines);
            if(sig!=_signature){_signature=sig;Rebuild(poles,machines);}
            Solve(dt);
        }
        // What the graph was made from: if any pole or machine came or went, this changes.
        private static int Signature(List<Pole> poles,List<Machine> machines)
        {
            unchecked
            {
                int s=poles.Count*7919+machines.Count;
                foreach(Pole p in poles)s+=p.GetInstanceID()*31;
                foreach(Machine m in machines)s+=m.GetInstanceID();
                return s;
            }
        }

        internal static void Rebuild()=>_signature=int.MinValue;

        private static void Rebuild(List<Pole> poles,List<Machine> machines)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            Nets.Clear();PoleNet.Clear();MachineNet.Clear();Loose.Clear();Edges.Clear();
            int n=poles.Count;
            // Wires: each pole to its nearest few in reach of both (sorted by distance, then place, so every game wires alike).
            var parent=new int[n];for(int i=0;i<n;i++)parent[i]=i;
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            var joined=new HashSet<long>();
            var near=new List<(float d,int j)>();
            for(int i=0;i<n;i++)
            {
                near.Clear();
                Vector3 a=poles[i].transform.position;
                for(int j=0;j<n;j++)
                {
                    if(j==i)continue;
                    float reach=Mathf.Min(poles[i].Reach,poles[j].Reach),d=Vector3.Distance(a,poles[j].transform.position);
                    if(d<=reach)near.Add((d,j));
                }
                near.Sort((x,y)=>x.d!=y.d?x.d.CompareTo(y.d):Order(poles[x.j]).CompareTo(Order(poles[y.j])));
                for(int k=0;k<near.Count&&k<Pole.MaxWires;k++)
                {
                    int j=near[k].j;
                    long key=(long)Mathf.Min(i,j)*100000+Mathf.Max(i,j);
                    if(!joined.Add(key))continue;
                    Edges.Add((poles[i],poles[j]));
                    parent[Root(i)]=Root(j);
                }
            }
            var byRoot=new Dictionary<int,PowerNet>();
            for(int i=0;i<n;i++)
            {
                int r=Root(i);
                if(!byRoot.TryGetValue(r,out PowerNet net)){net=new PowerNet();byRoot[r]=net;Nets.Add(net);}
                net.Poles.Add(poles[i]);PoleNet[poles[i]]=net;
            }
            // Machines: in the network of the first pole whose supply area (a square, as Factorio's) touches their footprint.
            foreach(Machine m in machines)
            {
                if(m==null)continue;
                bool source=m is PowerSource,bank=m is Accumulator;
                if(!source&&!bank&&m.PowerUse<=0f)continue;
                Bounds b=Footprint(m);
                PowerNet found=null;
                foreach(Pole p in poles)
                {
                    Vector3 c=p.transform.position;
                    float dx=Mathf.Max(b.min.x-c.x,0f,c.x-b.max.x),dz=Mathf.Max(b.min.z-c.z,0f,c.z-b.max.z);
                    if(dx<=p.Supply&&dz<=p.Supply&&c.y>b.min.y-5f&&c.y<b.max.y+5f){found=PoleNet[p];break;}
                }
                if(found==null){if(source)Loose.Add((PowerSource)m);continue;}
                MachineNet[m]=found;
                if(source)found.Sources.Add((PowerSource)m);
                else if(bank)found.Banks.Add((Accumulator)m);
                else found.Users.Add(m);
            }
            Wiring.Draw(Edges);
            RebuildMs=(float)watch.Elapsed.TotalMilliseconds;Rebuilds++;
        }
        private static float Order(Pole p){Vector3 v=p.transform.position;return v.x*1.31f+v.z*0.73f+v.y*0.11f;}
        private static Bounds Footprint(Machine m)
        {
            bool any=false;var b=new Bounds(m.transform.position,Vector3.zero);
            foreach(Collider c in m.GetComponentsInChildren<Collider>())
            {
                if(c.isTrigger)continue;
                if(!any){b=c.bounds;any=true;}else b.Encapsulate(c.bounds);
            }
            return b;
        }

        private static void Solve(float dt)
        {
            foreach(PowerSource s in Loose)if(s!=null)s.Draw=0f;
            foreach(PowerNet net in Nets)
            {
                float cap=0,demand=0,discharge=0,room=0,stored=0,max=0;
                foreach(PowerSource s in net.Sources)if(s!=null)cap+=s.Capacity;
                foreach(Machine u in net.Users)if(u!=null&&u.Wants)demand+=u.PowerUse;
                foreach(Accumulator b in net.Banks)
                {
                    if(b==null)continue;
                    discharge+=Mathf.Min(Accumulator.Rate,b.Charge/dt);room+=Mathf.Min(Accumulator.Rate,(Accumulator.Max-b.Charge)/dt);
                    stored+=b.Charge;max+=Accumulator.Max;
                }
                float fromGen,flow,sat;
                if(cap>=demand){flow=Mathf.Min(cap-demand,room);fromGen=demand+flow;sat=1f;}
                else{float fromBank=Mathf.Min(demand-cap,discharge);flow=-fromBank;fromGen=cap;sat=demand>0?(cap+fromBank)/demand:0f;}
                float load=cap>0?fromGen/cap:0f;
                foreach(PowerSource s in net.Sources)if(s!=null)s.Draw=s.Capacity>0?load:0f;
                // the banks: filled or drained in proportion to what each can take or give
                if(flow>0f&&room>0f)foreach(Accumulator b in net.Banks){if(b!=null)b.Shift(flow*dt*Mathf.Min(Accumulator.Rate,(Accumulator.Max-b.Charge)/dt)/room);}
                else if(flow<0f&&discharge>0f)foreach(Accumulator b in net.Banks){if(b!=null)b.Shift(flow*dt*Mathf.Min(Accumulator.Rate,b.Charge/dt)/discharge);}
                bool any=cap+discharge>0f;
                foreach(Machine u in net.Users)if(u!=null)u.Powered=demand>0f?sat:any?1f:0f;
                net.Capacity=cap;net.Demand=demand;net.Supplied=Mathf.Min(demand,cap+Mathf.Max(0,-flow));net.Satisfaction=demand>0?sat:any?1f:0f;
                net.Stored=stored;net.StoredMax=max;net.Flow=flow;
            }
        }

        internal static void Stop()
        {
            Nets.Clear();PoleNet.Clear();MachineNet.Clear();Loose.Clear();Edges.Clear();
            _signature=int.MinValue;_last=0;
            Wiring.Clear();
            Live<Pole>.Clear();
        }
    }

    // The wires between poles: sagging copper lines, each game its own (nothing networked), redrawn only when the graph changes.
    internal static class Wiring
    {
        private const int Points=10;
        private static GameObject _root;
        private static readonly List<LineRenderer> Lines=new List<LineRenderer>();

        internal static void Draw(List<(Pole a,Pole b)> edges)
        {
            if(_root==null){_root=new GameObject("FactorheimWires");Object.DontDestroyOnLoad(_root);Lines.Clear();}
            Material mat=Look.Unlit(new Color(0.42f,0.24f,0.12f));
            while(Lines.Count<edges.Count)
            {
                var go=new GameObject("wire");go.transform.SetParent(_root.transform,false);
                LineRenderer l=go.AddComponent<LineRenderer>();
                l.useWorldSpace=true;l.positionCount=Points;l.startWidth=l.endWidth=0.03f;l.numCapVertices=0;
                l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;l.receiveShadows=false;
                Lines.Add(l);
            }
            for(int i=0;i<Lines.Count;i++)
            {
                LineRenderer l=Lines[i];
                if(i>=edges.Count||edges[i].a==null||edges[i].b==null){l.enabled=false;continue;}
                l.enabled=true;l.sharedMaterial=mat;
                Vector3 a=edges[i].a.Top,b=edges[i].b.Top;
                float sag=0.08f+0.035f*Vector3.Distance(a,b);
                for(int k=0;k<Points;k++)
                {
                    float t=k/(float)(Points-1);
                    l.SetPosition(k,Vector3.Lerp(a,b,t)+Vector3.down*sag*4f*t*(1f-t));
                }
            }
        }
        internal static void Clear()
        {
            if(_root!=null)Object.Destroy(_root);
            _root=null;Lines.Clear();
        }
    }
}
