using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // Shared plumbing for the mod's own prefabs: an inactive holder for templates, registering with the scene, effects, icons.
    internal static class Assets
    {
        private static GameObject _holder;
        internal static readonly List<Object> Owned=new List<Object>();
        private static readonly List<GameObject> Registered=new List<GameObject>();
        internal static readonly MethodInfo CloneShared=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);

        // Templates live under an inactive object so they never wake up as real things.
        internal static Transform Holder
        {
            get
            {
                if(_holder==null){_holder=new GameObject("ShieldwallPrefabs");_holder.SetActive(false);Object.DontDestroyOnLoad(_holder);}
                return _holder.transform;
            }
        }
        internal static GameObject Find(string name)
        {
            if(string.IsNullOrEmpty(name))return null;
            GameObject found=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(found==null&&ObjectDB.instance!=null)found=ObjectDB.instance.GetItemPrefab(name);
            return found;
        }
        private static Dictionary<int,GameObject> Named(ZNetScene scene)=>(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
        internal static void Register(ZNetScene scene,GameObject prefab)
        {
            if(scene==null||prefab==null)return;
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            Named(scene)[prefab.name.GetStableHashCode()]=prefab; // saved copies load (a game without it deletes them as invalid)
            if(!Registered.Contains(prefab))Registered.Add(prefab);
        }
        internal static void UnregisterAll()
        {
            if(ZNetScene.instance!=null)
                foreach(GameObject prefab in Registered)
                {
                    if(prefab==null)continue;
                    ZNetScene.instance.m_prefabs.Remove(prefab);
                    var named=Named(ZNetScene.instance);int hash=prefab.name.GetStableHashCode();
                    if(named.TryGetValue(hash,out GameObject current)&&current==prefab)named.Remove(hash); // only our own entry
                }
            Registered.Clear();
            Owned.Clear(); // materials and icons stay alive for things already in the world until they reload
            if(_holder!=null)Object.Destroy(_holder);_holder=null;
        }
        // Every loaded networked object made from one of our prefabs (once, at load: not on a timer).
        internal static IEnumerable<ZNetView> Instances(int prefabHash)
        {
            if(ZNetScene.instance==null)yield break;
            var all=AccessTools.Field(typeof(ZNetScene),"m_instances").GetValue(ZNetScene.instance) as IDictionary;
            if(all==null)yield break;
            foreach(object view in all.Values.Cast<object>().ToList())
                if(view is ZNetView v&&v!=null&&v.IsValid()&&v.GetZDO().GetPrefab()==prefabHash)yield return v;
        }

        internal static IEnumerable<ZNetView> AllInstances()
        {
            if(ZNetScene.instance==null)yield break;
            var all=AccessTools.Field(typeof(ZNetScene),"m_instances").GetValue(ZNetScene.instance) as IDictionary;
            if(all==null)yield break;
            foreach(object view in all.Values.Cast<object>().ToList())if(view is ZNetView v&&v!=null&&v.IsValid())yield return v;
        }
        // The game's own earthworks: the pickaxe's dig (taken from the pickaxe itself) and the hoe's raise.
        internal static void Dig(Vector3 at)
        {
            GameObject dig=null;
            foreach(string tool in new[]{"PickaxeIron","PickaxeBronze","PickaxeAntler"})
            {
                dig=ObjectDB.instance?.GetItemPrefab(tool)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_spawnOnHitTerrain;
                if(dig!=null)break;
            }
            if(dig!=null)Object.Instantiate(dig,at,Quaternion.identity);
        }
        internal static void Raise(Vector3 at){GameObject raise=Find("raise_v2");if(raise!=null)Object.Instantiate(raise,at,Quaternion.identity);}
        // A networked effect everyone sees (made once, by whoever runs the siege).
        internal static void Effect(string prefab,Vector3 at,Quaternion? rotation=null)
        {
            GameObject found=Find(prefab);
            if(found!=null)Object.Instantiate(found,at,rotation??Quaternion.identity);
        }

        // The look of a prefab with nothing else: its meshes (most detailed level only), as children of a parent, without colliders.
        internal static GameObject Model(GameObject source,Transform parent,Vector3 position,Quaternion rotation,float scale=1,Func<Renderer,bool> keep=null)
        {
            if(source==null)return null;
            var model=new GameObject(source.name+"_look");
            model.transform.SetParent(parent,false);model.transform.localPosition=position;model.transform.localRotation=rotation;model.transform.localScale=Vector3.one*scale;
            var lod0=new HashSet<Renderer>(source.GetComponentsInChildren<LODGroup>(true).SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).Where(r=>r!=null));
            var lodded=new HashSet<Renderer>(source.GetComponentsInChildren<LODGroup>(true).SelectMany(g=>g.GetLODs()).SelectMany(l=>l.renderers).Where(r=>r!=null));
            foreach(MeshRenderer r in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter f=r.GetComponent<MeshFilter>();
                if(f==null||f.sharedMesh==null||lodded.Contains(r)&&!lod0.Contains(r)||!Shown(r.transform,source.transform)||keep!=null&&!keep(r))continue;
                var part=new GameObject(r.name);part.transform.SetParent(model.transform,false);
                part.transform.localPosition=source.transform.InverseTransformPoint(r.transform.position);
                part.transform.localRotation=Quaternion.Inverse(source.transform.rotation)*r.transform.rotation;
                part.transform.localScale=Divide(r.transform.lossyScale,source.transform.lossyScale);
                part.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
            }
            return model;
        }
        // Switched on, and so is every part above it up to the source (a staff's hidden "loaded" glow is not part of its look).
        private static bool Shown(Transform part,Transform root)
        {
            for(Transform t=part;t!=null&&t!=root;t=t.parent)if(!t.gameObject.activeSelf)return false;
            return true;
        }
        private static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(b.x!=0?a.x/b.x:a.x,b.y!=0?a.y/b.y:a.y,b.z!=0?a.z/b.z:a.z);
        internal static void Tint(GameObject root,Color tint,Color glow)
        {
            foreach(Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if(r.GetType().Name=="ParticleSystemRenderer")continue;
                r.sharedMaterials=r.sharedMaterials.Select(m=>
                {
                    if(m==null)return null;
                    var copy=new Material(m){hideFlags=HideFlags.HideAndDontSave};
                    if(copy.HasProperty("_Color"))copy.color=m.color*tint;
                    if(glow.maxColorComponent>0&&copy.HasProperty("_EmissionColor")){copy.EnableKeyword("_EMISSION");copy.SetColor("_EmissionColor",glow);}
                    Owned.Add(copy);return copy;
                }).ToArray();
            }
        }

        // An inventory icon: the model drawn by a temporary camera far below the world, on a layer nothing else uses.
        internal static Sprite Icon(GameObject item,string name,Quaternion turn)
        {
            try
            {
                var stage=new GameObject("ShieldwallIconStage");
                stage.transform.position=new Vector3(0,-3000,0);
                try
                {
                    int layer=31;for(int l=31;l>8;l--)if(string.IsNullOrEmpty(LayerMask.LayerToName(l))){layer=l;break;}
                    GameObject model=Model(item,stage.transform,Vector3.zero,turn);
                    if(model==null)return null;
                    foreach(Transform t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                    Renderer[] renderers=model.GetComponentsInChildren<Renderer>();
                    if(renderers.Length==0)return null;
                    Bounds bounds=renderers[0].bounds;foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);
                    var cameraObject=new GameObject("Camera");cameraObject.transform.SetParent(stage.transform,false);
                    Camera camera=cameraObject.AddComponent<Camera>();
                    camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                    camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=200;camera.allowHDR=false;camera.allowMSAA=false;
                    float radius=Mathf.Max(0.05f,bounds.extents.magnitude);
                    camera.transform.position=bounds.center+new Vector3(0.1f,0.4f,-1f).normalized*(radius/Mathf.Sin(10*Mathf.Deg2Rad))*1.05f;
                    camera.transform.LookAt(bounds.center);
                    var lightObject=new GameObject("Light");lightObject.transform.SetParent(stage.transform,false);lightObject.transform.rotation=Quaternion.Euler(40,20,0);
                    Light light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.shadows=LightShadows.None;light.cullingMask=1<<layer;
                    const int size=256;
                    RenderTexture target=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
                    RenderTexture previous=RenderTexture.active;
                    try
                    {
                        camera.targetTexture=target;camera.Render();
                        RenderTexture.active=target;
                        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
                        texture.ReadPixels(new Rect(0,0,size,size),0,0);texture.Apply();
                        if(!Drawn(texture)){Object.Destroy(texture);return null;}
                        texture.hideFlags=HideFlags.HideAndDontSave;Owned.Add(texture);
                        Sprite sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(0.5f,0.5f),100f);
                        sprite.name=name;sprite.hideFlags=HideFlags.HideAndDontSave;Owned.Add(sprite);
                        return sprite;
                    }
                    finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);}
                }
                finally{Object.DestroyImmediate(stage);}
            }
            catch(Exception e){Debug.LogWarning("[Shieldwall] could not draw an icon for "+name+": "+e.Message);return null;}
        }
        private static bool Drawn(Texture2D texture)
        {
            Color32[] pixels=texture.GetPixels32();
            int covered=0;
            for(int i=0;i<pixels.Length;i+=7)if(pixels[i].a>40)covered++;
            return covered>pixels.Length/7*0.02f;
        }
    }
}
