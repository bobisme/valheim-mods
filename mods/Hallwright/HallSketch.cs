using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Hallwright
{
    // The Sketch preview: each piece as an architect's drawing, inked along the edges where its surface folds and where it ends
    // (SketchEdges), washed in its own colour like a Piranesi drawing: timber brown, thatch straw, stone grey, each softened towards
    // the paper. No shader of our own (Valheim can't compile one while it runs): the wash is the piece's own material with its texture
    // swapped for a sheet of watercolour paper tinted the texture's average colour, so the sun, shade and fog fall on it as on
    // everything else (unlit, it glared white through the game's bloom and fog); the ink is the game's plain colour shader.
    // The ink is pen strokes (SketchEdges.Strokes: wide, overshooting, now and then gone over twice) over one-pixel lines, which keep
    // a far-off hall drawn where the strokes get thinner than a pixel.
    public sealed partial class Plugin
    {
        private static readonly string[] HallPreviewModes={"Sketch","Materials","Ghosts","Layout"};
        private bool _hallSketch=true;
        private int HallPreviewMode=>_hallGuideOnly?3:_hallSketch?0:_hallSolid?1:2;
        private void SetHallPreviewMode(int mode){_hallGuideOnly=mode==3;_hallSketch=mode==0;if(mode==1 || mode==2)_hallSolid=mode==1;}

        private static readonly Color SketchPaper=new Color(0.93f,0.9f,0.83f),SketchInk=new Color(0.16f,0.14f,0.13f),
            SketchFallsPaper=new Color(1f,0.55f,0.48f),SketchFallsInk=new Color(0.78f,0.12f,0.08f);
        private const float SketchWash=0.75f; // (how much of the piece's own colour shows through the paper)
        // A hand-laid wash is never even: each piece gets one of a few slightly different mixes of its colour.
        private static readonly Color[] SketchMixes={new Color(1,1,1),new Color(1.05f,1.02f,0.96f),new Color(0.95f,0.96f,1),new Color(1.02f,0.97f,0.94f),new Color(0.97f,1.01f,0.97f)};
        private Material _sketchInk,_sketchFallsInk;
        private Texture2D _sketchSheet;
        private readonly Dictionary<(Material,int),Material> _sketchPapers=new Dictionary<(Material,int),Material>();
        private readonly Dictionary<Material,Material> _sketchPaperSource=new Dictionary<Material,Material>();
        private readonly Dictionary<Texture,Color> _sketchAverages=new Dictionary<Texture,Color>();
        private readonly Dictionary<(Mesh,Vector3),Mesh> _sketchInkMeshes=new Dictionary<(Mesh,Vector3),Mesh>();
        private readonly Dictionary<Mesh,Mesh> _sketchFlatMeshes=new Dictionary<Mesh,Mesh>();
        private Material _sketchFaintInk;
        private Mesh _sketchConstruction;
        private readonly MaterialPropertyBlock _sketchFallsTint=new MaterialPropertyBlock();
        private readonly Dictionary<string,GameObject> _sketchTemplates=new Dictionary<string,GameObject>();

        // The ink: the game's plain colour shader, drawn over the paper.
        private static Material SketchInkMaterial(Color color)
        {
            Shader shader=new[]{"Hidden/Internal-Colored","Sprites/Default"}.Select(Shader.Find).FirstOrDefault(s=>s!=null && s.isSupported);
            if(shader==null)throw new InvalidOperationException("No supported sketch shader.");
            var m=new Material(shader){color=color,renderQueue=2460,hideFlags=HideFlags.HideAndDontSave};
            void Set(string name,int value){if(m.HasProperty(name))m.SetInt(name,value);}
            Set("_SrcBlend",(int)BlendMode.SrcAlpha);Set("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            Set("_ZWrite",0);Set("_ZTest",(int)CompareFunction.LessEqual);Set("_Cull",(int)CullMode.Off);
            return m;
        }
        // Watercolour paper: soft blotches where the pigment pooled, and a fine grain, tiling seamlessly (wrapped value noise).
        private Texture2D SketchSheet()
        {
            if(_sketchSheet!=null)return _sketchSheet;
            const int size=256;
            var random=new System.Random(1748);
            float[] Lattice(int cells){var v=new float[cells*cells];for(int i=0;i<v.Length;i++)v[i]=(float)random.NextDouble();return v;}
            float Noise(float[] lattice,int cells,int x,int y)
            {
                float fx=(float)x*cells/size,fy=(float)y*cells/size;int x0=(int)fx,y0=(int)fy;float tx=fx-x0,ty=fy-y0;
                tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);
                float At(int cx,int cy)=>lattice[(cy%cells)*cells+cx%cells];
                return Mathf.Lerp(Mathf.Lerp(At(x0,y0),At(x0+1,y0),tx),Mathf.Lerp(At(x0,y0+1),At(x0+1,y0+1),tx),ty);
            }
            float[] blotches=Lattice(4),pools=Lattice(9),grain=Lattice(64),fibre=Lattice(128);
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float wash=0.55f*Noise(blotches,4,x,y)+0.45f*Noise(pools,9,x,y);
                float v=0.78f+0.2f*wash+0.06f*(Noise(grain,64,x,y)-0.5f)+0.04f*(Noise(fibre,128,x,y)-0.5f);
                pixels[y*size+x]=new Color(v,v,v,1);
            }
            _sketchSheet=new Texture2D(size,size,TextureFormat.RGBA32,true){name="Hallwright sketch paper",wrapMode=TextureWrapMode.Repeat,hideFlags=HideFlags.HideAndDontSave};
            _sketchSheet.SetPixels(pixels);_sketchSheet.Apply(true);
            return _sketchSheet;
        }
        // A texture's average colour (weighted by its alpha, so the clear edges of a thatch card don't count). The game's textures can't be
        // read directly, so it's shrunk on the graphics card (its smallest mip levels do the averaging) and the few pixels read back.
        private Color SketchAverage(Texture texture)
        {
            if(texture==null)return Color.white;
            if(_sketchAverages.TryGetValue(texture,out var cached))return cached;
            Color average=Color.white;
            RenderTexture small=RenderTexture.GetTemporary(8,8,0,RenderTextureFormat.ARGB32),previous=RenderTexture.active;
            var read=new Texture2D(8,8,TextureFormat.RGBA32,false);
            try
            {
                Graphics.Blit(texture,small);RenderTexture.active=small;
                read.ReadPixels(new Rect(0,0,8,8),0,0);read.Apply();
                float r=0,g=0,b=0,weight=0;
                foreach(Color c in read.GetPixels()){r+=c.r*c.a;g+=c.g*c.a;b+=c.b*c.a;weight+=c.a;}
                if(weight>0.01f)average=new Color(r/weight,g/weight,b/weight);
            }
            catch(Exception ex){Logger.LogDebug("Sketch colour of "+texture.name+": "+ex.Message);}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(small);Destroy(read);}
            return _sketchAverages[texture]=average;
        }
        // The wash: the piece's material with watercolour paper in its piece's colour for its texture, and no grain in its surface (so its
        // light and shade stay). mix: which of the SketchMixes.
        private Material SketchPaperMaterial(Material source,int mix=0)
        {
            if(source==null)return null;
            if(_sketchPapers.TryGetValue((source,mix),out var cached))return cached;
            var paper=new Material(source){name=source.name+" (sketch paper)",hideFlags=HideFlags.HideAndDontSave};
            if(paper.HasProperty("_MainTex"))paper.SetTexture("_MainTex",SketchSheet());
            foreach(string grain in new[]{"_BumpMap","_DetailAlbedoMap","_DetailNormalMap","_EmissionMap"})if(paper.HasProperty(grain))paper.SetTexture(grain,null);
            Color own=source.HasProperty("_MainTex")?SketchAverage(source.GetTexture("_MainTex")):Color.white;
            if(source.HasProperty("_Color"))own*=source.GetColor("_Color");
            Color wash=Color.Lerp(SketchPaper,new Color(own.r,own.g,own.b),SketchWash)*SketchMixes[mix];
            if(paper.HasProperty("_Color"))paper.SetColor("_Color",new Color(wash.r,wash.g,wash.b,1));
            foreach(string shine in new[]{"_Glossiness","_Metallic","_Smoothness"})if(paper.HasProperty(shine))paper.SetFloat(shine,0);
            _sketchPaperSource[paper]=source;
            return _sketchPapers[(source,mix)]=paper;
        }
        // The ink for one mesh at the size it's drawn: strokes (submesh 0) over one-pixel lines (submesh 1); the edges of its box where the
        // game keeps the mesh from being read.
        private Mesh SketchInkMesh(Mesh source,Vector3 scale)
        {
            var key=(source,new Vector3((float)Math.Round(scale.x,3),(float)Math.Round(scale.y,3),(float)Math.Round(scale.z,3)));
            if(_sketchInkMeshes.TryGetValue(key,out var cached))return cached;
            float[] points;int[] lines;
            if(source.isReadable)
            {
                Vector3[] v=source.vertices;var xyz=new float[v.Length*3];
                for(int i=0;i<v.Length;i++){xyz[3*i]=v[i].x;xyz[3*i+1]=v[i].y;xyz[3*i+2]=v[i].z;}
                var triangles=new List<int>();
                for(int sub=0;sub<source.subMeshCount;sub++)if(source.GetTopology(sub)==MeshTopology.Triangles)triangles.AddRange(source.GetTriangles(sub));
                (points,lines)=SketchEdges.Find(xyz,triangles.ToArray());
            }
            else{points=new float[0];lines=new int[0];}
            if(lines.Length==0)
            {
                Bounds b=source.bounds;points=new float[24];
                for(int i=0;i<8;i++){Vector3 c=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));points[3*i]=c.x;points[3*i+1]=c.y;points[3*i+2]=c.z;}
                lines=new[]{0,1,2,3,4,5,6,7,0,2,1,3,4,6,5,7,0,4,1,5,2,6,3,7};
            }
            // Both are made at the part's scale; the ink's own transform undoes it (HallSketchTemplate).
            var (strokeXyz,strokeTriangles)=SketchEdges.Strokes(points,lines,scale.x,scale.y,scale.z);
            int strokeCorners=strokeXyz.Length/3,lineCorners=points.Length/3;
            var corners=new Vector3[strokeCorners+lineCorners];
            for(int i=0;i<strokeCorners;i++)corners[i]=new Vector3(strokeXyz[3*i],strokeXyz[3*i+1],strokeXyz[3*i+2]);
            for(int i=0;i<lineCorners;i++)corners[strokeCorners+i]=new Vector3(points[3*i]*scale.x,points[3*i+1]*scale.y,points[3*i+2]*scale.z);
            var ink=new Mesh{name=source.name+" (sketch ink)",hideFlags=HideFlags.HideAndDontSave};
            if(corners.Length>65535)ink.indexFormat=IndexFormat.UInt32;
            ink.vertices=corners;ink.subMeshCount=2;
            ink.SetIndices(strokeTriangles,MeshTopology.Triangles,0);
            ink.SetIndices(lines.Select(i=>i+strokeCorners).ToArray(),MeshTopology.Lines,1);
            ink.RecalculateBounds();
            return _sketchInkMeshes[key]=ink;
        }
        // A roof's mesh lit as its plain slopes (SketchEdges.Planes): its thatch bumps made the paper look crumpled in the sun. The shape is
        // the same; only the light on it changes.
        private Mesh SketchFlatMesh(Mesh source)
        {
            if(!source.isReadable)return source;
            if(_sketchFlatMeshes.TryGetValue(source,out var cached))return cached;
            Vector3[] v=source.vertices;var xyz=new float[v.Length*3];
            for(int i=0;i<v.Length;i++){xyz[3*i]=v[i].x;xyz[3*i+1]=v[i].y;xyz[3*i+2]=v[i].z;}
            var triangles=new List<int>();
            for(int sub=0;sub<source.subMeshCount;sub++)if(source.GetTopology(sub)==MeshTopology.Triangles)triangles.AddRange(source.GetTriangles(sub));
            float[] n=SketchEdges.Planes(xyz,triangles.ToArray());
            var flat=Object.Instantiate(source);flat.name=source.name+" (sketch planes)";flat.hideFlags=HideFlags.HideAndDontSave;
            var normals=new Vector3[v.Length];for(int i=0;i<normals.Length;i++)normals[i]=new Vector3(n[3*i],n[3*i+1],n[3*i+2]);
            flat.normals=normals;
            return _sketchFlatMeshes[source]=flat;
        }
        private GameObject HallSketchTemplate(string name)
        {
            if(_sketchTemplates.TryGetValue(name,out var cached) && cached!=null)return cached;
            if(_sketchInk==null){_sketchInk=SketchInkMaterial(SketchInk);_sketchFallsInk=SketchInkMaterial(SketchFallsInk);}
            if(_hallTemplateHolder==null){_hallTemplateHolder=new GameObject("Hallwright templates");_hallTemplateHolder.SetActive(false);}
            var template=new GameObject(name+" (sketch)");template.transform.SetParent(_hallTemplateHolder.transform,false);
            foreach(HallMesh mesh in HallMeshes(name))
            {
                var part=new GameObject(mesh.Path);part.transform.SetParent(template.transform,false);
                Vector3 scale=mesh.Local.lossyScale;
                part.transform.localPosition=mesh.Local.GetPosition();part.transform.localRotation=mesh.Local.rotation;part.transform.localScale=scale;
                part.AddComponent<MeshFilter>().sharedMesh=name.Contains("roof")?SketchFlatMesh(mesh.Mesh):mesh.Mesh;
                var fill=part.AddComponent<MeshRenderer>();fill.sharedMaterials=mesh.Materials.Select(m=>SketchPaperMaterial(m)).ToArray();
                fill.shadowCastingMode=ShadowCastingMode.Off;fill.receiveShadows=true;
                var lines=new GameObject("ink");lines.transform.SetParent(part.transform,false);
                lines.transform.localScale=new Vector3(Unscale(scale.x),Unscale(scale.y),Unscale(scale.z));
                lines.AddComponent<MeshFilter>().sharedMesh=SketchInkMesh(mesh.Mesh,scale);
                var pen=lines.AddComponent<MeshRenderer>();pen.sharedMaterials=new[]{_sketchInk,_sketchInk};pen.shadowCastingMode=ShadowCastingMode.Off;pen.receiveShadows=false;
            }
            return _sketchTemplates[name]=template;
        }
        private static float Unscale(float s)=>Mathf.Abs(s)<1e-4f?1:1/s;
        // One drawn piece: its own mix of the wash, chosen by where it stands (so a piece keeps its mix as the preview is rebuilt).
        private void SketchMix(GameObject copy,Vector3 at)
        {
            int mix=(int)((uint)(Mathf.RoundToInt(at.x*2)*73856093 ^ Mathf.RoundToInt(at.y*2)*19349663 ^ Mathf.RoundToInt(at.z*2)*83492791)%SketchMixes.Length);
            if(mix==0)return;
            foreach(var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.sharedMaterial!=_sketchInk)
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m!=null && _sketchPaperSource.TryGetValue(m,out var source)?SketchPaperMaterial(source,mix):m).ToArray();
        }
        // A piece the support estimate says would fall: red ink on pink paper.
        private void SketchFalls(GameObject copy)
        {
            _sketchFallsTint.Clear();_sketchFallsTint.SetColor("_Color",SketchFallsPaper);
            foreach(var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.sharedMaterial==_sketchInk)renderer.sharedMaterials=new[]{_sketchFallsInk,_sketchFallsInk};else renderer.SetPropertyBlock(_sketchFallsTint);
        }
        // Construction lines, as an architect lays a drawing out: faint, fine strokes carrying the hall's main lines (the floor and wall-top
        // outlines, each wing's eaves and ridge) past its corners and gables, and the corner posts' lines past the floor and wall top.
        private void DrawSketchConstruction(Transform under)
        {
            if(_hallDesign==null || _markers.Count<3)return;
            if(_sketchFaintInk==null)_sketchFaintInk=SketchInkMaterial(new Color(SketchInk.r,SketchInk.g,SketchInk.b,0.55f));
            const float Reach=1.2f;
            var segments=new List<(Vector3 a,Vector3 b)>();
            void Carry(Vector3 a,Vector3 b,float reach=Reach){Vector3 d=(b-a).normalized;if(d.sqrMagnitude>0.5f)segments.Add((a-d*reach,b+d*reach));}
            float floor=_hallFloorY,top=_hallFloorY+(_staveTemple?_hallHeight:_hallHeight*_hallStoreys);
            Vector3 At(Vector3 p,float y)=>new Vector3(p.x,y,p.z);
            for(int i=0;i<_markers.Count;i++)
            {
                Vector3 a=_markers[i],b=_markers[(i+1)%_markers.Count];
                Carry(At(a,floor),At(b,floor));Carry(At(a,top),At(b,top));
                segments.Add((At(a,floor-0.6f),At(a,top+0.8f)));
            }
            if(_hallShowRoof && _hallKit!=null)foreach(var wing in _hallDesign.Wings)
            {
                double width=wing.Width*2,length=wing.Length*2,peak=wing.RoofBase+width*0.5*_hallKit.Slope+wing.TierLift;
                Vector3 W(double u,double y,double v)=>HallWorld(wing.At(u,y,v));
                Carry(W(width/2,peak,0),W(width/2,peak,length));
                Carry(W(0,wing.RoofBase,0),W(0,wing.RoofBase,length),0.8f);Carry(W(width,wing.RoofBase,0),W(width,wing.RoofBase,length),0.8f);
            }
            Vector3 origin=new Vector3(_hallOrigin.x,_hallFloorY,_hallOrigin.z);
            var points=new float[segments.Count*6];var lines=new int[segments.Count*2];
            for(int i=0;i<segments.Count;i++)
            {
                Vector3 a=segments[i].a-origin,b=segments[i].b-origin;
                points[6*i]=a.x;points[6*i+1]=a.y;points[6*i+2]=a.z;points[6*i+3]=b.x;points[6*i+4]=b.y;points[6*i+5]=b.z;lines[2*i]=2*i;lines[2*i+1]=2*i+1;
            }
            var (xyz,triangles)=SketchEdges.Strokes(points,lines,width:SketchEdges.Width*0.8);
            if(_sketchConstruction==null)_sketchConstruction=new Mesh{name="Hallwright construction lines",hideFlags=HideFlags.HideAndDontSave};
            _sketchConstruction.Clear();
            var corners=new Vector3[xyz.Length/3];for(int i=0;i<corners.Length;i++)corners[i]=new Vector3(xyz[3*i],xyz[3*i+1],xyz[3*i+2]);
            _sketchConstruction.indexFormat=corners.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            _sketchConstruction.vertices=corners;_sketchConstruction.SetIndices(triangles,MeshTopology.Triangles,0);_sketchConstruction.RecalculateBounds();
            var go=new GameObject("construction lines");go.transform.SetParent(under,false);go.transform.position=origin;
            go.AddComponent<MeshFilter>().sharedMesh=_sketchConstruction;
            var pen=go.AddComponent<MeshRenderer>();pen.sharedMaterial=_sketchFaintInk;pen.shadowCastingMode=ShadowCastingMode.Off;pen.receiveShadows=false;
        }
        // Drawn in: when a piece first appears in the sketch (the draft opened, a corner added, a storey or porch switched on) it's drawn,
        // bottom up, ink first, and its colour washes in from bare paper a moment later. Pieces already drawn stay as they are, so
        // changing a setting doesn't replay the whole hall. A rebuild while pieces are still being drawn (a valid outline opens the settings,
        // which rebuilds it again) keeps their places in the drawing. The construction lines are there from the start, as an architect
        // lays them first.
        private const float SketchInkLead=0.25f,SketchWashIn=0.35f,SketchLongest=1.5f;
        private sealed class SketchArrival { internal string Key; internal float Start; internal Renderer[] Ink,Paper; internal Color[] Wash; internal bool Falls; }
        private readonly Dictionary<string,float> _sketchPending=new Dictionary<string,float>(); // (pieces still being drawn: when each started)
        private int _sketchLastDrawn,_sketchLastFresh,_sketchLastCarried,_sketchBuilds; // (for hall status)
        private readonly List<SketchArrival> _sketchArrivals=new List<SketchArrival>();
        private HashSet<string> _sketchShown=new HashSet<string>();
        private readonly MaterialPropertyBlock _sketchWashTint=new MaterialPropertyBlock();
        private static string SketchKey(PiecePose pose)=>pose.Prefab+"@"+Mathf.RoundToInt(pose.Position.x*20)+","+Mathf.RoundToInt(pose.Position.y*20)+","+Mathf.RoundToInt(pose.Position.z*20)+","+Mathf.RoundToInt(pose.Rotation.eulerAngles.y);
        // drawn: this rebuild's drawings and their poses; the new ones are scheduled, the rest shown at once.
        private void SketchArrive(List<(GameObject drawing,PiecePose pose,bool falls)> drawn)
        {
            _sketchArrivals.Clear();
            var shown=new HashSet<string>();var fresh=new List<(GameObject drawing,PiecePose pose,bool falls)>();
            var pending=new Dictionary<string,float>();
            foreach(var d in drawn)
            {
                string key=SketchKey(d.pose);shown.Add(key);
                if(_sketchPending.TryGetValue(key,out float start)){pending[key]=start;Schedule(d.drawing,key,start,d.falls);}
                else if(!_sketchShown.Contains(key))fresh.Add(d);
            }
            _sketchShown=shown;_sketchPending.Clear();foreach(var kv in pending)_sketchPending[kv.Key]=kv.Value;
            _sketchBuilds++;_sketchLastDrawn=drawn.Count;_sketchLastFresh=fresh.Count;_sketchLastCarried=pending.Count;
            if(fresh.Count==0)return;
            // Drawing order: from the ground up, then outwards from the first corner.
            Vector3 first=_markers.Count>0?_markers[0]:Vector3.zero;
            fresh.Sort((a,b)=>{int byHeight=Mathf.RoundToInt(a.pose.Position.y*2).CompareTo(Mathf.RoundToInt(b.pose.Position.y*2));return byHeight!=0?byHeight:(a.pose.Position-first).sqrMagnitude.CompareTo((b.pose.Position-first).sqrMagnitude);});
            float span=Mathf.Min(SketchLongest-SketchInkLead-SketchWashIn,0.004f*fresh.Count+0.3f),now=Time.unscaledTime;
            for(int i=0;i<fresh.Count;i++)
            {
                string key=SketchKey(fresh[i].pose);float start=now+span*i/Math.Max(1,fresh.Count-1);
                _sketchPending[key]=start;Schedule(fresh[i].drawing,key,start,fresh[i].falls);
            }
        }
        private void Schedule(GameObject drawing,string key,float start,bool falls)
        {
            var renderers=drawing.GetComponentsInChildren<MeshRenderer>(true);
            var arrival=new SketchArrival{Key=key,Start=start,Falls=falls,
                Ink=renderers.Where(r=>r.gameObject.name=="ink").ToArray<Renderer>(),Paper=renderers.Where(r=>r.gameObject.name!="ink").ToArray<Renderer>()};
            arrival.Wash=arrival.Paper.Select(r=>r.sharedMaterial!=null && r.sharedMaterial.HasProperty("_Color")?r.sharedMaterial.color:SketchPaper).ToArray();
            foreach(var r in renderers)r.enabled=false;
            _sketchArrivals.Add(arrival);
        }
        private void AnimateSketch()
        {
            if(_sketchArrivals.Count==0)return;
            float now=Time.unscaledTime;
            for(int i=_sketchArrivals.Count-1;i>=0;i--)
            {
                var a=_sketchArrivals[i];float t=now-a.Start;
                if(t<0)continue;
                foreach(var r in a.Ink)if(r!=null)r.enabled=true;
                if(t<SketchInkLead)continue;
                float wash=Mathf.Clamp01((t-SketchInkLead)/SketchWashIn);
                for(int k=0;k<a.Paper.Length;k++)
                {
                    var r=a.Paper[k];if(r==null)continue;
                    r.enabled=true;
                    if(a.Falls)continue; // (keeps its red tint)
                    if(wash>=1){r.SetPropertyBlock(null);continue;}
                    _sketchWashTint.Clear();_sketchWashTint.SetColor("_Color",Color.Lerp(SketchPaper,a.Wash[k],wash*wash));r.SetPropertyBlock(_sketchWashTint);
                }
                if(wash>=1){_sketchPending.Remove(a.Key);_sketchArrivals.RemoveAt(i);}
            }
        }
        // Markers over a sketch: corner stakes, door frames and chimney posts in sanguine, the red chalk architects annotate with.
        private static readonly Color SketchSanguine=new Color(0.62f,0.24f,0.13f);
        private Material _sketchMarkInk;
        private readonly List<Mesh> _sketchMarkMeshes=new List<Mesh>();
        private bool SketchMark(Transform under,Vector3[] points)
        {
            if(points==null || points.Length<2)return false;
            if(_sketchMarkInk==null)_sketchMarkInk=SketchInkMaterial(SketchSanguine);
            Vector3 origin=points[0];
            var xyz=new float[points.Length*3];var lines=new int[(points.Length-1)*2];
            for(int i=0;i<points.Length;i++){Vector3 p=points[i]-origin;xyz[3*i]=p.x;xyz[3*i+1]=p.y;xyz[3*i+2]=p.z;}
            for(int i=0;i+1<points.Length;i++){lines[2*i]=i;lines[2*i+1]=i+1;}
            var (strokeXyz,triangles)=SketchEdges.Strokes(xyz,lines,width:SketchEdges.Width*1.2);
            if(triangles.Length==0)return false;
            var mesh=new Mesh{name="Hallwright sketch marker",hideFlags=HideFlags.HideAndDontSave};
            var corners=new Vector3[strokeXyz.Length/3];for(int i=0;i<corners.Length;i++)corners[i]=new Vector3(strokeXyz[3*i],strokeXyz[3*i+1],strokeXyz[3*i+2]);
            mesh.vertices=corners;mesh.SetIndices(triangles,MeshTopology.Triangles,0);mesh.RecalculateBounds();_sketchMarkMeshes.Add(mesh);
            var go=new GameObject("sketch marker");go.transform.SetParent(under,false);go.transform.position=origin;go.layer=under.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var pen=go.AddComponent<MeshRenderer>();pen.sharedMaterial=_sketchMarkInk;pen.shadowCastingMode=ShadowCastingMode.Off;pen.receiveShadows=false;
            return true;
        }
        // With the guides they belong to (ClearVisuals).
        private void ClearSketchMarks(){foreach(var mesh in _sketchMarkMeshes)if(mesh!=null)Destroy(mesh);_sketchMarkMeshes.Clear();}
        private void DestroySketch()
        {
            _sketchArrivals.Clear();_sketchShown.Clear();_sketchPending.Clear();
            foreach(var t in _sketchTemplates.Values)if(t!=null)Destroy(t);_sketchTemplates.Clear();
            foreach(var paper in _sketchPapers.Values)if(paper!=null)Destroy(paper);_sketchPapers.Clear();_sketchPaperSource.Clear();_sketchAverages.Clear();
            foreach(var ink in _sketchInkMeshes.Values)if(ink!=null)Destroy(ink);_sketchInkMeshes.Clear();
            foreach(var flat in _sketchFlatMeshes.Values)if(flat!=null)Destroy(flat);_sketchFlatMeshes.Clear();
            if(_sketchConstruction!=null)Destroy(_sketchConstruction);_sketchConstruction=null;
            ClearSketchMarks();
            foreach(var m in new[]{_sketchInk,_sketchFallsInk,_sketchFaintInk,_sketchMarkInk})if(m!=null)Destroy(m);
            _sketchInk=_sketchFallsInk=_sketchFaintInk=_sketchMarkInk=null;
            if(_sketchSheet!=null)Destroy(_sketchSheet);_sketchSheet=null;
        }
    }
}
