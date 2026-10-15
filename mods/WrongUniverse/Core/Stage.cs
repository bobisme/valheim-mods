using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // Studio pictures of a model for working on it (`wu look`): a copy far below the world in a layer of its own, lit by a key, a fill
    // and a rim light, taken from four sides on a grey background. Copies are made with networking and terrain changes off, and lose
    // their scripts, colliders and physics before they wake, as Icon.cs does.
    internal static class Stage
    {
        // Saves one picture (2x2 views) of the prefab's child (or the whole prefab) and returns its path, or null.
        internal static string Shoot(GameObject prefab,string child,string file,int size=768,float yaw=35f,float pitch=20f)
        {
            if(prefab==null)return null;
            Transform part=child!=null?prefab.transform.Find(child):null;
            GameObject source=part!=null?part.gameObject:prefab;
            int layer=31;for(int i=31;i>=8;i--)if(string.IsNullOrEmpty(LayerMask.LayerToName(i))){layer=i;break;}
            bool oldInit=ZNetView.m_forceDisableInit,oldTerrain=TerrainOp.m_forceDisableTerrainOps;
            var stage=new GameObject("WrongUniverseStage");stage.SetActive(false);stage.transform.position=new Vector3(0,-3200,0);
            RenderTexture rt=null,previous=RenderTexture.active;
            try
            {
                ZNetView.m_forceDisableInit=true;TerrainOp.m_forceDisableTerrainOps=true;
                GameObject copy=Object.Instantiate(source,stage.transform);
                copy.transform.localPosition=Vector3.zero;copy.transform.localRotation=part!=null?part.localRotation:Quaternion.identity;
                copy.SetActive(true);
                foreach(MonoBehaviour m in copy.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(m);
                foreach(Collider c in copy.GetComponentsInChildren<Collider>(true))c.enabled=false;
                foreach(Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
                foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                foreach(Renderer r in copy.GetComponentsInChildren<Renderer>(true))r.enabled=!(r is ParticleSystemRenderer);
                foreach(Light l in copy.GetComponentsInChildren<Light>(true))l.cullingMask=1<<layer;
                stage.SetActive(true);
                bool any=false;var b=new Bounds();
                foreach(Renderer r in copy.GetComponentsInChildren<Renderer>())
                {
                    if(!r.enabled||r is ParticleSystemRenderer||r is LineRenderer||r is TrailRenderer)continue;
                    if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);
                }
                if(!any)return null;

                var camGo=new GameObject("cam");camGo.transform.SetParent(stage.transform,false);
                Camera cam=camGo.AddComponent<Camera>();
                cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0.32f,0.33f,0.35f,1f);
                cam.cullingMask=1<<layer;cam.fieldOfView=30f;cam.allowHDR=false;cam.allowMSAA=true;
                float radius=Mathf.Max(0.05f,b.extents.magnitude);
                float dist=radius/Mathf.Sin(cam.fieldOfView*0.5f*Mathf.Deg2Rad)*1.05f;
                cam.nearClipPlane=Mathf.Max(0.01f,dist-radius*2f);cam.farClipPlane=dist+radius*2f;
                Light(stage,layer,Quaternion.Euler(40,-30,0),1.15f,Color.white);
                Light(stage,layer,Quaternion.Euler(15,150,0),0.45f,new Color(0.75f,0.85f,1f));
                Light(stage,layer,Quaternion.Euler(-20,60,0),0.35f,new Color(1f,0.9f,0.8f));
                rt=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default,4);
                cam.targetTexture=rt;
                var sheet=new Texture2D(size*2,size*2,TextureFormat.RGB24,false);
                float[] yaws={yaw,yaw+90f,yaw+180f,yaw+270f};
                for(int v=0;v<4;v++)
                {
                    Quaternion look=Quaternion.Euler(v==3?70f:pitch,yaws[v],0);   // (the last one from high up)
                    cam.transform.position=b.center-look*Vector3.forward*dist;cam.transform.rotation=look;
                    cam.Render();
                    RenderTexture.active=rt;
                    sheet.ReadPixels(new Rect(0,0,size,size),(v%2)*size,(v<2?1:0)*size);
                }
                sheet.Apply();
                System.IO.File.WriteAllBytes(file,ImageConversion.EncodeToPNG(sheet));
                Object.Destroy(sheet);
                return file;
            }
            finally
            {
                ZNetView.m_forceDisableInit=oldInit;TerrainOp.m_forceDisableTerrainOps=oldTerrain;
                RenderTexture.active=previous;
                if(rt!=null)RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(stage);
            }
        }
        private static void Light(GameObject stage,int layer,Quaternion turn,float intensity,Color color)
        {
            var go=new GameObject("light");go.transform.SetParent(stage.transform,false);go.transform.rotation=turn;
            Light l=go.AddComponent<Light>();l.type=LightType.Directional;l.intensity=intensity;l.color=color;l.shadows=LightShadows.None;l.cullingMask=1<<layer;
        }
    }
}
