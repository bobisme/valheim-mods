using System.Collections.Generic;
using UnityEngine;

namespace Factorheim
{
    // How a machine looks while it works, on every game: its owner flips one ZDO int "fh_on" (only when it changes, and only off after a
    // second of idling, so a machine waiting a moment between jobs doesn't send two writes), and everyone turns the model's children named
    // "spin*" round their own up axis, shows the ones named "on*", and runs its smoke.
    internal sealed class Running:MonoBehaviour
    {
        internal const string Key="fh_on";
        private ZNetView _view;private float _readAt,_idleSince;private bool _on;
        private readonly List<Transform> _spin=new List<Transform>();
        private readonly List<GameObject> _lit=new List<GameObject>();
        private SmokeSpawner _smoke;
        internal float SpinSpeed=360f;

        private void Awake()
        {
            _view=GetComponent<ZNetView>();
            foreach(Transform t in GetComponentsInChildren<Transform>(true))
            {
                if(t.name.StartsWith("spin"))_spin.Add(t);
                else if(t.name.StartsWith("on"))_lit.Add(t.gameObject);
            }
            _smoke=GetComponentInChildren<SmokeSpawner>(true);
            Show(false);
        }

        // The owner says whether it worked this step.
        internal void Set(bool working)
        {
            if(_view==null||!_view.IsValid()||!_view.IsOwner())return;
            if(working)_idleSince=Time.time;
            bool on=working||Time.time-_idleSince<1f;
            ZDO z=_view.GetZDO();
            if((z.GetInt(Key,0)!=0)!=on)z.Set(Key,on?1:0);
        }

        private void Update()
        {
            if(_view==null||!_view.IsValid())return;
            if(Time.time>=_readAt){_readAt=Time.time+0.5f;bool on=_view.GetZDO().GetInt(Key,0)!=0;if(on!=_on)Show(on);}
            if(!_on)return;
            float turn=SpinSpeed*Time.deltaTime;
            foreach(Transform t in _spin)if(t!=null)t.Rotate(0f,turn,0f,Space.Self);
        }
        private void Show(bool on)
        {
            _on=on;
            foreach(GameObject g in _lit)if(g!=null)g.SetActive(on);
            if(_smoke!=null)_smoke.enabled=on;
        }
    }
}
