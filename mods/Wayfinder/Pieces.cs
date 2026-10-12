using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Wayfinder
{
    // Things to build for the sea: a gilded weather vane (veðurviti) that shows the wind and, asked, the weather to come; a beacon
    // fire (viti) on a timber tower, seen from far out at sea and marked on the maps of those who have seen it; and a floating
    // sea-mark with a little lamp, to mark a channel, a reef or a harbour mouth.
    internal static class Pieces
    {
        internal const string VanePrefab="BobWeatherVane",BeaconPrefab="BobBeaconFire",MarkPrefab="BobSeaMark";
        internal const string NameKey="bob_wayfinder_mark_name";
        private const string ModelVersion="Way1";
        private static GameObject _holder;
        internal static GameObject Vane,Beacon,Mark;
        private static readonly List<Object> Owned=new List<Object>();
        internal static Material GlowDot;

        internal static void Register(ZNetScene scene)
        {
            try{Build(scene);}
            catch(System.Exception e){Debug.LogError("[Wayfinder] Could not make the sea pieces; they are left out: "+e);return;}
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            foreach(GameObject p in new[]{Vane,Beacon,Mark})
            {
                if(p==null)continue;
                scene.m_prefabs.RemoveAll(x=>x!=p&&x!=null&&x.name==p.name);
                if(!scene.m_prefabs.Contains(p))scene.m_prefabs.Add(p);
                named[p.name.GetStableHashCode()]=p;
            }
            AddToHammer();
        }
        private static void Build(ZNetScene scene)
        {
            if(Vane!=null)return;
            var looks=new Looks(ObjectDB.instance,Owned);
            if(!looks.Ready){Debug.LogWarning("[Wayfinder] No materials yet; sea pieces wait.");return;}
            _holder=new GameObject("WayfinderPieces");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
            GlowDot=scene.GetPrefab("portal_wood")?.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null&&m.name.ToLower().Contains("sparc"))
                ??scene.GetPrefab("portal_wood")?.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            Material wood=PieceMaterial(scene,"wood_pole2")??looks.Get("Wood");
            Material stone=PieceMaterial(scene,"stone_wall_1x1")??looks.Get("Stone");

            // ---- the weather vane: a tall pole with the gilded quadrant at its top
            GameObject pole=scene.GetPrefab("wood_pole2");
            if(pole!=null)
            {
                Vane=Bare(pole,VanePrefab);
                Transform visual=NewVisual(Vane.transform);
                Models.Rod(visual,"Pole",wood,new Vector3(0,0,0),new Vector3(0,3.2f,0),0.07f);
                Models.Part(visual,"Foot",PrimitiveType.Cylinder,wood,new Vector3(0,0.12f,0),Vector3.zero,new Vector3(0.32f,0.12f,0.32f));
                Models.Part(visual,"Collar",PrimitiveType.Cylinder,looks.Get("Bronze"),new Vector3(0,3.15f,0),Vector3.zero,new Vector3(0.17f,0.05f,0.17f));
                for(int i=0;i<4;i++) // the cross-arms of the four winds
                {
                    Vector3 d=Quaternion.Euler(0,i*90,0)*Vector3.forward;
                    Models.Rod(visual,"Arm",looks.Get("Bronze"),new Vector3(0,2.85f,0),new Vector3(0,2.85f,0)+d*0.35f,0.012f);
                    Models.Part(visual,"Knob",PrimitiveType.Sphere,looks.Get("Gold"),new Vector3(0,2.85f,0)+d*0.37f,Vector3.zero,Vector3.one*0.05f);
                }
                var top=new GameObject("VaneTop").transform;top.SetParent(visual,false);top.localPosition=new Vector3(0,3.2f,0);
                Fittings.Vane(top,looks,0.85f);
                Collide(Vane.transform,new Vector3(0,1.6f,0),new Vector3(0.3f,3.2f,0.3f));
                var piece=Vane.GetComponent<Piece>();
                piece.m_name="Weather Vane";
                piece.m_description="A veðurviti, a gilded vane such as once rode the prows of the sea-kings' ships. It swings to the wind, and those who read it know what weather the sky is bringing.";
                piece.m_resources=Needs(scene,("Bronze",1),("FineWood",2));
                piece.m_category=Piece.PieceCategory.Misc;
                Finish(Vane,visual,WearNTear.MaterialType.Wood);
                Vane.AddComponent<VaneView>();
            }

            // ---- the beacon: the game's bonfire lifted onto a timber tower, with a glow seen from afar
            GameObject bonfire=scene.GetPrefab("bonfire");
            if(bonfire!=null)
            {
                Beacon=Object.Instantiate(bonfire,_holder.transform,false);
                Beacon.name=BeaconPrefab;
                foreach(LODGroup lod in Beacon.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
                const float H=4.6f;
                var lift=new GameObject("Lift").transform;lift.SetParent(Beacon.transform,false);lift.localPosition=new Vector3(0,H,0);lift.localScale=Vector3.one*0.7f;
                foreach(string child in new[]{"New","_enabled","_enabled_high","_enabled_low","bonfire_destruction"})
                {
                    Transform t=Beacon.transform.Find(child);
                    if(t!=null)t.SetParent(lift,false);
                }
                Transform visual=NewVisual(Beacon.transform);
                // a stone footing, four raked timber legs braced twice, an iron fire-basket
                for(int i=0;i<4;i++)
                {
                    Vector3 d=Quaternion.Euler(0,45+i*90,0)*Vector3.forward;
                    Vector3 foot=d*1.8f,head=d*1.05f+Vector3.up*H;
                    Models.Part(visual,"Footing",PrimitiveType.Cube,stone,foot+Vector3.up*0.25f,new Vector3(0,45+i*90,0),new Vector3(0.7f,0.5f,0.7f));
                    Models.Rod(visual,"Leg",wood,foot+Vector3.up*0.4f,head,0.13f);
                    Vector3 d2=Quaternion.Euler(0,45+(i+1)*90,0)*Vector3.forward;
                    foreach(float f in new[]{0.3f,0.68f})
                        Models.Rod(visual,"Brace",wood,Vector3.Lerp(foot+Vector3.up*0.4f,head,f),Vector3.Lerp(d2*1.8f+Vector3.up*0.4f,d2*1.05f+Vector3.up*H,f),0.07f);
                }
                Material iron=looks.Get("Burnt");
                Models.Part(visual,"Floor",PrimitiveType.Cylinder,iron,new Vector3(0,H-0.05f,0),Vector3.zero,new Vector3(2.4f,0.06f,2.4f));
                for(int i=0;i<16;i++)
                {
                    float a=i*22.5f;Vector3 d=Quaternion.Euler(0,a,0)*Vector3.forward;
                    Models.Rod(visual,"Bar",iron,d*1.15f+Vector3.up*(H-0.05f),d*1.35f+Vector3.up*(H+0.7f),0.035f);
                }
                Models.Part(visual,"Rim",PrimitiveType.Cylinder,iron,new Vector3(0,H+0.7f,0),Vector3.zero,new Vector3(2.75f,0.04f,2.75f));
                Collide(Beacon.transform,new Vector3(0,H/2,0),new Vector3(2.6f,H,2.6f));
                Collide(Beacon.transform,new Vector3(0,H+0.35f,0),new Vector3(2.7f,0.7f,2.7f));
                // the old bonfire's own pile collider and blockers went up with it; the ground blocker is not needed
                var piece=Beacon.GetComponent<Piece>();
                piece.m_name="Beacon Fire";
                piece.m_description="A viti: a fire-basket on a timber tower, as once burned on the headlands of the north to guide ships home and warn of raiders. Lit, it is seen from far out at sea, and those who have seen it have it on their maps.";
                piece.m_resources=Needs(scene,("Wood",12),("Stone",12),("Resin",6));
                piece.m_craftingStation=scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
                var wear=Beacon.GetComponent<WearNTear>();
                if(wear!=null){wear.m_new=visual.gameObject;wear.m_worn=visual.gameObject;wear.m_broken=visual.gameObject;wear.m_materialType=WearNTear.MaterialType.Wood;wear.m_health=Mathf.Max(wear.m_health,800);}
                var fire=Beacon.GetComponent<Fireplace>();
                if(fire!=null){fire.m_name="Beacon Fire";fire.m_maxFuel=Mathf.Max(fire.m_maxFuel,20);}
                Beacon.GetComponent<ZNetView>().m_distant=true; // (sent and drawn from farther away, like a mountain)
                Beacon.AddComponent<BeaconView>();
                Marker(visual,ModelVersion);
            }

            // ---- the sea-mark: a floating spar-buoy with a small lamp
            if(pole!=null)
            {
                Mark=Bare(pole,MarkPrefab);
                Transform visual=NewVisual(Mark.transform);
                var bob=new GameObject("Bob").transform;bob.SetParent(visual,false);
                Material red=looks.Get("Wood");
                Models.Part(bob,"Float",PrimitiveType.Sphere,looks.Get("DarkWood"),new Vector3(0,0.05f,0),Vector3.zero,new Vector3(0.9f,0.55f,0.9f));
                Models.Part(bob,"Band",PrimitiveType.Cylinder,red,new Vector3(0,0.12f,0),Vector3.zero,new Vector3(0.86f,0.06f,0.86f));
                Models.Rod(bob,"Spar",wood,new Vector3(0,0.2f,0),new Vector3(0,2.1f,0),0.045f);
                // the topmark: a bundle of birch withies bound to the spar, splaying upward like a broom (the old Norse way)
                for(int i=0;i<9;i++)
                {
                    Vector3 out_=Quaternion.Euler(0,i*40,0)*Vector3.forward;
                    Models.Rod(bob,"Withy",looks.Get("Wicker"),new Vector3(0,1.35f,0)+out_*0.03f,new Vector3(0,1.95f+0.05f*(i%3),0)+out_*(0.2f+0.04f*(i%2)),0.012f);
                }
                Models.Part(bob,"Binding",PrimitiveType.Cylinder,looks.Get("Leather"),new Vector3(0,1.42f,0),Vector3.zero,new Vector3(0.13f,0.05f,0.13f));
                var lamp=new GameObject("Lamp").transform;lamp.SetParent(bob,false);lamp.localPosition=new Vector3(0,2.3f,0);
                Fittings.Lantern(lamp,looks,out _);
                Collide(Mark.transform,new Vector3(0,0.5f,0),new Vector3(0.9f,1.2f,0.9f));
                var piece=Mark.GetComponent<Piece>();
                piece.m_name="Sea-Mark";
                piece.m_description="A floating spar-buoy with a little lamp that burns by night. Set it on the water to mark a channel, a reef or a harbour mouth. Name it (E) and it goes on your map.";
                piece.m_resources=Needs(scene,("Wood",4),("Resin",2));
                piece.m_category=Piece.PieceCategory.Misc;
                piece.m_waterPiece=true;piece.m_groundPiece=false;piece.m_noInWater=false;
                Finish(Mark,visual,WearNTear.MaterialType.Wood);
                var w=Mark.GetComponent<WearNTear>();
                if(w!=null){w.m_noSupportWear=false;w.m_noRoofWear=false;w.m_supports=false;}
                Mark.AddComponent<MarkView>();
            }
            foreach(GameObject p in new[]{Vane,Beacon,Mark})
            {
                if(p==null)continue;
                var piece=p.GetComponent<Piece>();
                try{Texture2D icon=Icons.Render(p.transform.Find("Visual"),new Vector3(0,30,0),64,new Vector3(0.25f,0.35f,-1f));if(icon!=null){icon.hideFlags=HideFlags.HideAndDontSave;Owned.Add(icon);var s=Sprite.Create(icon,new Rect(0,0,icon.width,icon.height),new Vector2(0.5f,0.5f),100);Owned.Add(s);piece.m_icon=s;}}
                catch(System.Exception e){Debug.LogWarning("[Wayfinder] No build icon for "+p.name+": "+e.Message);}
            }
        }

        // The game's piece with its own looks and colliders taken off: placing, saving and wearing are kept.
        private static GameObject Bare(GameObject vanilla,string name)
        {
            GameObject go=Object.Instantiate(vanilla,_holder.transform,false);
            go.name=name;
            foreach(LODGroup lod in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            foreach(Transform child in go.transform.Cast<Transform>().ToList())
                if(!child.name.StartsWith("$hud_snappoint"))Object.DestroyImmediate(child.gameObject);
            foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
            foreach(Renderer r in go.GetComponents<Renderer>())Object.DestroyImmediate(r);
            foreach(MeshFilter f in go.GetComponents<MeshFilter>())Object.DestroyImmediate(f);
            foreach(Transform child in go.transform.Cast<Transform>().ToList())Object.DestroyImmediate(child.gameObject); // (no snapping to poles)
            return go;
        }
        private static Transform NewVisual(Transform root)
        {
            var v=new GameObject("Visual").transform;
            v.SetParent(root,false);
            return v;
        }
        private static void Marker(Transform visual,string version){var m=new GameObject(version).transform;m.SetParent(visual,false);}
        private static void Collide(Transform root,Vector3 centre,Vector3 size)
        {
            var c=new GameObject("Collider");
            c.layer=LayerMask.NameToLayer("piece");
            c.transform.SetParent(root,false);
            c.transform.localPosition=centre;
            c.AddComponent<BoxCollider>().size=size;
        }
        private static void Finish(GameObject go,Transform visual,WearNTear.MaterialType material)
        {
            Marker(visual,ModelVersion);
            var wear=go.GetComponent<WearNTear>();
            if(wear!=null){wear.m_new=visual.gameObject;wear.m_worn=visual.gameObject;wear.m_broken=visual.gameObject;wear.m_materialType=material;}
        }
        private static Piece.Requirement[] Needs(ZNetScene scene,params (string item,int amount)[] items)
        {
            var list=new List<Piece.Requirement>();
            foreach(var (item,amount) in items)
            {
                ItemDrop drop=scene.GetPrefab(item)?.GetComponent<ItemDrop>();
                if(drop==null){Debug.LogWarning("[Wayfinder] Missing ingredient "+item);continue;}
                list.Add(new Piece.Requirement{m_resItem=drop,m_amount=amount,m_recover=true});
            }
            return list.ToArray();
        }
        private static Material PieceMaterial(ZNetScene scene,string prefab)
        {
            GameObject go=scene.GetPrefab(prefab);
            if(go==null)return null;
            var all=go.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials)
                .Where(m=>m!=null&&m.shader!=null&&!m.name.ToLower().Contains("snow")&&!m.shader.name.ToLower().Contains("snow")).ToList();
            return all.FirstOrDefault(m=>m.shader.name.Contains("Piece"))??all.FirstOrDefault();
        }

        internal static void AddToHammer()
        {
            if(ObjectDB.instance==null)return;
            PieceTable table=ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null)return;
            foreach(GameObject p in new[]{Vane,Beacon,Mark})
            {
                if(p==null)continue;
                table.m_pieces.RemoveAll(x=>x==null||(x!=p&&x.name==p.name));
                if(!table.m_pieces.Contains(p))table.m_pieces.Add(p);
            }
        }
        // Standing ones from an older copy get this copy's behaviour (and model, if it changed).
        internal static void Refresh()
        {
            if(ZNetScene.instance==null)return;
            foreach(ZNetView view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if(view==null||!view.IsValid())continue;
                int hash=view.GetZDO().GetPrefab();
                GameObject template=Vane!=null&&hash==Vane.name.GetStableHashCode()?Vane:Beacon!=null&&hash==Beacon.name.GetStableHashCode()?Beacon:Mark!=null&&hash==Mark.name.GetStableHashCode()?Mark:null;
                if(template==null)continue;
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())
                    if(stale!=null&&(stale.GetType().Name==nameof(VaneView)||stale.GetType().Name==nameof(BeaconView)||stale.GetType().Name==nameof(MarkView))&&stale.GetType().Assembly!=typeof(Pieces).Assembly)Object.DestroyImmediate(stale);
                if(template!=Beacon&&view.transform.Find("Visual/"+ModelVersion)==null)
                {
                    Transform old=view.transform.Find("Visual");if(old!=null)Object.DestroyImmediate(old.gameObject);
                    var copy=Object.Instantiate(template.transform.Find("Visual").gameObject,view.transform,false);copy.name="Visual";
                    var wear=view.GetComponent<WearNTear>();
                    if(wear!=null){wear.m_new=wear.m_worn=wear.m_broken=copy;}
                }
                if(template==Vane&&view.GetComponent<VaneView>()==null)view.gameObject.AddComponent<VaneView>();
                if(template==Beacon&&view.GetComponent<BeaconView>()==null)view.gameObject.AddComponent<BeaconView>();
                if(template==Mark&&view.GetComponent<MarkView>()==null)view.gameObject.AddComponent<MarkView>();
            }
        }
        internal static void Unregister()
        {
            PieceTable table=ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            foreach(GameObject p in new[]{Vane,Beacon,Mark})if(p!=null)table?.m_pieces.Remove(p);
            if(ZNetScene.instance==null&&_holder!=null)Object.Destroy(_holder);
            _holder=null;Vane=Beacon=Mark=null;
            Owned.Clear();
        }
    }

    // The vane swings to the wind; asked, it tells the wind and the weather to come.
    public sealed class VaneView:MonoBehaviour,Hoverable
    {
        private Transform _turn;
        private float _seed;
        private void Awake(){_turn=transform.Find("Visual/VaneTop/Turn");_seed=Random.value*10;}
        private void Update()
        {
            if(_turn==null||EnvMan.instance==null)return;
            if(GameCamera.instance!=null&&(GameCamera.instance.transform.position-transform.position).sqrMagnitude>120*120)return;
            Vector3 wind=EnvMan.instance.GetWindDir();float strength=EnvMan.instance.GetWindIntensity();
            Vector3 local=_turn.parent.InverseTransformDirection(wind);local.y=0;
            if(local.sqrMagnitude<1e-4)return;
            Quaternion want=Quaternion.LookRotation(-local.normalized,Vector3.up)*Quaternion.Euler(0,Mathf.Sin(Time.time*(1.5f+strength*4)+_seed)*7*strength,0);
            _turn.localRotation=Quaternion.Slerp(_turn.localRotation,want,Time.deltaTime*(1+strength*3));
        }
        public string GetHoverName()=>"Weather Vane";
        public float GetHoverOffset()=>0;
        public string GetHoverText()
        {
            if(EnvMan.instance==null)return GetHoverName();
            string f=Weather.Forecast(transform.position);
            return $"Weather Vane\n<color=#c0d8c8>{Weather.WindNow()}</color>"+(f!=""?$"\n<color=#d8b880>{f}</color>":"");
        }
    }

    // The beacon: its name, a far-seen glow while it burns, and its light reaching farther than a campfire's.
    public sealed class BeaconView:MonoBehaviour
    {
        private Fireplace _fire;
        private ZNetView _view;
        private Transform _glow;
        private Light _far;
        internal static readonly HashSet<BeaconView> Live=new HashSet<BeaconView>(); // (kept as they come and go: searching the scene for them cost ~10 ms)
        private void OnEnable()=>Live.Add(this);
        private void OnDisable()=>Live.Remove(this);
        private void Awake()
        {
            _fire=GetComponent<Fireplace>();_view=GetComponent<ZNetView>();
            if(Pieces.GlowDot!=null)
            {
                var g=new GameObject("FarGlow");
                g.transform.SetParent(transform,false);g.transform.localPosition=new Vector3(0,6.2f,0);
                g.AddComponent<MeshFilter>().sharedMesh=Models.Prim(PrimitiveType.Quad);
                var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=Pieces.GlowDot;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                var block=new MaterialPropertyBlock();block.SetColor("_TintColor",new Color(1f,0.55f,0.2f,0.9f));block.SetColor("_Color",new Color(1f,0.55f,0.2f,0.9f));r.SetPropertyBlock(block);
                _glow=g.transform;
            }
            var l=new GameObject("FarLight").AddComponent<Light>();
            l.transform.SetParent(transform,false);l.transform.localPosition=new Vector3(0,6.5f,0);
            l.type=LightType.Point;l.range=45;l.color=new Color(1f,0.6f,0.3f);l.intensity=0;l.shadows=LightShadows.None;
            _far=l;
        }
        internal bool Lit=>_fire!=null&&_view!=null&&_view.IsValid()&&_view.GetZDO().GetFloat(ZDOVars.s_fuel)>0;
        internal string Name=>_view!=null&&_view.IsValid()?_view.GetZDO().GetString(Pieces.NameKey):"";
        private void Update()
        {
            bool lit=Lit;
            Camera cam=Utils.GetMainCamera();
            if(_glow!=null)
            {
                _glow.gameObject.SetActive(lit&&cam!=null);
                if(lit&&cam!=null)
                {
                    float d=Vector3.Distance(cam.transform.position,_glow.position);
                    _glow.rotation=Quaternion.LookRotation(_glow.position-cam.transform.position);
                    float flicker=0.9f+0.1f*Mathf.PerlinNoise(Time.time*4,transform.position.x);
                    _glow.localScale=Vector3.one*Mathf.Max(5f,d*0.022f)*flicker*(d<30?Mathf.InverseLerp(10,30,d):1);
                }
            }
            if(_far!=null){_far.intensity=Mathf.MoveTowards(_far.intensity,lit&&!EnvMan.IsDay()?1.4f:0,Time.deltaTime);_far.enabled=_far.intensity>0.01f;}
        }
    }
    [HarmonyPatch(typeof(Fireplace),nameof(Fireplace.GetHoverText))]
    internal static class BeaconHover
    {
        private static void Postfix(Fireplace __instance,ref string __result)
        {
            BeaconView b=__instance.GetComponent<BeaconView>();
            if(b==null)return;
            string name=b.Name;
            __result=(name!=""?$"<color=#ffd27a>{name}</color>\n":"")+__result+"\n[<color=yellow><b>Shift + E</b></color>] Name the beacon";
        }
    }
    [HarmonyPatch(typeof(Fireplace),nameof(Fireplace.Interact))]
    internal static class BeaconRename
    {
        private static bool Prefix(Fireplace __instance,Humanoid user,bool hold,bool alt,ref bool __result)
        {
            if(!alt||hold||__instance.GetComponent<BeaconView>()==null||user!=Player.m_localPlayer)return true;
            if(TextInput.instance!=null)TextInput.instance.RequestText(new MarkNamer(__instance.GetComponent<ZNetView>()),"Name the beacon",24);
            __result=true;
            return false;
        }
    }
    internal sealed class MarkNamer:TextReceiver
    {
        private readonly ZNetView _view;
        internal MarkNamer(ZNetView view){_view=view;}
        public string GetText()=>_view!=null&&_view.IsValid()?_view.GetZDO().GetString(Pieces.NameKey):"";
        public void SetText(string text)
        {
            if(_view==null||!_view.IsValid())return;
            if(!_view.IsOwner())_view.ClaimOwnership();
            _view.GetZDO().Set(Pieces.NameKey,Policy.Clean(text));
            Landmarks.Forget(_view.transform.position); // (re-seen at once with the new name)
        }
    }

    // The sea-mark: it rides the waves, its lamp burns by night, and E names it.
    public sealed class MarkView:MonoBehaviour,Hoverable,Interactable
    {
        private Transform _bob;
        private Light _light;
        private Renderer _glow;
        private ZNetView _view;
        private float _seed;
        internal static readonly HashSet<MarkView> Live=new HashSet<MarkView>();
        private void OnEnable()=>Live.Add(this);
        private void OnDisable()=>Live.Remove(this);
        private void Awake()
        {
            _view=GetComponent<ZNetView>();
            _bob=transform.Find("Visual/Bob");
            _seed=Random.value*10;
            Transform lamp=_bob!=null?_bob.Find("Lamp"):null;
            if(lamp!=null)
            {
                _glow=lamp.Find("Horn")?.GetComponent<Renderer>();
                _light=new GameObject("Light").AddComponent<Light>();
                _light.transform.SetParent(lamp,false);_light.transform.localPosition=new Vector3(0,-0.1f,0);
                _light.type=LightType.Point;_light.range=9;_light.color=new Color(1f,0.75f,0.45f);_light.intensity=0;_light.shadows=LightShadows.None;
            }
        }
        private void Update()
        {
            if(_bob==null)return;
            Camera cam=Utils.GetMainCamera();
            if(cam!=null&&(cam.transform.position-transform.position).sqrMagnitude>250*250)return;
            float level=Floating.GetLiquidLevel(transform.position);
            float y=level>-10000?level-transform.position.y:0;
            _bob.localPosition=new Vector3(0,Mathf.Clamp(y,-1.5f,1.5f),0);
            _bob.localRotation=Quaternion.Euler(Mathf.Sin(Time.time*0.9f+_seed)*6,0,Mathf.Sin(Time.time*0.7f+_seed*2)*6);
            bool night=EnvMan.instance!=null&&!EnvMan.IsDay();
            if(_light!=null){_light.intensity=Mathf.MoveTowards(_light.intensity,night?1.1f*(0.9f+0.1f*Mathf.PerlinNoise(Time.time*3,_seed)):0,Time.deltaTime);_light.enabled=_light.intensity>0.01f;}
            if(_glow!=null)_glow.material.SetColor("_EmissionColor",new Color(1f,0.6f,0.25f)*(night?2f:0.05f));
        }
        internal string Name=>_view!=null&&_view.IsValid()?_view.GetZDO().GetString(Pieces.NameKey):"";
        public string GetHoverName()=>Name!=""?Name:"Sea-Mark";
        public float GetHoverOffset()=>0;
        public string GetHoverText()=>(Name!=""?$"<color=#ffd27a>{Name}</color>\n":"")+"Sea-Mark\n[<color=yellow><b>$KEY_Use</b></color>] Name it".Replace("$KEY_Use",Localization.instance!=null?Localization.instance.Localize("$KEY_Use"):"E");
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||user!=Player.m_localPlayer)return false;
            if(TextInput.instance!=null)TextInput.instance.RequestText(new MarkNamer(_view),"Name the sea-mark",24);
            return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    }

    // Beacons and named sea-marks you have seen stay on your map: their places are kept with your character for each world.
    internal static class Landmarks
    {
        private sealed class Seen{internal Vector3 At;internal string Name;internal bool Beacon;internal Minimap.PinData Pin;}
        private static readonly List<Seen> All=new List<Seen>();
        private static string _for;
        private static float _next;
        private const string Prefix="bob_wayfinder_marks:";
        internal static int Count=>All.Count;

        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            if(me==null||Plugin.World()==null||Time.time<_next)return;
            _next=Time.time+2f;
            string key=Prefix+Plugin.World();
            if(_for!=key+me.GetPlayerID()){Clear();_for=key+me.GetPlayerID();Load(me,key);}
            bool changed=false;
            Vector3 here=me.transform.position;
            foreach(BeaconView b in BeaconView.Live)
                changed|=Note(b.transform.position,b.Name,true,here);
            foreach(MarkView m in MarkView.Live)
                if(m.Name!="")changed|=Note(m.transform.position,m.Name,false,here);
            // one taken off the map by hand is forgotten; one gone from where it stood, too
            Minimap map=Minimap.instance;
            for(int i=All.Count-1;i>=0;i--)
            {
                Seen s=All[i];
                if(s.Pin!=null&&map!=null&&!PinList(map).Contains(s.Pin)){All.RemoveAt(i);changed=true;continue;}
                if((s.At-here).sqrMagnitude<50*50&&ZoneSystem.instance.IsZoneLoaded(s.At)&&!Standing(s)){Unpin(s);All.RemoveAt(i);changed=true;continue;}
                if(map==null||!Plugin.ShowLandmarks.Value){Unpin(s);continue;}
                string label=(s.Name!=""?s.Name:s.Beacon?"Beacon":"Sea-mark");
                if(s.Pin==null||s.Pin.m_name!=label)
                {
                    Unpin(s);
                    s.Pin=map.AddPin(s.At,s.Beacon?Minimap.PinType.Icon0:Minimap.PinType.Icon4,label,false,false);
                    Sprite icon=(s.Beacon?Pieces.Beacon:Pieces.Mark)?.GetComponent<Piece>()?.m_icon;
                    if(icon!=null)s.Pin.m_icon=icon;
                }
            }
            if(changed)Save(me,key);
        }
        private static bool Standing(Seen s)
        {
            foreach(BeaconView b in BeaconView.Live)if((b.transform.position-s.At).sqrMagnitude<9)return true;
            foreach(MarkView m in MarkView.Live)if((m.transform.position-s.At).sqrMagnitude<9)return true;
            return false;
        }
        private static bool Note(Vector3 at,string name,bool beacon,Vector3 here)
        {
            Seen s=All.FirstOrDefault(x=>(x.At-at).sqrMagnitude<9);
            if(s!=null){if(s.Name==name)return false;s.Name=name;return true;}
            All.Add(new Seen{At=at,Name=name,Beacon=beacon});
            return true;
        }
        internal static void Forget(Vector3 at){foreach(Seen s in All.Where(x=>(x.At-at).sqrMagnitude<9).ToList()){Unpin(s);All.Remove(s);}}
        private static readonly AccessTools.FieldRef<Minimap,List<Minimap.PinData>> PinList=AccessTools.FieldRefAccess<Minimap,List<Minimap.PinData>>("m_pins");
        private static void Unpin(Seen s){if(s.Pin!=null&&Minimap.instance!=null)Minimap.instance.RemovePin(s.Pin);s.Pin=null;}
        private static void Clear(){foreach(Seen s in All)Unpin(s);All.Clear();}
        private static void Load(Player me,string key)
        {
            if(!me.m_customData.TryGetValue(key,out string text)||string.IsNullOrEmpty(text))return;
            foreach(string e in text.Split(';'))
            {
                string[] f=e.Split('|');
                if(f.Length<4)continue;
                if(!float.TryParse(f[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)||!float.TryParse(f[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float y)||!float.TryParse(f[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float z))continue;
                All.Add(new Seen{At=new Vector3(x,y,z),Beacon=f[3]=="b",Name=f.Length>4?f[4]:""});
            }
        }
        private static void Save(Player me,string key)
        {
            me.m_customData[key]=string.Join(";",All.Select(s=>string.Join("|",s.At.x.ToString("0.0",CultureInfo.InvariantCulture),s.At.y.ToString("0.0",CultureInfo.InvariantCulture),s.At.z.ToString("0.0",CultureInfo.InvariantCulture),s.Beacon?"b":"m",Policy.Clean(s.Name))));
        }
        internal static void Stop(){Clear();_for=null;}
    }
}
