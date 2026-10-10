using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // The local portal piece: the game's wood portal for its building rules, with its frame, effects and teleporting replaced
    // by a wooden full-length mirror whose glass shows the view from the linked portal.
    internal static class PortalPrefab
    {
        internal const string Name="BobLocalPortal",DisplayName="Local Portal";
        private const string ModelVersion="Model4";
        internal static int Hash=>Name.GetStableHashCode();
        internal static GameObject Prefab=>_prefab;
        private static GameObject _holder,_prefab;
        private static Mesh _surface;
        private static readonly Dictionary<Vector3,Mesh> Boxes=new Dictionary<Vector3,Mesh>();
        private static Material _glowMaterial;

        private static GameObject Build(ZNetScene scene)
        {
            if(_prefab!=null)return _prefab;
            GameObject vanilla=scene.GetPrefab("portal_wood");
            if(vanilla==null){Debug.LogWarning("[LocalPortals] The game's portal_wood was not found; the local portal is not added.");return null;}
            _holder=new GameObject(Name+"Prefabs");
            _holder.SetActive(false); // keeps the template from waking up as a real object
            Object.DontDestroyOnLoad(_holder);
            GameObject go=Object.Instantiate(vanilla,_holder.transform,false);
            go.name=Name;
            go.transform.localPosition=Vector3.zero;

            Object.DestroyImmediate(go.GetComponent<TeleportWorld>());
            foreach(string child in new[]{"TELEPORT","_target_found_red","New","Mesh collider","Proximity","GuidePoint","portal_destruction"})
            {
                Transform t=go.transform.Find(child);
                if(t!=null)Object.DestroyImmediate(t.gameObject);
            }
            foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);

            var piece=go.GetComponent<Piece>();
            piece.m_name=DisplayName;
            piece.m_description="A tall wooden mirror that looks out of another one nearby: see the other side in its glass, walk into it and step out of the other. Build two within "+Policy.LinkRange+" m and the second links to the first; or name two the same (E), like the game's portals.";
            piece.m_resources=new[]{Requirement(scene,"FineWood",10),Requirement(scene,"GreydwarfEye",6),Requirement(scene,"SurtlingCore",1)}.Where(r=>r!=null).ToArray();

            Model(go.transform,scene,vanilla);
            var wear=go.GetComponent<WearNTear>();
            if(wear!=null)wear.m_materialType=WearNTear.MaterialType.Wood;
            go.AddComponent<LocalPortal>();
            _prefab=go;
            return go;
        }

        // The mirror: an arched frame of dark wood in three mouldings (a wide band, a pale bead round the glass and a raised
        // outer lip), a thin inlay along the band that glows in the portal's colour, carved capitals where the arch springs,
        // stepped feet, a crest at the top holding a surtling core, a solid board behind the glass, a soft light and motes of
        // light drifting up the glass. Frame, feet and back are solid; the back lets its own player through from the front
        // (Crossing), so they can step into the glass.
        private static void Model(Transform root,ZNetScene scene,GameObject vanilla)
        {
            Material dark=MaterialOf(scene,vanilla,"darkwood_beam","darkwood_pole","wood_beam");
            Material pale=MaterialOf(scene,vanilla,"wood_beam","wood_pole","woodwall");
            Material planks=MaterialOf(scene,vanilla,"wood_wall_half","woodwall","wood_floor");
            Material glow=GlowMaterial(dark);
            var visual=new GameObject("Visual").transform;
            visual.SetParent(root,false);
            new GameObject(ModelVersion).transform.SetParent(visual,false); // portals built with an older model are remade on a reload
            var solid=new GameObject("Frame").transform;
            solid.SetParent(root,false);
            solid.gameObject.layer=root.gameObject.layer;

            float hw=(float)Policy.HalfWidth,spring=(float)Policy.Spring,top=(float)Policy.Top,y0=(float)Policy.Bottom;
            Mesh band=Shapes.Moulding(-0.01f,0.17f,-0.12f,0.05f,y0);
            Part(visual,"Band",band,dark);
            Part(visual,"Bead",Shapes.Moulding(-0.015f,0.035f,-0.02f,0.085f,y0),pale);
            Part(visual,"Lip",Shapes.Moulding(0.15f,0.21f,-0.14f,0.075f,y0),dark);
            Part(visual,"Trim",Shapes.Moulding(0.075f,0.095f,0.04f,0.062f,y0+0.12f),glow,false);
            var bandCollider=new GameObject("Band");
            bandCollider.layer=solid.gameObject.layer;
            bandCollider.transform.SetParent(solid,false);
            bandCollider.AddComponent<MeshCollider>().sharedMesh=band;

            Mesh back=Shapes.Board(0.04f,(float)(Policy.BackZ-Policy.BackThickness/2),(float)(Policy.BackZ+Policy.BackThickness/2),y0);
            Part(visual,"BackBoard",back,planks);
            var backCollider=new GameObject("Back");
            backCollider.layer=solid.gameObject.layer;
            backCollider.transform.SetParent(solid,false);
            var mc=backCollider.AddComponent<MeshCollider>();mc.sharedMesh=back;mc.convex=true;

            float side=hw+0.09f,z=-0.035f;
            foreach(float s in new[]{-1f,1f})
            {
                string n=s<0?"L":"R";
                Block(visual,solid,"Foot"+n,dark,new Vector3(s*side,0.05f,z),new Vector3(0.34f,0.1f,0.78f));
                Block(visual,null,"Step"+n,dark,new Vector3(s*side,0.14f,z),new Vector3(0.26f,0.08f,0.56f));
                Block(visual,null,"Capital"+n,pale,new Vector3(s*side,spring,z+0.01f),new Vector3(0.3f,0.07f,0.27f));
                Block(visual,null,"Corbel"+n,dark,new Vector3(s*side,spring-0.08f,z),new Vector3(0.22f,0.09f,0.22f));
            }
            float crown=top+0.21f;
            Block(visual,null,"Crest",dark,new Vector3(0,crown+0.04f,z),new Vector3(0.34f,0.1f,0.26f));
            Block(visual,null,"CrestCap",pale,new Vector3(0,crown+0.11f,z),new Vector3(0.24f,0.05f,0.2f));
            Gem(visual,scene,new Vector3(0,crown+0.22f,z));

            var glass=new GameObject("Surface");
            glass.transform.SetParent(visual,false);
            glass.AddComponent<MeshFilter>().sharedMesh=_surface??=Shapes.Surface();
            var r=glass.AddComponent<MeshRenderer>();
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;

            var light=new GameObject("Light");
            light.transform.SetParent(visual,false);
            light.transform.localPosition=new Vector3(0,1.5f,0.5f);
            var l=light.AddComponent<Light>();
            l.type=LightType.Point;l.range=3.5f;l.intensity=0.45f;l.shadows=LightShadows.None;

            Motes(visual,vanilla);

            var wear=root.GetComponent<WearNTear>();
            if(wear!=null){wear.m_new=wear.m_worn=wear.m_broken=visual.gameObject;wear.m_fragmentRoots=null;}
        }

        private static void Part(Transform parent,string name,Mesh mesh,Material material,bool shadows=true)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();
            if(material!=null)r.sharedMaterial=material;
            r.shadowCastingMode=shadows?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // The game's own surtling core, set in the crest.
        private static void Gem(Transform parent,ZNetScene scene,Vector3 at)
        {
            MeshRenderer core=scene.GetPrefab("SurtlingCore")?.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x=>x.GetComponent<MeshFilter>()?.sharedMesh!=null);
            if(core==null)return;
            var go=new GameObject("Gem");
            go.transform.SetParent(parent,false);
            go.transform.localPosition=at;
            go.transform.localRotation=Quaternion.Euler(0,30,0);
            Mesh mesh=core.GetComponent<MeshFilter>().sharedMesh;
            float size=Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.y,mesh.bounds.size.z);
            go.transform.localScale=Vector3.one*(size>0?0.2f/size:1);
            go.transform.localPosition-=go.transform.localRotation*Vector3.Scale(mesh.bounds.center,go.transform.localScale);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=core.sharedMaterials;
        }

        // Motes of light drifting slowly up in front of the glass; each portal tints them.
        private static void Motes(Transform parent,GameObject vanilla)
        {
            Material spark=vanilla.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(x=>x.sharedMaterial).FirstOrDefault(m=>m!=null&&m.name.Contains("sparc"))
                ??vanilla.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(x=>x.sharedMaterial).FirstOrDefault(m=>m!=null);
            if(spark==null)return;
            var go=new GameObject("Motes");
            go.transform.SetParent(parent,false);
            go.transform.localPosition=new Vector3(0,(float)Policy.CentreY,0.12f);
            var ps=go.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.duration=5;main.loop=true;main.playOnAwake=true;
            main.startLifetime=new ParticleSystem.MinMaxCurve(3f,5f);
            main.startSpeed=0;
            main.startSize=new ParticleSystem.MinMaxCurve(0.012f,0.03f);
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=40;
            main.startColor=Color.white;
            var emission=ps.emission;emission.rateOverTime=5;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(2*(float)Policy.HalfWidth,(float)(Policy.Top-Policy.Bottom)*0.9f,0.2f);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;
            velocity.x=new ParticleSystem.MinMaxCurve(-0.03f,0.03f);velocity.y=new ParticleSystem.MinMaxCurve(0.04f,0.12f);velocity.z=new ParticleSystem.MinMaxCurve(-0.01f,0.03f);
            var noise=ps.noise;noise.enabled=true;noise.strength=0.05f;noise.frequency=0.4f;
            var fade=ps.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(0.9f,0.25f),new GradientAlphaKey(0.6f,0.7f),new GradientAlphaKey(0,1)});
            fade.color=gradient;
            var pr=go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial=spark;pr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        private static void Block(Transform visual,Transform solid,string name,Material material,Vector3 centre,Vector3 size,bool shadows=true)
        {
            if(!Boxes.TryGetValue(size,out Mesh mesh))Boxes[size]=mesh=Shapes.Box(size,1.2f);
            var go=new GameObject(name);
            go.transform.SetParent(visual,false);
            go.transform.localPosition=centre;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();
            if(material!=null)r.sharedMaterial=material;
            r.shadowCastingMode=shadows?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
            if(solid==null)return;
            var c=new GameObject(name);
            c.layer=solid.gameObject.layer;
            c.transform.SetParent(solid,false);
            c.transform.localPosition=centre;
            c.AddComponent<BoxCollider>().size=size;
        }

        private static Piece.Requirement Requirement(ZNetScene scene,string item,int amount)
        {
            ItemDrop drop=scene.GetPrefab(item)?.GetComponent<ItemDrop>();
            if(drop==null){Debug.LogWarning("[LocalPortals] Missing ingredient "+item);return null;}
            return new Piece.Requirement{m_resItem=drop,m_amount=amount,m_recover=true};
        }

        // The prefab's own building material: pieces also carry a snow overlay mesh, which is skipped.
        private static Material MaterialOf(ZNetScene scene,GameObject fallback,params string[] prefabs)
        {
            foreach(string name in prefabs)
            {
                Material m=Wood(scene.GetPrefab(name));
                if(m!=null)return m;
            }
            return Wood(fallback);
        }
        private static Material Wood(GameObject prefab)
        {
            if(prefab==null)return null;
            var all=prefab.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials)
                .Where(m=>m!=null&&m.shader!=null&&!m.name.ToLower().Contains("snow")&&!m.shader.name.ToLower().Contains("snow")).ToList();
            return all.FirstOrDefault(m=>m.shader.name.Contains("Piece"))??all.FirstOrDefault();
        }
        // The inlay: a copy of the wood's material (a shader the game surely has), untextured and glowing; each portal tints it.
        private static Material GlowMaterial(Material wood)
        {
            if(_glowMaterial!=null)return _glowMaterial;
            if(wood==null)return null;
            _glowMaterial=new Material(wood){name="LocalPortalGlow"};
            _glowMaterial.mainTexture=null;
            if(_glowMaterial.HasProperty("_Color"))_glowMaterial.color=Color.white;
            _glowMaterial.EnableKeyword("_EMISSION");
            return _glowMaterial;
        }

        // A hot reload leaves portals already in the world running the old copy's code: give them this copy's, and this
        // copy's model if they were built with an older one.
        internal static void Refresh()
        {
            if(ZNetScene.instance==null||_prefab==null)return;
            foreach(ZNetView view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if(view==null||!view.IsValid()||view.GetZDO().GetPrefab()!=Hash||view.GetComponent<LocalPortal>()!=null)continue;
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())
                    if(stale!=null&&stale.GetType().FullName==typeof(LocalPortal).FullName)Object.DestroyImmediate(stale);
                if(view.transform.Find("Visual/"+ModelVersion)==null)Remodel(view.transform);
                view.gameObject.AddComponent<LocalPortal>();
            }
        }
        private static void Remodel(Transform portal)
        {
            foreach(string child in new[]{"Visual","Frame","RingColliders"})
            {
                Transform old=portal.Find(child);
                if(old!=null)Object.DestroyImmediate(old.gameObject);
            }
            foreach(Collider c in portal.GetComponents<Collider>())Object.DestroyImmediate(c);
            foreach(string child in new[]{"Visual","Frame"})
            {
                GameObject copy=Object.Instantiate(_prefab.transform.Find(child).gameObject,portal,false);
                copy.name=child;
            }
            var wear=portal.GetComponent<WearNTear>();
            if(wear==null)return;
            GameObject visual=portal.Find("Visual").gameObject;
            wear.m_new=wear.m_worn=wear.m_broken=visual;
            wear.m_materialType=WearNTear.MaterialType.Wood;
            AccessTools.Method(typeof(WearNTear),"SetupColliders")?.Invoke(wear,null); // its support check keeps a list of its colliders
            // and its wear and highlighting keep lists of its renderers
            var renderers=AccessTools.Method(typeof(WearNTear),"GetHighlightRenderers")?.Invoke(wear,null);
            if(renderers!=null)AccessTools.Field(typeof(WearNTear),"m_renderers")?.SetValue(wear,renderers);
            AccessTools.Field(typeof(WearNTear),"m_oldMaterials")?.SetValue(wear,null);
        }

        internal static void Register(ZNetScene scene)
        {
            GameObject prefab;
            try{prefab=Build(scene);}
            catch(System.Exception e) // never stop a world from loading
            {
                Debug.LogError("[LocalPortals] Could not make the local portal; it is left out: "+e);
                if(_holder!=null)Object.Destroy(_holder);
                _holder=null;_prefab=null;
                return;
            }
            if(prefab==null)return;
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            named[Hash]=prefab; // a host without it deletes saved portals as invalid
            AddToHammer();
        }
        internal static void AddToHammer()
        {
            if(_prefab==null||ObjectDB.instance==null)return;
            PieceTable table=ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null)return;
            table.m_pieces.RemoveAll(p=>p==null||(p!=_prefab&&p.name==Name)); // an earlier copy's destroyed or stale entry
            if(table.m_pieces.Contains(_prefab))return;
            int vanilla=table.m_pieces.FindIndex(p=>p!=null&&p.name=="portal_wood");
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
}
