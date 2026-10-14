using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildShapes
{
    // Line: Shift+click the start (a marker), Shift+click the end (the real pieces appear), the scroll wheel turns them about the line
    // (in the game's own 22.5° steps), L or Enter plans, Delete clears the line and keeps the tool.
    public sealed partial class Plugin
    {
        private const float LineTurnStep=22.5f;
        private float _lineRoll; // (kept from one line to the next while the tool stays open)
        private bool LineReady=>_tool==Tool.Line && _markers.Count==2 && _output.Count>0;
        internal static bool LineTakesScroll=>Instance!=null && Instance.LineReady && Instance.ReservesHammer && !OptionsMenuOpen;
        private static int _lineEnterFrame=-1; // (planning clears the line, so the chat may look after it, in the same frame)
        internal static bool LineTakesEnter=>_lineEnterFrame==Time.frameCount || LineTakesScroll && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));

        // Line's own keys; true when one was used this frame.
        private bool UpdateLine(Player player)
        {
            if(Input.GetKeyDown(KeyCode.Delete))
            {_markers.Clear();Preview();Say("Line cleared. Shift+click a new start.");return true;}
            if(!LineReady)return false;
            float wheel=Input.mouseScrollDelta.y;
            if(wheel!=0){_lineRoll=Mathf.Repeat(_lineRoll+(wheel>0?LineTurnStep:-LineTurnStep),360);Preview();return true;}
            if(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)){_lineEnterFrame=Time.frameCount;_lastAction=Time.unscaledTime;Submit(player);return true;}
            return false;
        }
        // The pieces as they'll look: their own meshes and materials, no colliders, no cost, nothing in the world.
        private void ShowLinePieces()
        {
            foreach(PiecePose pose in _output)
            {
                var go=new GameObject("BuildShapes line piece"){layer=LayerMask.NameToLayer("Ignore Raycast")};
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
    }
    // Enter plans a ready line instead of opening the chat (for that keypress only).
    [HarmonyPatch]
    internal static class LineEnterNotChat
    {
        private static MethodBase TargetMethod()=>AccessTools.Method(typeof(Chat),"Update");
        private static bool Prepare()=>TargetMethod()!=null;
        private static bool Prefix()=>!Plugin.LineTakesEnter;
    }
}
