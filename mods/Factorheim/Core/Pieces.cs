using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // A hammer piece: a copy of a game piece (for its Piece, WearNTear and ZNetView: placing, health, repair, saving), with none of that
    // piece's look, colliders or snap points. Its model is built under a child named "Model" (the icon is drawn from it, and it is what
    // wears and breaks); unless BoxCollider is false, one box round the model on the piece layer is what you hit and stand on.
    internal sealed class PieceDef
    {
        internal string Prefab,Name,Description;
        internal string From="wood_pole";                      // the game piece copied
        internal string Cost="Wood:4";                         // "Item:amount, ..." (a setting: Costs section)
        internal string Station="piece_workbench";             // the station it must be built near ("" for none; a setting too)
        internal int Tier;                                     // research tier that puts it in the hammer's menu
        internal Piece.PieceCategory Category=Piece.PieceCategory.Crafting;
        internal WearNTear.MaterialType Material=WearNTear.MaterialType.Iron;
        internal float Health=600f;
        internal bool BoxCollider=true;
        internal Action<Transform> Model;                      // builds the look under "Model" (Look.Builder helps); colliders of its own too
        internal Action<GameObject> Setup;                     // components (AddComponent<Belt>()), snap points (SnapPoints), piece flags
        internal Vector3? IconView;                            // direction from the model to the icon camera

        internal GameObject Built;
        internal ConfigEntry<string> CCost,CStation;
        internal Piece Piece=>Built!=null?Built.GetComponent<Piece>():null;
    }

    internal static class Pieces
    {
        private static readonly MethodInfo UpdateKnownRecipes=AccessTools.Method(typeof(Player),"UpdateKnownRecipesList"),
            UpdateAvailablePieces=AccessTools.Method(typeof(Player),"UpdateAvailablePiecesList");
        private static ZNetScene _scene;private static float _next;private static bool _force;

        internal static IEnumerable<PieceDef> All=>Features.All.SelectMany(f=>f.Pieces);
        internal static PieceDef Find(string prefab)=>All.FirstOrDefault(d=>string.Equals(d.Prefab,prefab,StringComparison.OrdinalIgnoreCase));
        internal static PieceTable Hammer()=>ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal static void BindCosts(ConfigFile config)
        {
            foreach(PieceDef d in All)
            {
                d.CCost=config.Bind("9 - Costs",d.Prefab,d.Cost,$"What the {d.Name} takes to build: \"Item:amount, Item:amount\" (game prefab names). Needs a restart.");
                d.CStation=config.Bind("9 - Costs",d.Prefab+"Station",d.Station,$"The station the {d.Name} is built near (\"piece_workbench\", \"forge\", or nothing). Needs a restart.");
            }
            Tech.Changed+=()=>_force=true;
        }

        // From ZNetScene.Awake (via Things.Register) and again whenever needed: every piece built and registered, so saved ones load.
        internal static void Register(ZNetScene scene)
        {
            if(scene==null)return;
            _scene=scene; // (a new world's scene: the pieces built for the last one are kept and registered again, as the items are; rebuilt they'd pile up, a set a world load)
            foreach(PieceDef d in All)
            {
                if(d.Built==null)
                {
                    try{d.Built=Build(d,scene);}
                    catch(Exception e){Plugin.Warn($"Could not make the {d.Name}: {e}");}
                    if(d.Built==null)continue;
                }
                if(scene.GetPrefab(d.Prefab)!=d.Built){PieceReload.ReplacePrefab(scene,d.Built,null);PieceReload.Reattach(scene,d.Built);}
            }
        }

        // Once a second (and after research): costs filled in once the items exist, and the hammer's menu showing the unlocked ones.
        internal static void Tick()
        {
            if(!_force&&Time.unscaledTime<_next)return;
            _next=Time.unscaledTime+1f;
            ZNetScene scene=ZNetScene.instance;PieceTable table=Hammer();
            if(scene==null||table==null||ObjectDB.instance==null)return;
            if(scene!=_scene||All.Any(d=>d.Built==null))Register(scene);
            table.m_pieces.RemoveAll(p=>p==null);
            bool changed=_force;_force=false;
            foreach(PieceDef d in All)
            {
                if(d.Built==null)continue;
                Piece piece=d.Piece;
                if(piece.m_resources==null||piece.m_resources.Length==0)Costs(d,piece);
                bool show=Tech.Unlocked(d.Tier);
                if(show&&!table.m_pieces.Contains(d.Built)){PieceReload.ReplacePrefab(scene,d.Built,table);changed=true;}
                else if(!show&&table.m_pieces.Remove(d.Built))changed=true;
            }
            if(changed&&Player.m_localPlayer is Player p){UpdateKnownRecipes?.Invoke(p,null);UpdateAvailablePieces?.Invoke(p,null);}
        }

        internal static void Unload()
        {
            PieceTable table=Hammer();
            foreach(PieceDef d in All)PieceReload.UnloadPiece(d.Built,table);
            // (the prefabs stay registered and their meshes and materials alive: placed pieces keep drawing until the next copy takes over)
            foreach(PieceDef d in All)d.Built=null;
            _scene=null;
        }

        private static GameObject Build(PieceDef d,ZNetScene scene)
        {
            GameObject from=scene.GetPrefab(d.From);
            if(from==null||from.GetComponent<Piece>()==null){Plugin.Warn($"{d.Name}: the game has no piece {d.From} to copy");return null;}
            GameObject go=Object.Instantiate(from,Things.Holder,false);
            go.name=d.Prefab;
            foreach(LODGroup lod in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            for(int i=go.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
            foreach(Renderer r in go.GetComponents<Renderer>())Object.DestroyImmediate(r);
            foreach(MeshFilter m in go.GetComponents<MeshFilter>())Object.DestroyImmediate(m);
            int layer=LayerMask.NameToLayer("piece");
            go.layer=layer;

            var model=new GameObject("Model"){layer=layer};
            model.transform.SetParent(go.transform,false);
            try{d.Model?.Invoke(model.transform);}
            catch(Exception e){Plugin.Warn($"Could not build the {d.Name}'s model: {e}");}
            foreach(Transform t in model.GetComponentsInChildren<Transform>(true))if(t.gameObject.layer==0)t.gameObject.layer=layer;
            if(d.BoxCollider)
            {
                Bounds b=Look.Bounds(model.transform,go.transform);
                var box=go.AddComponent<BoxCollider>();
                box.center=b.center;box.size=Vector3.Max(b.size,Vector3.one*0.1f);
            }

            Piece piece=go.GetComponent<Piece>();
            piece.m_name=d.Name;piece.m_description=d.Description;
            piece.m_category=d.Category;piece.m_enabled=true;
            piece.m_comfort=0;piece.m_groundPiece=false;piece.m_cultivatedGroundOnly=false;piece.m_waterPiece=false;piece.m_noInWater=false;
            piece.m_resources=new Piece.Requirement[0];   // (filled in by Tick once the items exist)
            WearNTear wear=go.GetComponent<WearNTear>();
            if(wear!=null)
            {
                wear.m_new=model;wear.m_worn=model;wear.m_broken=model;
                wear.m_fragmentRoots=new GameObject[0];
                wear.m_materialType=d.Material;wear.m_health=d.Health;wear.m_noRoofWear=true;
            }
            SnapPoints.Clear(go);
            d.Setup?.Invoke(go);
            try{if(Icon.Make(go,d.IconView,null,null) is Sprite icon)piece.m_icon=icon;}
            catch(Exception e){Plugin.Warn($"No icon for {d.Name}: {e.Message}");}
            return go;
        }

        private static void Costs(PieceDef d,Piece piece)
        {
            var needs=new List<Piece.Requirement>();
            foreach(string part in (d.CCost?.Value??d.Cost).Split(','))
            {
                string[] kv=part.Split(':');
                if(kv[0].Trim()=="")continue;
                ItemDrop res=ObjectDB.instance.GetItemPrefab(kv[0].Trim())?.GetComponent<ItemDrop>();
                int amount=kv.Length>1&&int.TryParse(kv[1].Trim(),out int a)?a:1;
                if(res==null){Plugin.Warn($"{d.Name}: the game has no {kv[0].Trim()}; left out of its cost");continue;}
                needs.Add(new Piece.Requirement{m_resItem=res,m_amount=amount,m_recover=true});
            }
            piece.m_resources=needs.ToArray();
            string station=(d.CStation?.Value??d.Station)?.Trim();
            piece.m_craftingStation=string.IsNullOrEmpty(station)?null:ZNetScene.instance.GetPrefab(station)?.GetComponent<CraftingStation>();
        }
    }
}
