using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delivery
{
    // The horn's menu: every meal you can order, with what it fills and its price. "Blow" sounds that meal's call (one portion each
    // time); "That's all!" blows the end call and the order goes out.
    internal static class OrderMenu
    {
        private static RectTransform _window,_list;
        private static string _shown="";
        private static float _openedAt,_closedAt=-1;
        private static readonly List<KeyValuePair<Meal,int>> Calling=new List<KeyValuePair<Meal,int>>();
        private static Meal _blow;
        private static bool _send,_clear;
        internal static bool IsOpen=>_window!=null;
        internal static int Portions=>Calling.Sum(kv=>kv.Value);
        internal static IEnumerable<KeyValuePair<Meal,int>> Called=>Calling;
        internal static void Press(Meal m)=>_blow=m;
        internal static void PressSend()=>_send=true;

        internal static void Open()
        {
            if(Time.unscaledTime<_closedAt+0.25f||!GameUi.Ready())return;
            Player me=Player.m_localPlayer;
            if(me==null)return;
            if(me.InInterior()){me.Message(MessageHud.MessageType.Center,"Down here, no one would hear the horn.");return;}
            GameUi.IsOpen=()=>OrderMenu.IsOpen||Tip.IsOpen;
            GameUi_EscapeCloses.Close=()=>{if(Tip.IsOpen)Tip.Choose(0);else Close();};
            if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
            _openedAt=Time.unscaledTime;
            Build();
        }
        internal static void Close()
        {
            if(_window==null){Calling.Clear();return;}
            Object.Destroy(_window.gameObject);
            _window=null;_closedAt=Time.unscaledTime;UITooltip.HideTooltip(); // (only when it was open: hiding the game's tooltip otherwise kills it)
            if(Calling.Count>0&&Run.Now==Run.Stage.None)Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"You lower the horn without the end call: no one comes.");
            Calling.Clear();
        }

        internal static void Tick()
        {
            if(_window==null)return;
            Player me=Player.m_localPlayer;
            bool useAgain=Time.unscaledTime>_openedAt+0.2f&&ZInput.GetButtonDown("Use");
            if(me==null||me.IsDead()||global::Menu.IsVisible()||useAgain){Close();return;}
            if(_blow!=null){Meal m=_blow;_blow=null;Blow(me,m);}
            if(_clear){_clear=false;Calling.Clear();}
            if(_send)
            {
                _send=false;
                if(Run.Send(me,Calling.ToList())){Calling.Clear();Close();return;}
            }
            if(Signature(me)!=_shown)Build();
        }
        private static void Blow(Player me,Meal m)
        {
            if(Run.Now!=Run.Stage.None||Portions>=Policy.MaxPortions)return;
            int at=Calling.FindIndex(kv=>kv.Key==m);
            if(at<0)Calling.Add(new KeyValuePair<Meal,int>(m,1));
            else Calling[at]=new KeyValuePair<Meal,int>(m,Calling[at].Value+1);
            Calls.Blow(m.Tune);
        }

        private static int Coins(Player me)=>me.GetInventory().CountItems("$item_coins");
        private static string Signature(Player me)
        {
            var (fee,why)=Run.FeeAt(me.transform.position);
            return $"{Run.Now}|{Coins(me)}|{fee}|{why}|{string.Join(",",Calling.Select(kv=>kv.Key.Prefab+kv.Value))}|{(int)Run.SetsOutIn/5}|{Meals.For(me).Count}";
        }

        private static void Build()
        {
            Player me=Player.m_localPlayer;
            if(me==null)return;
            float scrolled=_list!=null?_list.anchoredPosition.y:0f;
            if(_window!=null)Object.Destroy(_window.gameObject);
            _shown=Signature(me);
            Courier c=Run.Info(me);
            _window=GameUi.Window("DeliveryMenu",new Vector2(900,640));
            GameUi.Label(_window,"Errand Horn",new Vector2(30,-18),new Vector2(400,44),34,GameUi.Warm,GameUi.Norse);
            GameUi.Label(_window,$"{c.Name} runs for you · <color=#ffd27a>{Policy.RegardName(c.Regard)}</color>{(c.Runs>0?$" · {c.Runs} run{(c.Runs==1?"":"s")}":"")}",
                new Vector2(430,-30),new Vector2(440,30),17,GameUi.Dim,null,TextAlignmentOptions.TopRight);
            GameUi.Braid(_window,new Vector2(0,-74),840);

            List<Meal> meals=Meals.For(me);
            int coins=Coins(me);
            var (fee,why)=Run.FeeAt(me.transform.position);
            int food=Calling.Sum(kv=>kv.Key.Price*kv.Value);
            bool open=Run.Now==Run.Stage.None;

            RectTransform box=GameUi.Box(_window,new Vector2(24,-96),new Vector2(560,520));
            _list=GameUi.Scroll(box,8);
            const float row=62;
            float w=_list.sizeDelta.x;
            if(meals.Count==0)
                GameUi.Label(_list,"You don't know any cooked meals yet. Cook something (or pick one up), and its call is yours.",new Vector2(12,-12),new Vector2(w-24,80),17,GameUi.Dim);
            for(int i=0;i<meals.Count;i++)
            {
                Meal m=meals[i];
                float y=-i*row;
                RectTransform r=GameUi.Rect(_list,"Meal",new Vector2(0,1),new Vector2(0,1),new Vector2(0,y),new Vector2(w,row));
                if(i%2==0)GameUi.Plain(GameUi.Stretch(r,"Shade",-2,-2,-2,-2),new Color(0,0,0,0.12f));
                GameUi.Icon(r,new Vector2(8,-7),new Vector2(48,48),m.Data.GetIcon());
                GameUi.Label(r,m.Name,new Vector2(64,-8),new Vector2(320,24),18,GameUi.Text);
                GameUi.Label(r,m.Stats,new Vector2(64,-33),new Vector2(320,22),14,GameUi.Dim);
                bool afford=food+m.Price+fee<=coins||Run.Free;
                GameUi.Label(r,$"{m.Price}",new Vector2(394,-18),new Vector2(36,26),18,afford?GameUi.Gold:GameUi.Warn,null,TextAlignmentOptions.TopRight);
                GameUi.Chip(r,new Vector2(432,-16),ObjectDB.instance.GetItemPrefab("Coins")?.GetComponent<ItemDrop>()?.m_itemData,"",24,14);
                Meal pick=m;
                GameUi.Button(r,"Blow",new Vector2(w-84,-11),new Vector2(78,40),()=>_blow=pick,open&&afford&&Portions<Policy.MaxPortions,17);
            }
            _list.sizeDelta=new Vector2(w,Mathf.Max(1,meals.Count)*row);
            _list.anchoredPosition=new Vector2(0,scrolled);

            // the order, or the courier's state
            RectTransform side=GameUi.Box(_window,new Vector2(600,-96),new Vector2(276,520));
            float sy=-14;
            TMP_Text Line(string text,float size,Color color,float height=24)
            {
                TMP_Text t=GameUi.Label(side,text,new Vector2(14,sy),new Vector2(248,height),size,color);
                float h=t.GetPreferredValues(text,248,0).y;
                sy-=Mathf.Max(h,size)+6;
                return t;
            }
            if(open)
            {
                Line("Your order",22,GameUi.Warm);
                if(Calling.Count==0)Line("Blow a meal's call for each portion you want (up to ten). Then the end call: that's all!",15,GameUi.Dim);
                foreach(var kv in Calling)Line($"{kv.Value}× {kv.Key.Name} <color=#bdb7a9>({kv.Key.Price*kv.Value})</color>",16,GameUi.Text);
                sy-=6;
                Line($"Food: {food} coins",16,GameUi.Text);
                Line($"Run: {fee} coins <color=#bdb7a9>({why})</color>",16,GameUi.Text);
                Line($"Total: <color=#ffd27a>{food+fee}</color> · you have {coins}",17,food+fee<=coins?GameUi.Text:GameUi.Warn);
                sy-=4;
                Line($"Paid when he gets here. {c.Name} sets out in about {Mathf.RoundToInt(Policy.Wait(Plugin.WaitSeconds.Value,c.Regard))} s; tips make him quicker.",14,GameUi.Dim);
                GameUi.Button(side,"That's all! (end call)",new Vector2(14,-418),new Vector2(248,44),()=>_send=true,Calling.Count>0&&(food+fee<=coins||Run.Free),17);
                GameUi.Button(side,"Start over",new Vector2(14,-466),new Vector2(248,40),()=>_clear=true,Calling.Count>0,16);
            }
            else
            {
                Line("On the way",22,GameUi.Warm);
                foreach(var kv in Run.Order)Line($"{kv.Value}× {kv.Key.Name}",16,GameUi.Text);
                sy-=6;
                Line($"Total: <color=#ffd27a>{Run.Total}</color> coins · you have {coins}",17,Run.Total<=coins?GameUi.Text:GameUi.Warn);
                string state=Run.Now==Run.Stage.Waiting?$"{c.Name} sets out in about {Mathf.CeilToInt(Run.SetsOutIn/5f)*5} s.":
                    Run.Now==Run.Stage.Coming?$"{c.Name} is running your way.":$"{c.Name} is here.";
                Line(state,16,GameUi.Text);
                Line("One order at a time: he has only two hands.",14,GameUi.Dim);
            }
        }
    }

    // When the food is handed over: tip him? (Nothing, or about a tenth, a fifth or half of the order.) Unanswered, it's nothing.
    internal static class Tip
    {
        private static RectTransform _window;
        private static int _chosen=-1;
        internal static bool IsOpen=>_window!=null;

        internal static void Open(string name,int total)
        {
            Close();
            if(!GameUi.Ready())return;
            Player me=Player.m_localPlayer;
            if(me==null)return;
            GameUi.IsOpen=()=>OrderMenu.IsOpen||Tip.IsOpen;
            GameUi_EscapeCloses.Close=()=>{if(Tip.IsOpen)Choose(0);else OrderMenu.Close();};
            if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
            int coins=me.GetInventory().CountItems("$item_coins");
            _window=GameUi.Window("DeliveryTip",new Vector2(560,250));
            _window.anchorMin=_window.anchorMax=_window.pivot=new Vector2(0.5f,0f);_window.anchoredPosition=new Vector2(0,120); // (low on the screen: he's standing in front of you)
            GameUi.Label(_window,$"Tip {name}?",new Vector2(30,-18),new Vector2(500,44),32,GameUi.Warm,GameUi.Norse);
            GameUi.Braid(_window,new Vector2(0,-70),500);
            GameUi.Label(_window,$"The order came to {total} coins; you have {coins} left. A tip makes him come quicker next time. No tip, and he takes his time.",
                new Vector2(30,-86),new Vector2(500,60),16,GameUi.Text);
            int[] tips=Policy.TipChoices(total);
            GameUi.Button(_window,"No tip",new Vector2(30,-170),new Vector2(110,48),()=>_chosen=0,true,17);
            for(int i=0;i<tips.Length;i++)
            {
                int t=tips[i];
                GameUi.Button(_window,t==1?"1 coin":$"{t} coins",new Vector2(160+i*126,-170),new Vector2(114,48),()=>_chosen=t,t<=coins,17);
            }
        }
        internal static void Choose(int tip)=>_chosen=tip;
        internal static void Tick()
        {
            if(_chosen<0)return;
            int t=_chosen;_chosen=-1;
            Close();
            Run.Tipped(t);
        }
        internal static void Close()
        {
            if(_window==null)return;
            Object.Destroy(_window.gameObject);
            _window=null;UITooltip.HideTooltip();
        }
    }
}
