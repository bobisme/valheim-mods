using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PassengerCart
{
    // The passenger cart prefab: the vanilla cart, bigger, with two benches of two seats and a cargo crate at the back.
    internal static class CartPrefab
    {
        internal const string Name="BobPassengerCart",DisplayName="Passenger Cart";
        internal static int Hash=>Name.GetStableHashCode();
        internal static GameObject Prefab=>_prefab;
        private static GameObject _holder,_prefab;

        private static GameObject Build(ZNetScene scene)
        {
            if(_prefab!=null)return _prefab;
            GameObject vanilla=scene.GetPrefab("Cart");
            if(vanilla==null){Debug.LogWarning("[PassengerCart] The game's Cart was not found; the passenger cart is not added.");return null;}
            _holder=new GameObject(Name+"Prefabs");
            _holder.SetActive(false); // keeps the template from waking up as a real object
            Object.DontDestroyOnLoad(_holder);
            GameObject go=Object.Instantiate(vanilla,_holder.transform,false);
            go.name=Name;
            go.transform.localPosition=Vector3.zero;
            go.transform.localScale=Vector3.one*(float)Policy.Scale;

            var vagon=go.GetComponent<Vagon>();
            vagon.m_name=DisplayName;
            vagon.m_baseMass=(float)Policy.Mass(vagon.m_baseMass);
            vagon.m_loadVis.Clear(); // the cargo pile would sit on the seats

            var piece=go.GetComponent<Piece>();
            piece.m_name=DisplayName;
            piece.m_description="A bigger cart with two benches: seats for four, and a crate at the back for cargo. Sturdier and steadier than the cart.";
            piece.m_resources=new[]{Requirement(scene,"Wood",30),Requirement(scene,"BronzeNails",16),Requirement(scene,"DeerHide",4)}.Where(r=>r!=null).ToArray();

            var wear=go.GetComponent<WearNTear>();
            if(wear!=null)wear.m_health=(float)Policy.Health(wear.m_health);

            // Everything added is placed in metres in the cart's frame, under one unscaled holder.
            var fittings=new GameObject("Fittings").transform;
            fittings.SetParent(go.transform,false);
            fittings.localScale=Vector3.one/(float)Policy.Scale;

            Cargo(go.transform,fittings);
            Benches(go.transform,fittings);
            Seats(fittings);
            go.AddComponent<Carriage>();
            _prefab=go;
            return go;
        }

        private static Piece.Requirement Requirement(ZNetScene scene,string item,int amount)
        {
            ItemDrop drop=scene.GetPrefab(item)?.GetComponent<ItemDrop>();
            if(drop==null){Debug.LogWarning("[PassengerCart] Missing ingredient "+item);return null;}
            return new Piece.Requirement{m_resItem=drop,m_amount=amount,m_recover=true};
        }

        // Two of the vanilla load crates at the back; the cart's container is opened from them.
        private static void Cargo(Transform root,Transform fittings)
        {
            Transform load=root.Find("load");
            var crates=new[]{"default","default (1)"}.Select(n=>load!=null?load.Find(n):null).Where(t=>t!=null).ToList();
            for(int i=0;i<crates.Count;i++)
            {
                Transform crate=crates[i];
                foreach(Collider c in crate.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                crate.SetParent(fittings,false);
                crate.localPosition=new Vector3((i==0?-1:1)*(float)Policy.CargoWidth/4,(float)Policy.Floor,(float)Policy.CargoZ);
                crate.localRotation=Quaternion.Euler(0,i==0?4:-7,0);
                crate.localScale=Vector3.one;
                crate.gameObject.SetActive(true);
            }
            if(load!=null)Object.DestroyImmediate(load.gameObject);

            Transform box=root.Find("Container");
            if(box==null)return;
            foreach(Collider c in box.GetComponents<Collider>())Object.DestroyImmediate(c);
            box.SetParent(fittings,false);
            box.localRotation=Quaternion.identity;
            box.localScale=Vector3.one;
            box.localPosition=new Vector3(0,(float)(Policy.Floor+Policy.CargoHeight/2+0.03),(float)Policy.CargoZ);
            var hover=box.gameObject.AddComponent<BoxCollider>(); // reaches above the walls, so it is found before the bed
            hover.size=new Vector3((float)Policy.CargoWidth,(float)Policy.CargoHeight,(float)Policy.CargoDepth);
            Container container=box.GetComponent<Container>();
            if(container!=null)container.m_name=DisplayName;
        }

        // Plank benches from the cart's own wood: a seat, a backrest and two end blocks per row. Looks only; no colliders.
        private static void Benches(Transform root,Transform fittings)
        {
            Transform plank=root.Find("Vagon/new/Vagon (1)");
            Mesh mesh=plank?.GetComponent<MeshFilter>()?.sharedMesh;
            Material wood=plank?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if(mesh==null||wood==null){Debug.LogWarning("[PassengerCart] Cart wood not found; the benches are not drawn.");return;}
            float top=(float)Policy.SeatTop,floor=(float)Policy.Floor,w=(float)Policy.BenchWidth,d=(float)Policy.SeatDepth;
            for(int r=0;r<Policy.Rows.Length;r++)
            {
                float z=(float)Policy.Rows[r];
                var bench=new GameObject("Bench"+r).transform;
                bench.SetParent(fittings,false);
                Board(bench,mesh,wood,new Vector3(0,top-0.03f,z),new Vector3(w,0.06f,d));
                Board(bench,mesh,wood,new Vector3(0,top+0.24f,z-d/2+0.03f),new Vector3(w,0.36f,0.05f),-8);
                foreach(float x in new[]{-w/2+0.04f,w/2-0.04f})
                    Board(bench,mesh,wood,new Vector3(x,(floor+top-0.06f)/2,z),new Vector3(0.08f,top-0.06f-floor,d-0.06f));
            }
        }
        private static void Board(Transform parent,Mesh mesh,Material material,Vector3 position,Vector3 size,float tilt=0)
        {
            var go=new GameObject("Board");
            go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(tilt,0,0);go.transform.localScale=size;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=material;
        }

        private static void Seats(Transform fittings)
        {
            Policy.Seat[] seats=Policy.Seats();
            int layer=LayerMask.NameToLayer("piece_nonsolid"); // found by the use ray, walked through by everyone
            for(int i=0;i<seats.Length;i++)
            {
                var go=new GameObject("Seat"+i);
                go.layer=layer;
                go.transform.SetParent(fittings,false);
                go.transform.localPosition=new Vector3((float)seats[i].X,(float)Policy.SeatTop+0.3f,(float)seats[i].Z);
                var box=go.AddComponent<BoxCollider>(); // from the seat up past the walls, so looking down into the cart finds it first
                box.size=new Vector3((float)Policy.SeatWidth*0.9f,0.7f,(float)Policy.SeatDepth+0.05f);
                var attach=new GameObject("attach").transform;
                attach.SetParent(go.transform,false);
                attach.localPosition=new Vector3(0,-0.3f-(float)Policy.SitDrop,0);
                Seat.Fit(go,i);
            }
        }

        // A hot reload leaves carts already in the world running the old copy's code: give them this copy's components.
        internal static void Refresh()
        {
            if(ZNetScene.instance==null)return;
            foreach(ZNetView view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if(view==null||!view.IsValid()||view.GetZDO().GetPrefab()!=Hash||view.GetComponent<Carriage>()!=null)continue;
                foreach(MonoBehaviour stale in view.GetComponentsInChildren<MonoBehaviour>(true))
                    if(stale!=null&&(stale.GetType().FullName==typeof(Carriage).FullName||stale.GetType().FullName==typeof(Seat).FullName))Object.DestroyImmediate(stale);
                Transform fittings=view.transform.Find("Fittings");
                if(fittings==null)continue;
                for(int i=0;i<Policy.Seats().Length;i++)
                {
                    Transform seat=fittings.Find("Seat"+i);
                    if(seat!=null)Seat.Fit(seat.gameObject,i);
                }
                view.gameObject.AddComponent<Carriage>();
            }
        }

        internal static void Register(ZNetScene scene)
        {
            GameObject prefab=Build(scene);
            if(prefab==null)return;
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            named[Hash]=prefab; // a host without it deletes saved carts as invalid
            AddToHammer();
        }
        internal static void AddToHammer()
        {
            if(_prefab==null||ObjectDB.instance==null)return;
            PieceTable table=ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null)return;
            table.m_pieces.RemoveAll(p=>p==null||(p!=_prefab&&p.name==Name)); // an earlier copy's destroyed or stale entry
            if(table.m_pieces.Contains(_prefab))return;
            int vanilla=table.m_pieces.FindIndex(p=>p!=null&&p.name=="Cart");
            if(vanilla>=0)table.m_pieces.Insert(vanilla+1,_prefab);else table.m_pieces.Add(_prefab);
        }
        internal static void Unregister()
        {
            if(_prefab==null)return;
            PieceTable table=ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            table?.m_pieces.Remove(_prefab);
            if(ZNetScene.instance!=null)
            {
                ZNetScene.instance.m_prefabs.Remove(_prefab);
                var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(ZNetScene.instance);
                if(named.TryGetValue(Hash,out GameObject current)&&current==_prefab)named.Remove(Hash); // only our own entry
            }
            Object.Destroy(_holder);_holder=null;_prefab=null;
        }
    }

    // On the cart: steadier physics, passengers' bodies kept out of the cart's, and passengers off a cart that tips over.
    internal sealed class Carriage:MonoBehaviour
    {
        internal static readonly List<Carriage> Loaded=new List<Carriage>();
        internal Seat[] Seats;
        private ZNetView _view;
        private Rigidbody _body;
        private Collider[] _colliders;
        private readonly HashSet<Player> _ignoring=new HashSet<Player>();
        private float _next;

        private void Awake()
        {
            _view=GetComponent<ZNetView>();
            if(_view==null||_view.GetZDO()==null){enabled=false;return;}
            Loaded.Add(this);
            Seats=GetComponentsInChildren<Seat>(true);
            _colliders=GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger).ToArray();
            _body=GetComponent<Rigidbody>();
            if(_body!=null)
            {
                Vector3 centre=_body.centerOfMass;
                _body.centerOfMass=new Vector3(centre.x,(float)Policy.CenterOfMassHeight,centre.z);
                _body.angularDamping=Mathf.Max(_body.angularDamping,(float)Policy.AngularDamping);
            }
        }
        private void OnDestroy()
        {
            Loaded.Remove(this);
            foreach(Player player in _ignoring)if(player!=null)Ignore(player,false);
            _ignoring.Clear();
        }

        private void Update()
        {
            if(Time.time<_next)return;
            _next=Time.time+0.2f;
            // Every client keeps every passenger's body out of the cart: a remote passenger's body would shove it.
            foreach(Player player in Player.GetAllPlayers())
            {
                if(player==null)continue;
                bool seated=SeatOf(player)!=null;
                if(seated&&_ignoring.Add(player))Ignore(player,true);
                else if(!seated&&_ignoring.Remove(player))Ignore(player,false);
            }
            _ignoring.RemoveWhere(p=>p==null);

            Player local=Player.m_localPlayer;
            if(local!=null&&local.IsAttached()&&Seats.Any(s=>s!=null&&local.GetAttachPoint()==s.m_attachPoint)&&Policy.Tipped(transform.up.y))
            {
                local.AttachStop();
                local.Message(MessageHud.MessageType.Center,"The cart tipped over!");
            }
        }

        internal Seat SeatOf(Player player)
        {
            Vector3 at=player.transform.position;
            foreach(Seat seat in Seats)
                if(seat!=null&&seat.m_attachPoint!=null&&Policy.Taken(Vector3.Distance(at,seat.m_attachPoint.position)))return seat;
            return null;
        }
        internal int Passengers()=>Player.GetAllPlayers().Count(p=>p!=null&&SeatOf(p)!=null);

        private void Ignore(Player player,bool ignore)
        {
            var body=player.GetComponent<CapsuleCollider>();
            if(body==null)return;
            foreach(Collider c in _colliders)if(c!=null)Physics.IgnoreCollision(body,c,ignore);
        }
    }

    // One seat. Like the game's Chair, but the passenger's body ignores the cart's colliders, so sitting never pushes the cart.
    internal sealed class Seat:MonoBehaviour,Hoverable,Interactable
    {
        // Public, so spawning a cart from the template copies them.
        public Transform m_attachPoint;
        public Vector3 m_detachOffset;
        private static float _lastSit;

        private void Awake()
        {
            if(m_attachPoint==null)m_attachPoint=transform.Find("attach");
        }

        internal static void Fit(GameObject go,int index)
        {
            Policy.Seat place=Policy.Seats()[index];
            var seat=go.AddComponent<Seat>();
            seat.m_attachPoint=go.transform.Find("attach");
            seat.m_detachOffset=new Vector3((float)(place.DetachX-place.X),0.3f,0);
        }

        public string GetHoverName()=>CartPrefab.DisplayName+" seat";
        public string GetHoverText()
        {
            Player player=Player.m_localPlayer;
            if(player==null||Time.time-_lastSit<2)return "";
            if(!InReach(player))return Localization.instance.Localize("<color=#888888>$piece_toofar</color>");
            if(Taken(player))return GetHoverName()+"\n<color=#888888>Taken</color>";
            return Localization.instance.Localize(GetHoverName()+"\n[<color=yellow><b>$KEY_Use</b></color>] Sit");
        }
        public float GetHoverOffset()=>0;
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;

        public bool Interact(Humanoid human,bool hold,bool alt)
        {
            if(hold||!(human is Player player)||player!=Player.m_localPlayer)return false;
            if(!InReach(player)||Time.time-_lastSit<2||player.IsAttached())return false;
            if(Taken(player)){player.Message(MessageHud.MessageType.Center,"$msg_blocked");return false;}
            Vagon vagon=GetComponentInParent<Vagon>();
            if(vagon!=null&&vagon.IsAttached(player)){player.Message(MessageHud.MessageType.Center,"You are pulling this cart.");return false;}
            if(player.IsEncumbered()){player.Message(MessageHud.MessageType.Center,"You are carrying too much to sit.");return false;}
            Sit(player);
            return false;
        }
        internal void Sit(Player player)
        {
            GameObject cart=GetComponentInParent<Carriage>()?.gameObject;
            player.AttachStart(m_attachPoint,cart,false,false,false,"attach_chair",m_detachOffset);
            _lastSit=Time.time;
        }
        internal bool Taken(Player except)
        {
            if(m_attachPoint==null)return true;
            foreach(Player other in Player.GetAllPlayers())
                if(other!=null&&other!=except&&Policy.Taken(Vector3.Distance(other.transform.position,m_attachPoint.position)))return true;
            return false;
        }
        private bool InReach(Player player)=>player!=null&&m_attachPoint!=null&&Vector3.Distance(player.transform.position,m_attachPoint.position)<Policy.UseDistance;
    }
}
