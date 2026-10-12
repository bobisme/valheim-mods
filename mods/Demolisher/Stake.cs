using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

[assembly: AssemblyMetadata("ClaudeTools.Reload","world")]

namespace Demolisher
{
    // The demolition stake: a thin pole with a red flag in the hammer's Misc tab. It is never built: a click marks a corner, and its
    // placement ghost stands where that corner will go.
    public sealed partial class Plugin
    {
        internal const string StakeName="DemolisherStake";
        private static readonly MethodInfo UpdateKnownRecipes=AccessTools.Method(typeof(Player),"UpdateKnownRecipesList"),UpdateAvailablePieces=AccessTools.Method(typeof(Player),"UpdateAvailablePiecesList");
        private static readonly FieldInfo PlacementGhost=AccessTools.Field(typeof(Player),"m_placementGhost");
        private GameObject _stakePrefab,_stakeHolder;
        private readonly List<Object> _stakeOwned=new List<Object>();
        private float _stakeCheck;

        private static PieceTable HammerTable()=>ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        private static bool IsStake(Piece piece)=>piece!=null && Utils.GetPrefabName(piece.gameObject)==StakeName;
        private static bool StakeSelected(Player player)=>player!=null && player.InPlaceMode() && IsStake(player.GetSelectedPiece());

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
                catch(Exception ex){Logger.LogWarning("Could not make the demolition stake: "+ex.Message);_stakeCheck=float.MaxValue;return;}
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
            if(_stakeHolder==null){_stakeHolder=new GameObject("Demolisher stake");_stakeHolder.SetActive(false);DontDestroyOnLoad(_stakeHolder);}
            GameObject stake=Instantiate(pole,_stakeHolder.transform,false);stake.name=StakeName;
            var snaps=stake.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("$hud_snappoint",StringComparison.Ordinal)).Select(t=>t.localPosition.y).ToList();
            float bottom=snaps.DefaultIfEmpty(0f).Min(),top=snaps.DefaultIfEmpty(1f).Max();
            // No snapping: the ghost goes where the next corner goes.
            foreach(Transform child in stake.transform.Cast<Transform>().ToList())if(child.name.StartsWith("$hud_snappoint",StringComparison.Ordinal))DestroyImmediate(child.gameObject);
            // Thin and tall, standing on the ground; scaled through one parent, as the pole's model is rotated inside the prefab.
            const float Thin=0.35f,Tall=1.8f;
            var model=new GameObject("Model").transform;model.SetParent(stake.transform,false);
            foreach(Transform child in stake.transform.Cast<Transform>().Where(t=>t!=model).ToList())child.SetParent(model,false);
            model.localPosition=new Vector3(0,-bottom*Tall,0);model.localScale=new Vector3(Thin,Tall,Thin);
            top=(top-bottom)*Tall;
            // A red flag: the pole's own material with a plain red texture, a little emissive so it shows from afar.
            Material wood=stake.GetComponentsInChildren<Renderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            Material cloth=null;
            if(wood!=null)
            {
                var red=new Texture2D(2,2,TextureFormat.RGBA32,false){name="Demolisher stake flag"};
                red.SetPixels(Enumerable.Repeat(new Color(0.9f,0.08f,0.05f),4).ToArray());red.Apply();_stakeOwned.Add(red);
                cloth=new Material(wood){name="Demolisher stake flag",mainTexture=red,color=Color.white};
                if(cloth.HasProperty("_EmissionColor")){cloth.EnableKeyword("_EMISSION");cloth.SetColor("_EmissionColor",new Color(0.4f,0.03f,0.02f));}
                _stakeOwned.Add(cloth);
            }
            var flag=GameObject.CreatePrimitive(PrimitiveType.Cube);flag.name="Flag";
            DestroyImmediate(flag.GetComponent<Collider>());
            flag.transform.SetParent(stake.transform,false);
            flag.transform.localPosition=new Vector3(0.3f,top-0.22f,0);flag.transform.localScale=new Vector3(0.56f,0.36f,0.02f);
            if(cloth!=null)flag.GetComponent<MeshRenderer>().sharedMaterial=cloth;
            var piece=stake.GetComponent<Piece>();
            piece.m_name="Demolition stake";
            piece.m_description="Mark an area to demolish. Click the ground for each corner, click the first corner (or press E) to close it, then E: everything highlighted red inside is removed as the hammer would, materials dropped. Backspace removes the last corner, Shift+Backspace clears. The stake isn't built.";
            piece.m_resources=new Piece.Requirement[0];
            piece.m_craftingStation=null;
            piece.m_category=Piece.PieceCategory.Misc;
            piece.m_clipEverything=true;piece.m_groundPiece=false;piece.m_groundOnly=false;
            piece.m_enabled=true;
            try{Sprite icon=StakeIcon(stake);if(icon!=null)piece.m_icon=icon;}
            catch(Exception ex){Logger.LogWarning("No icon for the demolition stake: "+ex.Message);}
            return stake;
        }
        private Sprite StakeIcon(GameObject stake)
        {
            var stage=new GameObject("Demolisher icon stage");stage.transform.position=new Vector3(0,-3000,0);
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
                var camera=new GameObject("Demolisher icon camera").AddComponent<Camera>();camera.transform.SetParent(stage.transform,false);
                camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=60;camera.allowHDR=false;camera.allowMSAA=false;
                camera.transform.position=bounds.center+new Vector3(0.15f,0.2f,-1f).normalized*(bounds.extents.magnitude/Mathf.Sin(10*Mathf.Deg2Rad))*1.02f;
                camera.transform.LookAt(bounds.center);
                var light=new GameObject("Demolisher icon light").AddComponent<Light>();light.transform.SetParent(stage.transform,false);
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
            // The prefab stays registered until the next copy of this mod replaces it (see PieceReload).
            foreach(var owned in _stakeOwned)if(owned!=null)Destroy(owned);
            _stakeOwned.Clear();
        }

        // The ghost stands where the next click would put a corner (on the first corner when it would close the outline).
        private void PlaceStakeGhost(Player player)
        {
            if(!_enabled.Value || !StakeSelected(player) || !(PlacementGhost?.GetValue(player) is GameObject ghost) || ghost==null || !ghost.activeSelf)return;
            if(!_closed && NextCorner(out Vector3 point,out _,out _))ghost.transform.position=point;
        }

        [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
        private static class StakeNeverBuilt
        {
            [HarmonyPrefix,HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance,Piece piece,ref bool __result)
            {
                if(!IsStake(piece))return true;
                __result=false;
                if(__instance==Player.m_localPlayer && Instance!=null && Instance._enabled.Value)Instance.Click(__instance);
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
