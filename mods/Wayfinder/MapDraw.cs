using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wayfinder
{
    // The course drawn on both maps: a dashed line through the waypoints (legs sailed fade), a fainter line from you to the next
    // waypoint, and numbered rune-rings at each; the helmsman's course in its own colour; and the raven's line. Lines are cut
    // to the map's visible part, so none spill past the minimap's edge.
    internal static class MapDraw
    {
        private sealed class Layer
        {
            internal RectTransform Root;
            internal readonly List<Image> Lines=new List<Image>();
            internal readonly List<RectTransform> Marks=new List<RectTransform>();
            internal int UsedLines,UsedMarks;
        }
        private static readonly Layer Large=new Layer(),Small=new Layer();
        private static Sprite _dash,_ring,_solid;
        private static readonly List<Object> Owned=new List<Object>();
        internal static readonly Color Ahead=new Color(1f,0.82f,0.42f,0.95f),Behind=new Color(0.8f,0.72f,0.6f,0.4f),
            Helm=new Color(0.55f,0.85f,1f,0.9f),RavenLine=new Color(0.15f,0.12f,0.18f,0.85f);

        internal static void Draw()
        {
            Minimap map=Minimap.instance;
            Player me=Player.m_localPlayer;
            bool show=map!=null&&me!=null&&Plugin.ShowOnMap.Value;
            Begin(Large,show&&map.m_mode==Minimap.MapMode.Large?map.m_pinRootLarge:null);
            Begin(Small,show&&map.m_mode==Minimap.MapMode.Small?map.m_pinRootSmall:null);
            if(show)
            {
                Layer layer=map.m_mode==Minimap.MapMode.Large?Large:map.m_mode==Minimap.MapMode.Small?Small:null;
                if(layer!=null&&layer.Root!=null)
                {
                    RawImage image=map.m_mode==Minimap.MapMode.Large?map.m_mapImageLarge:map.m_mapImageSmall;
                    bool large=layer==Large;
                    float width=large?3f:2f;
                    // the helmsman's course, under ours
                    var helm=Ships.HelmCourse;
                    if(helm!=null)Polyline(layer,map,image,helm,0,Helm,width,large,false);
                    var pts=Course.Points;
                    if(pts.Count>0)
                    {
                        var flat=new List<(double x,double z,string name)>(pts);
                        if(Course.Next>0)Polyline(layer,map,image,flat.GetRange(0,Mathf.Min(Course.Next+1,flat.Count)),0,Behind,width,large,false);
                        if(Course.Next<pts.Count)
                        {
                            Polyline(layer,map,image,flat.GetRange(Course.Next,flat.Count-Course.Next),Course.Next,Ahead,width,large,true);
                            Vector3 p=me.transform.position;
                            Segment(layer,map,image,p.x,p.z,(float)pts[Course.Next].x,(float)pts[Course.Next].z,new Color(Ahead.r,Ahead.g,Ahead.b,0.45f),width*0.7f,true);
                        }
                        for(int i=0;i<pts.Count;i++)Mark(layer,map,image,i,large);
                    }
                    foreach(var (a,b) in Raven.Lines)Segment(layer,map,image,a.x,a.z,b.x,b.z,RavenLine,width,true);
                }
            }
            End(Large);End(Small);
        }

        private static void Begin(Layer layer,RectTransform root)
        {
            if(root!=layer.Root)
            {
                foreach(var l in layer.Lines)if(l!=null)Object.Destroy(l.gameObject);
                foreach(var m in layer.Marks)if(m!=null)Object.Destroy(m.gameObject);
                layer.Lines.Clear();layer.Marks.Clear();
                layer.Root=root;
            }
            layer.UsedLines=layer.UsedMarks=0;
        }
        private static void End(Layer layer)
        {
            for(int i=layer.UsedLines;i<layer.Lines.Count;i++)if(layer.Lines[i]!=null&&layer.Lines[i].gameObject.activeSelf)layer.Lines[i].gameObject.SetActive(false);
            for(int i=layer.UsedMarks;i<layer.Marks.Count;i++)if(layer.Marks[i]!=null&&layer.Marks[i].gameObject.activeSelf)layer.Marks[i].gameObject.SetActive(false);
        }

        private static void Polyline(Layer layer,Minimap map,RawImage image,List<(double x,double z,string name)> pts,int first,Color colour,float width,bool large,bool dashed)
        {
            for(int i=0;i+1<pts.Count;i++)Segment(layer,map,image,(float)pts[i].x,(float)pts[i].z,(float)pts[i+1].x,(float)pts[i+1].z,colour,width,dashed);
        }
        private static void Uv(Minimap map,float x,float z,out double u,out double v)
        {
            float half=map.m_textureSize/2f;
            u=(x/map.m_pixelSize+half)/map.m_textureSize;v=(z/map.m_pixelSize+half)/map.m_textureSize;
        }
        private static Vector2 Gui(RawImage image,double u,double v)
        {
            Rect uv=image.uvRect,rect=image.rectTransform.rect;
            return new Vector2((float)((u-uv.xMin)/uv.width*rect.width),(float)((v-uv.yMin)/uv.height*rect.height));
        }
        private static void Segment(Layer layer,Minimap map,RawImage image,float ax,float az,float bx,float bz,Color colour,float width,bool dashed)
        {
            Uv(map,ax,az,out double u0,out double v0);Uv(map,bx,bz,out double u1,out double v1);
            Rect uv=image.uvRect;
            if(!Policy.Clip(ref u0,ref v0,ref u1,ref v1,uv.xMin,uv.yMin,uv.xMax,uv.yMax))return;
            Vector2 from=Gui(image,u0,v0),to=Gui(image,u1,v1),d=to-from;
            if(d.magnitude<1)return;
            if(layer.UsedLines>=layer.Lines.Count)layer.Lines.Add(NewLine(layer.Root));
            Image line=layer.Lines[layer.UsedLines++];
            var t=line.rectTransform;
            t.SetAsFirstSibling(); // (under the game's pins)
            line.gameObject.SetActive(true);
            line.sprite=dashed?_dash:_solid;
            line.type=dashed?Image.Type.Tiled:Image.Type.Simple;
            line.color=colour;
            t.anchoredPosition=from;
            t.sizeDelta=new Vector2(d.magnitude,width*(dashed?2.2f:1f));
            t.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);
        }
        private static void Mark(Layer layer,Minimap map,RawImage image,int i,bool large)
        {
            var p=Course.Points[i];
            Uv(map,(float)p.x,(float)p.z,out double u,out double v);
            Rect uv=image.uvRect;
            if(u<uv.xMin||u>uv.xMax||v<uv.yMin||v>uv.yMax)return;
            if(layer.UsedMarks>=layer.Marks.Count)layer.Marks.Add(NewMark(layer.Root,map));
            RectTransform mark=layer.Marks[layer.UsedMarks++];
            mark.gameObject.SetActive(true);
            mark.anchoredPosition=Gui(image,u,v);
            float size=large?26:14;
            mark.sizeDelta=new Vector2(size,size);
            bool done=i<Course.Next,next=i==Course.Next;
            var ring=mark.GetComponent<Image>();
            ring.color=done?Behind:next?new Color(1f,0.9f,0.55f,1f):Ahead;
            mark.localScale=Vector3.one*(next?1.25f+0.08f*Mathf.Sin(Time.unscaledTime*4):1f);
            var text=mark.GetComponentInChildren<TextMeshProUGUI>(true);
            if(text!=null)
            {
                text.gameObject.SetActive(large);
                if(large){text.text=(i+1)+(string.IsNullOrEmpty(p.name)?"":"  <size=70%>"+p.name+"</size>");text.color=ring.color;}
            }
        }

        private static Image NewLine(RectTransform root)
        {
            Sprites();
            var go=new GameObject("WayfinderLine",typeof(RectTransform),typeof(Image));
            var t=(RectTransform)go.transform;
            t.SetParent(root,false);
            Anchor(t,root);
            t.pivot=new Vector2(0,0.5f);
            var image=go.GetComponent<Image>();
            image.raycastTarget=false;
            image.pixelsPerUnitMultiplier=1.6f;
            return image;
        }
        private static RectTransform NewMark(RectTransform root,Minimap map)
        {
            Sprites();
            var go=new GameObject("WayfinderMark",typeof(RectTransform),typeof(Image));
            var t=(RectTransform)go.transform;
            t.SetParent(root,false);
            Anchor(t,root);
            t.pivot=new Vector2(0.5f,0.5f);
            var image=go.GetComponent<Image>();
            image.sprite=_ring;image.raycastTarget=false;
            var label=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));
            var lt=(RectTransform)label.transform;
            lt.SetParent(t,false);
            lt.anchorMin=lt.anchorMax=new Vector2(1,0.5f);lt.pivot=new Vector2(0,0.5f);
            lt.anchoredPosition=new Vector2(4,0);lt.sizeDelta=new Vector2(240,30);
            var text=label.GetComponent<TextMeshProUGUI>();
            if(map.m_biomeNameLarge!=null)text.font=map.m_biomeNameLarge.font;
            text.fontSize=18;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;
            text.alignment=TextAlignmentOptions.Left;
            text.outlineWidth=0.1f;text.outlineColor=new Color32(0,0,0,200);
            return t;
        }
        // Like the game's pins: placed from the map image's lower-left corner.
        private static void Anchor(RectTransform t,RectTransform root)
        {
            RectTransform like=Minimap.instance!=null&&Minimap.instance.m_pinPrefab!=null?Minimap.instance.m_pinPrefab.transform as RectTransform:null;
            t.anchorMin=like!=null?like.anchorMin:Vector2.zero;t.anchorMax=like!=null?like.anchorMax:Vector2.zero;
        }

        private static void Sprites()
        {
            if(_dash!=null)return;
            var dash=new Texture2D(32,8,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            for(int y=0;y<8;y++)for(int x=0;x<32;x++)
            {
                float a=x<20?1:0;
                a*=Mathf.Clamp01(1-Mathf.Abs(y-3.5f)/4f*1.2f+0.3f);
                dash.SetPixel(x,y,new Color(1,1,1,a));
            }
            dash.Apply();
            _dash=Sprite.Create(dash,new Rect(0,0,32,8),new Vector2(0.5f,0.5f),32);
            var solid=new Texture2D(4,4,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)solid.SetPixel(x,y,Color.white);
            solid.Apply();
            _solid=Sprite.Create(solid,new Rect(0,0,4,4),new Vector2(0.5f,0.5f),4);
            _ring=Sprite.Create(Art.Ring(64),new Rect(0,0,64,64),new Vector2(0.5f,0.5f),64);
            foreach(Object o in new Object[]{dash,solid,_dash,_solid,_ring,_ring.texture}){o.hideFlags=HideFlags.HideAndDontSave;Owned.Add(o);}
        }

        internal static void Stop()
        {
            Begin(Large,null);Begin(Small,null);
            foreach(Object o in Owned)if(o!=null)Object.Destroy(o);
            Owned.Clear();_dash=_ring=_solid=null;
        }
    }

    // Small pictures painted in code.
    internal static class Art
    {
        // A ring with a cross of runic ticks and a dot: a waypoint.
        internal static Texture2D Ring(int size)
        {
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            float c=(size-1)/2f;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=x-c,dy=y-c,r=Mathf.Sqrt(dx*dx+dy*dy)/c;
                float ring=Mathf.Clamp01(1-Mathf.Abs(r-0.72f)/0.11f*1.0f);
                float dot=Mathf.Clamp01((0.22f-r)/0.06f);
                bool tick=(Mathf.Abs(dx)<size*0.035f||Mathf.Abs(dy)<size*0.035f)&&r>0.82f&&r<1f;
                float a=Mathf.Max(ring,Mathf.Max(dot,tick?1:0));
                float edge=Mathf.Clamp01(1-Mathf.Abs(r-0.72f)/0.18f);
                Color col=Color.Lerp(Color.white,new Color(0.1f,0.07f,0.04f),Mathf.Clamp01(edge-ring)*0.9f);
                t.SetPixel(x,y,new Color(col.r,col.g,col.b,Mathf.Max(a,edge*0.6f)));
            }
            t.Apply();
            return t;
        }
        // A soft dark band, fading out at its ends: behind the compass strip and the navigator's notes.
        internal static Texture2D Band(int w,int h,float alpha)
        {
            var t=new Texture2D(w,h,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float ex=Mathf.Clamp01(Mathf.Min(x,w-1-x)/(w*0.18f)),ey=Mathf.Clamp01(Mathf.Min(y,h-1-y)/(h*0.25f));
                t.SetPixel(x,y,new Color(0.06f,0.045f,0.03f,alpha*Mathf.SmoothStep(0,1,ex)*Mathf.SmoothStep(0,1,ey)));
            }
            t.Apply();
            return t;
        }
        internal static Texture2D Diamond(int size)
        {
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            float c=(size-1)/2f;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float d=(Mathf.Abs(x-c)+Mathf.Abs(y-c))/c;
                float a=Mathf.Clamp01((1-d)*size*0.25f);
                float inner=Mathf.Clamp01((0.55f-d)*size*0.25f);
                Color col=Color.Lerp(new Color(0.15f,0.1f,0.05f),Color.white,Mathf.Max(inner,d<0.8f?1:0));
                t.SetPixel(x,y,new Color(col.r,col.g,col.b,a));
            }
            t.Apply();
            return t;
        }
    }
}
