using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Shieldwall
{
    // The war council (Shift+E on an idle Warstone): choose the boon the stone has earned, swear boasts for a harder siege and richer
    // spoils, and sound the horn. Built from the game's own crafting panel: its wood, braid, inset boxes, fonts and Craft button.
    internal static class Council
    {
        private static Warstone _stone;
        private static int _boasts,_escapeFrame=-10;
        private static GameObject _root;private static string _shown;private static float _nextCheck;
        internal static bool IsOpen=>_stone!=null;
        internal static bool ReservesEscape=>Time.frameCount-_escapeFrame<=1;

        internal static void Open(Warstone stone)
        {
            _stone=stone;_boasts=0;_shown=null;
            if(TextViewer.instance!=null&&TextViewer.instance.IsVisible())TextViewer.instance.Hide(); // the saga, if it is still up
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            Build();
        }
        internal static void Close(){_stone=null;if(_root!=null)Object.Destroy(_root);_root=null;_shown=null;}

        // Every frame: close when the stone goes, its siege starts, you walk off or die, or press Escape; redraw when what it shows changes.
        internal static void Tick()
        {
            if(_stone==null)return;
            Player me=Player.m_localPlayer;
            if(_stone.Z==null||me==null||me.IsDead()||_stone.Phase!=Phase.Idle||Vector3.Distance(me.transform.position,_stone.transform.position)>8){Close();return;}
            if(Input.GetKeyDown(KeyCode.Escape)){_escapeFrame=Time.frameCount;Close();return;}
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(Time.time>=_nextCheck){_nextCheck=Time.time+0.5f;if(Signature()!=_shown)Build();}
        }
        private static string Signature()
        {
            ZDO z=_stone.Z;
            return $"{_boasts}|{z.GetString(Stone.OfferKey,"")}|{z.GetString(Stone.BoonsKey,"")}|{Mathf.CeilToInt((float)_stone.CooldownLeft/60)}|{z.GetString(Stone.SagaKey,"").Length}|{_stone.Strength}|{Director.StageNow()}";
        }

        private static void Build()
        {
            if(_stone==null||!Ui.Ready())return;
            if(_root!=null)Object.Destroy(_root);
            _shown=Signature();
            Warstone stone=_stone;ZDO z=stone.Z;
            _root=Ui.Window("ShieldwallCouncil",560);
            Transform w=_root.transform;

            Ui.Title(w,"War council");
            Ui.Text(w,$"The Warstone · {Policy.Title(stone.Marks)}{(stone.Marks>0?" ("+Policy.Numeral(stone.Marks)+")":"")}{(stone.Cracked?" · cracked":"")}",Ui.Body,17,Ui.Muted,TMPro.TextAlignmentOptions.Center);
            Ui.Braid(w);

            // The boon it has earned.
            string[] offer=stone.BoonOffer;
            if(offer.Length>0)
            {
                Ui.Heading(w,"The stone held. Choose a boon for it to keep");
                foreach(string id in offer)
                {
                    Boon b=Policy.BoonOf(id);
                    Transform row=Ui.Inset(w,false);
                    Ui.Button(row,b.Name,()=>Choose(id));
                    Ui.Text(row,b.Text,Ui.Body,17,Color.white);
                }
                Ui.Space(w,6);
            }

            // The horn.
            Ui.Heading(w,"The war horn");
            double cooldown=stone.CooldownLeft;
            if(cooldown>0)Ui.Text(w,$"The stone still rings from the last siege: {Mathf.CeilToInt((float)cooldown/60)} more minutes.",Ui.Body,18,Color.white);
            else
            {
                int strength=stone.Strength,stage=Policy.SiegeStage(Director.StageNow(),strength);
                int players=Player.GetAllPlayers().Count(p=>p!=null&&Vector3.Distance(p.transform.position,stone.transform.position)<80);
                Ui.Text(w,$"A {Policy.Rosters[stage].Name} will gather and march on the stone: {Policy.Waves(strength)} waves, about {Policy.Total(strength,System.Math.Max(1,players))} strong.",Ui.Body,18,Color.white);
                Ui.Text(w,"Swear boasts for a harder siege and more warshards:",Ui.Body,16,Ui.Muted);
                foreach(Boast b in stone.BoastOffer)
                {
                    BoastInfo info=Policy.Boasts.First(x=>x.Id==b);
                    bool on=Policy.Has(_boasts,b);
                    Transform row=Ui.Inset(w,on);
                    Ui.Button(row,on?$"{info.Name}  ·  sworn":info.Name,()=>{_boasts^=1<<(int)b;Build();});
                    Ui.Text(row,$"{info.Text}  <color=#FFB75C>+{Mathf.RoundToInt((float)info.Bonus*100)}% warshards</color>",Ui.Body,17,Color.white);
                }
                if(_boasts!=0)Ui.Text(w,$"Sworn: +{Mathf.RoundToInt((float)Policy.BoastBonus(_boasts)*100)}% warshards if the stone holds.",Ui.Body,18,Ui.Gold,TMPro.TextAlignmentOptions.Center);
                Ui.Space(w,4);
                Ui.Button(w,"Sound the war horn",Horn,46);
            }

            // What it keeps, and its last saga.
            if(stone.Boons.Count>0)Ui.Text(w,"Boons: "+string.Join(", ",stone.Boons.Select(b=>Policy.BoonOf(b).Name)),Ui.Body,16,Ui.Muted);
            if(!string.IsNullOrEmpty(z.GetString(Stone.SagaKey,"")))Ui.Button(w,"Read the saga of the last siege",ReadSaga,36);
            Ui.Text(w,"Esc  Close",Ui.Body,15,Ui.Muted,TMPro.TextAlignmentOptions.Center);
        }

        private static void Choose(string boon)
        {
            Player me=Player.m_localPlayer;Warstone stone=_stone;
            if(me==null||stone==null)return;
            if(!PrivateArea.CheckAccess(stone.transform.position)){me.Message(MessageHud.MessageType.Center,"This stone is warded by another.");return;}
            Net.Choose(stone,boon,me.GetPlayerName());
        }
        private static void Horn(){Warstone stone=_stone;int boasts=_boasts;Close();if(stone!=null)Net.Horn(stone,boasts);}
        private static void ReadSaga(){Warstone stone=_stone;Close();if(stone?.Z!=null)Saga.Show(stone.Z.GetString(Stone.SagaTopicKey,"The last siege"),stone.Z.GetString(Stone.SagaKey,""));}
    }

    // Windows made of the game's own parts, found on the inventory's crafting panel: its wood background, the braid under its title, the
    // dark inset boxes, the recipe list's highlight, its fonts and its Craft button (with the game's click sounds).
    internal static class Ui
    {
        internal static readonly Color Orange=new Color(1f,0.718f,0.361f),Gold=new Color(1f,0.808f,0f),Muted=new Color(0.78f,0.74f,0.66f);
        private static Sprite _wood,_braid,_inset,_highlight;
        internal static TMPro.TMP_FontAsset Norse,Body,Serif;
        private static Button _button;
        internal static bool Ready()
        {
            InventoryGui gui=InventoryGui.instance;
            if(gui==null||gui.m_crafting==null||gui.m_craftButton==null)return false;
            if(_wood!=null&&_button!=null)return true;
            var images=gui.m_crafting.GetComponentsInChildren<Image>(true);
            Sprite Find(string name)=>images.Select(i=>i.sprite).FirstOrDefault(s=>s!=null&&s.name==name);
            _wood=Find("woodpanel_crafting_240");_braid=Find("BraidLineHorisontalMedium");_inset=Find("item_background");
            _highlight=gui.m_recipeElementPrefab?.transform.Find("selected")?.GetComponent<Image>()?.sprite;
            var texts=gui.m_crafting.GetComponentsInChildren<TMPro.TMP_Text>(true);
            TMPro.TMP_FontAsset Font(string name)=>texts.Select(t=>t.font).FirstOrDefault(f=>f!=null&&f.name.StartsWith(name));
            Norse=Font("Valheim-Norsebold");Body=Font("Valheim-AveriaSansLibre");Serif=Font("Valheim-AveriaSerifLibre");
            _button=gui.m_craftButton;
            return _wood!=null;
        }
        internal static GameObject Window(string name,float width)
        {
            Canvas canvas=InventoryGui.instance.GetComponentInParent<Canvas>().rootCanvas;
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter));
            go.transform.SetParent(canvas.transform,false);
            var rt=(RectTransform)go.transform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0.5f,0.5f);rt.sizeDelta=new Vector2(width,100);rt.anchoredPosition=new Vector2(0,40);
            Image bkg=go.GetComponent<Image>();bkg.sprite=_wood;bkg.type=Image.Type.Sliced;bkg.color=Color.white;
            var layout=go.GetComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(34,34,26,30);layout.spacing=8;
            layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            go.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }
        internal static TMPro.TMP_Text Text(Transform parent,string text,TMPro.TMP_FontAsset font,float size,Color color,TMPro.TextAlignmentOptions align=TMPro.TextAlignmentOptions.TopLeft)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI));go.transform.SetParent(parent,false);
            var t=go.GetComponent<TMPro.TextMeshProUGUI>();
            if(font!=null)t.font=font;t.fontSize=size;t.color=color;t.alignment=align;t.text=text;t.richText=true;t.raycastTarget=false;
            t.textWrappingMode=TMPro.TextWrappingModes.Normal;
            return t;
        }
        internal static void Title(Transform parent,string text)=>Text(parent,text,Norse,34,Orange,TMPro.TextAlignmentOptions.Center);
        internal static void Heading(Transform parent,string text){Space(parent,4);Text(parent,text,Serif,22,Gold);}
        internal static void Braid(Transform parent)
        {
            var go=new GameObject("Braid",typeof(RectTransform),typeof(Image),typeof(LayoutElement));go.transform.SetParent(parent,false);
            Image i=go.GetComponent<Image>();i.sprite=_braid;i.color=new Color(0.153f,0.118f,0.071f,1);i.raycastTarget=false;
            go.GetComponent<LayoutElement>().preferredHeight=16;
        }
        internal static void Space(Transform parent,float height)
        {
            var go=new GameObject("Space",typeof(RectTransform),typeof(LayoutElement));go.transform.SetParent(parent,false);
            go.GetComponent<LayoutElement>().preferredHeight=height;
        }
        // A dark inset box, as the crafting panel's description; lit like a chosen recipe when on.
        internal static Transform Inset(Transform parent,bool on)
        {
            var go=new GameObject("Inset",typeof(RectTransform),typeof(Image),typeof(VerticalLayoutGroup));go.transform.SetParent(parent,false);
            Image i=go.GetComponent<Image>();i.raycastTarget=false;
            if(on&&_highlight!=null){i.sprite=_highlight;i.type=Image.Type.Sliced;i.color=new Color(0.757f,0.506f,0.184f,0.55f);}
            else{i.sprite=_inset;i.type=Image.Type.Sliced;i.color=new Color(0,0,0,0.565f);}
            var layout=go.GetComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(12,12,10,12);layout.spacing=6;
            layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            return go.transform;
        }
        // The game's Craft button, with a new label and action.
        internal static Button Button(Transform parent,string label,System.Action onClick,float height=40)
        {
            GameObject go=Object.Instantiate(_button.gameObject,parent,false);go.name="Button";go.SetActive(true);
            // Not the Craft button's own gamepad hint or its "Missing requirement" tooltip.
            foreach(var c in go.GetComponentsInChildren<MonoBehaviour>(true))if(c!=null&&(c.GetType().Name=="UIGamePad"||c.GetType().Name=="UITooltip"))Object.DestroyImmediate(c);
            foreach(Transform t in go.transform.Cast<Transform>().ToList())if(t.name.StartsWith("gamepad"))Object.Destroy(t.gameObject);
            Button b=go.GetComponent<Button>();
            b.onClick=new Button.ButtonClickedEvent();b.onClick.AddListener(()=>onClick());b.interactable=true;
            var text=go.GetComponentInChildren<TMPro.TMP_Text>(true);if(text!=null){text.text=label;text.textWrappingMode=TMPro.TextWrappingModes.NoWrap;text.color=new Color(0.96f,0.93f,0.86f);text.fontSize=Mathf.Min(text.fontSize,height>=44?26:22);}
            var size=go.AddComponent<LayoutElement>();size.preferredHeight=height;size.minHeight=height;
            return b;
        }
    }

    // While the council is open the game takes no input: no walking, looking, swinging or scrolling, and Escape closes the window only.
    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class CouncilPlayerInput
    {
        private static void Postfix(Player __instance,ref bool __result){if(Council.IsOpen&&__instance==Player.m_localPlayer)__result=false;}
    }
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class CouncilControllerInput
    {
        private static void Postfix(ref bool __result){if(Council.IsOpen)__result=false;}
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    internal static class CouncilMouse
    {
        private static bool Prefix(){if(!Council.IsOpen)return true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return false;}
    }
    [HarmonyPatch(typeof(ZInput),nameof(ZInput.GetMouseScrollWheel))]
    internal static class CouncilScroll
    {
        private static void Postfix(ref float __result){if(Council.IsOpen)__result=0;}
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class CouncilAttack
    {
        private static bool Prefix(Humanoid __instance,ref bool __result){if(!Council.IsOpen||__instance!=Player.m_localPlayer)return true;__result=false;return false;}
    }
    [HarmonyPatch(typeof(Menu),"Update")]
    internal static class CouncilEscape
    {
        private static bool Prefix()=>!Council.IsOpen&&!Council.ReservesEscape;
    }
}
