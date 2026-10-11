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
            _holder=null;_camera=_sky=null;Cams.Clear();InnerWindows.Clear();
            if(_mid!=null){_mid.Release();Object.Destroy(_mid);_mid=null;}
            if(_small!=null){_small.Release();Object.Destroy(_small);_small=null;}
            if(_films!=null)foreach(Material m in _films)if(m!=null){Object.Destroy(m.mainTexture);Object.Destroy(m);}
            _films=null;
            if(_ghost!=null)Object.Destroy(_ghost);
            _ghost=null;
            foreach(Material m in Fades.Values)if(m!=null)Object.Destroy(m);
            Fades.Clear();
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
            int max=Plugin.MaxViews.Value,drawn=0,direct=Mathf.Min(Seen.Count,max);
            _budget=Policy.NestBudget(max,direct,Plugin.NestedViews.Value);
            Nested=0;
            // More mirrors in sight than views to draw: the nearest are drawn every frame and the rest take turns at the last
            // view, each keeping its last picture between turns (so every one of them shows its view).
            int turn=Seen.Count>max&&max>0?max-1+(_turn++%(Seen.Count-max+1)):-1;
            for(int i=0;i<Seen.Count;i++)
            {
                LocalPortal p=Seen[i];
                Vector4 window=new Vector4(0,0,1,1);
                bool mine=turn<0?i<direct:i<max-1||i==turn;
                bool ok=mine&&Draw(main,main,p,0,out window);
                if(ok){p.ShowPicture(true,window);drawn++;Sample(p);}
                else if(!mine&&p.Picture!=null&&p.PictureFrame>=0&&Time.frameCount-p.PictureFrame<=Policy.KeepPictureFrames(Seen.Count-max+1)){} // (its last picture, until its turn)
                else p.ShowPicture(false);
            }
            Drawn=drawn;
        }

        // A view through portal p as seen by `viewer` (the game camera, or the camera of a view that p shows in): drawn into
        // p's picture, or (depth 1) into its picture inside another view. A mirror seen in the view is drawn too, while views
        // are left in the frame's budget (Plugin.MaxViews: the ones the game camera sees come first).
        private static int _budget,_turn;
        internal static int Nested;         // views drawn inside views last frame
        private static readonly List<Camera> Cams=new List<Camera>();
        private static bool Draw(Camera main,Camera viewer,LocalPortal p,int depth,out Vector4 window)
        {
            LocalPortal q=p.Partner;
            Transform a=p.transform,b=q.transform;
            if(!Window(viewer,a,out window,out float px,out float py))return false;
            // The viewer, taken through: in front of this portal becomes behind the linked one.
            Vector3 local=a.InverseTransformPoint(viewer.transform.position);
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

            RenderTexture target=depth==0?(Picture(viewer,ref p.Picture,px,py,main.allowHDR)?p.Picture:null):(Picture(viewer,ref p.Nested,px,py,main.allowHDR)?p.Nested:null);
            if(target==null)return false;
            Ensure(main);
            Camera cam=Cam(main,depth);
            float far=Mathf.Max(main.farClipPlane,near+10);
            Quaternion turn=Quaternion.LookRotation(forward,up);
            cam.CopyFrom(main);
            cam.enabled=false;
            cam.targetTexture=target;
            cam.transform.SetPositionAndRotation(eye,turn);
            cam.nearClipPlane=near;cam.farClipPlane=far;
            cam.projectionMatrix=Matrix4x4.Frustum(left,rightEdge,bottom,top,near,far);

            // Mirrors in this view, nearest first, drawn as this camera sees them (only one level down).
            List<LocalPortal> inner=null;
            if(depth==0&&_budget>0)
            {
                GeometryUtility.CalculateFrustumPlanes(cam,InnerPlanes);
                var seen=new List<LocalPortal>();
                foreach(LocalPortal r in LocalPortal.Live)
                {
                    if(r==null||r==q||r.Partner==null||r.Surface==null||r.Partner.Surface==null)continue;
                    if(r.transform.InverseTransformPoint(eye).z<=0.02f||b.InverseTransformPoint(r.transform.position).z<=0)continue; // (facing the camera, and out past the glass it looks through)
                    if(Vector3.Distance(eye,r.transform.position)>Policy.ViewRange||!GeometryUtility.TestPlanesAABB(InnerPlanes,r.Surface.bounds))continue;
                    seen.Add(r);
                }
                seen.Sort((x,y)=>(x.transform.position-eye).sqrMagnitude.CompareTo((y.transform.position-eye).sqrMagnitude));
                foreach(LocalPortal r in seen)
                {
                    if(_budget<=0)break;
                    _budget--;
                    if(!Draw(main,cam,r,1,out Vector4 w))continue;
                    (inner??=new List<LocalPortal>()).Add(r);InnerWindows[r]=w;Nested++;
                }
            }

            // Every mirror's glass as this camera sees it: from behind, nothing; the glass it looks through is its near plane;
            // and never a picture drawn into itself.
            Kept.Clear();
            foreach(LocalPortal s in LocalPortal.Live)if(s!=null&&s.Surface!=null)Kept.Add((s,s.Surface.enabled,s.Surface.sharedMaterial));
            try
            {
                foreach(var (s,_,_) in Kept)
                {
                    bool show=s!=q&&s.transform.InverseTransformPoint(eye).z>0.02f;
                    if(inner!=null&&inner.Contains(s))s.Surface.sharedMaterial=s.NestedMaterial(InnerWindows[s]);
                    else if(s.Surface.sharedMaterial!=null&&s.Surface.sharedMaterial.mainTexture==target)show=false;
                    s.Surface.enabled=show;
                }
                Camera sky=GameCamera.instance.m_skyCamera;
                bool drawSky=sky!=null&&sky.isActiveAndEnabled&&(main.clearFlags==CameraClearFlags.Depth||main.clearFlags==CameraClearFlags.Nothing);
                if(drawSky)
                {
                    _sky.CopyFrom(sky);
                    _sky.enabled=false;
                    _sky.targetTexture=target;
                    _sky.transform.SetPositionAndRotation(eye,turn);
                    float n=Mathf.Max(sky.nearClipPlane,0.01f),k=n/near;
                    _sky.nearClipPlane=n;
                    _sky.projectionMatrix=Matrix4x4.Frustum(left*k,rightEdge*k,bottom*k,top*k,n,Mathf.Max(sky.farClipPlane,n+1));
                    _sky.Render();
                }
                if(!drawSky&&(cam.clearFlags==CameraClearFlags.Depth||cam.clearFlags==CameraClearFlags.Nothing))cam.clearFlags=CameraClearFlags.Skybox;
                cam.Render();
            }
            finally
            {
                foreach(var (s,enabled,material) in Kept)if(s!=null&&s.Surface!=null){s.Surface.enabled=enabled;s.Surface.sharedMaterial=material;}
                Kept.Clear();
            }
            if(depth==0)p.PictureFrame=Time.frameCount;
            return true;
        }
        private static readonly Plane[] InnerPlanes=new Plane[6];
        private static readonly Dictionary<LocalPortal,Vector4> InnerWindows=new Dictionary<LocalPortal,Vector4>();
        private static readonly List<(LocalPortal,bool,Material)> Kept=new List<(LocalPortal,bool,Material)>();
        private static Camera Cam(Camera main,int depth)
        {
            while(Cams.Count<=depth)
            {
                Camera c=Cams.Count==0?_camera:new GameObject("View"+Cams.Count).AddComponent<Camera>();
                if(Cams.Count>0){c.transform.SetParent(_holder.transform,false);c.enabled=false;Fog(main,c);}
                Cams.Add(c);
            }
            return Cams[depth];
        }

        // The far side's light (LocalPortal.SetSpill): the mean colour of a view just drawn, read back from the graphics card a
        // few times a second without waiting for it.
        private static RenderTexture _mid,_small;
        private static void Sample(LocalPortal p)
        {
            if(!Plugin.FarLight.Value||p.Picture==null||Time.time<p.NextSample||!SystemInfo.supportsAsyncGPUReadback)return;
            p.NextSample=Time.time+0.25f;
            if(_mid==null){_mid=new RenderTexture(32,32,0,RenderTextureFormat.ARGBHalf){name="LocalPortalSpill"};_mid.Create();}
            if(_small==null){_small=new RenderTexture(4,4,0,RenderTextureFormat.ARGBHalf){name="LocalPortalSpill"};_small.Create();}
            RenderTexture was=RenderTexture.active;
            Graphics.Blit(p.Picture,_mid);
            Graphics.Blit(_mid,_small);
            RenderTexture.active=was;
            LocalPortal target=p;
            UnityEngine.Rendering.AsyncGPUReadback.Request(_small,0,TextureFormat.RGBAFloat,r=>
            {
                if(r.hasError||target==null)return;
                var data=r.GetData<Color>();
                Color sum=Color.black;
                for(int i=0;i<data.Length;i++)sum+=data[i];
                if(data.Length>0)target.SetSpill(sum/data.Length);
            });
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

        private static bool Picture(Camera main,ref RenderTexture picture,float px,float py,bool hdr)
        {
            int sw=main.pixelWidth,sh=main.pixelHeight;
            double scale=Plugin.Resolution.Value;
            int w=Policy.PictureSize(px,sw,scale),h=Policy.PictureSize(py,sh,scale);
            RenderTexture rt=picture;
            if(rt!=null&&!Policy.Resize(rt.width,w)&&!Policy.Resize(rt.height,h))return true;
            if(rt!=null){rt.Release();Object.Destroy(rt);}
            // a little room to grow, so walking up to a portal does not remake its picture every few steps
            w=Mathf.Min(sw,Mathf.CeilToInt(w*1.25f/64)*64);h=Mathf.Min(sh,Mathf.CeilToInt(h*1.25f/64)*64);
            var format=hdr?RenderTextureFormat.DefaultHDR:RenderTextureFormat.Default;
            picture=new RenderTexture(w,h,24,format){name="LocalPortalView",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,antiAliasing=1};
            return picture.Create();
        }

        private static void Ensure(Camera main)
        {
            if(_camera!=null)return;
            _holder=new GameObject("LocalPortalCameras");
            Object.DontDestroyOnLoad(_holder);
            _camera=new GameObject("View").AddComponent<Camera>();
            _camera.transform.SetParent(_holder.transform,false);
            _camera.enabled=false;
            Fog(main,_camera);
            _sky=new GameObject("Sky").AddComponent<Camera>();
            _sky.transform.SetParent(_holder.transform,false);
            _sky.enabled=false;
        }

        // An unlit material showing a portal's picture as it is (the game camera's own effects then apply to it once).
        // The game draws its fog as a post effect on its camera. The view camera gets the same post-processing with only
        // the fog left on: colour grading, bloom and the rest are applied once, by the game camera, to the glass.
        private static void Fog(Camera main,Camera into)
        {
            var theirs=main.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
            if(theirs==null||theirs.profile==null)return;
            var profile=Object.Instantiate(theirs.profile);
            profile.name="LocalPortalViewProfile";
            profile.debugViews.enabled=false;profile.antialiasing.enabled=false;profile.ambientOcclusion.enabled=false;
            profile.screenSpaceReflection.enabled=false;profile.depthOfField.enabled=false;profile.motionBlur.enabled=false;
            profile.eyeAdaptation.enabled=false;profile.bloom.enabled=false;profile.colorGrading.enabled=false;profile.userLut.enabled=false;
            profile.chromaticAberration.enabled=false;profile.grain.enabled=false;profile.vignette.enabled=false;profile.dithering.enabled=false;
            var ours=into.gameObject.AddComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
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
        // A see-through copy of a wood material, for fading a mirror between solid and glow: the game's own piece shader
        // draws solid only, so the copy uses the standard shader in its fade mode, with the same textures and colour.
        private static readonly System.Collections.Generic.Dictionary<Material,Material> Fades=new System.Collections.Generic.Dictionary<Material,Material>();
        private static Shader _standard;
        internal static Material Fading(Material source)
        {
            if(source==null)return null;
            if(Fades.TryGetValue(source,out Material m))return m;
            if(_standard==null)foreach(Shader s in Resources.FindObjectsOfTypeAll<Shader>())if(s!=null&&s.name=="Standard"){_standard=s;break;}
            if(_standard==null){Fades[source]=null;return null;}
            m=new Material(_standard){name=source.name+" (fading)"};
            if(source.HasProperty("_MainTex")){m.mainTexture=source.mainTexture;m.mainTextureScale=source.mainTextureScale;m.mainTextureOffset=source.mainTextureOffset;}
            if(source.HasProperty("_BumpMap")&&source.GetTexture("_BumpMap")!=null)
            {
                m.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));
                if(source.HasProperty("_BumpScale"))m.SetFloat("_BumpScale",source.GetFloat("_BumpScale"));
                m.EnableKeyword("_NORMALMAP");
            }
            // matte, like the piece shader's wood: no shine or sky reflection (which turned it pale and blue)
            m.SetFloat("_Glossiness",0);m.SetFloat("_Metallic",0);
            m.SetFloat("_SpecularHighlights",0);m.SetFloat("_GlossyReflections",0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            if(source.IsKeywordEnabled("_EMISSION")&&source.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor",source.GetColor("_EmissionColor"));
            }
            m.SetFloat("_Mode",2);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);m.SetFloat("_ZWrite",1);
            m.DisableKeyword("_ALPHATEST_ON");m.EnableKeyword("_ALPHABLEND_ON");m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType","Transparent");
            m.renderQueue=2990;
            Fades[source]=m;
            return m;
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
