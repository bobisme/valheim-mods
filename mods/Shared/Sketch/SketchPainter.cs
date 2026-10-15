using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace SharedSketch
{
    // (Shared by Hallwright and BuildShapes: mods/Shared/Sketch, compiled into each mod, so each has its own copy and nothing links them.)
    // Pieces drawn as an architect's watercolour, in the manner of Piranesi: inked along the edges where their surface folds and where
    // they end (SketchEdges), washed in their own colour (timber brown, thatch straw, stone grey) softened towards the paper.
    // No shader of our own (Valheim can't compile one while it runs): the wash is the piece's own material with its texture swapped for a
    // sheet of watercolour paper tinted the texture's average colour, so the sun, shade and fog fall on it as on everything else (unlit,
    // it glared white through the game's bloom and fog); the ink is the game's plain colour shader. The ink is pen strokes
    // (SketchEdges.Strokes: wide, overshooting, now and then gone over twice) over one-pixel lines, which keep far-off pieces drawn
    // where the strokes get thinner than a pixel.
    internal sealed class SketchPart { internal Mesh Mesh; internal Matrix4x4 Local; internal Material[] Materials; internal string Path; }

    internal sealed class SketchPainter
    {
        internal static readonly Color Paper=new Color(0.93f,0.9f,0.83f),Ink=new Color(0.16f,0.14f,0.13f),
            FallsPaper=new Color(1f,0.55f,0.48f),FallsInk=new Color(0.78f,0.12f,0.08f),
            BluePencil=new Color(0.2f,0.33f,0.58f); // (markers: an architect's blue pencil; red read as an error)
        private const float Wash=0.75f; // (how much of the piece's own colour shows through the paper)
        // A hand-laid wash is never even: each piece gets one of a few slightly different mixes of its colour.
        private static readonly Color[] Mixes={new Color(1,1,1),new Color(1.05f,1.02f,0.96f),new Color(0.95f,0.96f,1),new Color(1.02f,0.97f,0.94f),new Color(0.97f,1.01f,0.97f)};

        private readonly string _name;
        private readonly Action<string> _log;
        private Material _ink,_fallsInk,_faint,_pencil;
        private Texture2D _sheet;
        private GameObject _holder;
        private readonly Dictionary<(Material,int),Material> _papers=new Dictionary<(Material,int),Material>();
        private readonly Dictionary<Material,Material> _paperSource=new Dictionary<Material,Material>();
        private readonly Dictionary<Texture,Color> _averages=new Dictionary<Texture,Color>();
        private readonly Dictionary<(Mesh,Vector3),Mesh> _inkMeshes=new Dictionary<(Mesh,Vector3),Mesh>();
        private readonly Dictionary<Mesh,Mesh> _flatMeshes=new Dictionary<Mesh,Mesh>();
        private readonly Dictionary<string,GameObject> _templates=new Dictionary<string,GameObject>();
        private readonly MaterialPropertyBlock _fallsTint=new MaterialPropertyBlock();

        // name: the mod's (for its objects' names); log: where the odd texture that can't be read is reported.
        internal SketchPainter(string name,Action<string> log=null){_name=name;_log=log;}

        internal Material InkMaterial=>_ink??(_ink=MakeInk(Ink));
        internal Material Faint=>_faint??(_faint=MakeInk(new Color(Ink.r,Ink.g,Ink.b,0.55f)));   // (construction lines)
        internal Material Pencil=>_pencil??(_pencil=MakeInk(BluePencil));                       // (markers)
        private Material FallsInkMaterial=>_fallsInk??(_fallsInk=MakeInk(FallsInk));

        // The ink: the game's plain colour shader, drawn over the paper.
        private static Material MakeInk(Color color)
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
        private Texture2D Sheet()
        {
            if(_sheet!=null)return _sheet;
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
            _sheet=new Texture2D(size,size,TextureFormat.RGBA32,true){name=_name+" sketch paper",wrapMode=TextureWrapMode.Repeat,hideFlags=HideFlags.HideAndDontSave};
            _sheet.SetPixels(pixels);_sheet.Apply(true);
            return _sheet;
        }
        // A texture's average colour (weighted by its alpha, so the clear edges of a thatch card don't count). The game's textures can't be
        // read directly, so it's shrunk on the graphics card (its smallest mip levels do the averaging) and the few pixels read back.
        private Color Average(Texture texture)
        {
            if(texture==null)return Color.white;
            if(_averages.TryGetValue(texture,out var cached))return cached;
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
            catch(Exception ex){_log?.Invoke("Sketch colour of "+texture.name+": "+ex.Message);}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(small);Object.Destroy(read);}
            return _averages[texture]=average;
        }
        // The wash: the piece's material with watercolour paper in its piece's colour for its texture, and no grain in its surface (so its
        // light and shade stay). mix: which of the Mixes.
        private Material PaperMaterial(Material source,int mix=0)
        {
            if(source==null)return null;
            if(_papers.TryGetValue((source,mix),out var cached))return cached;
            var paper=new Material(source){name=source.name+" (sketch paper)",hideFlags=HideFlags.HideAndDontSave};
            if(paper.HasProperty("_MainTex"))paper.SetTexture("_MainTex",Sheet());
            foreach(string grain in new[]{"_BumpMap","_DetailAlbedoMap","_DetailNormalMap","_EmissionMap"})if(paper.HasProperty(grain))paper.SetTexture(grain,null);
            Color own=source.HasProperty("_MainTex")?Average(source.GetTexture("_MainTex")):Color.white;
            if(source.HasProperty("_Color"))own*=source.GetColor("_Color");
            Color wash=Color.Lerp(Paper,new Color(own.r,own.g,own.b),Wash)*Mixes[mix];
            if(paper.HasProperty("_Color"))paper.SetColor("_Color",new Color(wash.r,wash.g,wash.b,1));
            foreach(string shine in new[]{"_Glossiness","_Metallic","_Smoothness"})if(paper.HasProperty(shine))paper.SetFloat(shine,0);
            _paperSource[paper]=source;
            return _papers[(source,mix)]=paper;
        }
        private static (float[] xyz,int[] triangles) Geometry(Mesh mesh)
        {
            Vector3[] v=mesh.vertices;var xyz=new float[v.Length*3];
            for(int i=0;i<v.Length;i++){xyz[3*i]=v[i].x;xyz[3*i+1]=v[i].y;xyz[3*i+2]=v[i].z;}
            var triangles=new List<int>();
            for(int sub=0;sub<mesh.subMeshCount;sub++)if(mesh.GetTopology(sub)==MeshTopology.Triangles)triangles.AddRange(mesh.GetTriangles(sub));
            return (xyz,triangles.ToArray());
        }
        // The ink for one mesh at the size it's drawn: strokes (submesh 0) over one-pixel lines (submesh 1); the edges of its box where the
        // game keeps the mesh from being read.
        private Mesh InkMesh(Mesh source,Vector3 scale)
        {
            var key=(source,new Vector3((float)Math.Round(scale.x,3),(float)Math.Round(scale.y,3),(float)Math.Round(scale.z,3)));
            if(_inkMeshes.TryGetValue(key,out var cached))return cached;
            float[] points;int[] lines;
            if(source.isReadable){var (xyz,triangles)=Geometry(source);(points,lines)=SketchEdges.Find(xyz,triangles);}
            else{points=new float[0];lines=new int[0];}
            if(lines.Length==0)
            {
                Bounds b=source.bounds;points=new float[24];
                for(int i=0;i<8;i++){Vector3 c=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));points[3*i]=c.x;points[3*i+1]=c.y;points[3*i+2]=c.z;}
                lines=new[]{0,1,2,3,4,5,6,7,0,2,1,3,4,6,5,7,0,4,1,5,2,6,3,7};
            }
            // Both are made at the part's scale; the ink's own transform undoes it (Template).
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
            return _inkMeshes[key]=ink;
        }
        // A mesh lit as its plain planes (SketchEdges.Planes): a thatch roof's bumps made the paper look crumpled in the sun. The shape is
        // the same; only the light on it changes.
        private Mesh FlatMesh(Mesh source)
        {
            if(!source.isReadable)return source;
            if(_flatMeshes.TryGetValue(source,out var cached))return cached;
            var (xyz,triangles)=Geometry(source);
            float[] n=SketchEdges.Planes(xyz,triangles);
            var flat=Object.Instantiate(source);flat.name=source.name+" (sketch planes)";flat.hideFlags=HideFlags.HideAndDontSave;
            var normals=new Vector3[n.Length/3];for(int i=0;i<normals.Length;i++)normals[i]=new Vector3(n[3*i],n[3*i+1],n[3*i+2]);
            flat.normals=normals;
            return _flatMeshes[source]=flat;
        }
        // A piece drawn, ready to copy (Instantiate) wherever it stands: its parts washed on paper, each with its ink over it.
        // key: the piece's prefab name; planes: light it as its plain planes (roofs).
        internal GameObject Template(string key,IEnumerable<SketchPart> parts,bool planes)
        {
            if(_templates.TryGetValue(key,out var cached) && cached!=null)return cached;
            if(_holder==null){_holder=new GameObject(_name+" sketch templates");_holder.SetActive(false);Object.DontDestroyOnLoad(_holder);}
            var template=new GameObject(key+" (sketch)");template.transform.SetParent(_holder.transform,false);
            foreach(SketchPart mesh in parts)
            {
                var part=new GameObject(mesh.Path??"part");part.transform.SetParent(template.transform,false);
                Vector3 scale=mesh.Local.lossyScale;
                part.transform.localPosition=mesh.Local.GetPosition();part.transform.localRotation=mesh.Local.rotation;part.transform.localScale=scale;
                part.AddComponent<MeshFilter>().sharedMesh=planes?FlatMesh(mesh.Mesh):mesh.Mesh;
                var fill=part.AddComponent<MeshRenderer>();fill.sharedMaterials=mesh.Materials.Select(m=>PaperMaterial(m)).ToArray();
                fill.shadowCastingMode=ShadowCastingMode.Off;fill.receiveShadows=true;
                var lines=new GameObject("ink");lines.transform.SetParent(part.transform,false);
                lines.transform.localScale=new Vector3(Unscale(scale.x),Unscale(scale.y),Unscale(scale.z));
                lines.AddComponent<MeshFilter>().sharedMesh=InkMesh(mesh.Mesh,scale);
                var pen=lines.AddComponent<MeshRenderer>();pen.sharedMaterials=new[]{InkMaterial,InkMaterial};pen.shadowCastingMode=ShadowCastingMode.Off;pen.receiveShadows=false;
            }
            return _templates[key]=template;
        }
        private static float Unscale(float s)=>Mathf.Abs(s)<1e-4f?1:1/s;
        // A drawn piece's own mix of the wash, chosen by where it stands (so it keeps its mix as a preview is rebuilt).
        internal void Mix(GameObject copy,Vector3 at)
        {
            int mix=(int)((uint)(Mathf.RoundToInt(at.x*2)*73856093 ^ Mathf.RoundToInt(at.y*2)*19349663 ^ Mathf.RoundToInt(at.z*2)*83492791)%Mixes.Length);
            if(mix==0)return;
            foreach(var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.sharedMaterial!=_ink)
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m!=null && _paperSource.TryGetValue(m,out var source)?PaperMaterial(source,mix):m).ToArray();
        }
        // A piece that would fall: red ink on pink paper.
        internal void Falls(GameObject copy)
        {
            _fallsTint.Clear();_fallsTint.SetColor("_Color",FallsPaper);
            foreach(var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.sharedMaterial==_ink)renderer.sharedMaterials=new[]{FallsInkMaterial,FallsInkMaterial};else renderer.SetPropertyBlock(_fallsTint);
        }
        // Pen strokes along world-space lines (construction lines, markers), as one object under `under`. The mesh goes on `owned` for the
        // caller to free with the object (FreeMeshes). Null when there's nothing to draw.
        internal GameObject Strokes(Transform under,IList<(Vector3 a,Vector3 b)> segments,Material ink,double width,List<Mesh> owned)
        {
            if(segments==null || segments.Count==0)return null;
            Vector3 origin=segments[0].a;
            var points=new float[segments.Count*6];var lines=new int[segments.Count*2];
            for(int i=0;i<segments.Count;i++)
            {
                Vector3 a=segments[i].a-origin,b=segments[i].b-origin;
                points[6*i]=a.x;points[6*i+1]=a.y;points[6*i+2]=a.z;points[6*i+3]=b.x;points[6*i+4]=b.y;points[6*i+5]=b.z;lines[2*i]=2*i;lines[2*i+1]=2*i+1;
            }
            var (xyz,triangles)=SketchEdges.Strokes(points,lines,width:width);
            if(triangles.Length==0)return null;
            var mesh=new Mesh{name=_name+" sketch strokes",hideFlags=HideFlags.HideAndDontSave};
            var corners=new Vector3[xyz.Length/3];for(int i=0;i<corners.Length;i++)corners[i]=new Vector3(xyz[3*i],xyz[3*i+1],xyz[3*i+2]);
            if(corners.Length>65535)mesh.indexFormat=IndexFormat.UInt32;
            mesh.vertices=corners;mesh.SetIndices(triangles,MeshTopology.Triangles,0);mesh.RecalculateBounds();owned.Add(mesh);
            var go=new GameObject(_name+" sketch strokes"){layer=under.gameObject.layer};go.transform.SetParent(under,false);go.transform.position=origin;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var pen=go.AddComponent<MeshRenderer>();pen.sharedMaterial=ink;pen.shadowCastingMode=ShadowCastingMode.Off;pen.receiveShadows=false;
            return go;
        }
        internal static void FreeMeshes(List<Mesh> meshes){foreach(var mesh in meshes)if(mesh!=null)Object.Destroy(mesh);meshes.Clear();}
        // Forget the drawn pieces (unlocks or the pieces changed), keeping the paper and ink.
        internal void ClearTemplates(){foreach(var t in _templates.Values)if(t!=null)Object.Destroy(t);_templates.Clear();}
        internal void Destroy()
        {
            ClearTemplates();
            foreach(var paper in _papers.Values)if(paper!=null)Object.Destroy(paper);_papers.Clear();_paperSource.Clear();_averages.Clear();
            foreach(var ink in _inkMeshes.Values)if(ink!=null)Object.Destroy(ink);_inkMeshes.Clear();
            foreach(var flat in _flatMeshes.Values)if(flat!=null)Object.Destroy(flat);_flatMeshes.Clear();
            foreach(var m in new[]{_ink,_fallsInk,_faint,_pencil})if(m!=null)Object.Destroy(m);
            _ink=_fallsInk=_faint=_pencil=null;
            if(_sheet!=null)Object.Destroy(_sheet);_sheet=null;
            if(_holder!=null)Object.Destroy(_holder);_holder=null;
        }
    }

    // Drawn in: each scheduled drawing appears at its start time, ink first, and its colour washes in from bare paper a moment later.
    // Which pieces to draw in, and in what order, is the caller's (Hallwright: new pieces only; BuildShapes: a shape as it's completed).
    internal sealed class SketchArrivals
    {
        internal const float InkLead=0.25f,WashIn=0.35f,Longest=1.5f;
        private sealed class Arrival { internal string Key; internal float Start; internal Renderer[] Ink,Paper; internal Color[] Wash; internal bool Falls; }
        private readonly List<Arrival> _arrivals=new List<Arrival>();
        private readonly MaterialPropertyBlock _tint=new MaterialPropertyBlock();
        internal int Count=>_arrivals.Count;
        // How long a run of `count` pieces takes to start (the last one's start after the first's).
        internal static float Span(int count)=>Mathf.Min(Longest-InkLead-WashIn,0.004f*count+0.3f);
        internal void Clear()=>_arrivals.Clear();
        internal void Schedule(GameObject drawing,float start,bool falls,string key=null)
        {
            var renderers=drawing.GetComponentsInChildren<MeshRenderer>(true);
            var arrival=new Arrival{Key=key,Start=start,Falls=falls,
                Ink=renderers.Where(r=>r.gameObject.name=="ink").ToArray<Renderer>(),Paper=renderers.Where(r=>r.gameObject.name!="ink").ToArray<Renderer>()};
            arrival.Wash=arrival.Paper.Select(r=>r.sharedMaterial!=null && r.sharedMaterial.HasProperty("_Color")?r.sharedMaterial.color:SketchPainter.Paper).ToArray();
            foreach(var r in renderers)r.enabled=false;
            _arrivals.Add(arrival);
        }
        // Every frame: finished: told the key of each drawing that's finished.
        internal void Tick(Action<string> finished=null)
        {
            if(_arrivals.Count==0)return;
            float now=Time.unscaledTime;
            for(int i=_arrivals.Count-1;i>=0;i--)
            {
                var a=_arrivals[i];float t=now-a.Start;
                if(t<0)continue;
                foreach(var r in a.Ink)if(r!=null)r.enabled=true;
                if(t<InkLead)continue;
                float wash=Mathf.Clamp01((t-InkLead)/WashIn);
                for(int k=0;k<a.Paper.Length;k++)
                {
                    var r=a.Paper[k];if(r==null)continue;
                    r.enabled=true;
                    if(a.Falls)continue; // (keeps its red tint)
                    if(wash>=1){r.SetPropertyBlock(null);continue;}
                    _tint.Clear();_tint.SetColor("_Color",Color.Lerp(SketchPainter.Paper,a.Wash[k],wash*wash));r.SetPropertyBlock(_tint);
                }
                if(wash>=1){finished?.Invoke(a.Key);_arrivals.RemoveAt(i);}
            }
        }
    }
}
