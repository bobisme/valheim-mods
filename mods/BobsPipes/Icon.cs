using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;
namespace BobsPipes
{
    internal static class Icon
    {
        private const int Size=128;
        internal static Sprite Make(Transform model,List<Object> owned)
        {
            Texture2D texture=null;
            try{texture=Render(model);}
            catch(Exception e){Debug.LogWarning("[BobsPipes] could not draw the icon: "+e.Message);}
            if(texture==null)texture=Paint();
            texture.hideFlags=HideFlags.HideAndDontSave;owned.Add(texture);
            Sprite sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100f);
            sprite.name=model.name;sprite.hideFlags=HideFlags.HideAndDontSave;owned.Add(sprite);
            return sprite;
        }
        private static Texture2D Render(Transform model)
        {
            var stage=new GameObject("PipeIconStage");
            stage.transform.position=new Vector3(0,-3000,0); // far below the world where nothing else is
            try
            {
                GameObject copy=Object.Instantiate(model.gameObject,stage.transform);
                copy.transform.localPosition=Vector3.zero;
                copy.transform.localRotation=Quaternion.Euler(10,110,25); // lies diagonally, lens up and right
                copy.SetActive(true);
                // Alone on a layer the game leaves unnamed, so the camera cannot pick up its water, sky or terrain.
                int layer=StageLayer();
                foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                Renderer[] renderers=copy.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0)return null;
                Bounds bounds=renderers[0].bounds;
                foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);

                var cameraObject=new GameObject("PipeIconCamera");
                cameraObject.transform.SetParent(stage.transform,false);
                Camera camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=20;camera.allowHDR=false;camera.allowMSAA=false;
                float radius=bounds.extents.magnitude;
                camera.transform.position=bounds.center+new Vector3(0.12f,0.18f,-1f).normalized*(radius/Mathf.Sin(10*Mathf.Deg2Rad))*1.02f;
                camera.transform.LookAt(bounds.center);
                var lightObject=new GameObject("PipeIconLight");
                lightObject.transform.SetParent(stage.transform,false);
                lightObject.transform.rotation=Quaternion.Euler(40,30,0);
                Light light=lightObject.AddComponent<Light>();
                light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.None;light.cullingMask=1<<layer;

                RenderTexture target=RenderTexture.GetTemporary(Size*2,Size*2,24,RenderTextureFormat.ARGB32);
                RenderTexture previous=RenderTexture.active;
                try
                {
                    camera.targetTexture=target;camera.Render();
                    RenderTexture.active=target;
                    var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                    texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
                    if(Drawn(texture))return texture;
                    Object.Destroy(texture);return null;
                }
                finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);}
            }
            finally{Object.DestroyImmediate(stage);} // the stage light must not reach the next rendered frame
        }
        private static int StageLayer()
        {
            for(int layer=31;layer>8;layer--)if(string.IsNullOrEmpty(LayerMask.LayerToName(layer)))return layer;
            return 31;
        }
        // The model is centred with a margin, so a real drawing leaves every corner see-through.
        private static bool Drawn(Texture2D texture)
        {
            Color32[] pixels=texture.GetPixels32();
            int w=texture.width,h=texture.height,edge=Math.Max(2,w/16);
            foreach(var (cx,cy) in new[]{(0,0),(w-edge,0),(0,h-edge),(w-edge,h-edge)})
                for(int y=cy;y<cy+edge;y++)for(int x=cx;x<cx+edge;x++)
                    if(pixels[y*w+x].a>8)return false;
            int covered=0;long brightness=0;
            for(int i=0;i<pixels.Length;i+=7)if(pixels[i].a>40){covered++;brightness+=pixels[i].r+pixels[i].g+pixels[i].b;}
            return covered>pixels.Length/7*0.04f&&brightness/Math.Max(1,covered)>60;
        }
        // A bowl and bent stem when off-screen rendering is unavailable.
        private static Texture2D Paint()
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                Color color=new Color(0,0,0,0);
                float stem=41+0.004f*(x-25)*(x-25);
                if(x>=15&&x<91&&Mathf.Abs(y-stem)<5)color=new Color(0.37f,0.22f,0.12f);
                float bowl=(x-91)*(x-91)/324f+(y-71)*(y-71)/784f;
                if(bowl<1&&y<91)color=new Color(0.53f,0.32f,0.16f);
                if((x-91)*(x-91)/324f+(y-89)*(y-89)/36f<1)color=new Color(0.16f,0.10f,0.06f);
                texture.SetPixel(x,y,color);
            }
            texture.Apply();return texture;
        }
    }
}
