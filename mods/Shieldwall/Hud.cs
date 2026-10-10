using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Shieldwall
{
    // A bar across the top of the screen while you are near a siege: the countdown while the horde gathers, then the wave, how many
    // are left and how the stone is holding. And the game's own raid music.
    internal static class SiegeBar
    {
        private static Warstone _stone;
        private static float _next;

        internal static Warstone Near()
        {
            if(Time.time<_next)return _stone;
            _next=Time.time+0.5f;
            Player me=Player.m_localPlayer;
            _stone=me==null?null:Warstone.Loaded.Where(w=>w!=null&&w.Z!=null&&w.Phase!=Phase.Idle&&Vector3.Distance(w.transform.position,me.transform.position)<100)
                .OrderBy(w=>Vector3.Distance(w.transform.position,me.transform.position)).FirstOrDefault();
            return _stone;
        }
        // The game's own boss bar (the one Eikthyr and the others get at the top of the screen), named for the siege.
        private static GameObject _bar;private static TMPro.TextMeshProUGUI _name;private static GuiBar _fast,_slow;
        private static bool Hidden()=>Menu.IsVisible()||Minimap.IsOpen()||Hud.instance==null||Hud.IsUserHidden();
        internal static void Tick()
        {
            Warstone stone=Near();
            bool show=stone!=null&&stone.Z!=null&&!Hidden();
            if(!show){if(_bar!=null&&_bar.activeSelf)_bar.SetActive(false);return;}
            if(_bar==null&&!Make())return;
            ZDO z=stone.Z;string text;float fill;
            if(stone.Phase==Phase.Gathering)
            {
                double wait=(z.GetLong(Stone.StartKey,0L)-ZNet.instance.GetTime().Ticks)/(double)System.TimeSpan.TicksPerSecond;
                double total=System.Math.Max(1,(z.GetLong(Stone.StartKey,0L)-z.GetLong(Stone.CalledKey,0L))/(double)System.TimeSpan.TicksPerSecond);
                text=$"The horde gathers · {Mathf.Max(0,Mathf.CeilToInt((float)wait))} s";
                fill=Mathf.Clamp01((float)(wait/total));
            }
            else
            {
                var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
                int index=Mathf.Clamp(z.GetInt(Stone.WaveKey,0),0,Mathf.Max(0,plan.Count-1)),wave=index+1;
                int left=plan.Count==0?0:plan.Skip(wave).Sum(u=>u.Count)+Mathf.Max(0,plan[index].Count-z.GetInt(Stone.QueueKey,0)); // still in the rift
                long siege=stone.Siege;
                int alive=Raider.Loaded.Count(r=>r!=null&&r.Siege==siege&&r.Body!=null&&!r.Body.IsDead());
                fill=Mathf.Clamp01(z.GetFloat(Stone.HealthKey,1)/Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1)));
                text=$"Warstone · wave {wave} of {plan.Count} · {alive+left} to go";
            }
            if(!_bar.activeSelf)_bar.SetActive(true);
            if(_name.text!=text)_name.text=text;
            _fast.SetValue(fill);_slow.SetValue(fill);
        }
        private static bool Make()
        {
            EnemyHud hud=EnemyHud.instance;
            if(hud==null||hud.m_baseHudBoss==null)return false;
            _bar=Object.Instantiate(hud.m_baseHudBoss,hud.m_hudRoot.transform);
            _bar.name="ShieldwallSiegeBar";
            foreach(string part in new[]{"level_2","level_3","Alerted","Aware","Health/health_fast_friendly"})
            {Transform t=_bar.transform.Find(part);if(t!=null)t.gameObject.SetActive(false);}
            _name=_bar.transform.Find("Name")?.GetComponent<TMPro.TextMeshProUGUI>();
            _fast=_bar.transform.Find("Health/health_fast")?.GetComponent<GuiBar>();
            _slow=_bar.transform.Find("Health/health_slow")?.GetComponent<GuiBar>();
            if(_name==null||_fast==null||_slow==null){Object.Destroy(_bar);_bar=null;return false;}
            _bar.SetActive(true);
            return true;
        }
        // ---- the music: the game's own raid music while a siege is near ----
        private static string _music;
        internal static string Music()
        {
            if(_music!=null)return _music;
            var events=RandEventSystem.instance?.m_events;
            _music=events?.Where(e=>e!=null&&!string.IsNullOrEmpty(e.m_forceMusic)).OrderByDescending(e=>e.m_name.StartsWith("army_")).Select(e=>e.m_forceMusic).FirstOrDefault()??"";
            return _music;
        }
        internal static void Clear(){_stone=null;_music=null;if(_bar!=null)Object.Destroy(_bar);_bar=null;}
    }

    [HarmonyPatch(typeof(RandEventSystem),nameof(RandEventSystem.GetMusicOverride))]
    internal static class SiegeMusic
    {
        private static void Postfix(ref string __result)
        {
            if(__result!=null||Plugin.Instance==null||SiegeBar.Near()==null)return;
            string music=SiegeBar.Music();
            if(!string.IsNullOrEmpty(music))__result=music;
        }
    }
}
