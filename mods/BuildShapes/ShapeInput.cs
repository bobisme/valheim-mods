using System.Linq;
using System.Reflection;
using HarmonyLib;
using SharedSketch;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildShapes
{
    // How every shape tool is driven once its markers are down: the real pieces show, the scroll wheel turns them (in the game's own
    // 22.5° steps), Ctrl+scroll changes Arch's height or Repeat's spacing (Shift with it for fine steps), E plans, Delete clears
    // the shape and keeps the tool. Repeat's full options (anchor, pitch, roll, exact values) open on the options key.
    public sealed partial class Plugin
    {
        private const float TurnStep=22.5f;
        private float _shapeRoll; // Curve, Arch, Line: the pieces turned about their own length (kept until the tool closes)
        private bool _archRiseSet;
        private float _archRise;
        private bool ShapeReady=>_tool!=Tool.None && _markers.Count==RequiredMarkers && _output.Count>0;
        private bool ShapeScrolls=>ShapeReady && _tool!=Tool.Mirror;
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
                    Instance.ReservesHammer && Input.GetKeyDown(Instance._plan.Value) && (Instance.ShapeReady || Instance._repeatMenu);
            }
        }

        // The keys every tool shares; true when one was used this frame.
        private bool UpdateShapeKeys(Player player)
        {
            if(Input.GetKeyDown(KeyCode.Delete))
            {CloseRepeatMenu();_markers.Clear();_sources.Clear();_archRiseSet=false;Preview();Say($"{_tool} cleared. Click to start again.");return true;}
            if(_tool==Tool.Repeat && _markers.Count==3 && Input.GetKeyDown(_options.Value)){OpenRepeatMenu();return true;}
            if(!ShapeReady)return false;
            float wheel=Input.mouseScrollDelta.y;
            if(wheel!=0 && ShapeScrolls)
            {
                float sign=wheel>0?1:-1;bool ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl),fine=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
                if(ctrl && (_tool==Tool.Arch || _tool==Tool.Arc))_archRise=Mathf.Clamp((float)System.Math.Round(_archRise+sign*(fine?0.05f:0.25f),2),0,Arch.MaximumRise);
                else if(ctrl && _tool==Tool.Repeat)_spacing.Value=Mathf.Clamp((float)System.Math.Round(SafeSpacing()+sign*(fine?0.05f:0.25f),2),0.25f,16f);
                else if(ctrl)return false;
                else if(_tool==Tool.Repeat)_yaw=Mathf.Repeat(_yaw+sign*TurnStep,360);
                else _shapeRoll=Mathf.Repeat(_shapeRoll+sign*TurnStep,360);
                Preview();return true;
            }
            if(Time.unscaledTime-_lastAction>0.5f && Input.GetKeyDown(_plan.Value)){_enterFrame=Time.frameCount;_confirmHeld=true;_lastAction=Time.unscaledTime;Submit(player);return true;}
            return false;
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
