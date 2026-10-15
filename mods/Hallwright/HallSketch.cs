using System;
using System.Collections.Generic;
using System.Linq;
using SharedSketch;
using UnityEngine;

namespace Hallwright
{
    // The Sketch preview: the hall as an architect's watercolour (the drawing itself is mods/Shared/Sketch, shared with BuildShapes).
    // Here: which preview is shown, the hall's construction lines and blue-pencil markers, and which pieces are drawn in.
    public sealed partial class Plugin
    {
        private static readonly string[] HallPreviewModes={"Sketch","Materials","Ghosts","Layout"};
        private bool _hallSketch=true;
        private int HallPreviewMode=>_hallGuideOnly?3:_hallSketch?0:_hallSolid?1:2;
        private void SetHallPreviewMode(int mode){_hallGuideOnly=mode==3;_hallSketch=mode==0;if(mode==1 || mode==2)_hallSolid=mode==1;}
        private static readonly Color SketchInk=SketchPainter.Ink,SketchBluePencil=SketchPainter.BluePencil; // (the guides' dashes, HallDrawing)

        private SketchPainter _sketchPainter;
        private SketchPainter Painter=>_sketchPainter??(_sketchPainter=new SketchPainter("Hallwright",m=>Logger.LogDebug(m)));
        private readonly SketchArrivals _sketchArrivals=new SketchArrivals();
        private readonly List<Mesh> _sketchConstructionMeshes=new List<Mesh>(),_sketchMarkMeshes=new List<Mesh>();

        private GameObject HallSketchTemplate(string name)=>
            Painter.Template(name,HallMeshes(name).Select(m=>new SketchPart{Mesh=m.Mesh,Local=m.Local,Materials=m.Materials,Path=m.Path}),name.Contains("roof"));
        private void SketchMix(GameObject copy,Vector3 at)=>Painter.Mix(copy,at);
        private void SketchFalls(GameObject copy)=>Painter.Falls(copy);

        // Construction lines, as an architect lays a drawing out: faint, fine strokes carrying the hall's main lines (the floor and wall-top
        // outlines, each wing's eaves and ridge) past its corners and gables, and the corner posts' lines past the floor and wall top.
        private void DrawSketchConstruction(Transform under)
        {
            SketchPainter.FreeMeshes(_sketchConstructionMeshes);
            if(_hallDesign==null || _markers.Count<3)return;
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
            Painter.Strokes(under,segments,Painter.Faint,SketchEdges.Width*0.8,_sketchConstructionMeshes);
        }
        // Drawn in: when a piece first appears in the sketch (the draft opened, a corner added, a storey or porch switched on) it's drawn,
        // bottom up, ink first, and its colour washes in from bare paper a moment later. Pieces already drawn stay as they are, so
        // changing a setting doesn't replay the whole hall. A rebuild while pieces are still being drawn (a valid outline opens the settings,
        // which rebuilds it again) keeps their places in the drawing. The construction lines are there from the start, as an architect
        // lays them first.
        private readonly Dictionary<string,float> _sketchPending=new Dictionary<string,float>(); // (pieces still being drawn: when each started)
        private int _sketchLastDrawn,_sketchLastFresh,_sketchLastCarried,_sketchBuilds; // (for hall status)
        private HashSet<string> _sketchShown=new HashSet<string>();
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
                if(_sketchPending.TryGetValue(key,out float start)){pending[key]=start;_sketchArrivals.Schedule(d.drawing,start,d.falls,key);}
                else if(!_sketchShown.Contains(key))fresh.Add(d);
            }
            _sketchShown=shown;_sketchPending.Clear();foreach(var kv in pending)_sketchPending[kv.Key]=kv.Value;
            _sketchBuilds++;_sketchLastDrawn=drawn.Count;_sketchLastFresh=fresh.Count;_sketchLastCarried=pending.Count;
            if(fresh.Count==0)return;
            // Drawing order: from the ground up, then outwards from the first corner.
            Vector3 first=_markers.Count>0?_markers[0]:Vector3.zero;
            fresh.Sort((a,b)=>{int byHeight=Mathf.RoundToInt(a.pose.Position.y*2).CompareTo(Mathf.RoundToInt(b.pose.Position.y*2));return byHeight!=0?byHeight:(a.pose.Position-first).sqrMagnitude.CompareTo((b.pose.Position-first).sqrMagnitude);});
            float span=SketchArrivals.Span(fresh.Count),now=Time.unscaledTime;
            for(int i=0;i<fresh.Count;i++)
            {
                string key=SketchKey(fresh[i].pose);float start=now+span*i/Math.Max(1,fresh.Count-1);
                _sketchPending[key]=start;_sketchArrivals.Schedule(fresh[i].drawing,start,fresh[i].falls,key);
            }
        }
        private void AnimateSketch()=>_sketchArrivals.Tick(key=>{if(key!=null)_sketchPending.Remove(key);});
        // Markers over a sketch: corner stakes, door frames and chimney posts in an architect's blue pencil.
        private bool SketchMark(Transform under,Vector3[] points)
        {
            if(points==null || points.Length<2)return false;
            var segments=new List<(Vector3,Vector3)>();for(int i=0;i+1<points.Length;i++)segments.Add((points[i],points[i+1]));
            return Painter.Strokes(under,segments,Painter.Pencil,SketchEdges.Width*1.2,_sketchMarkMeshes)!=null;
        }
        // With the guides they belong to (ClearVisuals).
        private void ClearSketchMarks()=>SketchPainter.FreeMeshes(_sketchMarkMeshes);
        private void DestroySketch()
        {
            _sketchArrivals.Clear();_sketchShown.Clear();_sketchPending.Clear();
            SketchPainter.FreeMeshes(_sketchConstructionMeshes);ClearSketchMarks();
            _sketchPainter?.Destroy();_sketchPainter=null;
        }
    }
}
