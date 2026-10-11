using System;
using UnityEngine;

namespace LocalPortals
{
    // For other mods: place looking-glasses (built-style or wild), link them by name, lock their names, and put them to sleep
    // until a player wakes them, with an offering or once the world has some global key (a boss slain). All of it is kept in
    // the mirror's own saved data, so a mod without a reference to this one can set the same keys on the ZDO itself:
    //   bob_lportal_name (string)        the name: two mirrors of a name within range link
    //   bob_lportal_locked (bool)        players cannot rename it
    //   bob_lportal_dormant (bool)       asleep: dark glass, no link, walked into like a wall
    //   bob_lportal_wake_item (string)   item prefab that wakes it, offered from the hotbar...
    //   bob_lportal_wake_count (int)     ...this many (default 1)
    //   bob_lportal_wake_key (string)    or: it wakes by itself once the world has this global key ("defeated_gdking")
    public static class LookingGlassApi
    {
        public const string BuiltPrefab=PortalPrefab.Name,WildPrefab=PortalPrefab.WildName;
        public const string NameKey=LocalPortal.NameKey,LockedKey=LocalPortal.LockedKey,DormantKey=LocalPortal.DormantKey;
        public const string WakeItemKey=LocalPortal.WakeItemKey,WakeCountKey=LocalPortal.WakeCountKey,WakeKeyKey=LocalPortal.WakeKeyKey;

        // Someone (a player or a creature) stepped through: who, the mirror they went into, the one they came out of. Raised on
        // the machine that moved them.
        public static event Action<Character,GameObject,GameObject> Crossed;
        // A sleeping mirror woke: the mirror, and the player who woke it (null if it woke by a global key).
        public static event Action<GameObject,Player> Woken;

        // A new mirror in the world (on the machine that owns the area: the host, or the player there). Wild ones are not built
        // with the hammer and cannot be broken. A name links it to the other mirror of that name; a named one is locked unless
        // `locked` is false.
        public static GameObject Place(Vector3 position,Quaternion rotation,bool wild,string name=null,bool? locked=null)
        {
            GameObject prefab=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(wild?WildPrefab:BuiltPrefab):null;
            if(prefab==null)return null;
            GameObject go;
            LocalPortal.Placing=true; // (not linked on waking to the nearest unnamed mirror: by name only)
            try{go=UnityEngine.Object.Instantiate(prefab,position,rotation);}
            finally{LocalPortal.Placing=false;}
            ZDO zdo=Zdo(go);
            if(zdo==null)return go;
            zdo.Set(LocalPortal.LinkedKey,true);
            if(!string.IsNullOrEmpty(name))zdo.Set(NameKey,Trim(name));
            zdo.Set(LockedKey,locked??!string.IsNullOrEmpty(name));
            return go;
        }

        public static string GetName(GameObject mirror)=>Zdo(mirror)?.GetString(NameKey)??"";
        public static void SetName(GameObject mirror,string name)=>Own(mirror)?.Set(NameKey,Trim(name??""));
        public static bool IsLocked(GameObject mirror)=>Zdo(mirror)?.GetBool(LockedKey)??false;
        public static void SetLocked(GameObject mirror,bool locked)=>Own(mirror)?.Set(LockedKey,locked);

        public static bool IsDormant(GameObject mirror)=>Zdo(mirror)?.GetBool(DormantKey)??false;
        // Asleep: dark glass, linked to nothing. Then woken by an offering, a global key, or WakeUp.
        public static void SetDormant(GameObject mirror,bool dormant)=>Own(mirror)?.Set(DormantKey,dormant);
        public static void WakeWithItem(GameObject mirror,string itemPrefab,int count=1)
        {
            ZDO zdo=Own(mirror);
            if(zdo==null)return;
            zdo.Set(DormantKey,true);zdo.Set(WakeItemKey,itemPrefab??"");zdo.Set(WakeCountKey,Mathf.Max(1,count));
        }
        public static void WakeOnGlobalKey(GameObject mirror,string globalKey)
        {
            ZDO zdo=Own(mirror);
            if(zdo==null)return;
            zdo.Set(DormantKey,true);zdo.Set(WakeKeyKey,globalKey??"");
        }
        public static void WakeUp(GameObject mirror)=>mirror?.GetComponent<LocalPortal>()?.Wake(null);

        // The mirror this one looks out of, if it is loaded.
        public static GameObject PartnerOf(GameObject mirror)
        {
            LocalPortal p=mirror!=null?mirror.GetComponent<LocalPortal>():null;
            return p!=null&&p.Partner!=null?p.Partner.gameObject:null;
        }
        public static bool IsMirror(GameObject go)=>go!=null&&go.GetComponent<LocalPortal>()!=null;

        internal static void RaiseCrossed(Character who,LocalPortal from,LocalPortal to)
        {
            if(Crossed==null)return;
            try{Crossed(who,from!=null?from.gameObject:null,to!=null?to.gameObject:null);}
            catch(Exception e){Debug.LogError("[LocalPortals] A Crossed handler failed: "+e);}
        }
        internal static void RaiseWoken(LocalPortal p,Player by)
        {
            if(Woken==null)return;
            try{Woken(p.gameObject,by);}
            catch(Exception e){Debug.LogError("[LocalPortals] A Woken handler failed: "+e);}
        }

        private static ZDO Zdo(GameObject go)
        {
            ZNetView view=go!=null?go.GetComponent<ZNetView>():null;
            return view!=null&&view.IsValid()?view.GetZDO():null;
        }
        private static ZDO Own(GameObject go)
        {
            ZNetView view=go!=null?go.GetComponent<ZNetView>():null;
            if(view==null||!view.IsValid())return null;
            if(!view.IsOwner())view.ClaimOwnership();
            return view.GetZDO();
        }
        private static string Trim(string name)=>name.Length>LocalPortal.NameLength?name.Substring(0,LocalPortal.NameLength):name;
    }
}
