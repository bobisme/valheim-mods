using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // The build menu's picture of the piece, drawn from its own model when it is made: three-quarters from the front, glowing
    // blue as a linked mirror does, on a see-through background. Drawn twice, on black and on white, so the background's
    // see-through-ness comes from the difference, whatever the piece shader writes to alpha.
    internal static class Icon
    {
        internal const int Size=128;
        internal static Texture2D Last;     // for the lportal command

        internal static Sprite Draw(GameObject prefab)
        {
            Transform model=prefab.transform.Find("Visual");
            if(model==null)return null;
            int layer=FreeLayer();
            if(layer<0)return null;
            GameObject copy=Object.Instantiate(model.gameObject);
            var target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGB32){antiAliasing=4};
            GameObject cameraObject=null,lightObject=null;
            try
            {
                copy.name="LocalPortalIcon";
                copy.transform.SetPositionAndRotation(new Vector3(0,6000,0),Quaternion.identity);
                foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                foreach(ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true))ps.gameObject.SetActive(false);
                foreach(Light l in copy.GetComponentsInChildren<Light>(true))l.enabled=false;
                Dress(copy.transform);

                var bounds=new Bounds(copy.transform.position+Vector3.up*1.6f,Vector3.zero);
                foreach(Renderer r in copy.GetComponentsInChildren<Renderer>())if(r.enabled)bounds.Encapsulate(r.bounds);

                cameraObject=new GameObject("LocalPortalIconCamera");
                var cam=cameraObject.AddComponent<Camera>();
                cam.enabled=false;cam.cullingMask=1<<layer;cam.clearFlags=CameraClearFlags.SolidColor;
                cam.fieldOfView=22;cam.nearClipPlane=0.1f;cam.farClipPlane=60;cam.targetTexture=target;
                cam.allowHDR=false;cam.allowMSAA=true;
                Vector3 from=Quaternion.Euler(10,-28,0)*Vector3.forward; // in front (+z), a little to the left and above
                float fit=Mathf.Max(bounds.extents.y,bounds.extents.x*1.05f)/Mathf.Tan(cam.fieldOfView*0.5f*Mathf.Deg2Rad);
                cam.transform.position=bounds.center+from*(fit*0.96f+bounds.extents.z);
                cam.transform.LookAt(bounds.center);

                lightObject=new GameObject("LocalPortalIconLight");
                var sun=lightObject.AddComponent<Light>();
                sun.type=LightType.Directional;sun.cullingMask=1<<layer;sun.intensity=1.5f;sun.color=new Color(1f,0.95f,0.88f);
                sun.transform.rotation=Quaternion.LookRotation(-(from+Vector3.up*1.2f+Vector3.right*0.5f).normalized);

                var fillObject=new GameObject("LocalPortalIconFill");fillObject.transform.SetParent(lightObject.transform,false);
                var fill=fillObject.AddComponent<Light>();
                fill.type=LightType.Directional;fill.cullingMask=1<<layer;fill.intensity=0.55f;fill.color=new Color(0.75f,0.82f,1f);
                fill.transform.rotation=Quaternion.LookRotation(-(from+Vector3.left*1.5f).normalized);
                Color[] black=Shoot(cam,target,Color.black),white=Shoot(cam,target,Color.white);
                var pixels=new Color[black.Length];
                for(int i=0;i<pixels.Length;i++)
                {
                    Color b=black[i],w=white[i];
                    float a=Mathf.Clamp01(1-((w.r-b.r)+(w.g-b.g)+(w.b-b.b))/3);
                    pixels[i]=a>0.004f?new Color(Mathf.Clamp01(b.r/a),Mathf.Clamp01(b.g/a),Mathf.Clamp01(b.b/a),a):new Color(0,0,0,0);
                }
                var icon=new Texture2D(Size,Size,TextureFormat.RGBA32,false){name="LookingGlassPortalIcon",wrapMode=TextureWrapMode.Clamp};
                icon.SetPixels(pixels);
                icon.Apply(false,false);
                Last=icon;
                Sprite sprite=Sprite.Create(icon,new Rect(0,0,Size,Size),new Vector2(0.5f,0.5f),100);
                sprite.name="LookingGlassPortalIcon";
                return sprite;
            }
            finally
            {
                Object.DestroyImmediate(copy);
                if(cameraObject!=null)Object.DestroyImmediate(cameraObject);
                if(lightObject!=null)Object.DestroyImmediate(lightObject);
                target.Release();Object.DestroyImmediate(target);
            }
        }

        private static Color[] Shoot(Camera cam,RenderTexture target,Color background)
        {
            cam.backgroundColor=background;
            cam.Render();
            RenderTexture was=RenderTexture.active;
            RenderTexture.active=target;
            var read=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            read.ReadPixels(new Rect(0,0,Size,Size),0,0);
            read.Apply(false,false);
            RenderTexture.active=was;
            Color[] pixels=read.GetPixels();
            Object.DestroyImmediate(read);
            return pixels;
        }

        // As a linked blue mirror looks: its glass showing the blue film, its glowing parts lit, the shards spread round the crystal.
        private static void Dress(Transform copy)
        {
            Color c=LocalPortal.Colours[0];
            var block=new MaterialPropertyBlock();
            foreach(Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                if(!r.name.StartsWith("Trim"))continue;
                r.GetPropertyBlock(block);
                block.SetColor("_Color",new Color(c.r*0.35f,c.g*0.35f,c.b*0.35f,1));
                block.SetColor("_EmissionColor",c*1.4f);
                r.SetPropertyBlock(block);
            }
            Transform glass=copy.Find("Surface");
            Material film=Views.Film(0);
            if(glass!=null){var r=glass.GetComponent<MeshRenderer>();if(film!=null)r.sharedMaterial=film;else r.enabled=false;}
            Transform crystal=copy.Find("TrimCrystal");
            for(int i=0;i<3&&crystal!=null;i++)
            {
                Transform shard=copy.Find("TrimShard"+i);
                if(shard==null)continue;
                float a=1.3f+i*2.1f;
                shard.localPosition=crystal.localPosition+Quaternion.Euler(18+i*24,i*60,0)*new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(0.16f+0.05f*i);
                shard.localRotation=Quaternion.Euler(a*40,a*65,i*30);
            }
        }

        // A layer nothing in the game uses, so the icon camera sees only the copy.
        private static int FreeLayer()
        {
            for(int i=31;i>=8;i--)if(string.IsNullOrEmpty(LayerMask.LayerToName(i)))return i;
            return -1;
        }
    }
}
