using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildShapes
{
    // How the shape radial looks: like the game's own radial menu. Translucent black wedges with gaps between them round a lighter band and
    // a dark centre disc; the hovered wedge stands out in a gold outline with the game's knotwork crest above it and its gold arc on the
    // inner ring (the game's own sprites, T_radialHighlighter and T_radialIndicator); white icons with a dark rim, gold when hovered; the
    // hovered shape's name in gold in the middle. The wedges and icons are drawn into textures once, here, anti-aliased by distance.
    public sealed partial class Plugin
    {
        private const int RingSize=700;
        private const float RingOuter=300,RingInner=168,BandInner=118,IconSize=100,WedgeGap=9;
        private Texture2D _ringTexture,_ringHighlight;
        private int _ringCount;
        private readonly Dictionary<string,Texture2D> _radialIcons=new Dictionary<string,Texture2D>();
        private GUIStyle _radialName,_radialText;
        private float _radialOpenedAt;

        // A coverage mask drawn with round-ended strokes and dots, baked into a texture in one colour.
        private sealed class Ink
        {
            private readonly int _size;private readonly float[] _a;
            internal Ink(int size){_size=size;_a=new float[size*size];}
            internal void Stroke(float x0,float y0,float x1,float y1,float width)
            {
                float r=width*0.5f;int minX=Math.Max(0,(int)(Math.Min(x0,x1)-r-1)),maxX=Math.Min(_size-1,(int)(Math.Max(x0,x1)+r+1)),minY=Math.Max(0,(int)(Math.Min(y0,y1)-r-1)),maxY=Math.Min(_size-1,(int)(Math.Max(y0,y1)+r+1));
                float dx=x1-x0,dy=y1-y0,l2=Math.Max(1e-6f,dx*dx+dy*dy);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    float px=x+0.5f,py=y+0.5f,t=Mathf.Clamp01(((px-x0)*dx+(py-y0)*dy)/l2),ex=px-(x0+t*dx),ey=py-(y0+t*dy);
                    float cover=Mathf.Clamp01(r+0.5f-Mathf.Sqrt(ex*ex+ey*ey));int i=y*_size+x;if(cover>_a[i])_a[i]=cover;
                }
            }
            internal void Path(IList<Vector2> points,float width){for(int i=0;i+1<points.Count;i++)Stroke(points[i].x,points[i].y,points[i+1].x,points[i+1].y,width);}
            internal void Dot(float x,float y,float radius)=>Stroke(x,y,x,y,radius*2);
            // The coverage grown by r pixels (a rim's shape).
            private float[] Grow(float r)
            {
                var grown=new float[_a.Length];int reach=(int)Math.Ceiling(r)+1;
                for(int y=0;y<_size;y++)for(int x=0;x<_size;x++)
                {
                    float best=0;
                    for(int oy=-reach;oy<=reach && best<1;oy++)for(int ox=-reach;ox<=reach;ox++)
                    {
                        int sx=x+ox,sy=y+oy;if(sx<0||sy<0||sx>=_size||sy>=_size)continue;float a=_a[sy*_size+sx];if(a<=0)continue;
                        float cover=a*Mathf.Clamp01(r+0.5f-Mathf.Sqrt(ox*ox+oy*oy));if(cover>best)best=cover;
                    }
                    grown[y*_size+x]=best;
                }
                return grown;
            }
            // White strokes with a dark rim around them, as the game draws its icons.
            internal Texture2D Bake(Color colour,string name,float rim=0)
            {
                float[] outline=rim>0?Grow(rim):null;
                var pixels=new Color[_a.Length];
                for(int i=0;i<_a.Length;i++)
                {
                    // The stroke over its rim: colour at coverage a, over dark grey at 85 % of the rim's coverage.
                    float a=_a[i]*colour.a,o=(outline!=null?outline[i]:0)*0.85f*(1-a),alpha=a+o;
                    pixels[i]=alpha<=0?new Color(0,0,0,0):new Color((colour.r*a+0.12f*o)/alpha,(colour.g*a+0.12f*o)/alpha,(colour.b*a+0.12f*o)/alpha,alpha);
                }
                var texture=new Texture2D(_size,_size,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
                texture.SetPixels(pixels);texture.Apply();return texture;
            }
        }
        private static float Cover(float d)=>Mathf.Clamp01(0.5f-d); // (signed distance in pixels, inside negative)
        private static float RingAngle(float dx,float dy)=>Mathf.Repeat(Mathf.Atan2(dx,dy)*Mathf.Rad2Deg,360); // (0 at the top, clockwise; texture y is up)
        private static float AngleGap(float a,float b){float d=Mathf.Repeat(a-b+180,360)-180;return Mathf.Abs(d);}

        // The ring: translucent black wedges with gaps between them, a lighter band inside them, a dark centre disc.
        private Texture2D RingTexture(int count)
        {
            var pixels=new Color[RingSize*RingSize];float c=RingSize*0.5f,step=360f/count;
            for(int y=0;y<RingSize;y++)for(int x=0;x<RingSize;x++)
            {
                float dx=x+0.5f-c,dy=y+0.5f-c,r=Mathf.Sqrt(dx*dx+dy*dy),angle=RingAngle(dx,dy);
                float gap=float.MaxValue;for(int i=0;i<count;i++)gap=Mathf.Min(gap,AngleGap(angle,(i+0.5f)*step)*Mathf.Deg2Rad*r);
                float wedge=Cover(Mathf.Max(Mathf.Max(r-RingOuter,RingInner-r),WedgeGap*0.5f-gap));
                float band=Cover(Mathf.Max(r-(RingInner-6),BandInner-r)),disc=Cover(r-BandInner);
                float alpha=Mathf.Max(0.6f*wedge,Mathf.Max(0.25f*band,0.66f*disc));
                pixels[y*RingSize+x]=new Color(0,0,0,alpha);
            }
            return Baked(pixels,"BuildShapes radial ring");
        }
        // The hovered wedge (at the top; turned to it when drawn): a gold outline, standing a little out of the ring.
        private Texture2D HighlightTexture(int count)
        {
            var pixels=new Color[RingSize*RingSize];float c=RingSize*0.5f,half=180f/count;
            for(int y=0;y<RingSize;y++)for(int x=0;x<RingSize;x++)
            {
                float dx=x+0.5f-c,dy=y+0.5f-c,r=Mathf.Sqrt(dx*dx+dy*dy);
                float side=(AngleGap(RingAngle(dx,dy),0)-half)*Mathf.Deg2Rad*r+WedgeGap*0.5f-6;
                float d=Mathf.Max(Mathf.Max(r-(RingOuter+18),(RingInner-4)-r),side);
                float rim=Cover(Mathf.Abs(d+2.5f)-2.2f),inside=Cover(d);
                pixels[y*RingSize+x]=Color.Lerp(new Color(0,0,0,0.35f*inside),RadialGold,rim);
            }
            return Baked(pixels,"BuildShapes radial highlight");
        }
        private static readonly Color RadialGold=new Color(0.91f,0.64f,0.29f,1);
        private static Texture2D Baked(Color[] pixels,string name)
        {
            int size=(int)Mathf.Sqrt(pixels.Length);
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        // The game's own radial sprites, found once (by name, when the radial first opens).
        private Sprite _radialCrest,_radialArrow,_radialHammer;
        private bool _radialSpritesLooked;
        private void FindRadialSprites()
        {
            if(_radialSpritesLooked)return;_radialSpritesLooked=true;
            foreach(Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if(sprite.name=="T_radialHighlighter")_radialCrest=sprite;
                else if(sprite.name=="T_radialIndicator")_radialArrow=sprite;
                else if(sprite.name=="Icon_Radial_Hammer")_radialHammer=sprite;
            }
        }
        private static void DrawSprite(Rect rect,Sprite sprite)
        {
            if(sprite==null || sprite.texture==null)return;
            Rect t=sprite.textureRect;float w=sprite.texture.width,h=sprite.texture.height;
            GUI.DrawTextureWithTexCoords(rect,sprite.texture,new Rect(t.x/w,t.y/h,t.width/w,t.height/h));
        }
        // Each shape's drawing, white (tinted when drawn), on a 96 px canvas with y up.
        private Texture2D RadialIcon(string shape)
        {
            if(_radialIcons.TryGetValue(shape,out var cached) && cached!=null)return cached;
            var ink=new Ink(96);const float W=5f;
            List<Vector2> Sample(Func<float,Vector2> at,int n=40){var p=new List<Vector2>();for(int i=0;i<=n;i++)p.Add(at(i/(float)n));return p;}
            switch(shape)
            {
                case "Curve":
                {
                    Vector2 a=new Vector2(14,26),m=new Vector2(48,70),b=new Vector2(84,38),k=m*2-(a+b)*0.5f;
                    ink.Path(Sample(t=>a*(1-t)*(1-t)+k*(2*t*(1-t))+b*(t*t)),W);ink.Dot(a.x,a.y,6);ink.Dot(m.x,m.y,6);ink.Dot(b.x,b.y,6);break;
                }
                case "Arch":
                    ink.Stroke(22,14,22,46,W);ink.Stroke(74,14,74,46,W);
                    ink.Path(Sample(t=>new Vector2(48-26*Mathf.Cos(Mathf.PI*t),46+34*Mathf.Sin(Mathf.PI*t))),W);
                    ink.Stroke(10,12,86,12,3);break;
                case "Arc":
                {
                    // Through (14,24), (48,62) and (82,24): its centre on x = 48.
                    float cy=(62f*62f-24f*24f-34f*34f)/(2*(62f-24f)),radius=62-cy,a0=Mathf.Atan2(24-cy,-34)+2*Mathf.PI,a1=Mathf.Atan2(24-cy,34); // (a0 past a half turn, so the sweep goes over the top)
                    ink.Path(Sample(t=>{float a=Mathf.Lerp(a0,a1,t);return new Vector2(48+radius*Mathf.Cos(a),cy+radius*Mathf.Sin(a));}),W);
                    ink.Dot(14,24,6);ink.Dot(82,24,6);ink.Stroke(10,12,86,12,3);break;
                }
                case "Line":
                    ink.Stroke(16,18,80,78,W);ink.Dot(16,18,6);ink.Dot(80,78,6);
                    foreach(float t in new[]{1/3f,2/3f}){float x=16+64*t,y=18+60*t;ink.Stroke(x-6,y+6.4f,x+6,y-6.4f,3);}
                    break;
                case "Mirror":
                    for(float y=8;y<88;y+=14)ink.Stroke(48,y,48,y+7,3);
                    ink.Path(new[]{new Vector2(38,20),new Vector2(14,20),new Vector2(38,70),new Vector2(38,20)},W);
                    ink.Path(new[]{new Vector2(58,20),new Vector2(82,20),new Vector2(58,70),new Vector2(58,20)},W);break;
                case "Repeat":
                {
                    Func<float,Vector2> path=t=>new Vector2(10+76*t,22+10*Mathf.Sin(Mathf.PI*t));
                    ink.Path(Sample(path),3);
                    for(int i=0;i<5;i++){Vector2 p=path(i/4f);ink.Stroke(p.x,p.y,p.x,p.y+40,W);}
                    break;
                }
                default: // Normal building: the hammer
                    ink.Stroke(26,16,62,62,7);ink.Path(new[]{new Vector2(50,74),new Vector2(70,54),new Vector2(84,68),new Vector2(64,88),new Vector2(50,74)},W);break;
            }
            return _radialIcons[shape]=ink.Bake(Color.white,"BuildShapes radial "+shape,3.5f);
        }
        private void RadialStyles()
        {
            if(_radialName!=null)return;
            GUISkin skin=Theme();
            _radialName=new GUIStyle(skin.label){fontSize=34,alignment=TextAnchor.MiddleCenter,wordWrap=false};Text(_radialName,RadialGold);
            _radialText=new GUIStyle(skin.label){fontSize=14,alignment=TextAnchor.UpperCenter,wordWrap=true};Text(_radialText,Cream);
        }
        private void DrawRadial()
        {
            var items=RadialItems();
            if(_ringTexture==null || _ringCount!=items.Count)
            {
                if(_ringTexture!=null)Destroy(_ringTexture);if(_ringHighlight!=null)Destroy(_ringHighlight);
                _ringTexture=RingTexture(items.Count);_ringHighlight=HighlightTexture(items.Count);_ringCount=items.Count;
            }
            RadialStyles();FindRadialSprites();
            Matrix4x4 saved=GUI.matrix;Color colour=GUI.color;
            try
            {
                float scale=Mathf.Max(0.6f,Screen.height/1080f);GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
                Vector2 centre=new Vector2(Screen.width/scale*0.5f,Screen.height/scale*0.5f);
                // Opens in a tenth of a second from the middle of the screen: grows from nine tenths and fades in. (GUIUtility's pivots
                // differ: ScaleAroundPivot passes its pivot through GUI.matrix, so it's in GUI coordinates like the rects; RotateAroundPivot
                // clears the matrix first, so its pivot is in screen pixels.)
                float t=Mathf.Clamp01((Time.unscaledTime-_radialOpenedAt)/0.12f),ease=1-(1-t)*(1-t);
                GUIUtility.ScaleAroundPivot(Vector2.one*(0.9f+0.1f*ease),centre);
                Color fade=new Color(1,1,1,ease);
                GUI.color=fade;
                var ring=new Rect(centre.x-RingSize*0.5f,centre.y-RingSize*0.5f,RingSize,RingSize);
                GUI.DrawTexture(ring,_ringTexture);
                float step=360f/items.Count;
                if(_radialHover>=0)
                {
                    Matrix4x4 beforeTurn=GUI.matrix;
                    GUIUtility.RotateAroundPivot(_radialHover*step,centre*scale);
                    GUI.DrawTexture(ring,_ringHighlight);
                    GUI.color=fade*RadialGold;
                    DrawSprite(new Rect(centre.x-80,centre.y-RingOuter-50,160,32),_radialCrest);
                    DrawSprite(new Rect(centre.x-46,centre.y-BandInner-26,92,18),_radialArrow);
                    GUI.color=fade;
                    GUI.matrix=beforeTurn;
                }
                for(int i=0;i<items.Count;i++)
                {
                    var item=items[i];bool hover=i==_radialHover,off=item.Reason!=null;
                    float angle=i*step*Mathf.Deg2Rad,mid=(RingInner+RingOuter)*0.5f+(hover?8:0);
                    Vector2 at=centre+new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle))*mid;
                    float size=IconSize*(hover?1.1f:1);
                    var rect=new Rect(at.x-size*0.5f,at.y-size*0.5f,size,size);
                    GUI.color=fade*(off?new Color(1,1,1,0.35f):hover?RadialGold:Color.white);
                    if(item.Exit && _radialHammer!=null)DrawSprite(new Rect(at.x-size*0.33f,at.y-size*0.5f,size*0.66f,size),_radialHammer);
                    else GUI.DrawTexture(rect,RadialIcon(item.Exit?"Build":item.Label));
                }
                GUI.color=fade;
                RadialItem shown=_radialHover>=0?items[_radialHover]:null;
                string name=shown==null?"Shapes":shown.Exit?"Build":shown.Label;
                string text=shown==null?"Flick towards a shape, let go of "+_radialKey.Value+".":shown.Reason??shown.Detail;
                GUI.Label(new Rect(centre.x-110,centre.y-34,220,44),name,_radialName);
                GUI.Label(new Rect(centre.x-90,centre.y+10,180,54),text,_radialText);
            }
            finally{GUI.matrix=saved;GUI.color=colour;}
        }
        private void DestroyRadial()
        {
            foreach(var texture in _radialIcons.Values)if(texture!=null)Destroy(texture);_radialIcons.Clear();
            if(_ringTexture!=null)Destroy(_ringTexture);if(_ringHighlight!=null)Destroy(_ringHighlight);_ringTexture=_ringHighlight=null;_ringCount=0;
            _radialName=_radialText=null;
        }
    }
}
