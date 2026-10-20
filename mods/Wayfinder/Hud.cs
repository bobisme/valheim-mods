using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wayfinder
{
    // On screen: the compass band across the top (with a sun-compass), and the navigator's notes under the minimap: the course's
    // next leg, the wind for it, the lead's sounding, the primstav's weather. Both hang under the game's HUD, so they hide with it.
    internal static class NavHud
    {
        private static RectTransform _root,_strip,_notes,_centre;
        private static Image _stripBack,_notesBack;
        private static TextMeshProUGUI _heading,_text;
        private static readonly List<Image> Ticks=new List<Image>();
        private static readonly List<TextMeshProUGUI> Letters=new List<TextMeshProUGUI>();
        private static readonly List<(Image mark,TextMeshProUGUI label)> Marks=new List<(Image,TextMeshProUGUI)>();
        private static Sprite _band,_diamond,_white;
        private static readonly List<Object> Owned=new List<Object>();
        private static TMP_FontAsset _font;
        private const float Width=620,Height=40,Span=170; // the band shows 170 degrees across
        private static float _wobble,_finishedAt=-100;
        private static int _seenNext=-1;

        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            if(Hud.instance==null||me==null||!Plugin.ShowHud.Value){Show(false);return;}
            if(_root==null||_root.parent!=Hud.instance.m_rootObject.transform)Build();
            if(_root==null)return;
            Show(true);
            Strip(me);
            Notes(me);
        }
        private static void Show(bool on){if(_root!=null&&_root.gameObject.activeSelf!=on)_root.gameObject.SetActive(on);}

        private static void Strip(Player me)
        {
            bool compass=Items.Has(me,Items.SunCompass);
            if(_strip.gameObject.activeSelf!=compass)_strip.gameObject.SetActive(compass);
            if(!compass||GameCamera.instance==null)return;
            Policy.Sky sky=Weather.SkyNow(me,Items.Has(me,Items.Sunstone));
            Vector3 f=GameCamera.instance.transform.forward;
            float heading=(float)Policy.Bearing(f.x,f.z);
            bool sure=sky==Policy.Sky.Sun||sky==Policy.Sky.Sunstone||sky==Policy.Sky.Star;
            // without the sun or the stars, the reckoning drifts
            float target=sure?0:Mathf.PerlinNoise(Time.time*0.15f,7.3f)*120-60;
            _wobble=Mathf.Lerp(_wobble,target,1-Mathf.Exp(-Time.deltaTime*(sure?6:0.6f)));
            float shown=heading+_wobble;
            float alpha=sky==Policy.Sky.Dark?0.25f:sure?1:0.45f;
            _stripBack.color=new Color(1,1,1,0.85f);
            int t=0,l=0;
            for(int deg=0;deg<360;deg+=15)
            {
                float x=(float)Policy.Turn(shown,deg)/Span*Width;
                if(Mathf.Abs(x)>Width/2-14)continue;
                float fade=alpha*Mathf.Clamp01(1.2f-Mathf.Abs(x)/(Width/2));
                bool cardinal=deg%90==0,inter=deg%45==0;
                Image tick=Pool(Ticks,t++,_strip,()=>NewImage(_strip,_white,"Tick"));
                tick.rectTransform.anchoredPosition=new Vector2(x,inter?-11:-13);
                tick.rectTransform.sizeDelta=new Vector2(cardinal?3:2,inter?12:7);
                tick.color=new Color(0.95f,0.88f,0.72f,fade);
                if(inter&&sky!=Policy.Sky.Dark)
                {
                    TextMeshProUGUI letter=Pool(Letters,l++,_strip,()=>NewText(_strip,"Letter",cardinal?22:16,TextAlignmentOptions.Center));
                    letter.text=Policy.Point(deg);
                    letter.fontSize=cardinal?22:15;
                    letter.rectTransform.anchoredPosition=new Vector2(x,4);
                    letter.color=deg==0?new Color(1f,0.45f,0.3f,fade):new Color(1f,0.94f,0.82f,fade);
                }
            }
            Hide(Ticks,t);Hide(Letters,l);
            // marks: the next waypoint, the helmsman's, the raven's flight, home
            int m=0;
            Vector3 here=me.transform.position;
            if(sky!=Policy.Sky.Dark)
            {
                if(Course.Active)Mark(ref m,shown,Course.At(Course.Next)-here,MapDraw.Ahead,(Course.Next+1).ToString(),alpha);
                var helm=Ships.HelmNext;
                if(helm.HasValue)Mark(ref m,shown,helm.Value-here,MapDraw.Helm,"helm",alpha);
                if(Raven.Heading.HasValue)Mark(ref m,shown,Raven.Heading.Value-here,new Color(0.2f,0.2f,0.25f,1),"raven",alpha);
                Vector3? home=Home();
                if(home.HasValue&&(home.Value-here).sqrMagnitude>60*60)Mark(ref m,shown,home.Value-here,new Color(0.6f,0.95f,0.6f,1),"home",alpha);
            }
            for(int i=m;i<Marks.Count;i++)if(Marks[i].mark.gameObject.activeSelf){Marks[i].mark.gameObject.SetActive(false);}
            string head=sure?$"{Mathf.RoundToInt(heading)%360}° {Policy.Point(heading)}":sky==Policy.Sky.Dark?"":"~"+Policy.Point(shown);
            _heading.color=new Color(1f,0.92f,0.75f,sure?1:0.6f);
            string by;
            switch(sky)
            {
                case Policy.Sky.Sun:by="<color=#ffd77a>by the sun</color>";break;
                case Policy.Sky.Sunstone:by="<color=#bfe6ff>by the sunstone</color>";break;
                case Policy.Sky.Star:by="<color=#d8d8ff>by the lodestar</color>";break;
                case Policy.Sky.Hidden:by="<color=#a09a90>sun hidden: dead reckoning</color>";break;
                default:by="<color=#80786c>no sky to read</color>";break;
            }
            _heading.text=head==""?by:head+"  <size=80%>"+by+"</size>";
        }
        private static void Mark(ref int m,float shown,Vector3 toward,Color colour,string label,float alpha)
        {
            float b=(float)Policy.Bearing(toward.x,toward.z);
            float x=(float)Policy.Turn(shown,b)/Span*Width;
            bool edge=Mathf.Abs(x)>Width/2-12;
            x=Mathf.Clamp(x,-Width/2+12,Width/2-12);
            if(m>=Marks.Count)
            {
                Image img=NewImage(_strip,_diamond,"Mark");
                TextMeshProUGUI txt=NewText(img.rectTransform,"Label",13,TextAlignmentOptions.Center);
                txt.rectTransform.anchoredPosition=new Vector2(0,-16);
                Marks.Add((img,txt));
            }
            var (mark,text)=Marks[m++];
            mark.gameObject.SetActive(true);
            mark.rectTransform.anchoredPosition=new Vector2(x,-4);
            mark.rectTransform.sizeDelta=edge?new Vector2(10,10):new Vector2(16,16);
            mark.color=new Color(colour.r,colour.g,colour.b,alpha*(edge?0.6f:1));
            text.text=label+(edge?"":"  <size=80%>"+Policy.Distance(new Vector2(toward.x,toward.z).magnitude)+"</size>");
            text.color=new Color(colour.r,colour.g,colour.b,alpha);
        }
        private static Vector3? Home()
        {
            PlayerProfile profile=Game.instance!=null?Game.instance.GetPlayerProfile():null;
            if(profile==null||!profile.HaveCustomSpawnPoint())return null;
            return profile.GetCustomSpawnPoint();
        }

        private static void Notes(Player me)
        {
            var sb=new StringBuilder();
            Vector3 here=me.transform.position;
            Ship ship=Ships.Aboard();
            float speed=ship!=null?Mathf.Abs(ship.GetSpeed()):me.GetVelocity().magnitude;
            Vector3? legDir=null;
            if(_seenNext!=Course.Next){if(Course.Points.Count>0&&Course.Next>=Course.Points.Count&&_seenNext>=0&&_seenNext<Course.Points.Count)_finishedAt=Time.time;_seenNext=Course.Next;}
            if(Course.Active)
            {
                Vector3 to=Course.At(Course.Next)-here;to.y=0;
                legDir=to;
                double left=to.magnitude+Policy.Length(Course.Flat,Course.Next);
                string eta=speed>1.5f?" · "+Policy.Duration(left/speed):"";
                sb.Append($"<color=#ffd27a>{Course.Next+1}/{Course.Points.Count}</color> {Course.Label(Course.Next)}  <color=#e8dcc0>{Policy.Distance(to.magnitude)} {Policy.Point(Policy.Bearing(to.x,to.z))}</color>");
                if(Course.Points.Count-Course.Next>1)sb.Append($"<size=85%><color=#b0a690> · {Policy.Distance(left)} to go{eta}</color></size>");
                else sb.Append($"<size=85%><color=#b0a690>{eta}</color></size>");
                sb.Append('\n');
            }
            else if(Course.Points.Count>0&&Time.time-_finishedAt<30)
                sb.Append("<color=#ffd27a>Course sailed.</color> <size=85%><color=#b0a690>Ctrl+R on the map turns it for home.</color></size>\n");
            var helmNext=Ships.HelmNext;
            if(helmNext.HasValue)
            {
                Vector3 to=helmNext.Value-here;to.y=0;
                if(!legDir.HasValue)legDir=to;
                sb.Append($"<color=#8fd8ff>Helm's course</color> {Ships.HelmLabel}  <color=#e8dcc0>{Policy.Distance(to.magnitude)} {Policy.Point(Policy.Bearing(to.x,to.z))}</color>\n");
            }
            if(EnvMan.instance!=null&&(ship!=null||legDir.HasValue))
            {
                Vector3 w=EnvMan.instance.GetWindDir();
                Vector3 h=ship!=null&&!Course.Active&&!helmNext.HasValue?ship.transform.forward:(legDir??me.transform.forward);
                double off=Policy.OffStern(w.x,w.z,h.x,h.z);
                double power=Policy.SailPower(w.x,w.z,EnvMan.instance.GetWindIntensity(),h.x,h.z);
                bool moder=ship!=null&&ship.IsWindControllActive(),fair=ship!=null&&Godforged.FairWind(ship);
                string forWhat=legDir.HasValue?"for this leg":"for her heading";
                sb.Append(moder?"<color=#c8e0ff>Moder's wind fills the sail.</color>\n":fair?"<color=#c8e0ff>Skíðblaðnir's fair wind fills the sail.</color>\n":
                    $"<color=#c0d8c8>Wind</color> {Weather.WindNow()}<size=85%><color=#b0a690> · {Policy.WindWord(off)} {forWhat}, sail {Mathf.RoundToInt((float)power*100)}%</color></size>\n");
            }
            if(ship!=null&&Items.Has(me,Items.SoundingLine)&&Sounding.Line!="")
                sb.Append(Sounding.Alert?$"<color=#ff7a5a>Lead</color> <color=#ffb39a>{Sounding.Line}</color>\n":$"<color=#c8b48a>Lead</color> {Sounding.Line}\n");
            if(Items.Has(me,Items.Primstav))
            {
                string f=Weather.Forecast(here);
                if(f!="")sb.Append($"<color=#d8b880>Primstav</color> {f}\n");
                string feast=Feasts.Next();
                if(feast!="")sb.Append($"<size=85%><color=#b0a690>   feast-day: {feast}</color></size>\n");
                if(Course.Active)
                {
                    Vector3 at=Course.At(Course.Next);
                    if(WorldGenerator.instance!=null&&WorldGenerator.instance.GetBiome(at)!=WorldGenerator.instance.GetBiome(here))
                    {
                        long p=Weather.Period(out _);
                        EnvSetup there=Weather.Foretell(at,p);
                        if(there!=null)sb.Append($"<size=85%><color=#b0a690>   at {Course.Label(Course.Next)} ({Biome(WorldGenerator.instance.GetBiome(at))}): {Policy.Weather(there.m_name)}</color></size>\n");
                    }
                }
            }
            string text=sb.ToString().TrimEnd('\n');
            bool any=text.Length>0;
            if(_notes.gameObject.activeSelf!=any)_notes.gameObject.SetActive(any);
            if(!any)return;
            _text.text=text;
            Place();
            Vector2 size=_text.GetPreferredValues(text,640,0);
            _notes.sizeDelta=new Vector2(Mathf.Min(700,size.x+60),size.y+22);
            ClearOfEffects();
        }
        // The game's status-effect icons (top right, growing leftward) are drawn over us: with several of them the notes ran under
        // them. Slid left until the box clears the leftmost icon.
        private static readonly Vector3[] Corners=new Vector3[4];
        private static void ClearOfEffects()
        {
            var list=Hud.instance!=null?Hud.instance.m_statusEffectListRoot:null;
            if(list==null)return;
            float left=float.MaxValue,bottom=float.MaxValue;
            foreach(RectTransform icon in list)
            {
                if(!icon.gameObject.activeInHierarchy)continue;
                icon.GetWorldCorners(Corners);left=Mathf.Min(left,Corners[0].x);bottom=Mathf.Min(bottom,Corners[0].y);
            }
            if(left==float.MaxValue)return;
            _notes.GetWorldCorners(Corners);
            float margin=12*_root.lossyScale.x,over=Corners[2].x-(left-margin);
            if(over<=0||Corners[1].y<=bottom)return; // (clear of them already, or wholly below them)
            _notes.anchoredPosition-=new Vector2(over/_root.lossyScale.x,0);
        }
        internal static string Biome(Heightmap.Biome b)
        {
            switch(b){case Heightmap.Biome.BlackForest:return "Black Forest";case Heightmap.Biome.AshLands:return "Ashlands";case Heightmap.Biome.DeepNorth:return "Deep North";default:return b.ToString();}
        }
        // Centred under the compass band (the corners belong to the minimap and other mods' panels).
        private static void Place()
        {
            _notes.anchorMin=_notes.anchorMax=new Vector2(0.5f,1);_notes.pivot=new Vector2(0.5f,1);
            _notes.anchoredPosition=new Vector2(0,_strip.gameObject.activeSelf?-88:-14);
        }

        private static void Build()
        {
            Stop();
            _font=Minimap.instance!=null&&Minimap.instance.m_biomeNameSmall!=null?Minimap.instance.m_biomeNameSmall.font:null;
            _band=Sprite.Create(Art.Band(256,64,0.95f),new Rect(0,0,256,64),new Vector2(0.5f,0.5f),100);
            _diamond=Sprite.Create(Art.Diamond(32),new Rect(0,0,32,32),new Vector2(0.5f,0.5f),100);
            var white=new Texture2D(2,2){hideFlags=HideFlags.HideAndDontSave};
            for(int y=0;y<2;y++)for(int x=0;x<2;x++)white.SetPixel(x,y,Color.white);
            white.Apply();
            _white=Sprite.Create(white,new Rect(0,0,2,2),new Vector2(0.5f,0.5f),100);
            foreach(Object o in new Object[]{_band,_band.texture,_diamond,_diamond.texture,white,_white}){o.hideFlags=HideFlags.HideAndDontSave;Owned.Add(o);}

            var root=new GameObject("WayfinderHud",typeof(RectTransform));
            _root=(RectTransform)root.transform;
            _root.SetParent(Hud.instance.m_rootObject.transform,false);
            _root.anchorMin=Vector2.zero;_root.anchorMax=Vector2.one;_root.offsetMin=_root.offsetMax=Vector2.zero;
            _root.SetAsFirstSibling(); // (under the game's own panels)

            var strip=new GameObject("Compass",typeof(RectTransform),typeof(Image));
            _strip=(RectTransform)strip.transform;
            _strip.SetParent(_root,false);
            _strip.anchorMin=_strip.anchorMax=new Vector2(0.5f,1);_strip.pivot=new Vector2(0.5f,1);
            _strip.anchoredPosition=new Vector2(0,-14);_strip.sizeDelta=new Vector2(Width,Height);
            _stripBack=strip.GetComponent<Image>();_stripBack.sprite=_band;_stripBack.type=Image.Type.Simple;_stripBack.raycastTarget=false;
            _centre=NewImage(_strip,_diamond,"Centre").rectTransform;
            _centre.anchoredPosition=new Vector2(0,Height/2+1);_centre.sizeDelta=new Vector2(14,14);
            _centre.GetComponent<Image>().color=new Color(1f,0.85f,0.45f,1);
            _heading=NewText(_strip,"Heading",18,TextAlignmentOptions.Center);
            _heading.rectTransform.anchoredPosition=new Vector2(0,-Height/2-13);_heading.rectTransform.sizeDelta=new Vector2(500,28);

            var notes=new GameObject("Notes",typeof(RectTransform),typeof(Image));
            _notes=(RectTransform)notes.transform;
            _notes.SetParent(_root,false);
            _notesBack=notes.GetComponent<Image>();_notesBack.sprite=_band;_notesBack.raycastTarget=false;_notesBack.color=new Color(1,1,1,0.9f);
            _text=NewText(_notes,"Text",19,TextAlignmentOptions.Top);
            var tt=_text.rectTransform;
            tt.anchorMin=Vector2.zero;tt.anchorMax=Vector2.one;tt.offsetMin=new Vector2(30,11);tt.offsetMax=new Vector2(-30,-11);
            _text.textWrappingMode=TextWrappingModes.Normal;
            _text.lineSpacing=-8;
        }
        private static T Pool<T>(List<T> list,int i,RectTransform parent,System.Func<T> make) where T:Component
        {
            if(i>=list.Count)list.Add(make());
            if(!list[i].gameObject.activeSelf)list[i].gameObject.SetActive(true);
            return list[i];
        }
        private static void Hide<T>(List<T> list,int from) where T:Component{for(int i=from;i<list.Count;i++)if(list[i].gameObject.activeSelf)list[i].gameObject.SetActive(false);}
        private static Image NewImage(RectTransform parent,Sprite sprite,string name)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));
            var t=(RectTransform)go.transform;
            t.SetParent(parent,false);
            t.anchorMin=t.anchorMax=new Vector2(0.5f,0.5f);t.pivot=new Vector2(0.5f,0.5f);
            var image=go.GetComponent<Image>();image.sprite=sprite;image.raycastTarget=false;
            return image;
        }
        private static TextMeshProUGUI NewText(RectTransform parent,string name,float size,TextAlignmentOptions align)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));
            var t=(RectTransform)go.transform;
            t.SetParent(parent,false);
            t.anchorMin=t.anchorMax=new Vector2(0.5f,0.5f);t.pivot=new Vector2(0.5f,0.5f);t.sizeDelta=new Vector2(240,28);
            var text=go.GetComponent<TextMeshProUGUI>();
            if(_font!=null)text.font=_font;
            text.fontSize=size;text.alignment=align;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.richText=true;
            text.outlineWidth=0.08f;text.outlineColor=new Color32(0,0,0,160);
            return text;
        }

        internal static void Stop()
        {
            if(_root!=null)Object.Destroy(_root.gameObject);
            _root=null;Ticks.Clear();Letters.Clear();Marks.Clear();
            foreach(Object o in Owned)if(o!=null)Object.Destroy(o);
            Owned.Clear();
        }
    }
}
