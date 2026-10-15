using System.Text;
using UnityEngine;

namespace Factorheim
{
    // Something items ride along toward its end and hand on to whatever is in front: lines flow into lines (straight on, or from the
    // side onto one running across), into machines, chests and smelters. Belts and splitters are conveyors.
    internal interface ILine
    {
        bool LineReady{get;}
        Vector3 Forward{get;}
        bool Accept(string item,Vector3 from);   // an item arriving at a world point (on the giver's end): put on the nearest spot
    }

    // A belt or a splitter: lanes of items, simulated on the owner's game only, with their contents in the ZDO ("Prefab@pos;..." per
    // lane, lanes joined by '|') written at most once a second and only when an item came on or went off. Everyone else redraws from
    // the ZDO when it changes and moves the items on at the belt's speed in between (blocking the same way), so a line looks alive
    // without any traffic while it just runs. Not an Update each: the Logistics feature ticks every live conveyor in one loop.
    internal abstract class Conveyor:MonoBehaviour,IPortHost,Hoverable,ILine
    {
        internal const float Height=0.12f,Size=0.22f,ShowDistance=60f;
        internal static int Writes;   // ZDO writes (for `fh belt`)
        private static readonly Collider[] Hits=new Collider[16];
        private static readonly StringBuilder Sb=new StringBuilder(128);
        private static readonly int LineMask=LayerMask.GetMask("piece","piece_nonsolid"),ItemMask=LayerMask.GetMask("item");
        private static int _stagger;

        protected ZNetView View;
        internal Lane[] Lanes;
        protected float[] LaneX;       // each lane's sideways place (local x)
        protected float[] OutX;        // each output's sideways place
        protected float Width=1f;
        internal float Speed;           // metres a second
        private uint _rev;private bool _dirty,_shown;private float _saveAt,_pickAt;
        private ILine[] _line;private IPort[] _port;private float[] _lookAt;

        internal bool Valid=>View!=null&&View.IsValid();
        internal bool Owner=>Valid&&View.IsOwner();
        public bool LineReady=>Owner;
        public Vector3 Forward=>transform.forward;
        internal string Title=>GetComponent<Piece>()?.m_name??name;
        internal int ItemCount{get{int n=0;foreach(Lane l in Lanes)n+=l.Count;return n;}}

        // Lanes, LaneX, OutX, Width and Speed for this kind (by prefab name: fields set on the prefab don't survive a hot reload).
        protected abstract void Configure(string prefab);
        // Hands lane l's front item on; true if it went.
        protected abstract bool Deliver(int lane);

        protected virtual void Awake()
        {
            View=GetComponent<ZNetView>();
            Configure(Utils.GetPrefabName(gameObject.name));
            _line=new ILine[OutX.Length];_port=new IPort[OutX.Length];_lookAt=new float[OutX.Length];
            _pickAt=Time.time+0.5f*(_stagger++%8)/8f;
            Live<Conveyor>.Add(this);
            if(Valid){Load();Hooks.OnDestroyed(GetComponent<WearNTear>(),Spill);}
        }
        protected virtual void OnDestroy()
        {
            if(_dirty&&Owner)Save();
            foreach(Lane l in Lanes)l.Forget();   // (the pictures are children: they go with it)
            Hooks.Remove(GetComponent<WearNTear>(),Spill);
            Live<Conveyor>.Remove(this);
        }
        // Unloading the mod: pictures back to the pool before it is emptied.
        internal void Hide(){foreach(Lane l in Lanes)l.HideAll();_shown=false;}

        internal void Tick(float dt,Vector3 camera)
        {
            if(!Valid)return;
            bool own=View.IsOwner();
            if(!own){uint r=View.GetZDO().DataRevision;if(r!=_rev)Load();}
            float d=Speed*dt;
            foreach(Lane l in Lanes)l.Move(d);
            if(own)
            {
                for(int i=0;i<Lanes.Length;i++)if(Lanes[i].AtEnd&&Deliver(i))_dirty=true;
                if(Time.time>=_pickAt){_pickAt=Time.time+0.5f;PickUp();}
                if(_dirty&&Time.time>=_saveAt)Save();
            }
            Draw(camera);
        }

        // ---- handing on ----
        // Item onto output o (the line or port in front of it); looks for what's in front again every 2 s while it can't.
        protected bool Out(int o,string item)
        {
            Vector3 end=transform.TransformPoint(OutX[o],Height,0.5f);
            if(_line[o]!=null&&(_line[o] as Component)==null)_line[o]=null;   // (torn down)
            if(Give(o,item,end))return true;
            if(Time.time>=_lookAt[o]){_lookAt[o]=Time.time+2f;Look(o,end);}
            return false;
        }
        private bool Give(int o,string item,Vector3 end)
        {
            if(_line[o]!=null)return _line[o].LineReady&&_line[o].Accept(item,end+transform.forward*0.02f);
            IPort p=_port[o];
            return p!=null&&p.Ready&&p.CanPut(item)&&p.Put(item);
        }
        private void Look(int o,Vector3 end)
        {
            _line[o]=null;_port[o]=null;
            Vector3 ahead=end+transform.forward*0.3f;
            int n=Physics.OverlapSphereNonAlloc(ahead,0.2f,Hits,LineMask);
            for(int i=0;i<n;i++)
            {
                ILine line=Hits[i]!=null?Hits[i].GetComponentInParent<ILine>():null;
                if(line==null||ReferenceEquals(line,this))continue;
                if(Vector3.Dot(line.Forward,transform.forward)<-0.5f)continue;   // (running back at us: not a way on)
                _line[o]=line;return;
            }
            _port[o]=Ports.At(ahead,0.35f,this);
        }

        // From another line: on the lane nearest where it arrives, at that spot along it.
        public virtual bool Accept(string item,Vector3 from)
        {
            Vector3 local=transform.InverseTransformPoint(from);
            int lane=NearestLane(local.x);
            if(!Lanes[lane].Insert(item,local.z+0.5f))return false;
            _dirty=true;return true;
        }
        protected int NearestLane(float x)
        {
            int best=0;
            for(int i=1;i<LaneX.Length;i++)if(Mathf.Abs(LaneX[i]-x)<Mathf.Abs(LaneX[best]-x))best=i;
            return best;
        }

        // Things dropped on it (a smelter's bars falling out of its chute, a player's armful) get on.
        private void PickUp()
        {
            Vector3 half=new Vector3(Width*0.5f,0.3f,0.5f);
            int n=Physics.OverlapBoxNonAlloc(transform.TransformPoint(0,0.35f,0),half,Hits,transform.rotation,ItemMask);
            for(int i=0;i<n;i++)
            {
                ItemDrop drop=Hits[i]!=null?Hits[i].GetComponentInParent<ItemDrop>():null;
                IPort g=GroundPort.Of(drop);
                if(g==null||!g.Ready)continue;
                string item=g.Peek();
                Vector3 local=transform.InverseTransformPoint(drop.transform.position);
                Lane lane=Lanes[NearestLane(local.x)];
                float p=Mathf.Clamp01(local.z+0.5f);
                if(item==null||Ports.Prefab(item)==null||!lane.CanInsert(p))continue;
                if(g.Take(item)){lane.Insert(item,p);_dirty=true;}
            }
        }

        // When it's torn down or broken: what rode on it falls off.
        private void Spill()
        {
            if(!Owner)return;
            for(int l=0;l<Lanes.Length;l++)
                for(int i=0;i<Lanes[l].Count;i++)
                {
                    GameObject prefab=Ports.Prefab(Lanes[l].Item[i]);
                    if(prefab!=null)Instantiate(prefab,transform.TransformPoint(LaneX[l],Height+0.2f,Lanes[l].Pos[i]-0.5f),Quaternion.identity);
                }
            foreach(Lane l in Lanes){l.HideAll();l.Count=0;}
            _dirty=false;
        }

        // ---- saving ----
        private void Save()
        {
            _saveAt=Time.time+1f;_dirty=false;
            Sb.Length=0;
            for(int l=0;l<Lanes.Length;l++){if(l>0)Sb.Append('|');Lanes[l].Write(Sb);}
            ZDO z=View.GetZDO();
            string s=Sb.ToString();
            if(z.GetString("fh_belt","")!=s){z.Set("fh_belt",s);Writes++;}
            _rev=z.DataRevision;
        }
        private void Load()
        {
            ZDO z=View.GetZDO();
            _rev=z.DataRevision;
            string[] parts=z.GetString("fh_belt","").Split('|');
            for(int l=0;l<Lanes.Length;l++)Lanes[l].Read(l<parts.Length?parts[l]:"");
        }

        // ---- pictures ----
        private void Draw(Vector3 camera)
        {
            bool show=(transform.position-camera).sqrMagnitude<ShowDistance*ShowDistance;
            if(!show){if(_shown)Hide();return;}
            _shown=true;
            for(int l=0;l<Lanes.Length;l++)
            {
                Lane lane=Lanes[l];
                for(int i=0;i<lane.Count;i++)
                {
                    GameObject v=lane.View[i];
                    if(v==null){v=lane.View[i]=ItemView.Take(lane.Item[i],transform,Size);if(v==null)continue;}
                    v.transform.localPosition=new Vector3(LaneX[l],Height+Size*0.5f,lane.Pos[i]-0.5f);
                }
            }
        }

        // ---- inserters reach in ----
        public IPort Port(Vector3 at)
        {
            Vector3 local=transform.InverseTransformPoint(at);
            return new ConveyorPort(this,NearestLane(local.x),Mathf.Clamp01(local.z+0.5f));
        }
        internal void Changed()=>_dirty=true;

        public string GetHoverName()=>Title;
        public string GetHoverText()
        {
            var sb=new StringBuilder(Title);
            sb.Append($"\n{Speed/Lane.Gap:0.#} items/s");
            int n=ItemCount;
            if(n==0)sb.Append("\nempty");
            else
            {
                sb.Append('\n');bool first=true;
                foreach(Lane l in Lanes)for(int i=0;i<l.Count;i++){if(!first)sb.Append(", ");sb.Append(Ports.Title(l.Item[i]));first=false;}
            }
            return sb.ToString();
        }
        public float GetHoverOffset()=>0f;
    }

    // A spot on a lane: an inserter puts in there, or takes the nearest item from within half a metre.
    internal sealed class ConveyorPort:IPort
    {
        private readonly Conveyor _c;private readonly int _lane;private readonly float _p;
        internal ConveyorPort(Conveyor c,int lane,float p){_c=c;_lane=lane;_p=p;}
        private Lane L=>_c.Lanes[_lane];
        public bool Ready=>_c!=null&&_c.Owner;
        public bool CanPut(string prefab)=>L.CanInsert(_p);
        public bool Put(string prefab){if(!L.Insert(prefab,_p))return false;_c.Changed();return true;}
        public string Peek(System.Func<string,bool> wants=null){int i=L.Nearest(_p,0.5f,wants);return i<0?null:L.Item[i];}
        public bool Take(string prefab)
        {
            int i=L.Nearest(_p,0.5f,x=>x==prefab);
            if(i<0)return false;
            L.RemoveAt(i);_c.Changed();return true;
        }
    }
}
