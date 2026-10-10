using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MeadowGolf
{
    internal static class Prefabs
    {
        internal const string Club="BobGolfClub",Tee="BobGolfTee",Cup="BobGolfCup",Ball="BobGolfBall";
        private static GameObject Holder;
        internal static GameObject ClubPrefab,TeePrefab,CupPrefab,BallPrefab;
        private static Recipe ClubRecipe;
        private static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly MethodInfo Registers=AccessTools.Method(typeof(ObjectDB),"UpdateRegisters");
        internal static readonly AccessTools.FieldRef<Humanoid,ItemDrop.ItemData> Right=AccessTools.FieldRefAccess<Humanoid,ItemDrop.ItemData>("m_rightItem");
        internal static bool IsClub(ItemDrop.ItemData item)=>item?.m_shared?.m_name=="Meadow golf club";
        private static GameObject Source(string name,ObjectDB db)=>ZNetScene.instance?.GetPrefab(name)??db?.m_items.FirstOrDefault(i=>i!=null&&i.name==name);
        private static Piece.Requirement Resource(string name,int n,ObjectDB db)=>new Piece.Requirement{m_resItem=Source(name,db)?.GetComponent<ItemDrop>(),m_amount=n,m_recover=true};
        private static void Ensure(ObjectDB db)
        {
            if(ClubPrefab!=null)return;
            GameObject source=Source("Club",db);if(source==null)return;
            var material=source.GetComponentsInChildren<Renderer>(true).Where(r=>r.GetType().Name!="ParticleSystemRenderer").Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            if(material==null)return;
            Models.Init(material);
            Holder=new GameObject("MeadowGolfPrefabs");Holder.SetActive(false);Object.DontDestroyOnLoad(Holder);
            ClubPrefab=Object.Instantiate(source,Holder.transform);ClubPrefab.name=Club;
            foreach(LODGroup lod in ClubPrefab.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            foreach(Transform child in ClubPrefab.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            foreach(Collider c in ClubPrefab.GetComponents<Collider>())Object.DestroyImmediate(c);
            Models.Club(ClubPrefab.transform);
            var collider=ClubPrefab.AddComponent<CapsuleCollider>();collider.radius=.06f;collider.height=.9f;collider.center=new Vector3(0,-.35f,0);
            var drop=ClubPrefab.GetComponent<ItemDrop>();drop.m_itemData=drop.m_itemData.Clone();
            drop.m_itemData.m_shared=(ItemDrop.ItemData.SharedData)Clone.Invoke(drop.m_itemData.m_shared,null);
            var shared=drop.m_itemData.m_shared;
            shared.m_name="Meadow golf club";shared.m_description="A carved wooden club. E at a golf tee starts a hole. Wheel: drive/chip/putt. Hold left mouse to charge; release to swing. G: scorecard.";
            shared.m_itemType=ItemDrop.ItemData.ItemType.Tool;shared.m_icons=new[]{Models.Icon(0)};shared.m_weight=1;shared.m_maxQuality=1;shared.m_useDurability=false;
            shared.m_damages=new HitData.DamageTypes();shared.m_damagesPerLevel=new HitData.DamageTypes();
            drop.m_itemData.m_dropPrefab=ClubPrefab;drop.m_itemData.m_quality=1;
        }
        private static GameObject MakePiece(string name,bool cup,ObjectDB db,ZNetScene scene)
        {
            GameObject source=scene.GetPrefab("sign");if(source==null)return null;
            GameObject go=Object.Instantiate(source,Holder.transform);go.name=name;
            foreach(LODGroup lod in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            var sign=go.GetComponent<Sign>();if(sign!=null)Object.DestroyImmediate(sign);
            foreach(Transform child in go.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
            var piece=go.GetComponent<Piece>();
            piece.m_name=cup?"Golf cup & flag":"Golf tee";piece.m_description="Shift+E: set Course:Hole:Par (for example Meadow:1:3). Match a tee and cup. E on the tee starts your hole.";
            piece.m_category=Piece.PieceCategory.Furniture;piece.m_icon=Models.Icon(cup?2:1);piece.m_comfort=0;
            piece.m_resources=cup?new[]{Resource("Wood",4,db),Resource("Stone",1,db)}:new[]{Resource("Wood",2,db)};
            piece.m_craftingStation=null;piece.m_groundOnly=false;piece.m_groundPiece=false;piece.m_notOnWood=false;
            piece.m_noClipping=false;piece.m_clipEverything=true;piece.m_allowedInDungeons=true;
            var wear=go.GetComponent<WearNTear>();
            if(wear!=null)
            {
                wear.m_new=wear.m_worn=wear.m_broken=wear.m_wet=null;wear.m_snow=wear.m_snowWorn=wear.m_snowBroken=null;
                wear.m_noSupportWear=true;wear.m_noRoofWear=true;wear.m_supports=false;wear.m_health=80;
            }
            Models.Marker(go.transform,cup);if(wear!=null)wear.m_fragmentRoots=new[]{go.transform.Find("GolfModel").gameObject};
            var box=go.AddComponent<BoxCollider>();box.center=cup?new Vector3(.43f,Models.FlagHeight/2,0):new Vector3(0,.12f,0);
            box.size=cup?new Vector3(.20f,Models.FlagHeight,.20f):new Vector3(.75f,.24f,.75f);
            go.AddComponent<GolfMarker>();return go;
        }
        internal static void Scene(ZNetScene scene)
        {
            Ensure(ObjectDB.instance);if(ClubPrefab==null)return;
            if(TeePrefab==null)TeePrefab=MakePiece(Tee,false,ObjectDB.instance,scene);
            if(CupPrefab==null)CupPrefab=MakePiece(Cup,true,ObjectDB.instance,scene);
            if(BallPrefab==null)
            {
                BallPrefab=new GameObject(Ball){layer=LayerMask.NameToLayer("item")};BallPrefab.transform.SetParent(Holder.transform,false);
                var body=BallPrefab.AddComponent<Rigidbody>();ShotPhysics.Configure(body);
                var sphere=BallPrefab.AddComponent<SphereCollider>();sphere.radius=.12f;
                sphere.sharedMaterial=new PhysicsMaterial("Meadow golf wood"){dynamicFriction=.4f,staticFriction=.5f,bounciness=.3f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Minimum};
                var view=BallPrefab.AddComponent<ZNetView>();view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;view.m_distant=false;
                var sync=BallPrefab.AddComponent<ZSyncTransform>();sync.m_syncPosition=true;sync.m_syncRotation=true;sync.m_syncBodyVelocity=true;
                Models.Ball(BallPrefab.transform,Models.BallColors[0]);BallPrefab.AddComponent<GolfBall>();
            }
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            foreach(GameObject prefab in All())if(prefab!=null)
            {scene.m_prefabs.RemoveAll(p=>p==null||p.name==prefab.name&&p!=prefab);if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);named[prefab.name.GetStableHashCode()]=prefab;}
            Tables(ObjectDB.instance);
        }
        private static IEnumerable<GameObject> All(){yield return ClubPrefab;yield return TeePrefab;yield return CupPrefab;yield return BallPrefab;}
        internal static void Database(ObjectDB db)
        {
            Ensure(db);if(ClubPrefab==null)return;
            db.m_items.RemoveAll(i=>i==null||i.name==Club&&i!=ClubPrefab);
            if(!db.m_items.Contains(ClubPrefab))db.m_items.Add(ClubPrefab);
            db.m_recipes.RemoveAll(r=>r==null||r.name=="Recipe_BobGolfClub"&&r!=ClubRecipe);
            if(ClubRecipe==null&&Source("Wood",db)!=null&&Source("LeatherScraps",db)!=null)
            {
                ClubRecipe=ScriptableObject.CreateInstance<Recipe>();ClubRecipe.name="Recipe_BobGolfClub";
                ClubRecipe.m_item=ClubPrefab.GetComponent<ItemDrop>();ClubRecipe.m_amount=1;ClubRecipe.m_enabled=true;ClubRecipe.m_minStationLevel=1;
                ClubRecipe.m_craftingStation=ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
                ClubRecipe.m_resources=new[]{Resource("Wood",6,db),Resource("LeatherScraps",2,db)};
            }
            // Main-menu databases have no scene/station. Rebind when the world's ObjectDB wakes.
            if(ClubRecipe!=null)
            {
                ClubRecipe.m_craftingStation=ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
                if(ClubRecipe.m_craftingStation!=null&&!db.m_recipes.Contains(ClubRecipe))db.m_recipes.Add(ClubRecipe);
            }
            Tables(db);
        }
        private static void Tables(ObjectDB db)
        {
            PieceTable table=Source("Hammer",db)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null)return;
            table.m_pieces.RemoveAll(p=>p==null||(p.name==Tee&&p!=TeePrefab)||(p.name==Cup&&p!=CupPrefab));
            foreach(var prefab in new[]{TeePrefab,CupPrefab})if(prefab!=null&&!table.m_pieces.Contains(prefab))table.m_pieces.Add(prefab);
        }
        internal static void Loaded()
        {
            if(ZNetScene.instance!=null)Scene(ZNetScene.instance);
            if(ObjectDB.instance!=null)Registers.Invoke(ObjectDB.instance,null);
            if(Player.m_localPlayer!=null)
                foreach(var item in Player.m_localPlayer.GetInventory().GetAllItems())if(IsClub(item))
                {item.m_shared=ClubPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;item.m_dropPrefab=ClubPrefab;}
            GolfWorld.AttachLoaded();
        }
        internal static void Unregister()
        {
            if(Holder==null)return;
            if(ZNetScene.instance!=null)
            {
                var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(ZNetScene.instance);
                foreach(var prefab in All())if(prefab!=null)
                {ZNetScene.instance.m_prefabs.Remove(prefab);int hash=prefab.name.GetStableHashCode();if(named.TryGetValue(hash,out var p)&&p==prefab)named.Remove(hash);}
            }
            foreach(ObjectDB db in Resources.FindObjectsOfTypeAll<ObjectDB>())
            {
                if(ClubPrefab!=null)db.m_items.Remove(ClubPrefab);if(ClubRecipe!=null)db.m_recipes.Remove(ClubRecipe);
                var table=Source("Hammer",db)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                if(table!=null){table.m_pieces.Remove(TeePrefab);table.m_pieces.Remove(CupPrefab);}
            }
            // Existing equipped models and saved world instances still use these material/mesh/icon assets.
            Object.Destroy(Holder);Holder=null;ClubPrefab=TeePrefab=CupPrefab=BallPrefab=null;ClubRecipe=null;
        }
    }
}
