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
        private static GUIStyle _style;
        private static Texture2D _white;

        internal static Warstone Near()
        {
            if(Time.time<_next)return _stone;
            _next=Time.time+0.5f;
            Player me=Player.m_localPlayer;
            _stone=me==null?null:Warstone.Loaded.Where(w=>w!=null&&w.Z!=null&&w.Phase!=Phase.Idle&&Vector3.Distance(w.transform.position,me.transform.position)<150)
                .OrderBy(w=>Vector3.Distance(w.transform.position,me.transform.position)).FirstOrDefault();
            return _stone;
        }
        internal static void Draw()
        {
            Warstone stone=Near();
            if(stone==null||stone.Z==null||Hidden())return;
            ZDO z=stone.Z;
            if(_style==null){_style=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=Mathf.RoundToInt(Screen.height/48f),fontStyle=FontStyle.Bold,richText=true};}
            if(_white==null){_white=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};_white.SetPixel(0,0,Color.white);_white.Apply();}
            float w=Screen.width*0.32f,h=Screen.height/30f,x=(Screen.width-w)/2,y=Screen.height*0.075f;
            string text;float fill;Color colour;
            if(stone.Phase==Phase.Gathering)
            {
                double wait=(z.GetLong(Stone.StartKey,0L)-ZNet.instance.GetTime().Ticks)/(double)System.TimeSpan.TicksPerSecond;
                double total=System.Math.Max(1,(z.GetLong(Stone.StartKey,0L)-z.GetLong(Stone.CalledKey,0L))/(double)System.TimeSpan.TicksPerSecond);
                text=$"The horde gathers · {Mathf.Max(0,Mathf.CeilToInt((float)wait))} s";
                fill=Mathf.Clamp01((float)(wait/total));colour=new Color(0.9f,0.55f,0.15f,0.85f);
            }
            else
            {
                var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
                int index=Mathf.Clamp(z.GetInt(Stone.WaveKey,0),0,Mathf.Max(0,plan.Count-1)),wave=index+1;
                int left=plan.Count==0?0:plan.Skip(wave).Sum(u=>u.Count)+Mathf.Max(0,plan[index].Count-z.GetInt(Stone.QueueKey,0)); // still in the rift
                long siege=stone.Siege;
                int alive=Raider.Loaded.Count(r=>r!=null&&r.Siege==siege&&r.Body!=null&&!r.Body.IsDead());
                float health=z.GetFloat(Stone.HealthKey,1),max=Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1));
                fill=Mathf.Clamp01(health/max);
                colour=Color.Lerp(new Color(0.85f,0.1f,0.08f,0.9f),new Color(0.75f,0.65f,0.4f,0.85f),fill);
                text=$"Wave {wave} of {plan.Count} · {alive+left} to go · Warstone {Mathf.RoundToInt(fill*100)}%";
            }
            GUI.color=new Color(0,0,0,0.55f);GUI.DrawTexture(new Rect(x-3,y-3,w+6,h+6),_white);
            GUI.color=colour;GUI.DrawTexture(new Rect(x,y,w*fill,h),_white);
            GUI.color=Color.black;GUI.Label(new Rect(x+1,y+1,w,h),text,_style);
            GUI.color=Color.white;GUI.Label(new Rect(x,y,w,h),text,_style);
        }
        // Not over menus, the map or the inventory.
        private static bool Hidden()=>Menu.IsVisible()||InventoryGui.IsVisible()||Minimap.IsOpen()||Hud.instance==null||Hud.IsUserHidden();

        // ---- the music: the game's own raid music while a siege is near ----
        private static string _music;
        internal static string Music()
        {
            if(_music!=null)return _music;
            var events=RandEventSystem.instance?.m_events;
            _music=events?.Where(e=>e!=null&&!string.IsNullOrEmpty(e.m_forceMusic)).OrderByDescending(e=>e.m_name.StartsWith("army_")).Select(e=>e.m_forceMusic).FirstOrDefault()??"";
            return _music;
        }
        internal static void Clear(){_stone=null;_music=null;if(_white!=null)Object.Destroy(_white);_white=null;_style=null;}
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
