using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // A pitch pit: a ring of stones round a pool of black pitch, built near a Warstone. Raiders who wade in are slowed (the game's own
    // tar); set it alight with fire (a fire shot from a stave or staff landing near it, a fire arrow) and it burns everything
    // of the horde standing in it, then must seep full again before it can burn once more.
    internal sealed class PitchPit:MonoBehaviour,Hoverable
    {
        internal const string PrefabName="BobPitchPit",LitKey="bob_sw_lit",SpentKey="bob_sw_spent";
        internal static readonly List<PitchPit> Loaded=new List<PitchPit>();
        internal static readonly Dictionary<Character,float> Burned=new Dictionary<Character,float>(); // for the saga: who the fire took
        private const string IgniteRpc="bob_sw_ignite";
        internal static float Radius=2f;
        private static readonly int Tared="Tared".GetStableHashCode();
        private ZNetView _view;private GameObject _fire;private float _next;

        private void Awake()
        {
            _view=GetComponent<ZNetView>();
            if(_view==null||!_view.IsValid())return;
            Loaded.Add(this);
            _view.Register(IgniteRpc,_=>Ignite());
            _fire=transform.Find("PitchFire")?.gameObject;
        }
        private void OnDestroy(){Loaded.Remove(this);if(_view!=null&&_view.IsValid())_view.Unregister(IgniteRpc);}
        internal static void DetachAll(){foreach(PitchPit p in Loaded.ToList())if(p!=null)Object.Destroy(p);Loaded.Clear();}
        internal static void AttachAll()
        {
            foreach(ZNetView view in Assets.Instances(PrefabName.GetStableHashCode()))
            {
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(PitchPit)&&stale.GetType()!=typeof(PitchPit))Object.DestroyImmediate(stale);
                if(view.GetComponent<PitchPit>()==null)view.gameObject.AddComponent<PitchPit>();
            }
        }

        private static long Now=>ZNet.instance.GetTime().Ticks;
        private bool Burning=>_view.GetZDO().GetLong(LitKey,0L)>Now;
        private bool Spent=>!Burning&&_view.GetZDO().GetLong(SpentKey,0L)>Now;

        // Fire near it: anyone may light it; the game that owns it keeps the time.
        internal static void IgniteNear(Vector3 at,float range)
        {
            foreach(PitchPit pit in Loaded)
                if(pit!=null&&Vector3.Distance(pit.transform.position,at)<=range+Radius)pit.Light();
        }
        internal void Light(){if(_view!=null&&_view.IsValid())_view.InvokeRPC(IgniteRpc);}
        private void Ignite()
        {
            if(!_view.IsOwner()||Burning||Spent)return;
            ZDO z=_view.GetZDO();
            z.Set(LitKey,Now+(long)(Policy.PitBurns*System.TimeSpan.TicksPerSecond));
            z.Set(SpentKey,Now+(long)((Policy.PitBurns+Policy.PitSeeps)*System.TimeSpan.TicksPerSecond));
            Assets.Effect("vfx_FireballHit",transform.position+Vector3.up*0.3f);
        }

        private void Update()
        {
            if(_view==null||!_view.IsValid())return;
            bool burning=Burning;
            if(_fire!=null&&_fire.activeSelf!=burning)_fire.SetActive(burning);
            if(!_view.IsOwner()||Time.time<_next)return;
            _next=Time.time+1;
            if(Spent)return;
            // The horde wading in: slowed, and burned while it is alight.
            foreach(Raider r in Raider.Loaded)
            {
                if(r==null||r.Body==null||r.Body.IsDead()||r.Body.IsFlying())continue;
                Vector3 d=r.transform.position-transform.position;
                if(new Vector2(d.x,d.z).magnitude>Radius||Mathf.Abs(d.y)>1.5f)continue;
                r.Body.GetSEMan().AddStatusEffect(Tared,true);
                if(!burning)continue;
                var hit=new HitData{m_point=r.Body.GetCenterPoint(),m_hitType=HitData.HitType.Burning};
                hit.m_damage.m_fire=Policy.PitFire;
                Burned[r.Body]=Time.time;
                r.Body.Damage(hit);
            }
        }

        public string GetHoverText()
        {
            if(_view==null||!_view.IsValid())return "";
            string state=Burning?"<color=#FF7050>Burning</color>":Spent?"<color=#A0A0A0>Burnt out: the pitch seeps back</color>":"Full of pitch: slows raiders who wade in. Fire sets it alight (a fire shot from a stave or staff landing near it, a fire arrow).";
            return Localization.instance.Localize($"Pitch pit\n{state}");
        }
        public string GetHoverName()=>"Pitch pit";
        public float GetHoverOffset()=>0.5f;
    }

    // Fire struck on a pitch pit lights it, and does it no harm.
    [HarmonyPatch(typeof(WearNTear),nameof(WearNTear.Damage))]
    internal static class PitchCatches
    {
        private static bool Prefix(WearNTear __instance,HitData hit)
        {
            if(hit==null||!(__instance.GetComponent<PitchPit>() is PitchPit pit))return true;
            if(hit.m_damage.m_fire>0){pit.Light();return false;}
            return true;
        }
    }
}
