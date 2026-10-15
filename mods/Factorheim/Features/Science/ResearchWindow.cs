using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // The research window (Shift+E on a rune lab): every tier, what it costs in rune packs, how far it has got, and what it unlocks.
    internal static class ResearchWindow
    {
        private static RectTransform _window,_list;
        private static RuneLab _lab;
        private static string _shown="";
        private static float _closedAt=-1f,_openedAt,_checkAt;
        internal static bool IsOpen=>_window!=null;

        internal static void Open(RuneLab lab)
        {
            if(Time.unscaledTime<_closedAt+0.25f||!GameUi.Ready())return;
            if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
            _lab=lab;_openedAt=Time.unscaledTime;
            Build();
        }
        internal static void Close()
        {
            if(_window==null)return;   // (closed already: HideTooltip would hide the inventory's tooltips too)
            Object.Destroy(_window.gameObject);
            _window=null;_list=null;_lab=null;_closedAt=Time.unscaledTime;UITooltip.HideTooltip();
        }
        internal static void Tick()
        {
            if(_window==null)return;
            Player p=Player.m_localPlayer;
            bool useAgain=Time.unscaledTime>_openedAt+0.2f&&ZInput.GetButtonDown("Use");
            if(p==null||p.IsDead()||global::Menu.IsVisible()||useAgain||_lab==null||Vector3.Distance(p.transform.position,_lab.transform.position)>8f){Close();return;}
            if(Time.unscaledTime<_checkAt)return;
            _checkAt=Time.unscaledTime+0.5f;
            if(Signature()!=_shown)Build();
        }
        private static string Signature()
        {
            int tier=Research.Current;
            return Tech.Level+"/"+tier+"/"+(tier>0?Research.Progress(tier):0);
        }

        private static void Build()
        {
            float scrolled=_list!=null?_list.anchoredPosition.y:0f;
            if(_window!=null)Object.Destroy(_window.gameObject);
            _shown=Signature();
            var size=new Vector2(940f,680f);
            _window=GameUi.Window("FactorheimResearch",size);
            GameUi.Label(_window,"Research",new Vector2(30f,-18f),new Vector2(400f,44f),34f,GameUi.Warm,GameUi.Norse);
            int current=Research.Current;
            string head=current==0?"Every tier is researched. The silo awaits.":$"Now: {Tech.Names[current]}  ({Research.Progress(current)} of {Research.Cost(current)} units)";
            GameUi.Label(_window,head,new Vector2(430f,-28f),new Vector2(480f,30f),18f,GameUi.Text,null,TextAlignmentOptions.TopRight);
            GameUi.Braid(_window,new Vector2(0f,-72f),880f);
            RectTransform box=GameUi.Box(_window,new Vector2(24f,-96f),new Vector2(size.x-48f,size.y-120f));
            _list=GameUi.Scroll(box,8f);
            float width=_list.sizeDelta.x>10f?_list.sizeDelta.x:size.x-80f;
            float y=-6f;
            for(int tier=0;tier<=Tech.Top;tier++)y-=Row(tier,y,width-12f)+10f;
            _list.sizeDelta=new Vector2(_list.sizeDelta.x,-y);
            _list.anchoredPosition=new Vector2(0f,scrolled);
        }

        // One tier: name and state, its packs and progress, and what it unlocks. Returns its height.
        private static float Row(int tier,float y,float width)
        {
            bool done=Tech.Unlocked(tier),now=tier==Research.Current;
            Color state=done?GameUi.Good:now?GameUi.Gold:GameUi.Dim;
            float x=10f;
            GameUi.Label(_list,"Tier "+tier,new Vector2(x,y),new Vector2(70f,28f),17f,GameUi.Dim);
            GameUi.Label(_list,Tech.Names[tier],new Vector2(x+70f,y-2f),new Vector2(330f,32f),24f,state,GameUi.Norse);
            GameUi.Label(_list,done?(tier==0?"known":"researched"):now?"researching":"locked",new Vector2(width-160f,y),new Vector2(160f,28f),17f,state,null,TextAlignmentOptions.TopRight);
            float top=y;y-=34f;
            if(tier>0)
            {
                float px=x+70f;
                foreach(string pack in Research.PacksFor(tier))
                {
                    var icon=GameUi.Icon(_list,new Vector2(px,y),new Vector2(28f,28f),Ports.IconOf(pack));
                    GameUi.Tip(icon,Ports.Title(pack),"One per unit of research.");
                    icon.raycastTarget=true;
                    px+=32f;
                }
                GameUi.Label(_list,$"x {Research.Cost(tier)} units",new Vector2(px+6f,y+2f),new Vector2(150f,28f),17f,GameUi.Text);
                float frac=done?1f:Research.Fraction(tier);
                GameUi.Bar(_list,new Vector2(px+160f,y),new Vector2(width-(px+160f)-10f,26f),frac,done?GameUi.Good:new Color(0.55f,0.4f,1f),
                    done?"done":$"{Research.Progress(tier)} / {Research.Cost(tier)}");
                y-=34f;
            }
            string unlocks=Unlocks(tier);
            TMP_Text text=GameUi.Label(_list,unlocks,new Vector2(x+70f,y),new Vector2(width-x-80f,20f),16f,GameUi.Text);
            float h=Mathf.Max(20f,text.GetPreferredValues(unlocks,width-x-80f,0f).y);
            ((RectTransform)text.transform).sizeDelta=new Vector2(width-x-80f,h);
            y-=h;
            return top-y;
        }

        // What a tier brings: its pieces, the things made by hand, and the machines' new recipes.
        internal static string Unlocks(int tier)
        {
            var parts=new List<string>();
            var pieces=Pieces.All.Where(d=>d.Tier==tier).Select(d=>d.Name).ToList();
            if(pieces.Count>0)parts.Add("<color=#ffd27a>Build:</color> "+string.Join(", ",pieces));
            var hand=Things.All.Where(d=>d.Tier==tier&&!string.IsNullOrWhiteSpace(d.CCost?.Value??d.Cost)).Select(d=>d.Name).ToList();
            if(hand.Count>0)parts.Add("<color=#ffd27a>Craft:</color> "+string.Join(", ",hand));
            var crafts=Crafts.All.Where(c=>c.Tier==tier).GroupBy(c=>c.Kind).Select(g=>Kind(g.Key)+": "+string.Join(", ",g.Select(c=>c.Title))).ToList();
            if(crafts.Count>0)parts.Add("<color=#ffd27a>Recipes:</color> "+string.Join("; ",crafts));
            return parts.Count>0?string.Join("\n",parts):"(nothing yet)";
        }
        private static string Kind(string k)=>k=="assembler"?"assembler":k=="chemical"?"chemical plant":k=="silo"?"rocket silo":k;
    }
}
