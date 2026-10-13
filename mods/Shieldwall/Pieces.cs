using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The mod's building pieces beside the Warstone: the stave socket (a stone cairn that holds a stave, on the ground, a wall or a
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
                // A dry-stone cairn under a capstone cut with the war rune, the stave stood in an iron ring on its collar stone (Models/socket),
                // in the Warstone's rock, iron and glowing rune. The game's black marble pedestal, if the model can't be had.
                GameObject socket;
                Mesh cairn=Look.SocketMesh;Material[] materials=Look.Materials;
                if(cairn!=null&&materials!=null)
                {
                    var source=new GameObject("BobStaveSocketLook");
                    try
                    {
                        source.AddComponent<MeshFilter>().sharedMesh=cairn;source.AddComponent<MeshRenderer>().sharedMaterials=materials;
                        socket=Build("stone_pillar",SocketName,source,Vector3.zero,Quaternion.identity,1,null);
                    }
                    finally{Object.DestroyImmediate(source);}
                }
                else socket=Build("stone_pillar",SocketName,Assets.Find("blackmarble_altar_crystal"),Vector3.zero,Quaternion.identity,0.9f,r=>r.name=="altar"); // the pedestal, without its glass case
                if(socket!=null)
                {
                    SocketTop=Top(socket)-0.05f;
                    Piece piece=socket.GetComponent<Piece>();
                    piece.m_name="Stave socket";
                    piece.m_description="A stone cairn for a war stave. Set a stave in it (Use, or drag one from your hotbar), take it out again, and Shift+Use to "+
                        "strengthen it with warshards. Build it on the ground, a wall or the top of a tower within reach of a Warstone; the stone feeds a few staves, more as you raise its level. "+
                        "The horde's sappers will try to tear it down.";
                    piece.m_resources=Needs(("Stone",10),("Resin",2));
                    socket.GetComponent<WearNTear>().m_health=1500;
                    // One slot, opened with Use like a chest: the game's own window, with your bag beside it, to choose the stave.
                    Container box=socket.AddComponent<Container>();
                    Container chest=Assets.Find("piece_chest_wood")?.GetComponent<Container>();
                    box.m_name="Stave socket";box.m_width=1;box.m_height=1;box.m_privacy=Container.PrivacySetting.Public;box.m_checkGuardStone=true;
                    if(chest!=null){box.m_bkg=chest.m_bkg;box.m_openEffects=chest.m_openEffects;box.m_closeEffects=chest.m_closeEffects;}
                    socket.AddComponent<Planted>();
                }
            }
            if(!Made.TryGetValue(PitchPit.PrefabName,out GameObject made)||made==null)Pit();
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
        // The pitch pit: the campfire's ring of stones, wide, round a pool of pitch, with the campfire's own fire to show when it burns.
        private static void Pit()
        {
            GameObject ring=Assets.Find("fire_pit");
            GameObject pit=Build("stone_pillar",PitchPit.PrefabName,ring,Vector3.zero,Quaternion.identity,2.2f,r=>r is MeshRenderer&&!(r.sharedMaterial!=null&&r.sharedMaterial.name.StartsWith("fire_log"))); // the ring and ash, not the campfire's glowing logs
            if(pit==null)return;
            Bounds b=MeshBounds(pit.transform,pit.transform.Find("new").gameObject);
            PitchPit.Radius=Mathf.Max(1.2f,Mathf.Min(b.extents.x,b.extents.z)*0.85f);
            // The pool: a flat disc of black, just above the ground inside the ring.
            GameObject pool=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pool.GetComponent<Collider>());
            pool.name="Pitch";pool.transform.SetParent(pit.transform,false);
            pool.transform.localPosition=new Vector3(b.center.x,0.06f,b.center.z);pool.transform.localScale=new Vector3(PitchPit.Radius*2,0.02f,PitchPit.Radius*2);
            Renderer stone=pit.transform.Find("new").GetComponentInChildren<MeshRenderer>(true);
            var renderer=pool.GetComponent<MeshRenderer>();
            if(stone!=null){var m=new Material(stone.sharedMaterial){name="BobPitch"};m.color=new Color(0.04f,0.035f,0.03f);if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",0.85f);renderer.sharedMaterial=m;Assets.Owned.Add(m);}
            pool.layer=LayerMask.NameToLayer("piece");
            // The fire: three of the campfire's own flames round the pool, lit only while it burns.
            GameObject flames=ring!=null?ring.GetComponent<Fireplace>()?.m_enabledObject:null;
            var fire=new GameObject("PitchFire");fire.transform.SetParent(pit.transform,false);fire.SetActive(false);
            if(flames!=null)for(int i=0;i<3;i++)
            {
                GameObject f=Object.Instantiate(flames,fire.transform,false);f.SetActive(true);
                f.transform.localPosition=Quaternion.Euler(0,120*i,0)*Vector3.forward*PitchPit.Radius*0.5f;f.transform.localScale=Vector3.one*1.4f;
            }
            Piece piece=pit.GetComponent<Piece>();
            piece.m_name="Pitch pit";
            piece.m_description="A ring of stones round a pool of pitch. Raiders who wade in are slowed; set it alight with fire (a fire shot from a stave or staff landing near it, a fire arrow) "+
                "and it burns the horde standing in it, then must seep full again. Lay them where the horde will walk.";
            piece.m_resources=Needs(("Stone",8),("Resin",8));
            piece.m_groundOnly=true;
            pit.GetComponent<WearNTear>().m_health=800;
            // Not a wall: on the layer rugs use, which the game's road-finding and creatures' bodies pass through, so the horde wades in.
            int open=LayerMask.NameToLayer("piece_nonsolid");
            foreach(Transform t in pit.GetComponentsInChildren<Transform>(true))t.gameObject.layer=open;
            pit.AddComponent<PitchPit>();
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
