using System.Linq;
using System.Reflection;
using HarmonyLib;
using SharedSketch;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildShapes
{
    // How every shape tool is driven once its markers are down: the pieces show, the scroll wheel rolls them about their length and
    // Shift+scroll turns each about its up (in the game's own 22.5° steps), Ctrl+scroll sets Curve's roundness or Arch's
    // height, Ctrl+Shift+scroll the gap between pieces, E plans, Delete clears the shape and keeps the tool.
    public sealed partial class Plugin
    {
        private const float TurnStep=22.5f;
        private float _shapeRoll; // Curve, Arch, Circle: the pieces rolled about their own length (kept until the tool closes)
        private float _pieceTurn; // Curve, Arch, Circle: each piece turned about its own up, on its middle (Shift+scroll)
        private bool _archRiseSet;
        private float _archRise;
        private bool ShapeReady=>_tool!=Tool.None && MarkersDone && _output.Count>0;
        // Curve: closed back to its first marker; how round it runs through its markers (Ctrl+scroll, 0 to 1); its pieces upright
        // (plumb, turned only to follow it: walls) or along it in 3D (beams, pillars), switched with Home.
        private bool _closed,_upright; // (_upright on a piece standing up its length, a pole or pillar: side by side along the curve)
        private float _curve=1;
        // Curve, Arch, Circle: the most room between pieces (Ctrl+Shift+scroll); the pieces still meet both ends, so it shrinks evenly to fit.
        private float _gap;
        private const float MaximumGap=16;
        // 0.1 m a notch up to a metre, then 0.25 m to four, then half a metre.
        private static float GapStep(float gap,float sign)
        {
            float step=gap+(sign>0?0.001f:-0.001f)<1?0.1f:gap+(sign>0?0.001f:-0.001f)<4?0.25f:0.5f;
            return Mathf.Clamp((float)System.Math.Round((gap+sign*step)/step)*step,0,MaximumGap);
        }
        // (the wheel works whenever the markers are down, also while the shape can't be laid: a bend too tight, a height too low, so it can be turned back)
        private bool ShapeScrolls=>_tool!=Tool.None && MarkersDone && _tool!=Tool.Mirror;
        internal static bool ShapeTakesScroll=>Instance!=null && Instance.ShapeScrolls && Instance.ReservesHammer && !OptionsMenuOpen;
        private static int _enterFrame=-1; // (planning clears the shape, so the game may look after it, in the same frame)
        private static bool _confirmHeld;  // (the confirm key, still held after planning: E's hold-to-use waits for it to be let go)
        // The confirm key plans a ready shape and does nothing else: E uses nothing, Enter opens no chat (whichever it's bound to).
        internal static KeyCode ConfirmKey=>Instance?._plan.Value??KeyCode.None;
        internal static bool ShapeTakesConfirm
        {
            get
            {
                if(Instance==null)return false;
                if(_confirmHeld && !Input.GetKey(Instance._plan.Value))_confirmHeld=false;
                return _enterFrame==Time.frameCount || _confirmHeld ||
                    Instance.ReservesHammer && Input.GetKeyDown(Instance._plan.Value) && Instance.ShapeReady;
            }
        }

        // The keys every tool shares; true when one was used this frame.
        private bool UpdateShapeKeys(Player player)
        {
            if(Input.GetKeyDown(KeyCode.Delete))
            {_markers.Clear();_sources.Clear();_archRiseSet=false;_closed=false;Preview();Say($"{_tool} cleared. Click to start again.");return true;}
            if((_tool==Tool.Curve || _tool==Tool.Circle) && Input.GetKeyDown(KeyCode.Home))
            {
                _upright=!_upright;Preview();
                Say(!_upright?"Pieces lie along the curve, following it in 3D.":UprightPossible()?"Pieces stand upright, end to end, turning to follow the curve.":"Pieces stand side by side along the curve.");return true;
            }
            if(!MarkersDone)return false;
            float wheel=Input.mouseScrollDelta.y;
            if(wheel!=0 && ShapeScrolls)
            {
                float sign=wheel>0?1:-1;bool ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl),shift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
                if(ctrl && shift)_gap=GapStep(_gap,sign);
                else if(ctrl && _tool==Tool.Curve)_curve=Mathf.Clamp((float)System.Math.Round(_curve+sign*0.1f,1),0,1);
                else if(ctrl && _tool==Tool.Circle)_circlePlane=Mathf.Clamp(_circlePlane+(int)sign,Spline.CircleLevel,Spline.CircleStanding);
                else if(ctrl && _tool==Tool.Arch)_archRise=Mathf.Clamp((float)System.Math.Round(_archRise+sign*0.25f,2),0,Arch.MaximumRise);
                else if(ctrl)return false;
                else if(shift)_pieceTurn=Mathf.Repeat(_pieceTurn+sign*TurnStep,360);
                else _shapeRoll=Mathf.Repeat(_shapeRoll+sign*TurnStep,360);
                Preview();return true;
            }
            if(ShapeReady && Time.unscaledTime-_lastAction>0.5f && Input.GetKeyDown(_plan.Value)){_enterFrame=Time.frameCount;_confirmHeld=true;_lastAction=Time.unscaledTime;Submit(player);return true;}
            return false;
        }
        // Curve: two markers make a straight line, more a curve through them all in order; closed, back through the first. At 0%
        // curve the sides are straight and each is laid on its own, so its pieces end at the corners (walls meet there); otherwise
        // the pieces run on along the whole curve (or evenly round the loop).
        private void PreviewCurve()
        {
            var points=_markers.Select(V).ToList();
            bool sideBySide=_upright && !UprightPossible();
            float length=sideBySide?SideWidth(out _):(_localEnd-_localStart).magnitude;
            var segments=new System.Collections.Generic.List<Segment>();
            if(_curve==0 && points.Count>2)
                for(int i=0;i<Spline.Sides(points.Count,_closed);i++)segments.AddRange(Curve.Plan(Spline.Side(points,_closed,0,i),length,_upright,false,Spline.MaximumLength,_gap));
            else segments=Curve.Plan(Spline.Path(points,_closed,_curve),length,_upright,_closed,Spline.MaximumLength,_gap);
            if(segments.Count>Curve.MaximumPieces)throw new System.ArgumentException("The shape is over 256 pieces.");
            if(sideBySide)AddSideBySide(segments);else if(_upright)AddUpright(segments);else AddAlong(segments);
        }
        // Circle: round the centre (the first marker), through the edge point (the second), from the edge point, in the plane
        // Ctrl+scroll picks (level, through both points, or standing); the pieces go evenly all the way round (as a closed Curve's
        // do), with the same rolls, turns, gaps and Home. It's laid out level, then tilted into its plane, pieces and all, so in a
        // tilted ring "upright" stands square to the ring and "along" keeps their tops to its side.
        private int _circlePlane=Spline.CircleThrough;
        private Vector3 _circleSide=Vector3.right; // (the camera's right when the edge was clicked: which way a ring stands when the edge is straight above)
        private void PreviewCircle()
        {
            V3 normal=Spline.CircleNormal(V(_markers[0]),V(_markers[1]),_circlePlane,V(_circleSide));
            Vector3 up=V(normal),centre=V(Spline.CircleCentre(V(_markers[0]),V(_markers[1]),normal)),edge=_markers[1];
            Quaternion tilt=Quaternion.FromToRotation(Vector3.up,up),level=Quaternion.Inverse(tilt);
            System.Func<double,V3> path=Spline.Circle(V(centre),V(centre+level*(edge-centre)));
            bool sideBySide=_upright && !UprightPossible();
            float length=sideBySide?SideWidth(out _):(_localEnd-_localStart).magnitude;
            var segments=Curve.Plan(path,length,_upright,true,Spline.MaximumLength,_gap);
            int first=_output.Count;
            if(sideBySide)AddSideBySide(segments);else if(_upright)AddUpright(segments);else AddAlong(segments);
            for(int i=first;i<_output.Count;i++)
            {
                PiecePose pose=_output[i];
                _output[i]=new PiecePose(pose.Id,pose.Prefab,centre+tilt*(pose.Position-centre),tilt*pose.Rotation);
            }
            // The radius, faint: centre to edge.
            Line(new[]{centre,edge},0.03f);
        }
        // Each piece's length along its segment, in 3D, its top kept as near the sky as the segment allows (so pieces don't twist
        // about the curve: a block's faces stay the way up they were); where a segment runs straight up, its top carries on from the
        // nearest piece that doesn't. The scroll wheel then turns each piece about its own length, through its end snaps.
        private void AddAlong(System.Collections.Generic.IList<Segment> segments)
        {
            Vector3 axis=(_localEnd-_localStart).normalized;
            Vector3 top=Vector3.ProjectOnPlane(Mathf.Abs(axis.y)<0.9f?Vector3.up:Vector3.forward,axis).normalized; // (the piece's own top, square to its length)
            Quaternion local=Quaternion.LookRotation(axis,top);
            var alongs=segments.Select(g=>V(g.End-g.Start).normalized).ToList();
            var skies=alongs.Select(d=>Mathf.Abs(Vector3.Dot(d,Vector3.up))<0.999f?Vector3.ProjectOnPlane(Vector3.up,d).normalized:Vector3.zero).ToList();
            Vector3 carried=skies.FirstOrDefault(v=>v!=Vector3.zero);
            if(carried==Vector3.zero)carried=Vector3.forward; // (a straight upright run: any side will do)
            for(int i=0;i<segments.Count;i++)
            {
                Vector3 along=alongs[i],sky=skies[i]!=Vector3.zero?skies[i]:Vector3.ProjectOnPlane(carried,along).normalized;
                carried=sky;
                Quaternion rotation=Quaternion.AngleAxis(_shapeRoll,along)*Quaternion.LookRotation(along,sky)*Quaternion.Inverse(local);
                AddTurned(segments[i],rotation,sky,_localStart,_localEnd);
            }
        }
        // Upright: each piece only turned about the vertical to follow its (level) segment, standing on it by the bottom of its end
        // faces; the scroll wheel still turns it about its length.
        private void AddUpright(System.Collections.Generic.IEnumerable<Segment> segments)
        {
            PieceEnds(_selected,out _,out _,out _,out Vector3 lowStart,out Vector3 lowEnd);
            Vector3 flat=Vector3.ProjectOnPlane(lowEnd-lowStart,Vector3.up);
            foreach(Segment segment in segments)
            {
                Vector3 along=V(segment.End-segment.Start);
                Quaternion rotation=Quaternion.AngleAxis(_shapeRoll,along.normalized)*Quaternion.AngleAxis(Vector3.SignedAngle(flat,along,Vector3.up),Vector3.up);
                AddTurned(segment,rotation,Vector3.up,lowStart,lowEnd);
            }
        }
        // A piece laid on its segment (its ends start and end, as rotated, on the segment's), then turned by Shift+scroll about up
        // through its middle, so it stays on the curve.
        private void AddTurned(Segment segment,Quaternion rotation,Vector3 up,Vector3 start,Vector3 end)
        {
            Vector3 middle=(start+end)*0.5f,at=V((segment.Start+segment.End)*0.5);
            rotation=Quaternion.AngleAxis(_pieceTurn,up)*rotation;
            _output.Add(new PiecePose(null,_selected,at-rotation*middle,rotation));
        }
        // Side by side: a piece standing up its length (a pole, a pillar) upright on each level segment, its widest side across the
        // ground along the curve, standing on it by its foot; scroll rolls it about the curve at its foot, Shift+scroll turns it.
        private void AddSideBySide(System.Collections.Generic.IEnumerable<Segment> segments)
        {
            SideWidth(out Vector3 across);
            Vector3 foot=_localStart.y<=_localEnd.y?_localStart:_localEnd;
            foreach(Segment segment in segments)
            {
                Vector3 along=V(segment.End-segment.Start).normalized,at=V((segment.Start+segment.End)*0.5);
                Quaternion rotation=Quaternion.AngleAxis(_pieceTurn,Vector3.up)*Quaternion.AngleAxis(_shapeRoll,along)*Quaternion.AngleAxis(Vector3.SignedAngle(across,along,Vector3.up),Vector3.up);
                _output.Add(new PiecePose(null,_selected,at-rotation*foot,rotation));
            }
        }
        // How much curve a standing piece takes: its wider side across the ground (X on a square one), and which way that runs.
        private float SideWidth(out Vector3 across)
        {
            Vector3 size=LocalBounds(_selected).size;
            across=size.z>size.x+0.001f?Vector3.forward:Vector3.right;
            return Mathf.Clamp(Mathf.Max(size.x,size.z),0.1f,8f);
        }
        // Upright works for a piece that lies along its length (a wall, a beam, a block), not one standing up it (a pillar).
        private bool UprightPossible()
        {
            Vector3 axis=_localEnd-_localStart;
            return axis.sqrMagnitude>0 && Vector3.ProjectOnPlane(axis,Vector3.up).magnitude>0.9f*axis.magnitude;
        }
        // Walls start upright: lying along their length, at least twice as tall as they're thick. Poles and pillars start side by side.
        private bool UprightByDefault(string name)
        {
            if(!UprightPossible())return true;
            Vector3 axis=_localEnd-_localStart,size=LocalBounds(name).size;
            float thick=Mathf.Abs(axis.x)>=Mathf.Abs(axis.z)?size.z:size.x;
            return size.y>2*thick;
        }
        // The preview: as an architect's watercolour (mods/Shared/Sketch, as Hallwright drafts are), or the pieces as they'll look
        // (PreviewStyle). Their own meshes either way: no colliders, no cost, nothing in the world. A shape draws itself in, along its
        // pieces, when it's first complete; turning or reshaping it afterwards shows at once (an adjustment mid-drawing keeps the drawing going).
        private SketchPainter _painter;
        private SketchPainter Painter=>_painter??(_painter=new SketchPainter("BuildShapes",m=>Logger.LogDebug(m)));
        private readonly SketchArrivals _arrivals=new SketchArrivals();
        private float _drawnAt=-1; // (when the current shape was first complete; -1 while it isn't)
        private void ShowPieces()
        {
            _arrivals.Clear();
            if(_output.Count==0){_drawnAt=-1;return;}
            if(_previewStyle.Value!="Pieces"){ShowSketch();return;}
            foreach(PiecePose pose in _output)
            {
                var go=new GameObject("BuildShapes piece preview"){layer=LayerMask.NameToLayer("Ignore Raycast")};
                go.transform.SetPositionAndRotation(pose.Position,pose.Rotation);
                foreach(var mesh in SharedMeshes(pose.Prefab))
                {
                    var part=new GameObject("part"){layer=go.layer};part.transform.SetParent(go.transform,false);
                    part.transform.localPosition=mesh.Local.GetPosition();part.transform.localRotation=mesh.Local.rotation;part.transform.localScale=mesh.Local.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh=mesh.Mesh;
                    var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterials=mesh.Materials;renderer.shadowCastingMode=ShadowCastingMode.Off;
                }
                _visuals.Add(go);
            }
        }
        private void ShowSketch()
        {
            float now=Time.unscaledTime;
            if(_drawnAt<0)_drawnAt=now;
            float span=SketchArrivals.Span(_output.Count);
            bool drawing=now<_drawnAt+span+SketchArrivals.InkLead+SketchArrivals.WashIn;
            for(int i=0;i<_output.Count;i++)
            {
                PiecePose pose=_output[i];
                GameObject template=Painter.Template(pose.Prefab,SharedMeshes(pose.Prefab).Select(m=>new SketchPart{Mesh=m.Mesh,Local=m.Local,Materials=m.Materials,Path="part"}),pose.Prefab.Contains("roof"));
                var copy=Instantiate(template,pose.Position,pose.Rotation);copy.layer=LayerMask.NameToLayer("Ignore Raycast");
                Painter.Mix(copy,pose.Position);
                if(drawing)_arrivals.Schedule(copy,_drawnAt+span*i/Mathf.Max(1,_output.Count-1),false);
                _visuals.Add(copy);
            }
        }
    }
    // The confirm key plans a ready shape instead of opening the chat (bound to Enter) or using what you look at (E).
    [HarmonyPatch]
    internal static class ShapeConfirmNotChat
    {
        private static MethodBase TargetMethod()=>AccessTools.Method(typeof(Chat),"Update");
        private static bool Prepare()=>TargetMethod()!=null;
        private static bool Prefix()=>!(Plugin.ShapeTakesConfirm && Plugin.ConfirmKey==KeyCode.Return);
    }
    [HarmonyPatch]
    internal static class ShapeConfirmNotUse
    {
        private static MethodBase TargetMethod()=>AccessTools.Method(typeof(Player),"Interact",new[]{typeof(GameObject),typeof(bool),typeof(bool)});
        private static bool Prepare()=>TargetMethod()!=null;
        private static bool Prefix(Player __instance)=>__instance!=Player.m_localPlayer || !Plugin.ShapeTakesConfirm;
    }
}
