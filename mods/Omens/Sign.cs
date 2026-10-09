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
        private ZNetView _view;
        internal Kind Kind;internal long Id;
        internal Omen Omen=>Policy.Of(Kind);
        private readonly List<Transform> _birds=new List<Transform>();
        private float _settleAt,_altitude=14,_radius=7;
        private List<Rigidbody> _bodies;
        private GameObject _carcass;
        private BoxCollider _hover;
        private readonly List<Transform> _wisps=new List<Transform>();
        private readonly List<GameObject> _candles=new List<GameObject>();
        private GameObject _banner,_wanderer,_ship;
        private Light _glow;private Vector3 _shipHome;private float _leaveAt=-1;
        private bool _answered; // responded to here, or the host showed a response: no second payment while the sign lingers

        private void Awake()
        {
            _view=GetComponent<ZNetView>();
            if(_view==null||!_view.IsValid())return;
            ZDO zdo=_view.GetZDO();
            Kind=(Kind)zdo.GetInt(SignPrefab.KindKey,0);Id=zdo.GetLong(SignPrefab.IdKey,0L);
            Loaded.Add(this);
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return; // a dedicated server draws nothing
            int before=transform.childCount;
            try{Build();}
            catch(System.Exception e){Debug.LogWarning("[Omens] could not build the "+Kind+" sign: "+e.Message);}
            for(int i=before;i<transform.childCount;i++)_built.Add(transform.GetChild(i).gameObject);
        }
        private void OnDestroy()=>Loaded.Remove(this);
        private readonly List<GameObject> _built=new List<GameObject>();

        // A hot reload keeps the signs already in the world, with the old copy's code and closed connection to the host.
        // The old copy takes its parts off on unload; the new one puts fresh parts on every loaded sign.
        internal static void DetachAll()
        {
            foreach(OmenSign sign in Loaded.ToList())
                if(sign!=null){foreach(GameObject part in sign._built)if(part!=null)Object.Destroy(part);Object.Destroy(sign);}
            Loaded.Clear();
        }
        internal static void AttachAll()
        {
            if(ZNetScene.instance==null)return;
            foreach(ZNetView view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if(view==null||!view.IsValid()||view.GetZDO().GetPrefab()!=SignPrefab.Hash||view.GetComponent<OmenSign>()!=null)continue;
                // An older copy (one without DetachAll) may still be attached: its component and every child are Omens' own visuals.
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().FullName==typeof(OmenSign).FullName)Object.DestroyImmediate(stale);
                foreach(Transform child in view.transform.Cast<Transform>().ToList())Object.Destroy(child.gameObject);
                view.gameObject.AddComponent<OmenSign>();
            }
        }

        private void Build()
        {
            switch(Kind)
            {
                case Kind.DeadTroll:Corpse("Troll_ragdoll",0.6f,new Vector3(3.5f,1.4f,3.5f));break;
                case Kind.DrainedDeer:Corpse("deer_ragdoll",0.4f,new Vector3(2,1,2));break;
                case Kind.Cairn:
                    // A cairn kicked apart: its stack on its side, stones flung about, and what it covered.
                    GameObject pile=Looks.Copy("stone_pile",transform,new Vector3(0.4f,0.2f,0),Quaternion.Euler(70,Random.Range(0,360f),0));
                    if(pile!=null)foreach(Collider c in pile.GetComponentsInChildren<Collider>(true))c.enabled=false;
                    foreach(var (prefab,x,z) in new[]{("Skull2",-0.6f,0.3f),("BoneFragments",-1.2f,-0.8f),("BoneFragments",0.9f,1.3f),("BoneFragments",-0.3f,1.6f),
                        ("Stone",1.8f,-0.6f),("Stone",-1.9f,0.9f),("Stone",0.6f,-1.9f),("Stone",2.1f,1.2f),("Stone",-1.1f,-2f)})
                        Looks.Item(prefab,transform,new Vector3(x,0,z),Quaternion.Euler(0,Random.Range(0,360f),0));
                    _hover=Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(4.5f,1f,4.5f));
                    break;
                case Kind.Catch:Birds("Seagal",10,6,0.9f);break;
                case Kind.BloodMoon:
                    // A circle of stones around a slaughtered boar, blood spilled over it.
                    Corpse("boar_ragdoll",0.4f,new Vector3(4.5f,1f,4.5f));
                    for(int i=0;i<8;i++)
                    {
                        float a=i*Mathf.PI/4;
                        Looks.Item("Stone",transform,new Vector3(Mathf.Cos(a)*2.4f,0,Mathf.Sin(a)*2.4f),Quaternion.Euler(0,Random.Range(0,360f),0));
                    }
                    foreach(var (x,z) in new[]{(0.7f,0.5f),(-0.6f,-0.4f),(0.2f,-0.9f)})
                        Looks.Item("Bloodbag",transform,new Vector3(x,0,z),Quaternion.Euler(0,Random.Range(0,360f),0));
                    Looks.Item("Skull1",transform,new Vector3(-1.5f,0,1.1f),Quaternion.Euler(0,Random.Range(0,360f),0));
                    break;
                case Kind.AbandonedCamp:
                    GameObject pit=Looks.Copy("fire_pit",transform,Vector3.zero,Quaternion.identity);
                    if(pit!=null)Looks.Smother(pit);
                    // Someone slept here and left in a hurry: a bedroll by the fire, belongings dropped, and a skull.
                    Looks.Item("Morkhalla_Bedroll1",transform,new Vector3(2.3f,0,0.4f),Quaternion.Euler(0,100,0));
                    foreach(var (prefab,x,z) in new[]{("Skull1",-1.3f,1.5f),("ArmorRagsChest",-2f,-0.7f),("ShieldWood",1.1f,-2f),("SpearFlint",0.2f,2.4f),
                        ("BoneFragments",-0.6f,2f),("BoneFragments",1.6f,1.6f),("Wood",-1.7f,-1.9f)})
                        Looks.Item(prefab,transform,new Vector3(x,0,z),Quaternion.Euler(0,Random.Range(0,360f),0));
                    Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(4.5f,0.8f,4.5f));
                    break;
                case Kind.Ravens:Birds("Crow",14,7,1.5f);break; // larger than the ambient crows, so they read as a sign
                case Kind.Wolves:
                    // A deer torn apart: bones strewn wide, blood on the ground.
                    Corpse("deer_ragdoll",0.4f,new Vector3(3,1,3));
                    Scatter(("BoneFragments",-1.4f,0.6f),("BoneFragments",1.2f,1.5f),("BoneFragments",0.4f,-1.7f),("BoneFragments",-0.8f,-1.3f),
                        ("Bloodbag",0.9f,-0.6f),("Bloodbag",-0.5f,1.1f),("Skull2",1.8f,-1.2f));
                    break;
                case Kind.WarBanner:
                    // A Fuling banner planted in the earth, skulls at its foot.
                    _banner=Looks.Copy("goblin_banner",transform,Vector3.zero,Quaternion.Euler(0,Random.Range(0,360f),Random.Range(-4f,4f)));
                    if(_banner!=null)foreach(Collider c in _banner.GetComponentsInChildren<Collider>(true))c.enabled=false;
                    Scatter(("Skull1",0.7f,0.4f),("Skull1",-0.5f,0.6f),("Skull2",0.1f,-0.8f),("BoneFragments",-1f,-0.6f),("SpearFlint",1.2f,-0.9f));
                    _hover=Looks.Hover(transform,new Vector3(0,1.5f,0),new Vector3(2.5f,3.5f,2.5f));
                    break;
                case Kind.Drowned:
                    Corpse("Draugr_ragdoll",0.4f,new Vector3(2.5f,1,2.5f));
                    Scatter(("ShieldWood",1.3f,0.8f),("Wood",-1.5f,-0.7f),("Wood",-0.9f,1.6f));
                    break;
                case Kind.Hoard:
                    // A grave-chest spilling gold, and the bones that still clutch at it.
                    GameObject chest=Looks.Copy("TreasureChest_meadows",transform,Vector3.zero,Quaternion.Euler(0,Random.Range(0,360f),0));
                    if(chest!=null)foreach(Collider c in chest.GetComponentsInChildren<Collider>(true))c.enabled=false;
                    Scatter(("Coins",0.8f,0.2f),("Coins",1.1f,-0.5f),("Coins",0.5f,0.9f),("Coins",1.5f,0.4f),("Ruby",0.9f,-0.1f),("Amber",1.3f,0.9f),
                        ("SilverNecklace",0.4f,-0.8f),("Skull1",-1.1f,0.4f),("BoneFragments",-0.8f,-0.6f),("BoneFragments",-1.5f,0.9f));
                    _hover=Looks.Hover(transform,new Vector3(0,0.5f,0),new Vector3(3.5f,1.2f,3.5f));
                    break;
                case Kind.GraveCandles:
                    // Grave candles in a ring around an old skull, blown out until someone relights them.
                    Looks.Item("Skull2",transform,Vector3.zero,Quaternion.Euler(0,Random.Range(0,360f),0));
                    for(int i=0;i<6;i++)
                    {
                        float a=i*Mathf.PI/3;
                        GameObject candle=Looks.Copy("Candle_resin",transform,new Vector3(Mathf.Cos(a)*1.4f,0,Mathf.Sin(a)*1.4f),Quaternion.identity);
                        if(candle==null)continue;
                        Looks.Ground(candle);Looks.Smother(candle);
                        _candles.Add(candle);
                    }
                    Scatter(("BoneFragments",0.4f,0.5f),("BoneFragments",-2.1f,0.3f),("stone_pile",2.3f,-1.4f));
                    _hover=Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(3.6f,1f,3.6f));
                    break;
                case Kind.Scorched:
                    // A burnt circle of coal around a surtling core that still burns.
                    Looks.Item("SurtlingCore",transform,Vector3.zero,Quaternion.identity);
                    Looks.Fire(transform,Vector3.zero,0.5f);
                    for(int i=0;i<9;i++)
                    {
                        float a=i*Mathf.PI*2/9+Random.Range(-0.2f,0.2f),r=Random.Range(1.2f,2.6f);
                        Looks.Item("Coal",transform,new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r),Quaternion.Euler(0,Random.Range(0,360f),0));
                    }
                    _hover=Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(4f,1f,4f));
                    break;
                case Kind.Wisps:
                    for(int i=0;i<3;i++)
                    {
                        GameObject wisp=Looks.Wisp(transform,new Vector3(0,1.5f,0));
                        if(wisp!=null)_wisps.Add(wisp.transform);
                    }
                    _hover=Looks.Hover(transform,new Vector3(0,1.5f,0),new Vector3(3f,2.5f,3f));
                    break;
                case Kind.GreatStag:
                    // One huge antler in the moss, and the tracks of something that shed it.
                    GameObject antler=Looks.Copy("HardAntler",transform,new Vector3(0,0.1f,0),Quaternion.Euler(0,Random.Range(0,360f),0));
                    if(antler!=null){antler.transform.localScale=Vector3.one*1.6f;Looks.Still(antler);Looks.Ground(antler);}
                    Scatter(("Stone",-1.2f,0.8f),("Stone",1.4f,-1f));
                    _hover=Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(2.5f,1f,2.5f));
                    break;
                case Kind.FallenStar:
                    // A dark stone smouldering in a ring of thrown-up rock.
                    GameObject star=Looks.Copy("Pickable_Meteorite",transform,Vector3.zero,Quaternion.Euler(Random.Range(-15f,15f),Random.Range(0,360f),0));
                    if(star!=null){Looks.Still(star);Looks.Ground(star);}
                    Looks.Fire(transform,new Vector3(0,0.2f,0),0.6f);
                    for(int i=0;i<8;i++)
                    {
                        float a=i*Mathf.PI/4+Random.Range(-0.2f,0.2f);
                        Looks.Item(i%2==0?"Stone":"Coal",transform,new Vector3(Mathf.Cos(a)*2.2f,0,Mathf.Sin(a)*2.2f),Quaternion.Euler(0,Random.Range(0,360f),0));
                    }
                    _hover=Looks.Hover(transform,new Vector3(0,0.5f,0),new Vector3(4.6f,1.2f,4.6f));
                    break;
                case Kind.Shrine:
                    // A small stone altar in a ring of stones, two candles still burning, the offering bowl's place empty.
                    GameObject altar=Looks.Copy("stone_pile",transform,Vector3.zero,Quaternion.Euler(0,Random.Range(0,360f),0));
                    if(altar!=null){altar.transform.localScale=new Vector3(0.7f,0.55f,0.7f);Looks.Still(altar);}
                    foreach(float x in new[]{-0.55f,0.55f})
                    {
                        GameObject candle=Looks.Copy("Candle_resin",transform,new Vector3(x,0.75f,0.2f),Quaternion.identity);
                        if(candle!=null)Looks.Kindle(candle);
                    }
                    for(int i=0;i<7;i++)
                    {
                        float a=i*Mathf.PI*2/7;
                        Looks.Item("Stone",transform,new Vector3(Mathf.Cos(a)*2f,0,Mathf.Sin(a)*2f),Quaternion.Euler(0,Random.Range(0,360f),0));
                    }
                    Scatter(("Feathers",0.3f,-0.9f),("Feathers",-0.4f,-1f));
                    _hover=Looks.Hover(transform,new Vector3(0,0.6f,0),new Vector3(2.4f,1.4f,2.4f));
                    break;
                case Kind.Aurora:
                    GameObject rune=Looks.Copy("RuneStone_Boars",transform,Vector3.zero,Quaternion.Euler(0,Random.Range(0,360f),0));
                    if(rune!=null){Looks.Still(rune);Looks.Ground(rune);}
                    var glow=new GameObject("Glow");glow.transform.SetParent(transform,false);glow.transform.localPosition=new Vector3(0,1.6f,0);
                    _glow=glow.AddComponent<Light>();
                    _glow.type=LightType.Point;_glow.color=new Color(0.35f,1f,0.75f);_glow.range=7;_glow.intensity=1.2f;_glow.shadows=LightShadows.None;
                    _hover=Looks.Hover(transform,new Vector3(0,1.2f,0),new Vector3(2f,2.6f,2f));
                    break;
                case Kind.GhostShip:
                    // Driftwood heaped on the shore for a beacon; out at sea, a dark longship riding at anchor with a sick green light aboard.
                    Scatter(("Wood",0.3f,0.2f),("Wood",-0.4f,0.1f),("Wood",0.1f,-0.5f),("Wood",-0.2f,0.6f),("RoundLog",0.6f,-0.3f),("Resin",0.9f,0.6f));
                    _hover=Looks.Hover(transform,new Vector3(0,0.4f,0),new Vector3(2.4f,1f,2.4f));
                    Vector3 sea=Seaward();
                    if(sea!=Vector3.zero)
                    {
                        float water=ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30f;
                        _shipHome=transform.InverseTransformPoint(new Vector3(transform.position.x+sea.x,water-0.3f,transform.position.z+sea.z));
                        _ship=Looks.Copy("VikingShip",transform,_shipHome,Quaternion.Inverse(transform.rotation)*Quaternion.LookRotation(Vector3.Cross(sea.normalized,Vector3.up)));
                        if(_ship!=null)
                        {
                            Looks.Still(_ship);Looks.Darken(_ship,new Color(0.28f,0.33f,0.3f));
                            var lamp=new GameObject("Lamp");lamp.transform.SetParent(_ship.transform,false);lamp.transform.localPosition=new Vector3(0,3f,0);
                            Light l=lamp.AddComponent<Light>();l.type=LightType.Point;l.color=new Color(0.4f,1f,0.55f);l.range=14;l.intensity=1.4f;l.shadows=LightShadows.None;
                        }
                    }
                    break;
                case Kind.Wanderer:
                    _wanderer=Looks.Copy("odin",transform,Vector3.zero,Quaternion.identity);
                    if(_wanderer!=null){Looks.Still(_wanderer);FaceNearestPlayer();}
                    break;
            }
        }
        // The way out to deep water from a shore sign (world offset, about 40 m), or zero when there is none.
        private Vector3 Seaward()
        {
            if(WorldGenerator.instance==null||ZoneSystem.instance==null)return Vector3.zero;
            float water=ZoneSystem.instance.m_waterLevel,best=float.MaxValue;Vector3 way=Vector3.zero;
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI*2/24;var dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float depth=0;
                foreach(float reach in new[]{25f,35f,45f})
                {
                    Vector3 p=transform.position+dir*reach;
                    depth+=WorldGenerator.instance.GetHeight(p.x,p.z)-water;
                }
                if(depth<best){best=depth;way=dir*40f;}
            }
            return best<-6f?way:Vector3.zero; // all three points under water, a few metres deep
        }
        // Props laid around the sign: (prefab, x, z) in its own frame.
        private void Scatter(params (string prefab,float x,float z)[] props)
        {
            foreach(var (prefab,x,z) in props)Looks.Item(prefab,transform,new Vector3(x,0,z),Quaternion.Euler(0,Random.Range(0,360f),0));
        }
        private void FaceNearestPlayer()
        {
            Player me=Player.m_localPlayer;
            if(me==null||_wanderer==null)return;
            Vector3 to=me.transform.position-_wanderer.transform.position;to.y=0;
            if(to.sqrMagnitude>0.01f)_wanderer.transform.rotation=Quaternion.LookRotation(to);
        }

        // What everyone sees when the host accepts a response.
        internal void Responded()
        {
            _answered=true;
            switch(Kind)
            {
                case Kind.DeadTroll:Looks.Burn(Carcass);break;
                case Kind.GraveCandles:foreach(GameObject candle in _candles)if(candle!=null)Looks.Kindle(candle);break;
                case Kind.WarBanner:if(_banner!=null)_banner.transform.localRotation=Quaternion.Euler(84,_banner.transform.localEulerAngles.y,0);break;
                case Kind.Drowned:Looks.Item("Coins",transform,transform.InverseTransformPoint(Carcass),Quaternion.identity);break;
                case Kind.Shrine:Scatter(("Honey",0.15f,0.25f),("Honey",-0.15f,0.3f),("Honey",0f,0.05f));break;
                case Kind.GhostShip:Looks.Fire(transform,new Vector3(0,0.2f,0),1.3f);_leaveAt=Time.time;break;
            }
        }
        // A ragdoll copy that collapses naturally, then holds still; the box people aim at follows it.
        private void Corpse(string prefab,float height,Vector3 box)
        {
            GameObject body=Looks.Copy(prefab,transform,new Vector3(0,height,0),Quaternion.Euler(0,Random.Range(0,360f),80));
            if(body!=null){_carcass=body;_bodies=body.GetComponentsInChildren<Rigidbody>().ToList();_settleAt=Time.time+8;}
            _hover=Looks.Hover(transform,new Vector3(0,height,0),box);
        }
        // Three birds circling above the treetops (or the forest hides them): the first thing a ray from high above meets, plus a margin.
        private void Birds(string prefab,float lowest,float radius,float scale)
        {
            _altitude=lowest;_radius=radius;
            if(Physics.Raycast(transform.position+Vector3.up*80,Vector3.down,out RaycastHit top,95,~0,QueryTriggerInteraction.Ignore))
                _altitude=Mathf.Clamp(top.point.y-transform.position.y+6,lowest,45);
            for(int i=0;i<3;i++)
            {
                GameObject bird=Looks.Copy(prefab,transform,Vector3.up*_altitude,Quaternion.identity);
                if(bird==null)continue;
                foreach(Transform part in bird.transform.Cast<Transform>().ToList()) // a perched model sits beside the flying one; only the flying one shows
                    if(part.name.IndexOf("sit",System.StringComparison.OrdinalIgnoreCase)>=0)part.gameObject.SetActive(false);
                bird.transform.localScale=Vector3.one*scale;
                _birds.Add(bird.transform);
            }
        }
        private void Update()
        {
            for(int i=0;i<_wisps.Count;i++)
            {
                if(_wisps[i]==null)continue;
                float t=Time.time*(0.7f+i*0.23f)+i*2.1f;
                _wisps[i].localPosition=new Vector3(Mathf.Cos(t)*(1.1f+i*0.3f),1.3f+Mathf.Sin(t*1.7f)*0.4f+i*0.25f,Mathf.Sin(t*1.3f)*(1.1f+i*0.3f));
            }
            if(_glow!=null)_glow.intensity=0.9f+0.5f*Mathf.Sin(Time.time*1.3f); // the rune stone hums
            if(_ship!=null)
            {
                // Riding the swell; once warned off, it turns its back and fades out to sea.
                float t=Time.time,gone=_leaveAt<0?0:Mathf.Clamp01((t-_leaveAt)/18f);
                Vector3 away=_shipHome;away.y=0;away.Normalize();
                _ship.transform.localPosition=_shipHome+away*gone*35f+Vector3.up*(Mathf.Sin(t*0.6f)*0.35f-gone*4f);
                _ship.transform.localRotation=Quaternion.LookRotation(Vector3.Cross(away,Vector3.up))*Quaternion.Euler(Mathf.Sin(t*0.5f)*2f,gone*90f,Mathf.Sin(t*0.7f)*3f);
                if(gone>=1)_ship.SetActive(false);
            }
            if(_wanderer!=null&&_wanderer.activeSelf)
            {
                // He watches whoever comes, and is gone before they reach him.
                Player me=Player.m_localPlayer;
                if(me!=null&&Vector3.Distance(me.transform.position,transform.position)<=Omen.SeenFrom)
                {
                    GameObject odin=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("odin"):null;
                    odin?.GetComponent<Odin>()?.m_despawn.Create(_wanderer.transform.position,_wanderer.transform.rotation);
                    _wanderer.SetActive(false);
                }
                else if(Time.frameCount%30==0)FaceNearestPlayer();
            }
            if(_bodies!=null)
            {
                FollowCarcass(); // the ragdoll can slide downhill; the box people aim at goes with it
                if(Time.time>=_settleAt){foreach(Rigidbody body in _bodies)if(body!=null)body.isKinematic=true;_bodies=null;}
            }
            for(int i=0;i<_birds.Count;i++)
            {
                if(_birds[i]==null)continue;
                float angle=Time.time*0.55f+i*2.1f,radius=_radius+i*1.5f;
                var local=new Vector3(Mathf.Cos(angle)*radius,_altitude+i*1.2f+Mathf.Sin(Time.time*0.9f+i)*0.6f,Mathf.Sin(angle)*radius);
                _birds[i].localPosition=local;
                // Beak (+Z) along the direction of flight, in the sign's own frame (each sign has a random yaw), inner wing dipped into the turn.
                _birds[i].localRotation=Quaternion.LookRotation(new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle)))*Quaternion.Euler(0,0,15);
            }
        }

        private void FollowCarcass()
        {
            if(_hover==null||_carcass==null)return;
            // The bones' own physics shapes are where the body really is; a skinned mesh's bounds are only approximate.
            Collider[] bones=_carcass.GetComponentsInChildren<Collider>();
            if(bones.Length==0)return;
            Bounds bounds=bones[0].bounds;
            foreach(Collider bone in bones)bounds.Encapsulate(bone.bounds);
            bounds.Expand(0.4f);
            Transform box=_hover.transform;
            box.position=bounds.center;box.rotation=Quaternion.identity;
            _hover.center=Vector3.zero;_hover.size=Vector3.Max(bounds.size,new Vector3(1.5f,0.8f,1.5f));
        }
        internal Vector3 Carcass=>_hover!=null?_hover.transform.position:transform.position; // where it lies, not where it was placed
        public string GetHoverName()=>Omen.Name;
        public float GetHoverOffset()=>0;
        public string GetHoverText()
        {
            if(!Omen.Respondable||_answered)return Omen.Name;
            string cost=Omen.Cost!=null?$" ({Omen.CostAmount} {CostName()})":"";
            return Localization.instance.Localize($"{Omen.Name}\n[<color=yellow><b>$KEY_Use</b></color>] {Omen.Action}{cost}");
        }
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||_answered||!Omen.Respondable||!(user is Player player)||player!=Player.m_localPlayer)return false;
            if(Omen.Cost!=null)
            {
                Inventory inventory=player.GetInventory();
                string cost=CostName();
                if(cost==null)return false;
                if(inventory.CountItems(cost)<Omen.CostAmount)
                {player.Message(MessageHud.MessageType.Center,Localization.instance.Localize($"You need {Omen.CostAmount} {cost}."));return false;}
                inventory.RemoveItem(cost,Omen.CostAmount);
            }
            _answered=true;
            Net.Respond(Id); // the host shows the response to everyone (Net.OnResponded) once it accepts
            return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        // The cost item's in-game name ("$item_resin"), from its prefab: the game counts and removes items by that name.
        private string CostName()=>ObjectDB.instance?.GetItemPrefab(Omen.Cost)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
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
            // Cold: the coals' glow is emission in the pit's own materials.
            foreach(Renderer r in pit.GetComponentsInChildren<Renderer>(true))
                if(r.GetType().Name!="ParticleSystemRenderer")
                    foreach(Material m in r.materials)if(m.HasProperty("_EmissionColor")){m.SetColor("_EmissionColor",Color.black);m.DisableKeyword("_EMISSION");}
            foreach(Renderer r in pit.GetComponentsInChildren<Renderer>(true))
                if(r.GetType().Name=="ParticleSystemRenderer"&&r.gameObject.name.IndexOf("smoke",System.StringComparison.OrdinalIgnoreCase)<0)r.gameObject.SetActive(false);
            foreach(Collider c in pit.GetComponentsInChildren<Collider>(true))c.enabled=false;
        }
        // A still prop on the ground: no physics, no hitbox, and none of the glint that marks loot.
        internal static void Item(string prefab,Transform parent,Vector3 local,Quaternion rotation)
        {
            GameObject item=Copy(prefab,parent,local,rotation);
            if(item==null)return;
            Still(item);
            foreach(Renderer r in item.GetComponentsInChildren<Renderer>(true))if(r.GetType().Name=="ParticleSystemRenderer")r.enabled=false;
            Ground(item);
        }
        internal static void Still(GameObject go)
        {
            foreach(Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
            foreach(Collider c in go.GetComponentsInChildren<Collider>(true))c.enabled=false;
        }
        internal static void Ground(GameObject go)
        {
            Vector3 top=go.transform.position+Vector3.up*3;
            if(Physics.Raycast(top,Vector3.down,out RaycastHit hit,6,LayerMask.GetMask("terrain","Default","static_solid")))
                go.transform.position=hit.point+Vector3.up*0.05f;
        }
        // A ghostly cast: every material dimmed and tinted.
        internal static void Darken(GameObject go,Color tint)
        {
            foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if(r.GetType().Name=="ParticleSystemRenderer"){r.enabled=false;continue;}
                foreach(Material m in r.materials)if(m.HasProperty("_Color"))m.color=m.color*tint;
            }
        }
        // A floating light: the game's lured wisp (or its wisp item), with a soft glow of its own so it shows whatever the copy keeps.
        internal static GameObject Wisp(Transform parent,Vector3 local)
        {
            GameObject wisp=Copy("LuredWisp",parent,local,Quaternion.identity)??Copy("Wisp",parent,local,Quaternion.identity);
            if(wisp==null){wisp=new GameObject("Wisp");wisp.transform.SetParent(parent,false);wisp.transform.localPosition=local;}
            Still(wisp);
            var glow=new GameObject("Glow");glow.transform.SetParent(wisp.transform,false);
            Light light=glow.AddComponent<Light>();
            light.type=LightType.Point;light.color=new Color(0.55f,0.85f,1f);light.range=5;light.intensity=1.6f;light.shadows=LightShadows.None;
            return wisp;
        }
        // Lights the stripped copy of a candle or fire: its hidden flame objects shown, its lights on.
        internal static void Kindle(GameObject go)
        {
            foreach(Transform t in go.GetComponentsInChildren<Transform>(true))t.gameObject.SetActive(true);
            foreach(Light light in go.GetComponentsInChildren<Light>(true))light.enabled=true;
            foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))r.enabled=true;
            foreach(Collider c in go.GetComponentsInChildren<Collider>(true))c.enabled=false;
        }
        // A light that drifts from one spot to another over the ground, waits there a while, then fades.
        internal static void Guide(Vector3 from,Vector3 to)
        {
            GameObject wisp=Wisp(null,from+Vector3.up*1.6f);
            if(wisp==null)return;
            wisp.AddComponent<Drift>().Begin(from,to);
            Guides.Add(wisp);
        }
        private static readonly List<GameObject> Guides=new List<GameObject>();
        // A non-solid box so the crosshair finds the sign without anyone bumping into it.
        internal static BoxCollider Hover(Transform parent,Vector3 center,Vector3 size)
        {
            var go=new GameObject("Hover"){layer=LayerMask.NameToLayer("piece_nonsolid")};
            go.transform.SetParent(parent,false);
            var box=go.AddComponent<BoxCollider>();box.center=center;box.size=size;
            return box;
        }
        // The game's own fire-pit flames over the carcass for a few seconds, as everyone near it sees it burn.
        internal static void Burn(Vector3 at)
        {
            GameObject copy=Fire(null,at,1.6f);
            if(copy!=null)Object.Destroy(copy,12);
        }
        // The game's own fire-pit flames alone, without the pit: a fire on the ground.
        internal static GameObject Fire(Transform parent,Vector3 local,float scale)
        {
            GameObject copy=Copy("fire_pit",parent,local,Quaternion.identity);
            if(copy==null)return null;
            copy.transform.localScale=Vector3.one*scale;
            foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.SetActive(true); // the lit-fire objects start hidden
            foreach(Renderer r in copy.GetComponentsInChildren<Renderer>(true))if(r.GetType().Name!="ParticleSystemRenderer")r.enabled=false; // only the flames
            foreach(Collider c in copy.GetComponentsInChildren<Collider>(true))c.enabled=false;
            return copy;
        }
        internal static void Clear()
        {
            foreach(GameObject guide in Guides)if(guide!=null)Object.Destroy(guide);
            Guides.Clear();
            if(_staging!=null)Object.Destroy(_staging);_staging=null;
        }
    }

    // A guiding light's flight: low over the ground from the sign to the treasure, waiting there, then gone.
    internal sealed class Drift:MonoBehaviour
    {
        private Vector3 _from,_to;private float _start,_travel;
        private const float Speed=4.5f,Wait=120;
        internal void Begin(Vector3 from,Vector3 to){_from=from;_to=to;_start=Time.time+1.5f;_travel=Vector3.Distance(from,to)/Speed;}
        private void Update()
        {
            float t=Mathf.Clamp01((Time.time-_start)/Mathf.Max(1,_travel));
            Vector3 p=Vector3.Lerp(_from,_to,Mathf.SmoothStep(0,1,t));
            float ground=ZoneSystem.instance!=null&&ZoneSystem.instance.GetGroundHeight(p,out float h)?h:p.y;
            p.y=ground+1.6f+Mathf.Sin(Time.time*2.3f)*0.25f;
            transform.position=p;
            if(Time.time>_start+_travel+Wait)Object.Destroy(gameObject);
        }
    }
}
