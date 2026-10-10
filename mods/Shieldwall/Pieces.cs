using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The mod's building pieces beside the Warstone: the stave socket (a stone pedestal that holds a stave, on the ground, a wall or a
    // tower) and the four upgrades that raise the stone's level (the game's own crafting-station extensions, like the workbench's).
    internal static class Pieces
    {
        internal const string SocketName="BobStaveSocket";
        private static readonly Dictionary<string,GameObject> Made=new Dictionary<string,GameObject>();
        internal static GameObject Socket=>Made.TryGetValue(SocketName,out GameObject go)?go:null;
        internal static IEnumerable<GameObject> All=>Made.Values.Where(v=>v!=null);
        internal static float SocketTop=0.95f;   // where a stave stands in the pedestal

        internal static void Ensure()
        {
            if(ZNetScene.instance==null||Stone.Station==null)return;
            if(Socket==null)
            {
                GameObject altar=Assets.Find("blackmarble_altar_crystal");
                GameObject socket=Build("stone_pillar",SocketName,altar,Vector3.zero,Quaternion.identity,0.9f,r=>r.name=="altar"); // the pedestal, without its glass case
                if(socket!=null)
                {
                    SocketTop=Top(socket)-0.05f;
                    Piece piece=socket.GetComponent<Piece>();
                    piece.m_name="Stave socket";
                    piece.m_description="A stone pedestal for a war stave. Set a stave in it (Use, or drag one from your hotbar), take it out again, and Shift+Use to "+
                        "strengthen it with warshards. Build it on the ground, a wall or the top of a tower within reach of a Warstone; the stone feeds a few staves, more as you raise its level. "+
                        "The horde's sappers will try to tear it down.";
                    piece.m_resources=Needs(("Stone",10),("Resin",2));
                    socket.GetComponent<WearNTear>().m_health=1500;
                    socket.AddComponent<Planted>();
                }
            }
            foreach(var up in Policy.Upgrades)
            {
                if(Made.ContainsKey(up.prefab)&&Made[up.prefab]!=null)continue;
                GameObject look=Assets.Find(up.source);
                GameObject go=Build("piece_workbench_ext1",up.prefab,look,Vector3.zero,Quaternion.identity,1,null);
                if(go==null)continue;
                Piece piece=go.GetComponent<Piece>();
                piece.m_name=up.name;
                piece.m_description=$"Raises a Warstone's level by one when built within 10 m of it (each kind counts once): more staves it can feed, farther reach, stronger stave upgrades.";
                piece.m_resources=Needs(new[]{(Policy.ShardPrefab,up.shards)}.Concat(up.also).ToArray());
                StationExtension ext=go.GetComponent<StationExtension>();
                ext.m_craftingStation=Stone.Station;ext.m_maxStationDistance=10;ext.m_stack=false;
                go.GetComponent<WearNTear>().m_health=2000;
            }
        }
        // A copy of a vanilla piece (its working parts: network view, piece, wear and tear) wearing another prefab's look.
        private static GameObject Build(string baseName,string name,GameObject look,Vector3 offset,Quaternion rotation,float scale,System.Func<Renderer,bool> keep)
        {
            GameObject basePrefab=Assets.Find(baseName);
            if(basePrefab==null||look==null){Plugin.Log($"Shieldwall: cannot make {name} ({baseName}, {look?.name})");return null;}
            GameObject go=Object.Instantiate(basePrefab,Assets.Holder);
            go.name=name;
            foreach(Transform child in go.transform.Cast<Transform>().ToList())Object.DestroyImmediate(child.gameObject);
            foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
            int layer=LayerMask.NameToLayer("piece");
            GameObject model=Assets.Model(look,go.transform,offset,rotation,scale,keep);
            model.name="new";
            // Stand it on the ground, whatever height its own origin is at.
            Bounds raw=MeshBounds(go.transform,model);
            model.transform.localPosition+=Vector3.up*-raw.min.y;
            foreach(Transform t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
            // A solid box round the look, for building, standing on and being struck.
            Bounds b=MeshBounds(go.transform,model);
            var box=new GameObject("collider"){layer=layer};box.transform.SetParent(go.transform,false);
            // Reaching 5 cm below its base, so it rests in what it stands on (the game's support check needs the touch).
            BoxCollider collider=box.AddComponent<BoxCollider>();collider.center=b.center+Vector3.down*0.025f;collider.size=b.size+Vector3.up*0.05f;
            WearNTear wear=go.GetComponent<WearNTear>();
            if(wear!=null){wear.m_new=model;wear.m_worn=model;wear.m_broken=model;}
            Piece piece=go.GetComponent<Piece>();
            piece.m_category=Piece.PieceCategory.Misc;
            piece.m_craftingStation=Stone.Station;
            Sprite icon=Assets.Icon(model,name,Quaternion.Euler(0,-30,0));
            if(icon!=null)piece.m_icon=icon;
            Made[name]=go;
            return go;
        }
        private static Bounds MeshBounds(Transform root,GameObject model)
        {
            bool any=false;Bounds b=new Bounds();
            foreach(MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if(f.sharedMesh==null)continue;
                Bounds m=f.sharedMesh.bounds;
                Matrix4x4 toRoot=root.worldToLocalMatrix*f.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    Vector3 corner=m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    Vector3 p=toRoot.MultiplyPoint3x4(corner);
                    if(!any){b=new Bounds(p,Vector3.zero);any=true;}else b.Encapsulate(p);
                }
            }
            return any?b:new Bounds(Vector3.up*0.5f,Vector3.one);
        }
        private static float Top(GameObject go)=>MeshBounds(go.transform,go.transform.Find("new").gameObject).max.y;
        private static Piece.Requirement[] Needs(params (string item,int amount)[] needs)=>needs.Select(n=>new Piece.Requirement{
            m_resItem=(n.item==Policy.ShardPrefab?Items.Get(n.item):Assets.Find(n.item))?.GetComponent<ItemDrop>(),m_amount=n.amount,m_recover=true}).Where(r=>r.m_resItem!=null).ToArray();
        internal static void Forget(){Made.Clear();}
    }
}
