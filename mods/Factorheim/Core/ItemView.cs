using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // Pictures of items in the world that are only pictures: an item riding a belt, in an inserter's claw, on a machine's tray. A copy of
    // the item's model (renderers only: no ZNetView, no physics, no pickup), scaled so its longest side is `size`, drawn by each game
    // locally (nothing on the network). Get one with Take, give it back with Give (they are pooled: belts make and drop hundreds).
    internal static class ItemView
    {
        private static Transform _templates,_pool;
        private static readonly Dictionary<string,GameObject> Templates=new Dictionary<string,GameObject>();
        private static readonly Dictionary<string,Stack<GameObject>> Free=new Dictionary<string,Stack<GameObject>>();

        internal static GameObject Take(string item,Transform parent,float size=0.3f)
        {
            GameObject t=Template(item);
            if(t==null)return null;
            GameObject go;
            if(Free.TryGetValue(item,out var stack)&&stack.Count>0){go=stack.Pop();if(go==null)return Take(item,parent,size);}
            else{go=Object.Instantiate(t);go.name=item;}
            go.transform.SetParent(parent,false);
            go.transform.localRotation=Quaternion.identity;
            go.transform.localScale=t.transform.localScale*size;
            go.SetActive(true);
            return go;
        }
        internal static void Give(GameObject go)
        {
            if(go==null)return;
            if(_pool==null){Object.Destroy(go);return;}
            go.SetActive(false);go.transform.SetParent(_pool,false);
            if(!Free.TryGetValue(go.name,out var stack))Free[go.name]=stack=new Stack<GameObject>();
            if(stack.Count<256)stack.Push(go);else Object.Destroy(go);
        }

        // The item's look, once: a copy of its prefab with everything but transforms, mesh filters and renderers taken off, its
        // pivot at the middle of its model, scale such that size 1 means 1 m across at its longest.
        private static GameObject Template(string item)
        {
            if(Templates.TryGetValue(item,out GameObject t)&&t!=null)return t;
            GameObject prefab=Ports.Prefab(item);
            if(prefab==null)return null;
            if(_templates==null)
            {
                var holder=new GameObject("FactorheimItemViews");holder.SetActive(false);Object.DontDestroyOnLoad(holder);_templates=holder.transform;
                var pool=new GameObject("FactorheimItemPool");Object.DontDestroyOnLoad(pool);_pool=pool.transform;
            }
            GameObject copy=Object.Instantiate(prefab,_templates,false);   // (under a switched-off holder: nothing in it wakes up)
            copy.name=item;
            var keep=new HashSet<System.Type>{typeof(Transform),typeof(MeshFilter),typeof(MeshRenderer),typeof(SkinnedMeshRenderer)};
            // (twice: some components need others gone first)
            for(int pass=0;pass<2;pass++)
                foreach(Component c in copy.GetComponentsInChildren<Component>(true))
                    if(c!=null&&!keep.Contains(c.GetType()))Object.DestroyImmediate(c);
            foreach(Renderer r in copy.GetComponentsInChildren<Renderer>(true))r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            // centre and size: wrap in a root so the pivot is the model's middle
            var root=new GameObject(item);root.transform.SetParent(_templates,false);
            Bounds b=Look.Bounds(copy.transform,copy.transform);
            copy.transform.SetParent(root.transform,false);
            copy.transform.localPosition=-b.center;
            float longest=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
            root.transform.localScale=Vector3.one/(longest>0.01f?longest:1f);
            foreach(Transform x in root.GetComponentsInChildren<Transform>(true))x.gameObject.layer=LayerMask.NameToLayer("Default_small");
            Templates[item]=root;
            return root;
        }

        internal static void Clear()
        {
            foreach(GameObject t in Templates.Values)if(t!=null)Object.Destroy(t);
            Templates.Clear();Free.Clear();
            if(_templates!=null)Object.Destroy(_templates.gameObject);
            if(_pool!=null)Object.Destroy(_pool.gameObject);
            _templates=null;_pool=null;
        }
    }
}
