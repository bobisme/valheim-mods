using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // Draws what each portal looks onto. Just before the game camera draws a frame, a second camera stands where the game
    // camera would be if it had gone through the portal, and draws into the portal's picture through the linked portal's
    // opening: its frustum is cut to exactly that opening (an off-axis projection whose near plane is the linked portal's
    // surface), so the picture fits the surface corner to corner and nothing between the camera and the far opening shows.
    internal static class Views
    {
        private static GameObject _holder;
        private static Camera _camera,_sky;
        private static readonly Plane[] Planes=new Plane[6];
        private static readonly List<LocalPortal> Seen=new List<LocalPortal>();
        private static bool _drawing,_failed;
        private static Material[] _films;
        private static Shader _viewShader;
        private static bool _logged;
        internal static string ViewShaderName=>_viewShader!=null?_viewShader.name:"(none)";
        internal static int Drawn;          // portal views drawn last frame

        internal static void Start()=>Camera.onPreCull+=BeforeCull;
        internal static void Stop()
        {
            Camera.onPreCull-=BeforeCull;
            foreach(LocalPortal p in LocalPortal.Live)p.ShowPicture(false);
            if(_holder!=null)Object.Destroy(_holder);
            _holder=null;_camera=_sky=null;
            if(_films!=null)foreach(Material m in _films)if(m!=null){Object.Destroy(m.mainTexture);Object.Destroy(m);}
            _films=null;
            if(_ghost!=null)Object.Destroy(_ghost);
            _ghost=null;
        }

        private static Camera GameCam()=>GameCamera.instance!=null?GameCamera.instance.GetComponent<Camera>():null;

        private static void BeforeCull(Camera cam)
        {
            if(_drawing||cam==null||LocalPortal.Live.Count==0)return;
            Camera main=GameCam();
            if(cam!=main)return;
            _drawing=true;
            try{Doubles.Sync();DrawAll(main);}
            catch(Exception e){if(!_failed){_failed=true;Debug.LogError("[LocalPortals] Drawing a portal view failed: "+e);}}
            finally{_drawing=false;}
        }

        private static void DrawAll(Camera main)
        {
            GeometryUtility.CalculateFrustumPlanes(main,Planes);
            Vector3 eye=main.transform.position;
            Seen.Clear();
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Surface==null)continue;
                bool front=p.transform.InverseTransformPoint(eye).z>0.02f;
                p.Surface.enabled=front; // the back of the surface shows nothing: you see straight through the ring
                if(!front||!GeometryUtility.TestPlanesAABB(Planes,p.Surface.bounds))continue;
                if(p.Partner!=null&&p.Partner.Surface!=null&&Vector3.Distance(eye,p.transform.position)<Policy.ViewRange)Seen.Add(p);
                else p.ShowPicture(false);
            }
            Seen.Sort((a,b)=>(a.transform.position-eye).sqrMagnitude.CompareTo((b.transform.position-eye).sqrMagnitude));
            int drawn=0;
            for(int i=0;i<Seen.Count;i++)
            {
                LocalPortal p=Seen[i];
                Vector4 window=new Vector4(0,0,1,1);
                bool ok=i<Plugin.MaxViews.Value&&Draw(main,p,p.Partner,out window);
                p.ShowPicture(ok,window);
                if(ok)drawn++;
            }
            Drawn=drawn;
        }

        private static bool Draw(Camera main,LocalPortal p,LocalPortal q,out Vector4 window)
        {
            Transform a=p.transform,b=q.transform;
            if(!Window(main,a,out window,out float px,out float py))return false;
            // The game camera, taken through: in front of this portal becomes behind the linked one.
            Vector3 local=a.InverseTransformPoint(main.transform.position);
            Vector3 eye=b.TransformPoint(new Vector3(-local.x,local.y,-local.z));
            float hw=(float)Policy.HalfWidth,y0=(float)Policy.Bottom,y1=(float)Policy.Top;
            float X(float u)=>hw-2*hw*u;           // this glass's x at u (u runs to the viewer's right, the portal's -x)
            float Y(float v)=>y0+(y1-y0)*v;
            // The part of the linked opening's box that shows through the visible part of this glass: lower left, lower
            // right and upper left as seen from that camera (this glass's x is the linked glass's -x).
            Vector3 pa=b.TransformPoint(new Vector3(-X(window.x),Y(window.y),0)),pb=b.TransformPoint(new Vector3(-X(window.z),Y(window.y),0)),pc=b.TransformPoint(new Vector3(-X(window.x),Y(window.w),0));
            Vector3 right=(pb-pa).normalized,up=(pc-pa).normalized,forward=Vector3.Cross(right,up);
            float near=Vector3.Dot(forward,pa-eye);
            if(near<0.03f)return false;
            float left=Vector3.Dot(right,pa-eye),rightEdge=Vector3.Dot(right,pb-eye),bottom=Vector3.Dot(up,pa-eye),top=Vector3.Dot(up,pc-eye);

            if(!Picture(main,p,px,py))return false;
            Ensure(main);
            float far=Mathf.Max(main.farClipPlane,near+10);
            Matrix4x4 projection=Matrix4x4.Frustum(left,rightEdge,bottom,top,near,far);
            Quaternion turn=Quaternion.LookRotation(forward,up);

            bool mine=p.Surface.enabled,theirs=q.Surface.enabled;
            p.Surface.enabled=false;q.Surface.enabled=false; // never draw a picture into itself; the far surface is the near plane
            try
            {
                Camera sky=GameCamera.instance.m_skyCamera;
                bool drawSky=sky!=null&&sky.isActiveAndEnabled&&(main.clearFlags==CameraClearFlags.Depth||main.clearFlags==CameraClearFlags.Nothing);
                if(drawSky)
                {
                    _sky.CopyFrom(sky);
                    _sky.enabled=false;
                    _sky.targetTexture=p.Picture;
                    _sky.transform.SetPositionAndRotation(eye,turn);
                    float n=Mathf.Max(sky.nearClipPlane,0.01f),s=n/near;
                    _sky.nearClipPlane=n;
                    _sky.projectionMatrix=Matrix4x4.Frustum(left*s,rightEdge*s,bottom*s,top*s,n,Mathf.Max(sky.farClipPlane,n+1));
                    _sky.Render();
                }
                _camera.CopyFrom(main);
                _camera.enabled=false;
                _camera.targetTexture=p.Picture;
                _camera.transform.SetPositionAndRotation(eye,turn);
                if(!drawSky&&(_camera.clearFlags==CameraClearFlags.Depth||_camera.clearFlags==CameraClearFlags.Nothing))_camera.clearFlags=CameraClearFlags.Skybox;
                _camera.nearClipPlane=near;_camera.farClipPlane=far;
                _camera.projectionMatrix=projection;
                _camera.Render();
            }
            finally{p.Surface.enabled=mine;q.Surface.enabled=theirs;}
            p.PictureFrame=Time.frameCount;
            return true;
        }

        // The part of a portal's glass box that is on screen, as (u0, v0, u1, v1) of the whole, and how many screen pixels it
        // covers. Drawing only that part keeps the view as sharp as the screen even with the glass filling it.
        private static readonly Vector3[] Corner=new Vector3[4];
        private static readonly Vector2[] CornerUv={new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
        private static bool Window(Camera main,Transform a,out Vector4 window,out float px,out float py)
        {
            window=new Vector4(0,0,1,1);
            float hw=(float)Policy.HalfWidth,y0=(float)Policy.Bottom,y1=(float)Policy.Top;
            Corner[0]=new Vector3(hw,y0,0);Corner[1]=new Vector3(-hw,y0,0);Corner[2]=new Vector3(hw,y1,0);Corner[3]=new Vector3(-hw,y1,0);
            float minX=1,maxX=0,minY=1,maxY=0;
            bool behind=false;
            var screen=new Vector3[4];
            for(int i=0;i<4;i++)
            {
                screen[i]=main.WorldToViewportPoint(a.TransformPoint(Corner[i]));
                if(screen[i].z<=main.nearClipPlane){behind=true;continue;}
                minX=Mathf.Min(minX,screen[i].x);maxX=Mathf.Max(maxX,screen[i].x);minY=Mathf.Min(minY,screen[i].y);maxY=Mathf.Max(maxY,screen[i].y);
            }
            if(behind){minX=0;maxX=1;minY=0;maxY=1;}
            minX=Mathf.Max(minX,0);maxX=Mathf.Min(maxX,1);minY=Mathf.Max(minY,0);maxY=Mathf.Min(maxY,1);
            px=(maxX-minX)*main.pixelWidth;py=(maxY-minY)*main.pixelHeight;
            if(maxX<=minX||maxY<=minY)return false;
            // the glass's own corners on screen, and where the screen region's corners land on the glass
            float u0=1,u1=0,v0=1,v1=0;
            bool any=false;
            void Take(Vector2 uv){u0=Mathf.Min(u0,uv.x);u1=Mathf.Max(u1,uv.x);v0=Mathf.Min(v0,uv.y);v1=Mathf.Max(v1,uv.y);any=true;}
            for(int i=0;i<4;i++)
                if(screen[i].z>main.nearClipPlane&&screen[i].x>=minX-0.001f&&screen[i].x<=maxX+0.001f&&screen[i].y>=minY-0.001f&&screen[i].y<=maxY+0.001f)Take(CornerUv[i]);
            var plane=new Plane(a.forward,a.position);
            foreach(var c in new[]{new Vector2(minX,minY),new Vector2(maxX,minY),new Vector2(minX,maxY),new Vector2(maxX,maxY)})
            {
                Ray ray=main.ViewportPointToRay(new Vector3(c.x,c.y,0));
                if(!plane.Raycast(ray,out float t)||t<=0)continue;
                Vector3 l=a.InverseTransformPoint(ray.GetPoint(t));
                Take(new Vector2(Mathf.Clamp01((hw-l.x)/(2*hw)),Mathf.Clamp01((l.y-y0)/(y1-y0))));
            }
            if(!any)return true;
            const float margin=0.01f;
            window=new Vector4(Mathf.Max(0,u0-margin),Mathf.Max(0,v0-margin),Mathf.Min(1,u1+margin),Mathf.Min(1,v1+margin));
            return window.z>window.x&&window.w>window.y;
        }

        private static bool Picture(Camera main,LocalPortal p,float px,float py)
        {
            int sw=main.pixelWidth,sh=main.pixelHeight;
            double scale=Plugin.Resolution.Value;
            int w=Policy.PictureSize(px,sw,scale),h=Policy.PictureSize(py,sh,scale);
            RenderTexture rt=p.Picture;
            if(rt!=null&&!Policy.Resize(rt.width,w)&&!Policy.Resize(rt.height,h))return true;
            if(rt!=null){rt.Release();Object.Destroy(rt);}
            // a little room to grow, so walking up to a portal does not remake its picture every few steps
            w=Mathf.Min(sw,Mathf.CeilToInt(w*1.25f/64)*64);h=Mathf.Min(sh,Mathf.CeilToInt(h*1.25f/64)*64);
            var format=main.allowHDR?RenderTextureFormat.DefaultHDR:RenderTextureFormat.Default;
            p.Picture=new RenderTexture(w,h,24,format){name="LocalPortalView",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,antiAliasing=1};
            return p.Picture.Create();
        }

        private static void Ensure(Camera main)
        {
            if(_camera!=null)return;
            _holder=new GameObject("LocalPortalCameras");
            Object.DontDestroyOnLoad(_holder);
            _camera=new GameObject("View").AddComponent<Camera>();
            _camera.transform.SetParent(_holder.transform,false);
            _camera.enabled=false;
            Fog(main);
            _sky=new GameObject("Sky").AddComponent<Camera>();
            _sky.transform.SetParent(_holder.transform,false);
            _sky.enabled=false;
        }

        // An unlit material showing a portal's picture as it is (the game camera's own effects then apply to it once).
        // The game draws its fog as a post effect on its camera. The view camera gets the same post-processing with only
        // the fog left on: colour grading, bloom and the rest are applied once, by the game camera, to the glass.
        private static void Fog(Camera main)
        {
            var theirs=main.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
            if(theirs==null||theirs.profile==null)return;
            var profile=Object.Instantiate(theirs.profile);
            profile.name="LocalPortalViewProfile";
            profile.debugViews.enabled=false;profile.antialiasing.enabled=false;profile.ambientOcclusion.enabled=false;
            profile.screenSpaceReflection.enabled=false;profile.depthOfField.enabled=false;profile.motionBlur.enabled=false;
            profile.eyeAdaptation.enabled=false;profile.bloom.enabled=false;profile.colorGrading.enabled=false;profile.userLut.enabled=false;
            profile.chromaticAberration.enabled=false;profile.grain.enabled=false;profile.vignette.enabled=false;profile.dithering.enabled=false;
            var ours=_camera.gameObject.AddComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
            ours.profile=profile;
            Plugin.Log("Portal views draw the game's fog: "+profile.fog.enabled);
        }

        // The game has no plain unlit shader, so the picture is shown with its own unlit particle shader set to opaque:
        // drawn solid and depth-tested like a wall, not blended with what is behind the portal.
        internal static Material ViewMaterial()
        {
            if(_viewShader==null)
            {
                // Shader.Find misses the game's own shaders (they come from its asset bundles): look among those loaded, once.
                Shader[] loaded=Resources.FindObjectsOfTypeAll<Shader>();
                foreach(string name in new[]{"Unlit/Texture","Particles/Standard Unlit2","Particles/Standard Unlit","Sprites/Default"})
                {
                    _viewShader=Shader.Find(name);
                    if(_viewShader==null)foreach(Shader s in loaded)if(s!=null&&s.name==name){_viewShader=s;break;}
                    if(_viewShader!=null)break;
                }
            }
            if(_viewShader==null)return null;
            if(!_logged){_logged=true;Plugin.Log("Portal glass shader: "+_viewShader.name);}
            var m=new Material(_viewShader){name="LocalPortalView"};
            if(_viewShader.name.StartsWith("Particles/"))
            {
                m.SetFloat("_Mode",0);m.SetFloat("_SrcBlend",1);m.SetFloat("_DstBlend",0);m.SetFloat("_ZWrite",1);
                m.SetFloat("_SoftParticlesEnabled",0);m.SetFloat("_CameraFadingEnabled",0);m.SetFloat("_DistortionEnabled",0);
                foreach(string k in new[]{"_ALPHATEST_ON","_ALPHABLEND_ON","_ALPHAPREMULTIPLY_ON","_ALPHAMODULATE_ON","_SOFTPARTICLES_ON","_FADING_ON","_DISTORTION_ON","_COLORADDSUBDIFF_ON","_COLOROVERLAY_ON","_COLORCOLOR_ON","_FLIPBOOKBLENDING_ON","_EMISSION"})m.DisableKeyword(k);
                m.SetOverrideTag("RenderType","Opaque");
                m.SetColor("_Color",Color.white);
                m.renderQueue=2000;
            }
            return m;
        }
        // Faint and see-through: added onto what is behind, never hiding it, and tinted per portal (_Color).
        private static Material _ghost;
        internal static Material Ghost()
        {
            if(_ghost!=null)return _ghost;
            Material m=ViewMaterial();
            if(m==null||!m.shader.name.StartsWith("Particles/"))return null;
            m.name="LocalPortalGhost";
            m.mainTexture=Texture2D.whiteTexture;
            m.SetFloat("_Mode",4);m.SetFloat("_SrcBlend",1);m.SetFloat("_DstBlend",1);m.SetFloat("_ZWrite",0);
            m.SetOverrideTag("RenderType","Transparent");
            m.renderQueue=3000;
            return _ghost=m;
        }
        internal static Material Film(int colour)
        {
            _films??=new Material[3];
            int i=colour<0?2:Mathf.Clamp(colour,0,1);
            if(_films[i]!=null)return _films[i];
            Material m=ViewMaterial();
            if(m==null)return null;
            m.name="LocalPortalFilm";
            m.mainTexture=Shapes.Film(i<2?LocalPortal.Colours[i]:new Color(0.5f,0.5f,0.55f));
            return _films[i]=m;
        }
    }
}
