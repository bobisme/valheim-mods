using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // The recipe picker of an assembler or chemical plant (Shift+E on it): every recipe of its kind researched so far, with what goes in,
    // what comes out and how long it takes in this machine. Click one to set it; E, Escape or walking away closes it.
    internal static class RecipeWindow
    {
        private static RectTransform _window,_list;private static Assembler _for;
        private static float _closedAt=-1f,_openedAt;private static string _shown="";
        private static Craft _pending;private static bool _clear;
        internal static bool IsOpen=>_window!=null;

        internal static void Open(Assembler a)
        {
            if(a==null||Time.unscaledTime<_closedAt+0.25f||!GameUi.Ready())return;
            if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
            _for=a;_openedAt=Time.unscaledTime;_pending=null;_clear=false;
            Build();
        }
        internal static void Close()
        {
            if(_window==null)return;
            Object.Destroy(_window.gameObject);
            _window=null;_list=null;_for=null;_closedAt=Time.unscaledTime;UITooltip.HideTooltip();
        }
        internal static void Tick()
        {
            if(_window==null)return;
            Player p=Player.m_localPlayer;
            bool useAgain=Time.unscaledTime>_openedAt+0.2f&&ZInput.GetButtonDown("Use");
            if(p==null||p.IsDead()||global::Menu.IsVisible()||useAgain||_for==null||Vector3.Distance(p.transform.position,_for.transform.position)>8f){Close();return;}
            if(_pending!=null||_clear){_for.Choose(_clear?null:_pending,p);_pending=null;_clear=false;Close();return;}
            if(Signature()!=_shown)Build();
        }
        private static string Signature()=>_for.Recipe?.Id+"|"+Tech.Level;

        private static void Build()
        {
            float scrolled=_list!=null?_list.anchoredPosition.y:0f;
            if(_window!=null)Object.Destroy(_window.gameObject);
            _shown=Signature();
            Craft[] crafts=Crafts.Unlocked(_for.Kind).ToArray();
            _window=GameUi.Window("FactorheimRecipes",new Vector2(720f,600f));
            GameUi.Label(_window,_for.Title,new Vector2(30f,-18f),new Vector2(500f,44f),32f,GameUi.Warm,GameUi.Norse);
            GameUi.Label(_window,$"Speed {_for.Speed:0.##}×   Power {Machine.Watts(_for.PowerUse)}",new Vector2(30f,-60f),new Vector2(660f,26f),16f,GameUi.Dim);
            GameUi.Braid(_window,new Vector2(0f,-90f),660f);
            RectTransform box=GameUi.Box(_window,new Vector2(24f,-110f),new Vector2(672f,410f));
            _list=GameUi.Scroll(box,8f);
            const float row=58f;
            float y=0f,width=_list.sizeDelta.x;
            if(crafts.Length==0)GameUi.Label(_list,"Nothing researched for this machine yet.",new Vector2(12f,-12f),new Vector2(width-24f,30f),18f,GameUi.Dim);
            foreach(Craft c in crafts)
            {
                bool chosen=_for.Recipe==c;
                Craft pick=c;
                GameUi.Icon(_list,new Vector2(8f,y-6f),new Vector2(44f,44f),Ports.IconOf(c.Out[0].item));
                var b=GameUi.Button(_list,c.Title,new Vector2(58f,y-8f),new Vector2(230f,40f),()=>_pending=pick,true,17f);
                if(chosen)GameUi.Label(b).color=GameUi.Gold;
                float x=300f;
                foreach(var i in c.In)
                {
                    GameUi.Icon(_list,new Vector2(x,y-12f),new Vector2(32f,32f),Ports.IconOf(i.item));
                    GameUi.Label(_list,i.n.ToString(),new Vector2(x+30f,y-18f),new Vector2(30f,24f),16f,GameUi.Text);
                    x+=58f;
                }
                GameUi.Label(_list,$"{c.Seconds/_for.Speed:0.#} s",new Vector2(width-70f,y-18f),new Vector2(64f,24f),16f,GameUi.Dim);
                y-=row;
            }
            _list.sizeDelta=new Vector2(width,-y+8f);
            _list.anchoredPosition=new Vector2(0f,scrolled);
            GameUi.Button(_window,"No recipe",new Vector2(24f,-536f),new Vector2(200f,44f),()=>_clear=true,_for.Recipe!=null);
            GameUi.Label(_window,"Changing the recipe gives back what was in it.",new Vector2(240f,-548f),new Vector2(460f,26f),15f,GameUi.Dim);
        }
    }
}
