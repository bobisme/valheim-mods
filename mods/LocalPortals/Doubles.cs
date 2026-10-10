using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // A body double for anyone partway through a portal. The part of a body past the glass is hidden by the glass (from in
    // front) and by the board behind it (from behind), so on its own it would seem to sink into the mirror. The double is a
    // copy of the body's model standing where the body would be if it had gone through, posed bone for bone like it each
    // frame: the part that has gone in comes out of the other mirror, and is seen there, in the world and through the glass.
    internal static class Doubles
    {
        private sealed class Double
        {
            internal Player Who;
            internal LocalPortal At;           // the portal the body is passing, the double stands at its partner
            internal GameObject Copy;
            internal Transform[] From,To;      // the body's model and the copy, bone for bone
            internal int Parts;                // the model's renderer count when copied (equipment changes remake it)
            internal float Seen,Checked;
        }
        private static readonly List<Double> All=new List<Double>();
        private static readonly List<Renderer> Renderers=new List<Renderer>();
        private static GameObject _holder;      // an inactive parent: copies made under it wake nothing up
        internal static int Count=>All.Count;

        // Called just before the game camera draws (after all animation): places and poses every double.
        internal static void Sync()
        {
            if(!Plugin.BodyDoubles.Value){Clear();return;}
            foreach(Player who in Player.GetAllPlayers())
            {
                if(who==null||who.IsDead())continue;
                LocalPortal at=Straddled(who);
                Double d=All.Find(x=>x.Who==who);
                if(at==null)continue;
                if(d==null){d=new Double{Who=who};All.Add(d);}
                if(d.At!=at||d.Copy==null||(Time.time>d.Checked&&PartsChanged(d)))Make(d,at);
                d.Seen=Time.time;
            }
            for(int i=All.Count-1;i>=0;i--)
            {
                Double d=All[i];
                if(d.Who==null||d.Copy==null||d.At==null||d.At.Partner==null||Time.time-d.Seen>0.25f){Drop(d);All.RemoveAt(i);continue;}
                bool now=Time.time-d.Seen<0.01f;
                if(d.Copy.activeSelf!=now)d.Copy.SetActive(now);
                if(now)Pose(d);
            }
        }

        // The portal whose glass a body is passing through, if any: its middle within a step of the glass, inside the opening.
        private static LocalPortal Straddled(Player who)
        {
            Vector3 middle=who.transform.position+Vector3.up*(float)Policy.BodyHeight;
            LocalPortal best=null;
            float bestDepth=0.75f;
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Partner==null||(p.transform.position-middle).sqrMagnitude>16)continue;
                Vector3 l=p.transform.InverseTransformPoint(middle);
                if(Mathf.Abs(l.z)<bestDepth&&Policy.InOpening(l.x,l.y)){best=p;bestDepth=Mathf.Abs(l.z);}
            }
            return best;
        }

        private static bool PartsChanged(Double d)
        {
            d.Checked=Time.time+0.5f;
            GameObject visual=d.Who.GetVisual();
            if(visual==null)return false;
            visual.GetComponentsInChildren(true,Renderers);
            return Renderers.Count!=d.Parts;
        }

        private static void Make(Double d,LocalPortal at)
        {
            Drop(d);
            d.At=at;
            GameObject visual=d.Who.GetVisual();
            if(visual==null)return;
            if(_holder==null){_holder=new GameObject("LocalPortalDoubles");_holder.SetActive(false);Object.DontDestroyOnLoad(_holder);}
            GameObject copy=Object.Instantiate(visual,_holder.transform,false);
            copy.name="LocalPortalDouble";
            Strip(copy);
            d.From=visual.GetComponentsInChildren<Transform>(true);
            d.To=copy.GetComponentsInChildren<Transform>(true);
            visual.GetComponentsInChildren(true,Renderers);
            d.Parts=Renderers.Count;
            d.Checked=Time.time+0.5f;
            copy.transform.SetParent(null,false);
            Object.DontDestroyOnLoad(copy);
            d.Copy=copy;
            if(d.From.Length!=d.To.Length){Drop(d);return;} // (should not happen: a fresh copy has the same bones)
            Pose(d);
        }

        // Only the look is kept: no scripts, physics, animation, sound or cloth simulation.
        private static void Strip(GameObject copy)
        {
            foreach(MonoBehaviour m in copy.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(m);
            foreach(Component c in copy.GetComponentsInChildren<Component>(true))
                if(c is Animator||c is Cloth||c is Collider||c is Joint||c is AudioSource||c is Rigidbody)Object.DestroyImmediate(c);
        }

        private static void Pose(Double d)
        {
            Transform a=d.At.transform,b=d.At.Partner.transform;
            Transform[] from=d.From,to=d.To;
            Transform root=from[0];
            to[0].SetPositionAndRotation(Crossing.Point(a,b,root.position),Crossing.Turn(a,b)*root.rotation);
            to[0].localScale=root.lossyScale;
            for(int i=1;i<from.Length;i++)
            {
                Transform f=from[i],t=to[i];
                if(f==null||t==null)continue;
                t.localPosition=f.localPosition;t.localRotation=f.localRotation;t.localScale=f.localScale;
                bool on=f.gameObject.activeSelf;
                if(t.gameObject.activeSelf!=on)t.gameObject.SetActive(on);
            }
        }

        private static void Drop(Double d)
        {
            if(d.Copy!=null)Object.Destroy(d.Copy);
            d.Copy=null;d.From=d.To=null;
        }
        internal static void Clear()
        {
            foreach(Double d in All)Drop(d);
            All.Clear();
        }
        internal static void Stop()
        {
            Clear();
            if(_holder!=null)Object.Destroy(_holder);
            _holder=null;
        }
    }
}
