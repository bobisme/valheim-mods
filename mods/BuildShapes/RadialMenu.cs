using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildShapes
{
    // The shape radial: pick a piece in the hammer as usual, hold Left Ctrl (a third of a second, without clicking or scrolling),
    // flick towards a shape and let go (or click). Ctrl+click and Ctrl+scroll stay the shape tools' own: a slow one that opens the
    // radial, with the pointer still in its middle, closes it and does what it would have done.
    public sealed partial class Plugin
    {
        private const float RadialHold=0.35f,RadialDeadzone=45,RadialRadius=190;
        private bool _radial;
        internal static bool RadialOpen=>Instance!=null && Instance._radial;
        internal static bool RadialKeyIsCtrl=>Instance!=null && Instance._enabled.Value && Instance._radialKey.Value==KeyCode.LeftControl;
        private float _radialKeyAt=-1;
        private bool _radialKeyUsed;
        private int _radialHover=-1;
        private sealed class RadialItem { internal Tool Tool; internal string Label,Detail,Reason; internal bool Exit; }

        private List<RadialItem> RadialItems()
        {
            Piece piece=Player.m_localPlayer?.GetSelectedPiece();
            string selected=piece!=null?Utils.GetPrefabName(piece.gameObject):"";
            string beam=selected!="" && PieceEnds(selected,out _,out _,out string problem)?null:"no length";
            string extended=_planner.Extended?null:"Update BuildOrders for Mirror.";
            var items=new List<RadialItem>
            {
                new RadialItem{Tool=Tool.Curve,Label="Curve",Detail="Click points: a line, then a curve through them.",Reason=beam},
                new RadialItem{Tool=Tool.Arch,Label="Arch",Detail="Two ends; a circle through its top.",Reason=beam},
                new RadialItem{Tool=Tool.Circle,Label="Circle",Detail="The centre, then a point on the edge.",Reason=beam},
                new RadialItem{Tool=Tool.Mirror,Label="Mirror",Detail="Reflect pieces across a line.",Reason=extended},
            };
            // Only what works with the piece in hand.
            items.RemoveAll(i=>i.Reason!=null);
            if(_tool!=Tool.None)items.Add(new RadialItem{Label="Normal building",Detail="Leave the shape tool.",Exit=true});
            return items;
        }
        private void OpenRadial(){_radial=true;_radialHover=-1;_radialOpenedAt=Time.unscaledTime;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        private void CloseRadial(){_radial=false;_radialHover=-1;_radialPinned=-2;}
        private int _radialPinned=-2; // (a Claude Tools test: held open with this wedge lit, -1 for none)
        private int RadialHoverIndex(int count)
        {
            float scale=Mathf.Max(0.6f,Screen.height/1080f);
            Vector2 p=(Vector2)Input.mousePosition-new Vector2(Screen.width*0.5f,Screen.height*0.5f);
            if(count==0 || p.magnitude<RadialDeadzone*scale)return -1;
            float angle=Mathf.Repeat(Mathf.Atan2(p.x,p.y)*Mathf.Rad2Deg,360); // (0 at the top, clockwise)
            return Mathf.RoundToInt(angle/(360f/count))%count;
        }
        // The radial's keys; true when it used this frame's input.
        private bool UpdateRadial(Player player)
        {
            KeyCode key=_radialKey.Value;
            bool held=Input.GetKey(key),click=Input.GetKeyDown(_mark.Value);float wheel=Input.mouseScrollDelta.y;
            if(Input.GetKeyDown(key)){_radialKeyAt=Time.unscaledTime;_radialKeyUsed=false;}
            if(!_radial)
            {
                if(held && (click || wheel!=0))_radialKeyUsed=true;
                if(!held){_radialKeyAt=-1;return false;}
                if(!_radialKeyUsed && _radialKeyAt>=0 && Time.unscaledTime-_radialKeyAt>=RadialHold)
                {
                    _radialKeyUsed=true;
                    if(RadialItems().Count==0){Say("No shapes for this piece.");return true;}
                    OpenRadial();return true;
                }
                return false;
            }
            var items=RadialItems();
            if(items.Count==0){CloseRadial();return true;} // (the piece changed to one no shape takes)
            if(_radialPinned>-2){_radialHover=Mathf.Min(_radialPinned,items.Count-1);if(Input.GetKeyDown(KeyCode.Escape))CloseRadial();return true;}
            _radialHover=RadialHoverIndex(items.Count);
            if(Input.GetKeyDown(KeyCode.Escape)){_escapeFrame=Time.frameCount;CloseRadial();return true;}
            // A slow Ctrl+click or Ctrl+scroll: do that instead.
            if((click || wheel!=0) && _radialHover<0){CloseRadial();_radialKeyUsed=true;return false;}
            if(click || !held)
            {
                int pick=_radialHover;CloseRadial();_radialKeyAt=-1;_radialKeyUsed=true;
                if(pick<0)return true;
                var item=items[pick];
                if(item.Exit){Stop();Say("Back to normal building.");}
                else ChooseMode(item.Tool);
                return true;
            }
            return true;
        }
        private void ChooseMode(Tool requested)
        {
            Player player=Player.m_localPlayer;
            Piece piece=player.GetSelectedPiece();
            string selected=piece!=null?Utils.GetPrefabName(piece.gameObject):"";
            // Choosing the current shape with the same piece keeps its markers; anything else starts afresh (submitted ghosts stay).
            if(requested!=_tool || selected!=_selected)Begin(player,requested);
        }
    }
    // While the radial is up, the build panel (the selected piece, its cost) steps aside, as it does for the game's own radial.
    [HarmonyPatch(typeof(Hud),"UpdateBuild")]
    internal static class RadialHidesBuildPanel
    {
        private static void Postfix(Hud __instance){if(Plugin.RadialOpen && __instance.m_buildHud!=null)__instance.m_buildHud.SetActive(false);}
    }
}
namespace BuildShapes
{
    // Holding Left Ctrl in build mode makes the game ripple its placement ghost (a wobble on the ghost's material, nothing more).
    // Ctrl is the shape radial's key, so with the hammer that wobble stays off.
    [HarmonyPatch(typeof(Player),"UpdatePlacement")]
    internal static class RadialKeyNoRipple
    {
        private static readonly AccessTools.FieldRef<Player,System.Collections.Generic.Dictionary<Material,float>> Ripple=
            AccessTools.FieldRefAccess<Player,System.Collections.Generic.Dictionary<Material,float>>("m_ghostRippleDistance");
        private static bool Prepare()=>AccessTools.Field(typeof(Player),"m_ghostRippleDistance")!=null;
        private static void Postfix(Player __instance)
        {
            if(__instance!=Player.m_localPlayer || !Plugin.RadialKeyIsCtrl || !ZInput.GetKey(KeyCode.LeftControl))return;
            foreach(Material material in Ripple(__instance).Keys)if(material!=null)material.SetFloat("_RippleDistance",0f);
        }
    }
}
