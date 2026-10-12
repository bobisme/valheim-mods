using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

[assembly: AssemblyMetadata("ClaudeTools.Reload","world")]

namespace BuildShapes
{
    // The Hallwright stake: a little flagged stake in the hammer's build menu. Pick it and Hallwright is on; click the ground to set a
    // corner (Shift+click an entrance, Ctrl+click a smoke hole or chimney), press E inside the outline for the settings. The stake
    // itself is never built: its placement ghost stands where the next corner will go.
    public sealed partial class Plugin
    {
        internal const string StakeName="BuildShapesHallStake";
        private static readonly MethodInfo UpdateKnownRecipes=AccessTools.Method(typeof(Player),"UpdateKnownRecipesList"),UpdateAvailablePieces=AccessTools.Method(typeof(Player),"UpdateAvailablePiecesList");
        private GameObject _stakePrefab,_stakeHolder;
        private readonly List<Object> _stakeOwned=new List<Object>();
        private float _stakeCheck;
        private bool _hallViaStake;

        private static PieceTable HammerTable()=>ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        internal static bool IsStake(Piece piece)=>piece!=null && Utils.GetPrefabName(piece.gameObject)==StakeName;
        private static bool StakeSelected(Player player)=>player!=null && HoldingHammer(player) && IsStake(player.GetSelectedPiece());
        private bool StakeMode=>_tool==Tool.Hall && StakeSelected(Player.m_localPlayer);

        // Once a second: the stake stays in the hammer's menu through world loads and reloads of this mod.
        private void EnsureStake()
        {
            if(Time.unscaledTime<_stakeCheck)return;
            _stakeCheck=Time.unscaledTime+1;
            ZNetScene scene=ZNetScene.instance;PieceTable table=HammerTable();
            if(scene==null || table==null || !_enabled.Value)return;
            if(_stakePrefab==null)
            {
                try{_stakePrefab=BuildStake(scene);}
                catch(Exception ex){Logger.LogWarning("Could not make the Hallwright stake: "+ex.Message);_stakeCheck=float.MaxValue;return;}
                if(_stakePrefab==null)return;
            }
            if(table.m_pieces.Contains(_stakePrefab) && scene.GetPrefab(StakeName)==_stakePrefab)return;
            PieceReload.ReplacePrefab(scene,_stakePrefab,table);
            Player player=Player.m_localPlayer;
            if(player!=null){UpdateKnownRecipes?.Invoke(player,null);UpdateAvailablePieces?.Invoke(player,null);}
        }
        private GameObject BuildStake(ZNetScene scene)
        {
            GameObject pole=scene.GetPrefab("wood_pole");
            if(pole==null)return null;
            if(_stakeHolder==null){_stakeHolder=new GameObject("BuildShapes stake");_stakeHolder.SetActive(false);DontDestroyOnLoad(_stakeHolder);}
            GameObject stake=Instantiate(pole,_stakeHolder.transform,false);stake.name=StakeName;
            var snaps=stake.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("$hud_snappoint",StringComparison.Ordinal)).Select(t=>t.localPosition.y).ToList();
            float bottom=snaps.DefaultIfEmpty(0f).Min(),top=snaps.DefaultIfEmpty(1f).Max();
            // No snapping: the ghost goes where the next corner goes.
            foreach(Transform child in stake.transform.Cast<Transform>().ToList())if(child.name.StartsWith("$hud_snappoint",StringComparison.Ordinal))DestroyImmediate(child.gameObject);
            // A surveyor's stake: the pole made thin and tall, standing on the ground.
            // Scaled through one parent: the pole's model is rotated inside the prefab, so scaling its parts would stretch the wrong way.
            const float Thin=0.35f,Tall=1.8f;
            var model=new GameObject("Model").transform;model.SetParent(stake.transform,false);
            foreach(Transform child in stake.transform.Cast<Transform>().Where(t=>t!=model).ToList())child.SetParent(model,false);
            model.localPosition=new Vector3(0,-bottom*Tall,0);model.localScale=new Vector3(Thin,Tall,Thin);
            top=(top-bottom)*Tall;
            // A bright flag at the top: the pole's own material with a plain orange texture, so it shows from afar.
            Material wood=stake.GetComponentsInChildren<Renderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            Material cloth=null;
            if(wood!=null)
            {
                var orange=new Texture2D(2,2,TextureFormat.RGBA32,false){name="BuildShapes stake flag"};
                orange.SetPixels(Enumerable.Repeat(new Color(1f,0.32f,0.08f),4).ToArray());orange.Apply();_stakeOwned.Add(orange);
                cloth=new Material(wood){name="BuildShapes stake flag",mainTexture=orange,color=Color.white};
                if(cloth.HasProperty("_EmissionColor")){cloth.EnableKeyword("_EMISSION");cloth.SetColor("_EmissionColor",new Color(0.45f,0.12f,0.02f));}
                _stakeOwned.Add(cloth);
            }
            var flag=GameObject.CreatePrimitive(PrimitiveType.Cube);flag.name="Flag";
            DestroyImmediate(flag.GetComponent<Collider>());
            flag.transform.SetParent(stake.transform,false);
            flag.transform.localPosition=new Vector3(0.3f,top-0.22f,0);flag.transform.localScale=new Vector3(0.56f,0.36f,0.02f);
            if(cloth!=null)flag.GetComponent<MeshRenderer>().sharedMaterial=cloth;
            var piece=stake.GetComponent<Piece>();
            piece.m_name="Hallwright stake";
            piece.m_description="Stake out a hall. Click the ground for each corner, Shift+click for an entrance, Ctrl+click for a smoke hole or chimney, and press E inside the outline for its settings. The stake isn't built: Plan shell turns the outline into ghosts to build.";
            piece.m_resources=new Piece.Requirement[0];
            piece.m_craftingStation=null;
            piece.m_category=Piece.PieceCategory.Misc;
            piece.m_clipEverything=true;piece.m_groundPiece=false;piece.m_groundOnly=false;
            piece.m_enabled=true;
            try{Sprite icon=StakeIcon(stake);if(icon!=null)piece.m_icon=icon;}
            catch(Exception ex){Logger.LogWarning("No icon for the Hallwright stake: "+ex.Message);}
            return stake;
        }
        private Sprite StakeIcon(GameObject stake)
        {
            var stage=new GameObject("BuildShapes icon stage");stage.transform.position=new Vector3(0,-3000,0);
            try
            {
                // As the game's own placement ghost: no network object, nothing in the world.
                GameObject copy;ZNetView.m_forceDisableInit=true;
                try{copy=Instantiate(stake,stage.transform);}finally{ZNetView.m_forceDisableInit=false;}
                copy.transform.localPosition=Vector3.zero;copy.transform.localRotation=Quaternion.Euler(0,30,0);
                foreach(var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                copy.SetActive(true);
                int layer=31;for(int l=31;l>8;l--)if(string.IsNullOrEmpty(LayerMask.LayerToName(l))){layer=l;break;}
                foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                Renderer[] renderers=copy.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if(renderers.Length==0)return null;
                Bounds bounds=renderers[0].bounds;foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);
                var camera=new GameObject("BuildShapes icon camera").AddComponent<Camera>();camera.transform.SetParent(stage.transform,false);
                camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=60;camera.allowHDR=false;camera.allowMSAA=false;
                camera.transform.position=bounds.center+new Vector3(0.15f,0.2f,-1f).normalized*(bounds.extents.magnitude/Mathf.Sin(10*Mathf.Deg2Rad))*1.02f;
                camera.transform.LookAt(bounds.center);
                var light=new GameObject("BuildShapes icon light").AddComponent<Light>();light.transform.SetParent(stage.transform,false);
                light.transform.rotation=Quaternion.Euler(40,30,0);light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.None;light.cullingMask=1<<layer;
                RenderTexture target=RenderTexture.GetTemporary(128,128,24,RenderTextureFormat.ARGB32),previous=RenderTexture.active;
                try
                {
                    camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                    var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,128,128),0,0);texture.Apply();
                    texture.hideFlags=HideFlags.HideAndDontSave;_stakeOwned.Add(texture);
                    var sprite=Sprite.Create(texture,new Rect(0,0,128,128),new Vector2(0.5f,0.5f),100);sprite.name=StakeName;sprite.hideFlags=HideFlags.HideAndDontSave;_stakeOwned.Add(sprite);
                    return sprite;
                }
                finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);}
            }
            finally{DestroyImmediate(stage);}
        }
        private void DestroyStake()
        {
            PieceReload.UnloadPiece(_stakePrefab,HammerTable());
            // The prefab stays registered until the next copy of this mod replaces it (see PieceReload); its holder goes with it then.
            foreach(var owned in _stakeOwned)if(owned!=null)Destroy(owned);
            _stakeOwned.Clear();
        }

        // A click with the stake: the corner, entrance or chimney Hallwright would mark with the hammer.
        private void StakeClick(Player player)
        {
            if(_tool!=Tool.Hall || _hallMenu || _modeMenu)return;
            if(Input.GetKey(KeyCode.LeftControl))MarkHallChimney(player);
            else if(Input.GetKey(_modifier.Value))MarkHallDoor(player);
            else MarkHall(player);
        }
        // E inside the outline (looking at nothing else to use) opens the settings.
        private bool StakeLooksInside(Player player)
        {
            if(_markers.Count<3 || player.GetHoverObject()!=null || !CameraRay(out Ray ray) || !Physics.Raycast(ray,out RaycastHit hit,80,BuildLayers,QueryTriggerInteraction.Ignore))return false;
            Vector3 local=Quaternion.Inverse(_hallFrame)*(hit.point-_hallOrigin);
            var corners=_markers.Select(p=>{Vector3 v=Quaternion.Inverse(_hallFrame)*(p-_hallOrigin);return new V3(v.x,0,v.z);}).ToArray();
            return HallLayout.Inside(corners,local.x,local.z);
        }
        // The stake's ghost stands where the next click would mark: the snapped corner.
        private void PlaceStakeGhost(Player player)
        {
            if(!StakeMode || _hallMenu || !(PlacementGhost?.GetValue(player) is GameObject ghost) || ghost==null || !ghost.activeSelf)return;
            if(!CameraRay(out Ray ray) || !Physics.Raycast(ray,out RaycastHit hit,80,BuildLayers,QueryTriggerInteraction.Ignore))return;
            Vector3 point=HallMarkerGround(hit.point);
            if(_markers.Count>=1 && _markers.Count<HallLayout.MaximumCorners && HallSnap(hit.point,out Vector3 snapped,out _,out _,out _))point=snapped;
            ghost.transform.SetPositionAndRotation(point,Quaternion.Euler(0,_hallFrame.eulerAngles.y,0));
        }

        [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
        private static class StakeNeverBuilt
        {
            [HarmonyPrefix,HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance,Piece piece,ref bool __result)
            {
                if(!IsStake(piece))return true;
                __result=false;
                if(__instance==Player.m_localPlayer && Instance!=null && Instance._enabled.Value)Instance.StakeClick(__instance);
                return false;
            }
        }
        [HarmonyPatch(typeof(Player),"UpdatePlacementGhost")]
        private static class StakeGhost
        {
            private static void Postfix(Player __instance)
            {if(__instance==Player.m_localPlayer)Instance?.PlaceStakeGhost(__instance);}
        }
    }
}
