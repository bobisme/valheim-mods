using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The spoils of a held siege: a chest of the horde's land that is gone, in a puff, once it has been emptied and closed.
    internal sealed class Spoils:MonoBehaviour
    {
        internal const string Key="bob_sw_spoils";
        private static readonly List<Spoils> Loaded=new List<Spoils>();
        private ZNetView _view;private Container _container;private float _next;private bool _full;

        private void Awake()
        {
            _view=GetComponent<ZNetView>();_container=GetComponent<Container>();
            if(_container!=null)_container.m_name="Spoils of the siege";
            Loaded.Add(this);
        }
        private void OnDestroy()=>Loaded.Remove(this);
        internal static void Mark(GameObject chest)
        {
            chest.GetComponent<ZNetView>()?.GetZDO()?.Set(Key,true);
            if(chest.GetComponent<Spoils>()==null)chest.AddComponent<Spoils>();
        }
        internal static void DetachAll(){foreach(Spoils s in Loaded.ToList())if(s!=null)Object.Destroy(s);Loaded.Clear();}
        internal static void AttachAll()
        {
            foreach(ZNetView view in Assets.AllInstances().Where(v=>v.GetZDO().GetBool(Key,false)).ToList())
            {
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(Spoils)&&stale.GetType()!=typeof(Spoils))Object.DestroyImmediate(stale);
                if(view.GetComponent<Spoils>()==null)view.gameObject.AddComponent<Spoils>();
            }
        }

        private void Update()
        {
            if(Time.time<_next||_view==null||!_view.IsValid()||_container==null)return;
            _next=Time.time+1;
            // A chest just loaded fills from its saved contents a moment later: only one seen full and then emptied goes
            // (every game watches, as whoever empties it may take it over).
            if(_container.GetInventory().NrOfItems()>0){_full=true;return;}
            if(!_full||!_view.IsOwner()||_container.IsInUse())return;
            Assets.Effect("vfx_spawn_small",transform.position+Vector3.up*0.5f);
            ZNetScene.instance.Destroy(gameObject);
        }
    }

    // A spoils chest loaded from the world (or another player's) gets its watcher back.
    [HarmonyPatch(typeof(Container),"Awake")]
    internal static class SpoilsLoaded
    {
        private static void Postfix(Container __instance)
        {
            ZDO z=__instance.GetComponent<ZNetView>()?.GetZDO();
            if(z!=null&&z.GetBool(Spoils.Key,false)&&__instance.GetComponent<Spoils>()==null)__instance.gameObject.AddComponent<Spoils>();
        }
    }
}
