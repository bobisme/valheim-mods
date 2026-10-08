using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Omens
{
    // The omen in the world: one networked, saved marker object whose look is built locally from the game's own models.
    internal static class SignPrefab
    {
        internal const string Name="BobOmenSign",KindKey="bob_omen_kind",IdKey="bob_omen_id";
        internal static int Hash=>Name.GetStableHashCode();
        private static GameObject _holder,_prefab;

        internal static GameObject Ensure()
        {
            if(_prefab!=null)return _prefab;
            _holder=new GameObject(Name+"Prefabs");
            _holder.SetActive(false); // keeps the template from waking up as a real object
            Object.DontDestroyOnLoad(_holder);
            var go=new GameObject(Name);
            go.transform.SetParent(_holder.transform,false);
            ZNetView view=go.AddComponent<ZNetView>();
            view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;view.m_distant=false;
            go.AddComponent<OmenSign>();
            _prefab=go;
            return go;
        }
        internal static void Register(ZNetScene scene)
        {
            GameObject prefab=Ensure();
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            named[Hash]=prefab; // a host without it deletes saved signs as invalid
        }
        internal static void Unregister()
        {
            if(_prefab==null)return;
            if(ZNetScene.instance!=null)
            {
                ZNetScene.instance.m_prefabs.Remove(_prefab);
                var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(ZNetScene.instance);
                if(named.TryGetValue(Hash,out GameObject current)&&current==_prefab)named.Remove(Hash); // only our own entry
            }
            Object.Destroy(_holder);_holder=null;_prefab=null;
        }
    }

    internal sealed class OmenSign:MonoBehaviour,Hoverable,Interactable
    {
        internal static readonly List<OmenSign> Loaded=new List<OmenSign>();
        internal const string Resin="$item_resin";internal const int ResinCost=5;
        private ZNetView _view;
        internal Kind Kind;internal long Id;
        internal Omen Omen=>Policy.Of(Kind);
        private readonly List<Transform> _birds=new List<Transform>();
        private float _settleAt;
        private List<Rigidbody> _bodies;

        private void Awake()
        {
            _view=GetComponent<ZNetView>();
            if(_view==null||!_view.IsValid())return;
            ZDO zdo=_view.GetZDO();
            Kind=(Kind)zdo.GetInt(SignPrefab.KindKey,0);Id=zdo.GetLong(SignPrefab.IdKey,0L);
            Loaded.Add(this);
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return; // a dedicated server draws nothing
            try{Build();}
            catch(System.Exception e){Debug.LogWarning("[Omens] could not build the "+Kind+" sign: "+e.Message);}
        }
        private void OnDestroy()=>Loaded.Remove(this);

        private void Build()
        {
            switch(Kind)
            {
                case Kind.DeadTroll:
                    GameObject troll=Looks.Copy("Troll_ragdoll",transform,new Vector3(0,0.6f,0),Quaternion.Euler(0,Random.Range(0,360f),80));
                    if(troll!=null){_bodies=troll.GetComponentsInChildren<Rigidbody>().ToList();_settleAt=Time.time+8;} // collapse naturally, then hold still
                    Looks.Hover(transform,new Vector3(0,0.6f,0),new Vector3(3.5f,1.4f,3.5f));
                    break;
                case Kind.AbandonedCamp:
                    GameObject pit=Looks.Copy("fire_pit",transform,Vector3.zero,Quaternion.identity);
                    if(pit!=null)Looks.Smother(pit);
                    foreach(var (prefab,x,z) in new[]{("BoneFragments",1.3f,0.4f),("BoneFragments",-0.9f,1.1f),("Wood",1.6f,-1.2f),("Wood",1.9f,-0.8f),
                        ("LeatherScraps",-1.5f,-0.6f),("TrophySkeleton",-0.4f,-1.6f)})
                        Looks.Item(prefab,transform,new Vector3(x,0,z));
                    Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(4.5f,0.8f,4.5f));
                    break;
                case Kind.Ravens:
                    for(int i=0;i<3;i++)
                    {
                        GameObject bird=Looks.Copy("Crow",transform,Vector3.up*14,Quaternion.identity)??Looks.Copy("Ravens",transform,Vector3.up*14,Quaternion.identity);
                        if(bird!=null)_birds.Add(bird.transform);
                    }
                    break;
            }
        }
        private void Update()
        {
            if(_bodies!=null&&Time.time>=_settleAt){foreach(Rigidbody body in _bodies)if(body!=null)body.isKinematic=true;_bodies=null;}
            for(int i=0;i<_birds.Count;i++)
            {
                if(_birds[i]==null)continue;
                float angle=Time.time*0.55f+i*2.1f,radius=7+i*1.5f;
                var local=new Vector3(Mathf.Cos(angle)*radius,14+i*1.2f+Mathf.Sin(Time.time*0.9f+i)*0.6f,Mathf.Sin(angle)*radius);
                _birds[i].localPosition=local;
                _birds[i].rotation=Quaternion.LookRotation(new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle)))*Quaternion.Euler(0,0,-18); // banking into the turn
            }
        }

        public string GetHoverName()=>Omen.Name;
        public float GetHoverOffset()=>0;
        public string GetHoverText()
        {
            if(!Omen.Respondable)return Omen.Name;
            return Localization.instance.Localize($"{Omen.Name}\n[<color=yellow><b>$KEY_Use</b></color>] Burn the carcass ({ResinCost} $item_resin)");
        }
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||!Omen.Respondable||!(user is Player player)||player!=Player.m_localPlayer)return false;
            Inventory inventory=player.GetInventory();
            if(inventory.CountItems(Resin)<ResinCost)
            {player.Message(MessageHud.MessageType.Center,Localization.instance.Localize($"You need {ResinCost} $item_resin to burn it."));return false;}
            inventory.RemoveItem(Resin,ResinCost);
            Net.Respond(Id);
            Looks.Burn(transform);
            return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    }

    // Visual-only copies of the game's prefabs: no networking, AI, loot or building, so a sign can never be mined, picked up or raided as a base.
    internal static class Looks
    {
        private static GameObject _staging;
        private static Transform Staging()
        {
            if(_staging==null){_staging=new GameObject("BobOmenStaging");_staging.SetActive(false);Object.DontDestroyOnLoad(_staging);}
            return _staging.transform;
        }
        internal static GameObject Copy(string prefab,Transform parent,Vector3 local,Quaternion rotation)
        {
            GameObject source=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(prefab):null;
            if(source==null){Debug.Log("[Omens] no prefab "+prefab+" for a sign");return null;}
            GameObject copy=Object.Instantiate(source,Staging()); // inactive: nothing wakes up while it is stripped
            Strip(copy);
            copy.transform.SetParent(parent,false);
            copy.transform.localPosition=local;copy.transform.localRotation=rotation;
            return copy;
        }
        private static void Strip(GameObject go)
        {
            // Behaviours that others require go last; a few passes clear dependency chains.
            for(int pass=0;pass<3;pass++)
                foreach(MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                    if(behaviour!=null)Object.DestroyImmediate(behaviour);
            foreach(AudioSource audio in go.GetComponentsInChildren<AudioSource>(true))Object.DestroyImmediate(audio);
        }
        internal static void Smother(GameObject pit)
        {
            foreach(Light light in pit.GetComponentsInChildren<Light>(true))light.enabled=false;
            foreach(Renderer r in pit.GetComponentsInChildren<Renderer>(true))
                if(r.GetType().Name=="ParticleSystemRenderer"&&r.gameObject.name.IndexOf("smoke",System.StringComparison.OrdinalIgnoreCase)<0)r.gameObject.SetActive(false);
            foreach(Collider c in pit.GetComponentsInChildren<Collider>(true))c.enabled=false;
        }
        internal static void Item(string prefab,Transform parent,Vector3 local)
        {
            GameObject item=Copy(prefab,parent,local,Quaternion.Euler(0,Random.Range(0,360f),0));
            if(item==null)return;
            foreach(Rigidbody body in item.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
            foreach(Collider c in item.GetComponentsInChildren<Collider>(true))c.enabled=false;
            Vector3 top=item.transform.position+Vector3.up*3;
            if(Physics.Raycast(top,Vector3.down,out RaycastHit hit,6,LayerMask.GetMask("terrain","Default","static_solid")))
                item.transform.position=hit.point+Vector3.up*0.05f;
        }
        // A non-solid box so the crosshair finds the sign without anyone bumping into it.
        internal static void Hover(Transform parent,Vector3 center,Vector3 size)
        {
            var go=new GameObject("Hover"){layer=LayerMask.NameToLayer("piece_nonsolid")};
            go.transform.SetParent(parent,false);
            var box=go.AddComponent<BoxCollider>();box.center=center;box.size=size;
        }
        // The game's own fire-pit flames over the carcass for a few seconds, as everyone near it sees it burn.
        internal static void Burn(Transform at)
        {
            GameObject copy=Copy("fire_pit",null,at.position+Vector3.up*0.3f,Quaternion.identity);
            if(copy==null)return;
            copy.transform.localScale=Vector3.one*1.6f;
            foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.SetActive(true); // the lit-fire objects start hidden
            foreach(Renderer r in copy.GetComponentsInChildren<Renderer>(true))if(r.GetType().Name!="ParticleSystemRenderer")r.enabled=false; // only the flames
            foreach(Collider c in copy.GetComponentsInChildren<Collider>(true))c.enabled=false;
            Object.Destroy(copy,12);
        }
        internal static void Clear(){if(_staging!=null)Object.Destroy(_staging);_staging=null;}
    }
}
